using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.Ocr;

namespace NAPS2.Pdf;

internal class SavePdfOperation : OperationBase
{
    // CCP v0.2.16 upload targets.
    //
    // 1) CCP intentionally keeps the signed/uploaded PDF below a 20 MB operational target for faster upload, even
    //    though the receiving system now allows a larger total file. The unsigned file is kept at or below
    //    19,000,000 bytes, leaving about 1 MB headroom for the downstream digital signature.
    // 2) Each page must remain below 1 MB including the digital-signature allowance. The user's supplied signed samples
    //    added about 118 KB per file, so CCP reserves 130,000 bytes and targets each unsigned standalone page at
    //    <= 860,000 bytes. That gives a conservative projected signed-page ceiling of about 990 KB.
    //
    // Digital signatures are appended at file level, not literally to every page. Treating the full 130 KB reserve
    // against every standalone page is intentionally conservative and protects even a one-page PDF.
    internal const long CCP_MAX_SIGNED_PDF_BYTES = 20_000_000L;
    internal const long CCP_TARGET_UNSIGNED_PDF_BYTES = 19_000_000L;
    internal const long CCP_MAX_SIGNED_PAGE_BYTES = 1_000_000L;
    internal const long CCP_SIGNATURE_RESERVE_BYTES = 130_000L;
    internal const long CCP_TARGET_UNSIGNED_PAGE_BYTES = 860_000L;

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
        if (singleFile && File.Exists(subFileName))
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
                    result = await ExportCcpUploadSafePdf(
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

    private async Task<bool> ExportCcpUploadSafePdf(string fileName, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams, ProgressHandler progress)
    {
        var imageList = images.ToArray();
        if (imageList.Length == 0)
        {
            return false;
        }

        // Measure each page as a real one-page PDF. Pages already within the 860 KB unsigned target remain untouched;
        // only oversized pages receive explicit DPI/JPEG controls.
        var measureCache = new Dictionary<(int PageIndex, int Dpi, int Quality), long>();
        var plans = new CcpPagePlan[imageList.Length];

        for (int pageIndex = 0; pageIndex < imageList.Length; pageIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            Status.StatusText =
                $"Kiểm tra trang {pageIndex + 1}/{imageList.Length}: mục tiêu <1 MB sau ký số...";
            InvokeStatusChanged();

            plans[pageIndex] = await FindBestPagePlan(
                imageList[pageIndex],
                pageIndex,
                pdfSettings,
                ocrParams,
                CCP_TARGET_UNSIGNED_PAGE_BYTES,
                measureCache);
        }

        var tempFile = Path.Combine(Path.GetDirectoryName(fileName) ?? Paths.Temp,
            $".{Path.GetFileNameWithoutExtension(fileName)}.ccp-v0216-{Guid.NewGuid():N}.pdf");

        try
        {
            var success = await ExportWithPlans(tempFile, imageList, plans, pdfSettings, ocrParams);
            if (!success || !File.Exists(tempFile))
            {
                return false;
            }

            // Page constraints are already satisfied. If the whole file is still over 19 MB, selectively reduce the
            // largest pages until the document also meets the whole-file target.
            var pass = 0;
            var maxPasses = Math.Max(12, imageList.Length * 6);

            while (new FileInfo(tempFile).Length > CCP_TARGET_UNSIGNED_PDF_BYTES)
            {
                if (CancelToken.IsCancellationRequested)
                {
                    return false;
                }

                if (++pass > maxPasses)
                {
                    throw new InvalidOperationException(
                        "Không thể giảm PDF xuống dưới 19 MB trong giới hạn chất lượng tối thiểu đã cấu hình.");
                }

                var currentFileSize = new FileInfo(tempFile).Length;
                var excess = currentFileSize - CCP_TARGET_UNSIGNED_PDF_BYTES;
                Status.StatusText =
                    $"PDF {currentFileSize / 1_000_000.0:F1} MB - cần giảm thêm " +
                    $"{Math.Ceiling(excess / 1000.0):F0} KB để dưới 19 MB...";
                InvokeStatusChanged();

                var orderedPlans = plans.OrderByDescending(x => x.StandalonePdfBytes).ToArray();
                long plannedReduction = 0;
                int changedPages = 0;

                for (int position = 0; position < orderedPlans.Length && plannedReduction < excess + 40_000; position++)
                {
                    var currentPlan = orderedPlans[position];
                    var remaining = Math.Max(1L, excess + 40_000 - plannedReduction);
                    var pagesLeft = Math.Max(1, orderedPlans.Length - position);
                    var desiredReduction = Math.Max(20_000L,
                        (long) Math.Ceiling(remaining / (double) pagesLeft));
                    var targetBytes = Math.Max(100_000L, currentPlan.StandalonePdfBytes - desiredReduction);

                    try
                    {
                        var candidate = await FindBestPagePlan(
                            imageList[currentPlan.PageIndex],
                            currentPlan.PageIndex,
                            pdfSettings,
                            ocrParams,
                            targetBytes,
                            measureCache);

                        if (candidate.StandalonePdfBytes + 1_000 < currentPlan.StandalonePdfBytes)
                        {
                            plans[currentPlan.PageIndex] = candidate;
                            plannedReduction += currentPlan.StandalonePdfBytes - candidate.StandalonePdfBytes;
                            changedPages++;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // This page has reached the 200 DPI / quality-40 floor. Continue with other pages.
                    }
                }

                if (changedPages == 0)
                {
                    throw new InvalidOperationException(
                        "PDF vẫn lớn hơn 19 MB nhưng không thể giảm thêm mà vẫn giữ tối thiểu 200 DPI.");
                }

                success = await ExportWithPlans(tempFile, imageList, plans, pdfSettings, ocrParams);
                if (!success || !File.Exists(tempFile))
                {
                    return false;
                }
            }

            FileSystemHelper.EnsureParentDirExists(fileName);
            File.Copy(tempFile, fileName, true);
            progress.Report(imageList.Length, imageList.Length);

            var finalSize = new FileInfo(fileName).Length;
            Log.Info(
                $"CCP v0.2.16 PDF upload-safe export: pages={imageList.Length}, " +
                $"unsigned-size={finalSize} bytes, whole-file-target={CCP_TARGET_UNSIGNED_PDF_BYTES} bytes, " +
                $"page-target={CCP_TARGET_UNSIGNED_PAGE_BYTES} bytes, signature-reserve={CCP_SIGNATURE_RESERVE_BYTES} bytes.");

            foreach (var plan in plans)
            {
                var mode = plan.Options == null
                    ? "original"
                    : $"{plan.Options.TargetDpi}dpi/q{plan.Options.JpegQuality}";
                Log.Info(
                    $"CCP v0.2.16 page {plan.PageIndex + 1}: {mode}, " +
                    $"standalone-pdf={plan.StandalonePdfBytes} bytes.");
            }

            return true;
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

    private async Task<CcpPagePlan> FindBestPagePlan(ProcessedImage image, int pageIndex,
        PdfSettings pdfSettings, OcrParams ocrParams, long maxPageBytes,
        Dictionary<(int PageIndex, int Dpi, int Quality), long> measureCache)
    {
        var limit = Math.Min(CCP_TARGET_UNSIGNED_PAGE_BYTES, Math.Max(1, maxPageBytes));

        // A null option means preserve normal/original export behavior for this page.
        var originalSize = await MeasurePagePdfBytes(
            image, pageIndex, pdfSettings, ocrParams, null, measureCache);
        if (originalSize <= limit)
        {
            return new CcpPagePlan(pageIndex, null, originalSize);
        }

        foreach (var dpi in CCP_DPI_LEVELS)
        {
            var highOptions = new PdfPageExportOptions
            {
                TargetDpi = dpi,
                JpegQuality = CCP_MAX_JPEG_QUALITY
            };
            var highSize = await MeasurePagePdfBytes(
                image, pageIndex, pdfSettings, ocrParams, highOptions, measureCache);
            if (highSize <= limit)
            {
                return new CcpPagePlan(pageIndex, highOptions, highSize);
            }

            var lowOptions = new PdfPageExportOptions
            {
                TargetDpi = dpi,
                JpegQuality = CCP_MIN_JPEG_QUALITY
            };
            var lowSize = await MeasurePagePdfBytes(
                image, pageIndex, pdfSettings, ocrParams, lowOptions, measureCache);
            if (lowSize > limit)
            {
                continue;
            }

            var bestQuality = CCP_MIN_JPEG_QUALITY;
            var bestSize = lowSize;
            var low = CCP_MIN_JPEG_QUALITY + 1;
            var high = CCP_MAX_JPEG_QUALITY - 1;

            while (low <= high)
            {
                var mid = low + (high - low) / 2;
                var options = new PdfPageExportOptions
                {
                    TargetDpi = dpi,
                    JpegQuality = mid
                };
                var candidateSize = await MeasurePagePdfBytes(
                    image, pageIndex, pdfSettings, ocrParams, options, measureCache);

                if (candidateSize <= limit)
                {
                    bestQuality = mid;
                    bestSize = candidateSize;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return new CcpPagePlan(
                pageIndex,
                new PdfPageExportOptions
                {
                    TargetDpi = dpi,
                    JpegQuality = bestQuality
                },
                bestSize);
        }

        throw new InvalidOperationException(
            $"Trang {pageIndex + 1} không thể đạt mục tiêu {limit / 1000.0:F0} KB trước ký số " +
            $"mà vẫn giữ tối thiểu {CCP_DPI_LEVELS[^1]} DPI và JPEG quality {CCP_MIN_JPEG_QUALITY}.");
    }

    private async Task<long> MeasurePagePdfBytes(ProcessedImage image, int pageIndex, PdfSettings pdfSettings,
        OcrParams ocrParams, PdfPageExportOptions? options,
        Dictionary<(int PageIndex, int Dpi, int Quality), long> measureCache)
    {
        var key = (
            pageIndex,
            options?.TargetDpi ?? 0,
            options?.JpegQuality ?? 0);

        if (measureCache.TryGetValue(key, out var cachedSize))
        {
            return cachedSize;
        }

        using var stream = new MemoryStream();
        var exportParams = options == null
            ? CreatePdfExportParams(pdfSettings)
            : CreatePdfExportParams(pdfSettings, new PdfPageExportOptions?[] { options });

        var success = await _pdfExporter.Export(
            stream,
            new[] { image },
            exportParams,
            ocrParams,
            new ProgressHandler(null, CancelToken));

        if (!success)
        {
            throw new OperationCanceledException();
        }

        var size = stream.Length;
        measureCache[key] = size;
        return size;
    }

    private async Task<bool> ExportWithPlans(string fileName, ProcessedImage[] images, CcpPagePlan[] plans,
        PdfSettings pdfSettings, OcrParams ocrParams)
    {
        var pageOptions = plans
            .OrderBy(x => x.PageIndex)
            .Select(x => x.Options)
            .ToArray();

        return await _pdfExporter.Export(
            fileName,
            images,
            CreatePdfExportParams(pdfSettings, pageOptions),
            ocrParams,
            new ProgressHandler(null, CancelToken));
    }

    private static PdfExportParams CreatePdfExportParams(PdfSettings pdfSettings,
        IReadOnlyList<PdfPageExportOptions?>? pageOptions = null) =>
        new(pdfSettings.Metadata, pdfSettings.Encryption, pdfSettings.Compat)
        {
            PageOptions = pageOptions
        };

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

    private record CcpPagePlan(int PageIndex, PdfPageExportOptions? Options, long StandalonePdfBytes);
}
