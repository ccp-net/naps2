using Microsoft.Extensions.Logging;
using NAPS2.Images.Bitwise;

namespace NAPS2.Scan.Internal;

internal class RemotePostProcessor : IRemotePostProcessor
{
    private const int CCP_QC_FAINT_CONTENT_WHITE_THRESHOLD = 85;
    private const int CCP_QC_FAINT_CONTENT_COVERAGE_THRESHOLD = 3;
    private const int CCP_QC_DARK_PIXEL_WHITE_THRESHOLD = 20;
    private const double CCP_QC_DARK_PAGE_COVERAGE_THRESHOLD = 0.85;

    private readonly ScanningContext _scanningContext;
    private readonly ILogger _logger;

    public RemotePostProcessor(ScanningContext scanningContext)
    {
        _scanningContext = scanningContext;
        _logger = scanningContext.Logger;
    }

    public ProcessedImage? PostProcess(IMemoryImage image, ScanOptions options,
        PostProcessingContext postProcessingContext)
    {
        image = DoInitialTransforms(image, options);
        try
        {
            var blankOp = new BlankDetectionImageOp(options.BlankPageWhiteThreshold, options.BlankPageCoverageThreshold);
            blankOp.Perform(image);
            if (options.ExcludeBlankPages && blankOp.IsBlank)
            {
                return null;
            }

            bool isBlankPageCandidate = false;
            double qcCoverage = blankOp.Coverage;
            if (blankOp.IsBlank)
            {
                var faintContentOp = new BlankDetectionImageOp(
                    Math.Max(options.BlankPageWhiteThreshold, CCP_QC_FAINT_CONTENT_WHITE_THRESHOLD),
                    Math.Min(options.BlankPageCoverageThreshold, CCP_QC_FAINT_CONTENT_COVERAGE_THRESHOLD));
                faintContentOp.Perform(image);
                isBlankPageCandidate = faintContentOp.IsBlank;
                qcCoverage = faintContentOp.Coverage;
            }

            // Red QC should represent a genuinely near-black/badly exposed scan, not simply a page whose background is
            // non-white. Old Party dossier paper is often yellow/brown across nearly the entire page, so using the normal
            // blank detector's non-white coverage produced false red warnings. This second pass counts only very dark
            // pixels (roughly luma < 52/255) and flags the page only when at least 85% of the whole image is that dark.
            var darkOp = new BlankDetectionImageOp(CCP_QC_DARK_PIXEL_WHITE_THRESHOLD, 100);
            darkOp.Perform(image);
            bool isDarkPageCandidate = darkOp.Coverage >= CCP_QC_DARK_PAGE_COVERAGE_THRESHOLD;

            var scannedImage = _scanningContext.CreateProcessedImage(image, options.MaxQuality,
                options.Quality, options.PageSize);
            DoRevertibleTransforms(ref scannedImage, ref image, options, postProcessingContext,
                isBlankPageCandidate, qcCoverage, isDarkPageCandidate);
            postProcessingContext.TempPath = SaveForBackgroundOcr(image, options);
            return scannedImage;
        }
        finally
        {
            image.Dispose();
        }
    }

    private IMemoryImage DoInitialTransforms(IMemoryImage original, ScanOptions options)
    {
        if (!options.UseNativeUI && options.BitDepth == BitDepth.BlackAndWhite)
        {
            original = original.PerformTransform(new BlackWhiteTransform(-options.Brightness));
        }

        var scaled = original;
        if (!options.UseNativeUI && options.ScaleRatio > 1)
        {
            var scaleFactor = 1.0 / options.ScaleRatio;
            scaled = scaled.PerformTransform(new ScaleTransform(scaleFactor));
        }

        if (!options.UseNativeUI && (options.StretchToPageSize || options.CropToPageSize))
        {
            scaled = CropAndStretch(original, options, scaled);
        }

        return scaled;
    }

    private IMemoryImage CropAndStretch(IMemoryImage original, ScanOptions options, IMemoryImage scaled)
    {
        if (original.HorizontalResolution <= 0 || original.VerticalResolution <= 0)
        {
            _logger.LogDebug("Skipping StretchToPageSize/CropToPageSize as there is no resolution data");
            return scaled;
        }

        float width = original.Width / original.HorizontalResolution;
        float height = original.Height / original.VerticalResolution;

        if ((options.PageSize!.Width > options.PageSize.Height) ^ (width > height))
        {
            if (options.CropToPageSize)
            {
                scaled = scaled.PerformTransform(new CropTransform(
                    0,
                    (int) ((width - (float) options.PageSize.HeightInInches) * original.HorizontalResolution),
                    0,
                    (int) ((height - (float) options.PageSize.WidthInInches) * original.VerticalResolution)
                ));
            }
            else
            {
                scaled.SetResolution((float) (original.Width / options.PageSize.HeightInInches),
                    (float) (original.Height / options.PageSize.WidthInInches));
            }
        }
        else
        {
            if (options.CropToPageSize)
            {
                scaled = scaled.PerformTransform(new CropTransform
                (
                    0,
                    (int) ((width - (float) options.PageSize.WidthInInches) * original.HorizontalResolution),
                    0,
                    (int) ((height - (float) options.PageSize.HeightInInches) * original.VerticalResolution)
                ));
            }
            else
            {
                scaled.SetResolution((float) (original.Width / options.PageSize.WidthInInches),
                    (float) (original.Height / options.PageSize.HeightInInches));
            }
        }
        return scaled;
    }

    private void DoRevertibleTransforms(ref ProcessedImage processedImage, ref IMemoryImage image, ScanOptions options,
        PostProcessingContext postProcessingContext, bool isBlankPageCandidate, double blankPageCoverage,
        bool isDarkPageCandidate)
    {
        var data = processedImage.PostProcessingData with
        {
            PageNumber = postProcessingContext.PageNumber,
            IsBlankPageCandidate = isBlankPageCandidate,
            BlankPageCoverage = blankPageCoverage,
            IsDarkPageCandidate = isDarkPageCandidate
        };

        if ((!options.UseNativeUI && options.BrightnessContrastAfterScan) ||
            options.Driver is not (Driver.Wia or Driver.Twain))
        {
            processedImage = processedImage.WithTransform(new BrightnessTransform(options.Brightness), true);
            processedImage = processedImage.WithTransform(new TrueContrastTransform(options.Contrast), true);
        }

        if (options.PaperSource == PaperSource.Duplex)
        {
            data = data with
            {
                PageSide = postProcessingContext.PageNumber % 2 == 0 ? PageSide.Back : PageSide.Front
            };
            if (options.FlipDuplexedPages && data.PageSide == PageSide.Back)
            {
                processedImage = processedImage.WithTransform(new RotationTransform(180), true);
            }
        }

        if (options.RotateDegrees != 0)
        {
            processedImage = processedImage.WithTransform(new RotationTransform(options.RotateDegrees), true);
        }

        // v0.2.11: straighten first, then detect scanner edge strips on the geometry the user will actually see.
        // This improves HP/ADF scans where a slightly skewed page makes a dark side strip discontinuous in raw pixels.
        if (options.AutoDeskew)
        {
            processedImage = processedImage.WithTransform(Deskewer.GetDeskewTransform(image), true);
        }

        if (options.AutoPaperSize)
        {
            ApplyScannerBorderCleanup(ref processedImage, image);
            ApplySafeAutoCrop(ref processedImage, image, options);
        }

        if (!data.Barcode.IsDetected)
        {
            data = data with
            {
                Barcode = BarcodeDetector.Detect(image, options.BarcodeDetectionOptions)
            };
        }
        if (options.ThumbnailSize.HasValue)
        {
            data = data with
            {
                Thumbnail = image.Clone()
                    .PerformAllTransforms(processedImage.TransformState.Transforms)
                    .PerformTransform(new ThumbnailTransform(options.ThumbnailSize.Value)),
                ThumbnailTransformState = processedImage.TransformState
            };
        }
        processedImage = processedImage.WithPostProcessingData(data, true);
    }

    private void ApplyScannerBorderCleanup(ref ProcessedImage processedImage, IMemoryImage sourceImage)
    {
        try
        {
            using var detectionImage = sourceImage.Clone()
                .PerformAllTransforms(processedImage.TransformState.Transforms);
            var result = ScannerBorderCropper.Detect(detectionImage);
            if (result == null)
            {
                _logger.LogDebug("CCP Border Cleanup - no scanner edge strip detected.");
                return;
            }

            processedImage = processedImage.WithTransform(result.Transform, true);
            _logger.LogInformation(
                "CCP Border Cleanup - applied adaptive edge crop: L={Left}, R={Right}, T={Top}, B={Bottom}; interior luma={InteriorLuma}.",
                result.Transform.Left, result.Transform.Right, result.Transform.Top, result.Transform.Bottom,
                result.InteriorLuma);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CCP Border Cleanup failed; preserving the full scanned page.");
        }
    }

    private void ApplySafeAutoCrop(ref ProcessedImage processedImage, IMemoryImage sourceImage, ScanOptions options)
    {
        try
        {
            // Run detection against the same geometry the operator sees in the thumbnail. Crop remains a reversible
            // transform, so a rare false positive can be undone without rescanning the original page.
            using var detectionImage = sourceImage.Clone()
                .PerformAllTransforms(processedImage.TransformState.Transforms);
            var result = SafeAutoCropper.Detect(detectionImage, options.PageSize);
            if (result == null)
            {
                _logger.LogDebug("CCP Safe Auto Crop - no high-confidence crop detected; preserving full scan.");
                return;
            }

            processedImage = processedImage.WithTransform(result.Transform, true);
            if (result.DetectedPageSize != null)
            {
                processedImage = processedImage.WithMetadata(
                    new ImageMetadata(processedImage.Metadata.Lossless, result.DetectedPageSize), true);
            }
            _logger.LogInformation(
                "CCP Safe Auto Crop - applied {Reason}: L={Left}, R={Right}, T={Top}, B={Bottom}.",
                result.Reason, result.Transform.Left, result.Transform.Right, result.Transform.Top,
                result.Transform.Bottom);
        }
        catch (Exception ex)
        {
            // Cropping is optional. A driver-specific image format or detector error must never block delivery of the
            // original dossier page.
            _logger.LogWarning(ex, "CCP Safe Auto Crop failed; preserving the full scanned page.");
        }
    }

    private string? SaveForBackgroundOcr(IMemoryImage bitmap, ScanOptions options)
    {
        if (!string.IsNullOrEmpty(options.OcrParams.LanguageCode))
        {
            return _scanningContext.SaveToTempFile(bitmap);
        }
        return null;
    }
}
