using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.Ocr;

namespace NAPS2.Pdf;

internal class SavePdfOperation : OperationBase
{
    // CCP v0.2.15:
    // The receiving system limits the complete PDF to under 20 MB. CCP targets 18.9 MB before signing so the
    // downstream digital signature (measured at ~118 KB/file in the user's samples) has ample headroom.
    // There is deliberately NO per-page size limit and NO per-page optimization.
    internal const long CCP_MAX_UNSIGNED_PDF_BYTES = 19_000_000L;
    internal const long CCP_TARGET_UNSIGNED_PDF_BYTES = 18_900_000L;
    internal const int CCP_MIN_JPEG_QUALITY = 40;
    internal const int CCP_MAX_JPEG_QUALITY = 92;
    internal static readonly int[] CCP_DPI_LEVELS = { 300, 275, 250, 225, 200 };

    private readonly PdfExporter _pdfExporter;
    private readonly IOverwritePrompt _overwritePrompt;
    private readonly IEmailProviderFactory? _emailProviderFactory;

    public SavePdfOperation(PdfExporter pdfExporter, IOverwritePrompt overwritePrompt,
        IEmailProviderFactory? emailProviderFactory = null)
    {
        _pdfExporter = pdfExporter;
        _overwritePrompt = overwritePrompt;
        _emailProviderFactory = emailProviderFactory;

        AllowCancel = true;
        AllowBackground = true;
    }

    public string? FirstFileSaved { get; private set; }

    public bool Start(string fileName, Placeholders placeholders, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams, EmailMessage? emailMessage = null,
        string? overwriteFile = null)
    {
        ProgressTitle = emailMessage != null ? MiscResources.EmailPdfProgress : MiscResources.SavePdfProgress;
        var subFileName = placeholders.Substitute(fileName);
        Status = new OperationStatus
        {
            StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(subFileName)),
            MaxProgress = images.Count
        };

        if (Directory.Exists(subFileName))
        {
            subFileName = placeholders.Substitute(Path.Combine(subFileName, "$(n).pdf"));
        }
        var singleFile = !pdfSettings.SinglePagePdfs || images.Count == 1;
        if (singleFile)
        {
            if (File.Exists(subFileName))
            {
                if (subFileName != overwriteFile &&
                    _overwritePrompt.ConfirmOverwrite(subFileName) != OverwriteResponse.Yes)
                {
                    FailedToStart();
                    return false;
                }
                if (FileSystemHelper.IsFileInUse(subFileName, out var ex))
                {
                    InvokeError(MiscResources.FileInUse, ex!);
                    FailedToStart();
                    return false;
                }
            }
        }

        var imagesByFile = pdfSettings.SinglePagePdfs
            ? images.Select(x => new[] { x }).ToArray()
            : new[] { images.ToArray() };
        RunAsync(async () =>
        {
            bool result = false;
            try
            {
                int digits = (int) Math.Floor(Math.Log10(images.Count)) + 1;
                int i = 0;
                foreach (var imagesForFile in imagesByFile)
                {
                    var currentFileName = placeholders.Substitute(fileName, true, i, singleFile ? 0 : digits);
                    Status.StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(currentFileName));
                    InvokeStatusChanged();
                    if (singleFile && IsFileInUse(currentFileName, out var ex))
                    {
                        InvokeError(MiscResources.FileInUse, ex!);
                        break;
                    }

                    var progress = new ProgressHandler(singleFile ? OnProgress : null, CancelToken);
                    result = await ExportCcpSizeLimitedPdf(
                        currentFileName, imagesForFile, pdfSettings, ocrParams, progress);
                    if (!result || CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    emailMessage?.Attachments.Add(new EmailAttachment
                    {
                        FilePath = currentFileName,
                        AttachmentName = Path.GetFileName(currentFileName)
                    });
                    if (i == 0)
                    {
                        FirstFileSaved = subFileName;
                    }
                    i++;
                    if (!singleFile)
                    {
                        OnProgress(i, imagesByFile.Length);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                InvokeError(MiscResources.DontHavePermission, ex);
            }
            catch (Exception ex)
            {
                Log.ErrorException(MiscResources.ErrorSaving, ex);
                InvokeError(MiscResources.ErrorSaving, ex);
            }
            finally
            {
                GC.Collect();
            }

            if (result && emailMessage != null && _emailProviderFactory != null)
            {
                Status.StatusText = MiscResources.UploadingEmail;
                Status.CurrentProgress = 0;
                Status.MaxProgress = 1;
                Status.ProgressType = OperationProgressType.MB;
                InvokeStatusChanged();

                try
                {
                    result = await _emailProviderFactory.Default.SendEmail(emailMessage, ProgressHandler);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.ErrorException(MiscResources.ErrorEmailing, ex);
                    InvokeError(MiscResources.ErrorEmailing, ex);
                }
            }

            return result;
        });
        Success.ContinueWith(task =>
        {
            if (task.Result)
            {
                Log.Event(emailMessage != null ? EventType.Email : EventType.SavePdf, new EventParams
                {
                    Name = emailMessage != null ? MiscResources.EmailPdf : MiscResources.SavePdf,
                    Pages = images.Count,
                    FileFormat = ".pdf"
                });
            }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        return true;
    }

    private async Task<bool> ExportCcpSizeLimitedPdf(string fileName, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams, ProgressHandler progress)
    {
        var imageList = images.ToArray();
        if (imageList.Length == 0)
        {
            return false;
        }

        // First save normally. If the complete PDF is already below the target, do not recompress or resample anything.
        var success = await _pdfExporter.Export(
            fileName,
            imageList,
            CreatePdfExportParams(pdfSettings),
            ocrParams,
            progress);
        if (!success || !File.Exists(fileName) || CancelToken.IsCancellationRequested)
        {
            return false;
        }

        var originalSize = new FileInfo(fileName).Length;
        if (originalSize <= CCP_TARGET_UNSIGNED_PDF_BYTES)
        {
            Log.Info(
                $"CCP v0.2.15 PDF kept at original export quality: {originalSize / 1_000_000.0:F2} MB, " +
                $"target < {CCP_MAX_UNSIGNED_PDF_BYTES / 1_000_000.0:F0} MB.");
            return true;
        }

        Status.StatusText =
            $"PDF {originalSize / 1_000_000.0:F1} MB - đang tối ưu toàn bộ file xuống dưới 19 MB...";
        InvokeStatusChanged();

        var tempFile = Path.Combine(Path.GetDirectoryName(fileName) ?? Paths.Temp,
            $".{Path.GetFileNameWithoutExtension(fileName)}.ccp-v0215-{Guid.NewGuid():N}.pdf");

        try
        {
            // Preserve the highest possible common DPI. At each DPI, binary-search one JPEG quality used for ALL pages.
            // This avoids the v0.2.14 behavior of independently compressing individual pages.
            foreach (var dpi in CCP_DPI_LEVELS)
            {
                var highSize = await ExportCandidate(
                    tempFile, imageList, pdfSettings, ocrParams, dpi, CCP_MAX_JPEG_QUALITY);
                if (highSize <= CCP_TARGET_UNSIGNED_PDF_BYTES)
                {
                    File.Copy(tempFile, fileName, true);
                    LogFinalChoice(fileName, dpi, CCP_MAX_JPEG_QUALITY);
                    return true;
                }

                var lowSize = await ExportCandidate(
                    tempFile, imageList, pdfSettings, ocrParams, dpi, CCP_MIN_JPEG_QUALITY);
                if (lowSize > CCP_TARGET_UNSIGNED_PDF_BYTES)
                {
                    continue;
                }

                var bestQuality = CCP_MIN_JPEG_QUALITY;
                var low = CCP_MIN_JPEG_QUALITY + 1;
                var high = CCP_MAX_JPEG_QUALITY - 1;

                while (low <= high)
                {
                    var mid = low + (high - low) / 2;
                    var candidateSize = await ExportCandidate(
                        tempFile, imageList, pdfSettings, ocrParams, dpi, mid);
                    if (candidateSize <= CCP_TARGET_UNSIGNED_PDF_BYTES)
                    {
                        bestQuality = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                // Export once more with the selected settings so tempFile is guaranteed to match bestQuality.
                await ExportCandidate(tempFile, imageList, pdfSettings, ocrParams, dpi, bestQuality);
                File.Copy(tempFile, fileName, true);
                LogFinalChoice(fileName, dpi, bestQuality);
                return true;
            }

            throw new InvalidOperationException(
                "Không thể giảm toàn bộ PDF xuống dưới 19 MB mà vẫn giữ tối thiểu 200 DPI và JPEG quality 40.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
            }
        }
    }

    private async Task<long> ExportCandidate(string tempFile, ProcessedImage[] images, PdfSettings pdfSettings,
        OcrParams ocrParams, int dpi, int jpegQuality)
    {
        if (CancelToken.IsCancellationRequested)
        {
            throw new OperationCanceledException();
        }

        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }

        var pageOptions = Enumerable.Range(0, images.Length)
            .Select(_ => new PdfPageExportOptions
            {
                TargetDpi = dpi,
                JpegQuality = jpegQuality
            })
            .ToArray();

        var success = await _pdfExporter.Export(
            tempFile,
            images,
            CreatePdfExportParams(pdfSettings, pageOptions),
            ocrParams,
            new ProgressHandler(null, CancelToken));
        if (!success || !File.Exists(tempFile))
        {
            throw new IOException("Không thể tạo file PDF tạm trong quá trình tối ưu dung lượng.");
        }
        return new FileInfo(tempFile).Length;
    }

    private static PdfExportParams CreatePdfExportParams(PdfSettings pdfSettings,
        IReadOnlyList<PdfPageExportOptions>? pageOptions = null) =>
        new(pdfSettings.Metadata, pdfSettings.Encryption, pdfSettings.Compat)
        {
            PageOptions = pageOptions
        };

    private static void LogFinalChoice(string fileName, int dpi, int jpegQuality)
    {
        var finalSize = new FileInfo(fileName).Length;
        Log.Info(
            $"CCP v0.2.15 whole-file PDF optimization completed: {finalSize / 1_000_000.0:F2} MB, " +
            $"dpi={dpi}, jpeg-quality={jpegQuality}, target={CCP_TARGET_UNSIGNED_PDF_BYTES / 1_000_000.0:F2} MB.");
    }

    private bool IsFileInUse(string filePath, out Exception? exception)
    {
        exception = null;
        if (File.Exists(filePath))
        {
            try
            {
                using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            catch (IOException ex)
            {
                exception = ex;
                return true;
            }
        }
        return false;
    }
}
