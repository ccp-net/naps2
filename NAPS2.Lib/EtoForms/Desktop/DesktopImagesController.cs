using Microsoft.Extensions.Logging;
using NAPS2.Ocr;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Desktop;

public class DesktopImagesController
{
    private const double AUTO_ORIENTATION_MIN_CONFIDENCE = 0.82;
    private const int AUTO_ORIENTATION_MIN_SAMPLES = 3;
    private const int AUTO_ORIENTATION_MIN_WEIGHTED_CHARACTERS = 30;

    private readonly UiImageList _imageList;
    private readonly ScanningContext _scanningContext;
    private readonly DocumentOrientationDetector _orientationDetector;

    public DesktopImagesController(UiImageList imageList, ScanningContext scanningContext)
    {
        _imageList = imageList;
        _scanningContext = scanningContext;
        _orientationDetector = new DocumentOrientationDetector(scanningContext);
    }

    /// <summary>
    /// Constructs a receiver for scanned images.
    /// This keeps images from the same source together, even if multiple sources are providing images at the same time.
    /// </summary>
    /// <returns></returns>
    public Action<ProcessedImage> ReceiveScannedImage()
    {
        var lockObj = new object();
        UiImage? last = null;
        return scannedImage =>
        {
            UiImage uiImage;
            lock (lockObj)
            {
                uiImage = new UiImage(scannedImage);
                var shouldSelect = !_imageList.Selection.Any();
                _imageList.Mutate(new ImageListMutation.InsertAfter(uiImage, last), isPassiveInteraction: true);
                if (shouldSelect)
                {
                    _imageList.UpdateSelection(ListSelection.Of(uiImage));
                }
                last = uiImage;
            }

            // CCP v0.1.2: orientation analysis is deliberately asynchronous so scanner acquisition/ADF throughput is
            // not blocked by Tesseract. Blank separator candidates are skipped because they do not contain enough
            // meaningful text to orient reliably.
            if (!scannedImage.PostProcessingData.IsBlankPageCandidate)
            {
                _ = Task.Run(() => AutoOrient(uiImage));
            }
        };
    }

    private void AutoOrient(UiImage uiImage)
    {
        try
        {
            if (uiImage.IsDisposed)
            {
                return;
            }

            using var image = uiImage.GetClonedImage();
            using var rendered = image.Render();
            var result = _orientationDetector.Detect(rendered);
            if (!result.IsAvailable)
            {
                return;
            }

            bool confident = result.Confidence >= AUTO_ORIENTATION_MIN_CONFIDENCE &&
                             result.SampleCount >= AUTO_ORIENTATION_MIN_SAMPLES &&
                             result.WeightedCharacters >= AUTO_ORIENTATION_MIN_WEIGHTED_CHARACTERS;
            if (!confident)
            {
                _scanningContext.Logger.LogDebug(
                    "CCP Auto Orientation: keeping page unchanged (rotation {Rotation}, confidence {Confidence:P0}, lines {Lines}, chars {Chars}).",
                    result.RotationDegrees, result.Confidence, result.SampleCount, result.WeightedCharacters);
                return;
            }

            if (result.RotationDegrees == 0)
            {
                _scanningContext.Logger.LogDebug(
                    "CCP Auto Orientation: page already upright (confidence {Confidence:P0}).", result.Confidence);
                return;
            }

            Invoker.Current.InvokeDispatch(() =>
            {
                if (!uiImage.IsDisposed)
                {
                    uiImage.AddTransform(new RotationTransform(result.RotationDegrees));
                    _scanningContext.Logger.LogDebug(
                        "CCP Auto Orientation: rotated page {Rotation} degrees (confidence {Confidence:P0}, lines {Lines}, chars {Chars}).",
                        result.RotationDegrees, result.Confidence, result.SampleCount, result.WeightedCharacters);
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // The user may delete/clear a page while background analysis is still running.
        }
        catch (Exception ex)
        {
            // Auto Orientation must never interrupt the normal scan workflow.
            _scanningContext.Logger.LogDebug(ex, "CCP Auto Orientation failed; page left unchanged.");
        }
    }

    public void AppendImageBatch(IEnumerable<ProcessedImage> images)
    {
        _imageList.Mutate(
            new ImageListMutation.Append(images.Select(image => new UiImage(image))),
            isPassiveInteraction: true);
    }
}