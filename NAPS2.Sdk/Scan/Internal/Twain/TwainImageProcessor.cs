#if !MAC
using Microsoft.Extensions.Logging;
using NAPS2.Images.Bitwise;
using NAPS2.Remoting.Worker;

namespace NAPS2.Scan.Internal.Twain;

/// <summary>
/// Run in the main 64-bit process, this class receives the Twain events from the remote Twain session in the worker
/// process and converts them into IMemoryImage objects and page progress events for the IScanDriver interface.
/// </summary>
internal class TwainImageProcessor : ITwainEvents, IDisposable
{
    private const double PAGE_SIZE_TOLERANCE_INCHES = 0.08;

    private readonly ScanningContext _scanningContext;
    private readonly ILogger _logger;
    private readonly Action<IMemoryImage> _callback;
    private readonly ScanOptions _options;
    private readonly bool _stretchRequestedByUser;
    private TwainImageData? _currentImageData;
    private IMemoryImage? _currentImage;
    private int _transferredWidth;
    private int _transferredHeight;
    private long _transferredPixels;
    private long _totalPixels;
    private readonly TwainProgressEstimator _progressEstimator;

    public TwainImageProcessor(ScanningContext scanningContext, ScanOptions options, IScanEvents scanEvents,
        Action<IMemoryImage> callback)
    {
        _scanningContext = scanningContext;
        _logger = scanningContext.Logger;
        _callback = callback;
        _options = options;
        _stretchRequestedByUser = options.StretchToPageSize;
        _progressEstimator = new TwainProgressEstimator(options, scanEvents);

        // Automatic paper-size mode uses the configured page size only as a fallback when the driver cannot report a
        // reliable physical size. Never override an explicit Stretch to page size choice made by the operator.
        if (_options.AutoPaperSize && _options.PageSize != null && !_stretchRequestedByUser)
        {
            _options.StretchToPageSize = true;
        }
    }

    public void PageStart(TwainPageStart pageStart)
    {
        Flush();
        _currentImageData = pageStart.ImageData;
        UpdatePageSizeFallback(pageStart);
        _currentImage?.Dispose();
        _currentImage = null;
        _transferredWidth = 0;
        _transferredHeight = 0;
        _transferredPixels = 0;
        _totalPixels = _currentImageData == null ? 0 : _currentImageData.Width * (long) _currentImageData.Height;
        _progressEstimator.MarkStart(_totalPixels);
    }

    private void UpdatePageSizeFallback(TwainPageStart pageStart)
    {
        if (!_options.AutoPaperSize || _options.PageSize == null)
        {
            return;
        }

        // The user's Advanced setting always wins. Automatic mixed-page normalization must never silently turn off a
        // Stretch to page size choice that the operator explicitly enabled.
        if (_stretchRequestedByUser)
        {
            _options.StretchToPageSize = true;
            return;
        }

        var imageData = pageStart.ImageData;
        if (imageData == null || imageData.XRes <= 0 || imageData.YRes <= 0)
        {
            // Native transfer or missing size metadata: keep the configured page-size normalization only as a fallback.
            _options.StretchToPageSize = true;
            _logger.LogDebug("NAPS2.TW - No reliable page-size metadata; keeping configured page-size normalization.");
            return;
        }

        double widthInches = imageData.Width / imageData.XRes;
        double heightInches = imageData.Height / imageData.YRes;
        double targetWidth = (double) _options.PageSize.WidthInInches;
        double targetHeight = (double) _options.PageSize.HeightInInches;

        bool matchesTarget =
            NearlyEqual(widthInches, targetWidth) && NearlyEqual(heightInches, targetHeight) ||
            NearlyEqual(widthInches, targetHeight) && NearlyEqual(heightInches, targetWidth);

        // If TWAIN returns a real size that differs from the configured fallback, automatic sizing is working and that
        // page keeps its detected physical dimensions. If the driver reports the fallback size, normalize to it.
        _options.StretchToPageSize = matchesTarget;

        if (matchesTarget)
        {
            _logger.LogDebug(
                "NAPS2.TW - Page size {Width:0.###}x{Height:0.###} in matches configured fallback; normalization enabled.",
                widthInches, heightInches);
        }
        else
        {
            _logger.LogDebug(
                "NAPS2.TW - Detected page size {Width:0.###}x{Height:0.###} in; preserving detected size.",
                widthInches, heightInches);
        }
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= PAGE_SIZE_TOLERANCE_INCHES;
    }

    public void NativeImageTransferred(TwainNativeImage nativeImage)
    {
        using var image = _scanningContext.ImageContext.Load(new MemoryStream(nativeImage.Buffer.ToByteArray()));
        _callback(image);
        _progressEstimator.MarkCompletion();
    }

    public void MemoryBufferTransferred(TwainMemoryBuffer memoryBuffer)
    {
        if (_currentImageData == null)
        {
            throw new InvalidOperationException();
        }
        if (memoryBuffer.Columns == 0 && memoryBuffer.BytesPerRow > 0)
        {
            // Workaround for bug with Kyocera drivers where Columns is unspecified
            memoryBuffer.Columns = memoryBuffer.BytesPerRow * 8 / _currentImageData.BitsPerPixel;
            _logger.LogDebug(
                "NAPS2.TW - Correcting memory buffer columns to {w} based on bytes/row {bpr}, bits/pixel {bpp}",
                memoryBuffer.Columns, memoryBuffer.BytesPerRow, _currentImageData.BitsPerPixel);
        }
        if (memoryBuffer.Columns <= 0 || memoryBuffer.Rows <= 0 || memoryBuffer.BytesPerRow <= 0)
        {
            var b = memoryBuffer;
            _logger.LogError(
                "NAPS2.TW - Invalid memory buffer: w {w}, h {h}, bpr {bpr}, len {len}, x {x}, y {y}",
                b.Columns, b.Rows, b.BytesPerRow, b.Buffer.Length, b.XOffset, b.YOffset);
            return;
        }

        _transferredPixels += memoryBuffer.Columns * (long) memoryBuffer.Rows;
        _transferredWidth = Math.Max(_transferredWidth, memoryBuffer.Columns + memoryBuffer.XOffset);
        _transferredHeight = Math.Max(_transferredHeight, memoryBuffer.Rows + memoryBuffer.YOffset);

        var pixelFormat = _currentImageData.BitsPerPixel == 1 ? ImagePixelFormat.BW1 : ImagePixelFormat.RGB24;
        _currentImage ??= _scanningContext.ImageContext.Create(
            Math.Max(_currentImageData.Width, _transferredWidth),
            Math.Max(_currentImageData.Height, _transferredHeight),
            pixelFormat);
        _currentImage.SetResolution((float) _currentImageData.XRes, (float) _currentImageData.YRes);

        // In case the real image dimensions don't match the specified image dimensions, we may need to get more memory.
        // The image will be realloc'd to the real size once we're done and know what that is.
        if (_transferredWidth > _currentImage.Width)
        {
            ReallocImage(Math.Max(_currentImage.Width * 2, _transferredWidth), _currentImage.Height);
        }
        if (_transferredHeight > _currentImage.Height)
        {
            ReallocImage(_currentImage.Width, Math.Max(_currentImage.Height * 2, _transferredHeight));
        }

        TwainMemoryBufferReader.CopyBufferToImage(memoryBuffer, _currentImageData, _currentImage);
        _progressEstimator.MarkProgress(Math.Min(_transferredPixels, _totalPixels), _totalPixels);
    }

    private void ReallocImage(int width, int height)
    {
        _logger.LogDebug($"NAPS2.TW - Realloc image {_currentImage!.Width}x{_currentImage.Height} -> {width}x{height}");
        var copy = _scanningContext.ImageContext.Create(width, height, _currentImage!.PixelFormat);
        new CopyBitwiseImageOp
        {
            Columns = Math.Min(width, _currentImage.Width),
            Rows = Math.Min(height, _currentImage.Height)
        }.Perform(_currentImage, copy);
        _currentImage.Dispose();
        _currentImage = copy;
    }

    public void TransferCanceled(TwainTransferCanceled transferCanceled)
    {
        _currentImage?.Dispose();
        _currentImage = null;
    }

    public void Flush()
    {
        if (_currentImage != null && _transferredWidth > 0 && _transferredHeight > 0)
        {
            if (_transferredWidth != _currentImage.Width || _transferredHeight != _currentImage.Height)
            {
                // The real image dimensions don't match the specified image dimensions, so we have to realloc.
                ReallocImage(_transferredWidth, _transferredHeight);
            }
            _progressEstimator.MarkCompletion();
            _callback(_currentImage);
            _currentImage = null;
        }
    }

    public void Dispose()
    {
        if (_currentImage != null && _transferredPixels == _totalPixels &&
            _transferredWidth == _currentImageData?.Width && _transferredHeight == _currentImageData?.Height)
        {
            // If we have an error after a successful scan (so Flush isn't called normally) we still want to flush.
            // Obviously this won't work if the image dimensions are off (as we can't tell if the scan is complete or
            // not) but that should be a rare case.
            Flush();
        }
        _currentImage?.Dispose();
    }
}
#endif