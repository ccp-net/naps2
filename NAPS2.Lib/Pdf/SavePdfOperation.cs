using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.Ocr;

namespace NAPS2.Pdf;

internal class SavePdfOperation : OperationBase
{
    // CCP v0.2.14 rules:
    // - Every page must measure <= 500,000 bytes when exported as a standalone PDF page.
    // - Reserve 130,000 bytes per PDF file for the user's downstream digital signature. This value was calibrated from
    //   the supplied before/after-signing samples, where the signature append was ~118 KB/file.
    // - Prefer 300 dpi and only step down to 275/250/225/200 dpi. Never auto-reduce below 200 dpi.
    internal const long CCP_MAX_BYTES_PER_PAGE = 500_000L;
    internal const long CCP_SIGNATURE_RESERVE_BYTES = 130_000L;
    private const long CCP_PAGE_MEASURE_TARGET_BYTES = 495_000L;
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

    // TODO: Do something with this re: notifications?
    public string? FirstFileSaved { get; private set; }

    public bool Start(string fileName, Placeholders placeholders, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams, EmailMessage? emailMessage = null,
        string? overwriteFile = null)
    {
        // TODO: This needs tests. And ideally simplification.
        ProgressTitle = emailMessage != null ? MiscResources.EmailPdfProgress : MiscResources.SavePdfProgress;
        var subFileName = placeholders.Substitute(fileName);
        Status = new OperationStatus
        {
            StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(subFileName)),
            MaxProgress = images.Count
        };

        if (Directory.Exists(subFileName))
        {
            // Not supposed to be a directory, but ok...
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
                    result = await ExportCcpOptimizedPdf(
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
                if (emailMessage != null)
                {
                    Log.Event(EventType.Email, new EventParams
                    {
                        Name = MiscResources.EmailPdf,
                        Pages = images.Count,
                        FileFormat = ".pdf"
                    });
                }
                else
                {
                    Log.Event(EventType.SavePdf, new EventParams
                    {
                        Name = MiscResources.SavePdf,
                        Pages = images.Count,
                        FileFormat = ".pdf"
                    });
                }
            }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        return true;
    }

    private async Task<bool> ExportCcpOptimizedPdf(string fileName, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams, ProgressHandler progress)
    {
        var imageList = images.ToArray();
        if (imageList.Length == 0)
        {
            return false;
        }

        // Cache measured (page, DPI, JPEG quality) results. Signature-reserve rebalancing often revisits the same
        // candidates, so this avoids re-rendering/re-encoding identical page variants.
        var measureCache = new Dictionary<(int PageIndex, int Dpi, int Quality), long>();
        var plans = new CcpPagePlan[imageList.Length];
        for (int pageIndex = 0; pageIndex < imageList.Length; pageIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            Status.StatusText =
                $"Đang tối ưu trang {pageIndex + 1}/{imageList.Length} (≤500 KB, chừa 130 KB cho ký số)...";
            InvokeStatusChanged();
            plans[pageIndex] = await FindBestPagePlan(
                imageList[pageIndex], pageIndex, pdfSettings, ocrParams, CCP_PAGE_MEASURE_TARGET_BYTES, measureCache);
        }

        var tempFile = Path.Combine(Path.GetDirectoryName(fileName) ?? Paths.Temp,
            $".{Path.GetFileNameWithoutExtension(fileName)}.ccp-v0214-{Guid.NewGuid():N}.pdf");

        try
        {
            var success = await ExportWithPlans(tempFile, imageList, plans, pdfSettings, ocrParams);
            if (!success || !File.Exists(tempFile))
            {
                return false;
            }

            var maxUnsignedBytes = GetMaxUnsignedPdfBytes(imageList.Length);
            var optimizationPass = 0;
            var maxOptimizationPasses = Math.Max(12, imageList.Length * 4);

            while (new FileInfo(tempFile).Length > maxUnsignedBytes)
            {
                if (CancelToken.IsCancellationRequested)
                {
                    return false;
                }
                if (++optimizationPass > maxOptimizationPasses)
                {
                    throw new InvalidOperationException(
                        "Không thể tạo PDF đủ khoảng trống 130 KB cho ký số trong giới hạn 500 KB/trang.");
                }

                var currentFileSize = new FileInfo(tempFile).Length;
                var excess = currentFileSize - maxUnsignedBytes;
                Status.StatusText =
                    $"Đang chừa dung lượng ký số: cần giảm thêm {Math.Ceiling(excess / 1000.0):F0} KB...";
                InvokeStatusChanged();

                var reduced = false;
                foreach (var currentPlan in plans.OrderByDescending(x => x.SinglePagePdfBytes))
                {
                    var reductions = new[]
                    {
                        Math.Max(excess + 8_000L, 12_000L),
                        Math.Max(excess / 2, 15_000L),
                        10_000L
                    };

                    foreach (var reduction in reductions.Distinct())
                    {
                        var targetBytes = Math.Max(60_000L, currentPlan.SinglePagePdfBytes - reduction);
                        try
                        {
                            var candidate = await FindBestPagePlan(
                                imageList[currentPlan.PageIndex],
                                currentPlan.PageIndex,
                                pdfSettings,
                                ocrParams,
                                targetBytes,
                                measureCache);
                            if (candidate.SinglePagePdfBytes + 1_000 < currentPlan.SinglePagePdfBytes)
                            {
                                plans[currentPlan.PageIndex] = candidate;
                                reduced = true;
                                break;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // This target may be too aggressive for the 200 dpi / quality-40 floor. Try a smaller
                            // reduction or another page instead of silently going below the agreed quality floor.
                        }
                    }

                    if (reduced)
                    {
                        break;
                    }
                }

                if (!reduced)
                {
                    throw new InvalidOperationException(
                        "PDF chưa đủ khoảng trống cho ký số nhưng không thể giảm thêm mà vẫn giữ tối thiểu 200 DPI.");
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
                $"CCP v0.2.14 PDF optimized: {imageList.Length} page(s), unsigned={finalSize} bytes, " +
                $"unsigned-limit={maxUnsignedBytes} bytes, signature-reserve={CCP_SIGNATURE_RESERVE_BYTES} bytes.");
            foreach (var plan in plans)
            {
                Log.Info(
                    $"CCP v0.2.14 page {plan.PageIndex + 1}: dpi={plan.Dpi}, jpeg-quality={plan.JpegQuality}, " +
                    $"standalone-pdf={plan.SinglePagePdfBytes} bytes.");
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
        var limit = Math.Min(CCP_MAX_BYTES_PER_PAGE, Math.Max(1, maxPageBytes));

        foreach (var dpi in CCP_DPI_LEVELS)
        {
            var highQualitySize = await MeasurePagePdfBytes(
                image, pageIndex, pdfSettings, ocrParams, dpi, CCP_MAX_JPEG_QUALITY, measureCache);
            if (highQualitySize <= limit)
            {
                return new CcpPagePlan(pageIndex, dpi, CCP_MAX_JPEG_QUALITY, highQualitySize);
            }

            var lowQualitySize = await MeasurePagePdfBytes(
                image, pageIndex, pdfSettings, ocrParams, dpi, CCP_MIN_JPEG_QUALITY, measureCache);
            if (lowQualitySize > limit)
            {
                continue;
            }

            var bestQuality = CCP_MIN_JPEG_QUALITY;
            var bestSize = lowQualitySize;
            var low = CCP_MIN_JPEG_QUALITY + 1;
            var high = CCP_MAX_JPEG_QUALITY - 1;

            // JPEG output size is effectively monotonic with quality for scanned pages. Binary search finds the
            // highest quality that still satisfies the measured one-page PDF limit.
            while (low <= high)
            {
                var mid = low + (high - low) / 2;
                var candidateSize = await MeasurePagePdfBytes(image, pageIndex, pdfSettings, ocrParams, dpi, mid, measureCache);
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

            return new CcpPagePlan(pageIndex, dpi, bestQuality, bestSize);
        }

        throw new InvalidOperationException(
            $"Trang {pageIndex + 1} không thể đạt giới hạn {limit / 1000.0:F0} KB mà vẫn giữ tối thiểu " +
            $"{CCP_DPI_LEVELS[^1]} DPI và JPEG quality {CCP_MIN_JPEG_QUALITY}. Không tự động giảm chất lượng thấp hơn.");
    }

    private async Task<long> MeasurePagePdfBytes(ProcessedImage image, int pageIndex, PdfSettings pdfSettings,
        OcrParams ocrParams, int dpi, int jpegQuality,
        Dictionary<(int PageIndex, int Dpi, int Quality), long> measureCache)
    {
        var key = (pageIndex, dpi, jpegQuality);
        if (measureCache.TryGetValue(key, out var cachedSize))
        {
            return cachedSize;
        }

        using var stream = new MemoryStream();
        var pageOptions = new PdfPageExportOptions
        {
            TargetDpi = dpi,
            JpegQuality = jpegQuality
        };
        var success = await _pdfExporter.Export(
            stream,
            new[] { image },
            CreateCcpPdfExportParams(pdfSettings, new[] { pageOptions }),
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
            .Select(x => new PdfPageExportOptions
            {
                TargetDpi = x.Dpi,
                JpegQuality = x.JpegQuality
            })
            .ToArray();

        return await _pdfExporter.Export(
            fileName,
            images,
            CreateCcpPdfExportParams(pdfSettings, pageOptions),
            ocrParams,
            new ProgressHandler(null, CancelToken));
    }

    private static PdfExportParams CreateCcpPdfExportParams(PdfSettings pdfSettings,
        IReadOnlyList<PdfPageExportOptions> pageOptions) =>
        new(pdfSettings.Metadata, pdfSettings.Encryption, pdfSettings.Compat)
        {
            PageOptions = pageOptions
        };

    internal static long GetMaxUnsignedPdfBytes(int pageCount)
    {
        // There is no independent total-file cap. The only overall restriction is derived from 500,000 bytes per page
        // minus the empirically calibrated 130,000-byte reserve for the downstream digital signature.
        var pages = Math.Max(1, pageCount);
        return (long) pages * CCP_MAX_BYTES_PER_PAGE - CCP_SIGNATURE_RESERVE_BYTES;
    }

    private bool IsFileInUse(string filePath, out Exception? exception)
    {
        // TODO: Generalize this for images too
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

    private record CcpPagePlan(int PageIndex, int Dpi, int JpegQuality, long SinglePagePdfBytes);
}
