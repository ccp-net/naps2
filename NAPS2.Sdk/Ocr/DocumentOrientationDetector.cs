using System.Globalization;
using System.Xml.Linq;
using NAPS2.Scan;
using NAPS2.Unmanaged;

namespace NAPS2.Ocr;

/// <summary>
/// Lightweight orientation detection for scanned documents. This uses Tesseract's hOCR layout output and aggregates
/// per-line textangle values. It intentionally refuses to guess when there is not enough text or the detected lines do
/// not strongly agree on one of the four right-angle orientations.
/// </summary>
internal class DocumentOrientationDetector
{
    private const int PROCESS_TIMEOUT_MS = 7000;
    private const int MIN_WORD_CONFIDENCE = 40;
    private const int MIN_LINE_WEIGHT = 4;

    private readonly ScanningContext _scanningContext;

    public DocumentOrientationDetector(ScanningContext scanningContext)
    {
        _scanningContext = scanningContext;
    }

    public OrientationDetectionResult Detect(IMemoryImage image)
    {
        var language = FindOrientationLanguage();
        if (language == null)
        {
            return OrientationDetectionResult.Unavailable;
        }

        string? imagePath = null;
        string? outputBase = null;
        string? hocrPath = null;
        try
        {
            imagePath = _scanningContext.SaveToTempFile(image);
            outputBase = Path.Combine(_scanningContext.TempFolderPath, Path.GetRandomFileName());
            hocrPath = outputBase + ".hocr";

            var startInfo = new ProcessStartInfo
            {
                FileName = NativeLibrary.FindExePath(PlatformCompat.System.TesseractExecutableName),
                Arguments = $"\"{imagePath}\" \"{outputBase}\" -l {language.Value.code} --psm 3 hocr",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables["TESSDATA_PREFIX"] = language.Value.tessdataPath;

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return OrientationDetectionResult.Unavailable;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(PROCESS_TIMEOUT_MS))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }
                _scanningContext.Logger.LogDebug("CCP Auto Orientation: Tesseract layout analysis timed out.");
                return OrientationDetectionResult.Unavailable;
            }

            _ = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(hocrPath))
            {
                _scanningContext.Logger.LogDebug(
                    "CCP Auto Orientation: Tesseract layout analysis failed with exit code {ExitCode}. {Error}",
                    process.ExitCode, stderr);
                return OrientationDetectionResult.Unavailable;
            }

            return ParseHocr(XDocument.Load(hocrPath));
        }
        catch (Exception ex)
        {
            // Orientation is an enhancement, never a reason to fail a scan.
            _scanningContext.Logger.LogDebug(ex, "CCP Auto Orientation analysis failed; leaving page unchanged.");
            return OrientationDetectionResult.Unavailable;
        }
        finally
        {
            DeleteQuietly(imagePath);
            DeleteQuietly(hocrPath);
            DeleteQuietly(outputBase + ".txt");
        }
    }

    private (string code, string tessdataPath)? FindOrientationLanguage()
    {
        var basePath = _scanningContext.OcrLanguageDataPath;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return null;
        }

        // Prefer Vietnamese for CCP dossiers, then English, then any installed Latin-language model. Fast data is enough
        // for layout/orientation analysis and keeps scan latency lower than the "best" models.
        foreach (var subfolder in new[] { "fast", "best" })
        {
            var tessdataPath = Path.Combine(basePath, subfolder);
            foreach (var code in new[] { "vie", "eng" })
            {
                if (File.Exists(Path.Combine(tessdataPath, code + ".traineddata")))
                {
                    return (code, tessdataPath);
                }
            }

            if (Directory.Exists(tessdataPath))
            {
                var fallback = Directory.EnumerateFiles(tessdataPath, "*.traineddata")
                    .Select(Path.GetFileNameWithoutExtension)
                    .FirstOrDefault(code => !string.Equals(code, "osd", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    return (fallback, tessdataPath);
                }
            }
        }
        return null;
    }

    private static OrientationDetectionResult ParseHocr(XDocument document)
    {
        var weights = new Dictionary<int, int>
        {
            [0] = 0,
            [90] = 0,
            [180] = 0,
            [270] = 0
        };
        var samples = new Dictionary<int, int>
        {
            [0] = 0,
            [90] = 0,
            [180] = 0,
            [270] = 0
        };

        int totalWeight = 0;
        int totalSamples = 0;
        foreach (var line in document.Descendants().Where(x =>
                     GetClass(x) is "ocr_line" or "ocr_header" or "ocr_textfloat"))
        {
            int lineWeight = 0;
            foreach (var word in line.Descendants().Where(x => GetClass(x) == "ocrx_word"))
            {
                var text = word.Value.Trim();
                if (text.Length == 0)
                {
                    continue;
                }
                var confidence = GetIntTitleValue(word, "x_wconf");
                if (confidence.HasValue && confidence.Value < MIN_WORD_CONFIDENCE)
                {
                    continue;
                }
                lineWeight += text.Count(char.IsLetterOrDigit);
            }

            if (lineWeight < MIN_LINE_WEIGHT)
            {
                continue;
            }

            var rawAngle = GetDoubleTitleValue(line, "textangle") ?? 0;
            var angle = NormalizeRightAngle(rawAngle);
            if (!angle.HasValue)
            {
                continue;
            }

            weights[angle.Value] += lineWeight;
            samples[angle.Value]++;
            totalWeight += lineWeight;
            totalSamples++;
        }

        if (totalWeight == 0 || totalSamples == 0)
        {
            return OrientationDetectionResult.Unavailable;
        }

        var dominant = weights.OrderByDescending(x => x.Value).First();
        var confidenceValue = dominant.Value / (double) totalWeight;
        return new OrientationDetectionResult(dominant.Key, confidenceValue, totalSamples, totalWeight, true);
    }

    private static int? NormalizeRightAngle(double angle)
    {
        angle %= 360;
        if (angle < 0)
        {
            angle += 360;
        }
        var rounded = ((int) Math.Round(angle / 90.0) * 90) % 360;
        var error = Math.Abs(angle - rounded);
        error = Math.Min(error, 360 - error);
        return error <= 10 ? rounded : null;
    }

    private static string? GetClass(XElement element) => element.Attribute("class")?.Value;

    private static int? GetIntTitleValue(XElement element, string key)
    {
        var value = GetTitleValue(element, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double? GetDoubleTitleValue(XElement element, string key)
    {
        var value = GetTitleValue(element, key);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? GetTitleValue(XElement element, string key)
    {
        var title = element.Attribute("title")?.Value;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        foreach (var part in title.Split(';'))
        {
            var tokens = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && string.Equals(tokens[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return tokens[1];
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
}

internal readonly record struct OrientationDetectionResult(
    int RotationDegrees,
    double Confidence,
    int SampleCount,
    int WeightedCharacters,
    bool IsAvailable)
{
    public static readonly OrientationDetectionResult Unavailable = new(0, 0, 0, 0, false);
}