using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using NAPS2.Scan;
using NAPS2.Unmanaged;

namespace NAPS2.Ocr;

/// <summary>
/// Conservative document orientation detection for CCP Scan. Instead of depending on Tesseract hOCR textangle (which
/// is not reliably emitted for whole-page 90/180/270 degree rotations), this detector OCR-scores the page in all four
/// right-angle orientations and chooses a correction only when the best result clearly beats the runner-up.
/// </summary>
public class DocumentOrientationDetector
{
    private const int PROCESS_TIMEOUT_MS = 5000;
    private const int MIN_WORD_CONFIDENCE = 25;

    private readonly ScanningContext _scanningContext;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loggedMissingLanguage;

    public DocumentOrientationDetector(ScanningContext scanningContext)
    {
        _scanningContext = scanningContext;
    }

    public OrientationDetectionResult Detect(IMemoryImage image)
    {
        _gate.Wait();
        try
        {
            var language = FindOrientationLanguage();
            if (language == null)
            {
                if (!_loggedMissingLanguage)
                {
                    _loggedMissingLanguage = true;
                    _scanningContext.Logger.LogWarning(
                        "CCP Auto Orientation is inactive because no Tesseract language data was found. Install vie or eng traineddata to enable it.");
                }
                return OrientationDetectionResult.Unavailable;
            }

            _scanningContext.Logger.LogDebug(
                "CCP Auto Orientation: using Tesseract language {Language} from {Path}.",
                language.Value.code, language.Value.tessdataPath);

            var scores = new List<OrientationCandidateScore>(4);
            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                scores.Add(ScoreCandidate(image, rotation, language.Value.code, language.Value.tessdataPath));
            }

            var ordered = scores.OrderByDescending(x => x.Score).ToList();
            var best = ordered[0];
            var second = ordered[1];

            if (best.Score <= 0)
            {
                _scanningContext.Logger.LogDebug(
                    "CCP Auto Orientation: no usable OCR text detected at any orientation.");
                return OrientationDetectionResult.Unavailable;
            }

            // Confidence is the relative advantage of the best orientation over the second-best orientation. A value
            // near 0 means ambiguous; a value near 1 means the winning orientation is overwhelmingly better.
            var dominance = Math.Max(0, (best.Score - second.Score) / best.Score);

            _scanningContext.Logger.LogDebug(
                "CCP Auto Orientation scores: 0={Score0:F0}, 90={Score90:F0}, 180={Score180:F0}, 270={Score270:F0}; best={Best} margin={Margin:P0}, words={Words}, chars={Chars}.",
                scores.First(x => x.RotationDegrees == 0).Score,
                scores.First(x => x.RotationDegrees == 90).Score,
                scores.First(x => x.RotationDegrees == 180).Score,
                scores.First(x => x.RotationDegrees == 270).Score,
                best.RotationDegrees, dominance, best.WordCount, best.WeightedCharacters);

            return new OrientationDetectionResult(
                best.RotationDegrees,
                dominance,
                best.WordCount,
                best.WeightedCharacters,
                true);
        }
        catch (Exception ex)
        {
            // Auto Orientation is an enhancement and must never fail or interrupt a scan.
            _scanningContext.Logger.LogDebug(ex, "CCP Auto Orientation analysis failed; leaving page unchanged.");
            return OrientationDetectionResult.Unavailable;
        }
        finally
        {
            _gate.Release();
        }
    }

    private OrientationCandidateScore ScoreCandidate(
        IMemoryImage image, int rotationDegrees, string languageCode, string tessdataPath)
    {
        string? imagePath = null;
        string? outputBase = null;
        string? tsvPath = null;
        try
        {
            using var candidate = rotationDegrees == 0
                ? image.Clone()
                : image.PerformTransform(new RotationTransform(rotationDegrees));

            imagePath = _scanningContext.SaveToTempFile(candidate);
            outputBase = Path.Combine(_scanningContext.TempFolderPath, Path.GetRandomFileName());
            tsvPath = outputBase + ".tsv";

            var startInfo = new ProcessStartInfo
            {
                FileName = NativeLibrary.FindExePath(PlatformCompat.System.TesseractExecutableName),
                Arguments = $"\"{imagePath}\" \"{outputBase}\" -l {languageCode} --psm 6 tsv",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables["TESSDATA_PREFIX"] = tessdataPath;

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new OrientationCandidateScore(rotationDegrees, 0, 0, 0);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(PROCESS_TIMEOUT_MS))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }
                _scanningContext.Logger.LogDebug(
                    "CCP Auto Orientation: Tesseract timed out for {Rotation} degrees.", rotationDegrees);
                return new OrientationCandidateScore(rotationDegrees, 0, 0, 0);
            }

            _ = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(tsvPath))
            {
                _scanningContext.Logger.LogDebug(
                    "CCP Auto Orientation: OCR scoring failed at {Rotation} degrees, exit code {ExitCode}. {Error}",
                    rotationDegrees, process.ExitCode, stderr);
                return new OrientationCandidateScore(rotationDegrees, 0, 0, 0);
            }

            return ParseTsv(rotationDegrees, tsvPath);
        }
        catch (Exception ex)
        {
            _scanningContext.Logger.LogDebug(
                ex, "CCP Auto Orientation: OCR scoring failed at {Rotation} degrees.", rotationDegrees);
            return new OrientationCandidateScore(rotationDegrees, 0, 0, 0);
        }
        finally
        {
            DeleteQuietly(imagePath);
            DeleteQuietly(tsvPath);
            DeleteQuietly(outputBase + ".txt");
        }
    }

    private static OrientationCandidateScore ParseTsv(int rotationDegrees, string tsvPath)
    {
        double score = 0;
        int wordCount = 0;
        int weightedCharacters = 0;

        foreach (var line in File.ReadLines(tsvPath).Skip(1))
        {
            var parts = line.Split('\t');
            if (parts.Length < 12 || parts[0] != "5")
            {
                continue;
            }

            if (!double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) ||
                confidence < MIN_WORD_CONFIDENCE)
            {
                continue;
            }

            var text = parts[11].Trim();
            int chars = text.Count(char.IsLetterOrDigit);
            if (chars == 0)
            {
                continue;
            }

            wordCount++;
            weightedCharacters += chars;
            score += chars * confidence;
        }

        return new OrientationCandidateScore(rotationDegrees, score, wordCount, weightedCharacters);
    }

    private (string code, string tessdataPath)? FindOrientationLanguage()
    {
        var candidateDirectories = new List<string>();
        var basePath = _scanningContext.OcrLanguageDataPath;
        if (!string.IsNullOrWhiteSpace(basePath))
        {
            candidateDirectories.Add(Path.Combine(basePath, "fast"));
            candidateDirectories.Add(Path.Combine(basePath, "best"));
            candidateDirectories.Add(basePath);
        }

        var envTessdata = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(envTessdata))
        {
            candidateDirectories.Add(envTessdata);
        }

        try
        {
            var tessExe = NativeLibrary.FindExePath(PlatformCompat.System.TesseractExecutableName);
            var exeDir = Path.GetDirectoryName(tessExe);
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                candidateDirectories.Add(Path.Combine(exeDir, "tessdata"));
            }
        }
        catch
        {
        }

        foreach (var tessdataPath in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(tessdataPath))
            {
                continue;
            }

            foreach (var code in new[] { "vie", "eng" })
            {
                if (File.Exists(Path.Combine(tessdataPath, code + ".traineddata")))
                {
                    return (code, tessdataPath);
                }
            }

            var fallback = Directory.EnumerateFiles(tessdataPath, "*.traineddata")
                .Select(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty)
                .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code) &&
                                        !string.Equals(code, "osd", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return (fallback, tessdataPath);
            }
        }

        return null;
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private readonly record struct OrientationCandidateScore(
        int RotationDegrees,
        double Score,
        int WordCount,
        int WeightedCharacters);
}

public readonly record struct OrientationDetectionResult(
    int RotationDegrees,
    double Confidence,
    int SampleCount,
    int WeightedCharacters,
    bool IsAvailable)
{
    public static readonly OrientationDetectionResult Unavailable = new(0, 0, 0, 0, false);
}