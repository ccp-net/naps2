using NAPS2.Images.Bitwise;
using NAPS2.Scan.Internal;
using Xunit;

namespace NAPS2.Sdk.Tests.Scan;

public class SafeAutoCropperTests : ContextualTests
{
    [Fact]
    public void DetectsClearlySmallerColouredPaperOnFallbackCanvas()
    {
        using var image = CreateImage(1000, 1400, 100, (255, 255, 255));
        PaintRectangle(image, 250, 300, 500, 700, (220, 196, 145));

        var result = SafeAutoCropper.Detect(image, new PageSize(10m, 14m, PageSizeUnit.Inch));

        Assert.NotNull(result);
        Assert.Equal("smaller-paper", result.Reason);
        Assert.InRange(result.Transform.Left, 235, 245);
        Assert.InRange(result.Transform.Right, 235, 245);
        Assert.InRange(result.Transform.Top, 285, 295);
        Assert.InRange(result.Transform.Bottom, 385, 395);
        Assert.NotNull(result.DetectedPageSize);
        Assert.InRange(result.DetectedPageSize!.WidthInInches, 5.1m, 5.3m);
        Assert.InRange(result.DetectedPageSize.HeightInInches, 7.1m, 7.3m);
    }

    [Fact]
    public void DetectsColouredPaperTouchingTopLeftDespiteNoiseInBlankCanvas()
    {
        using var image = CreateImage(1000, 1400, 100, (255, 255, 255));
        PaintRectangle(image, 0, 0, 600, 850, (0, 145, 125));

        // Simulate faint/dark flatbed dust and gradients below the actual sheet. The old 0.6% line threshold treated
        // this low-density noise as content all the way to the A4 boundary and rejected the crop.
        for (int y = 900; y < 1400; y += 2)
        {
            PaintRectangle(image, 20, y, 20, 1, (225, 225, 225));
        }

        var result = SafeAutoCropper.Detect(image, new PageSize(10m, 14m, PageSizeUnit.Inch));

        Assert.NotNull(result);
        Assert.Equal("smaller-paper", result.Reason);
        Assert.Equal(0, result.Transform.Left);
        Assert.InRange(result.Transform.Right, 385, 395);
        Assert.Equal(0, result.Transform.Top);
        Assert.InRange(result.Transform.Bottom, 535, 545);
        Assert.NotNull(result.DetectedPageSize);
        Assert.InRange(result.DetectedPageSize!.WidthInInches, 6.0m, 6.2m);
        Assert.InRange(result.DetectedPageSize.HeightInInches, 8.5m, 8.7m);
    }

    [Fact]
    public void PreservesOrdinaryA4TextMargins()
    {
        using var image = CreateImage(1000, 1400, 100, (255, 255, 255));
        for (int y = 180; y < 1220; y += 35)
        {
            PaintRectangle(image, 100, y, 800, 2, (25, 25, 25));
        }

        var result = SafeAutoCropper.Detect(image, new PageSize(10m, 14m, PageSizeUnit.Inch));

        Assert.Null(result);
    }

    [Fact]
    public void RemovesNeutralBlackScannerBorders()
    {
        using var image = CreateImage(1000, 1400, 100, (255, 255, 255));
        PaintRectangle(image, 0, 0, 6, 1400, (12, 12, 12));
        PaintRectangle(image, 994, 0, 6, 1400, (12, 12, 12));
        PaintRectangle(image, 0, 0, 1000, 5, (12, 12, 12));

        var result = SafeAutoCropper.Detect(image, new PageSize(10m, 14m, PageSizeUnit.Inch));

        Assert.NotNull(result);
        Assert.Equal("neutral-black-border", result.Reason);
        Assert.InRange(result.Transform.Left, 5, 7);
        Assert.InRange(result.Transform.Right, 5, 7);
        Assert.InRange(result.Transform.Top, 4, 6);
        Assert.Equal(0, result.Transform.Bottom);
    }

    [Fact]
    public void DoesNotTreatDarkColouredPageAsBlackBorder()
    {
        using var image = CreateImage(1000, 1400, 100, (20, 90, 40));

        var result = SafeAutoCropper.Detect(image, new PageSize(10m, 14m, PageSizeUnit.Inch));

        Assert.Null(result);
    }

    [Fact]
    public void DoesNotContentCropWhenDriverAlreadyReturnedPhysicalSize()
    {
        using var image = CreateImage(600, 900, 100, (255, 255, 255));
        PaintRectangle(image, 140, 180, 320, 480, (215, 190, 140));

        var result = SafeAutoCropper.Detect(image, PageSize.A4);

        Assert.Null(result);
    }

    private IMemoryImage CreateImage(int width, int height, float dpi, (byte r, byte g, byte b) colour)
    {
        var image = ImageContext.Create(width, height, ImagePixelFormat.RGB24);
        image.SetResolution(dpi, dpi);
        new FillColorImageOp(colour.r, colour.g, colour.b, 255).Perform(image);
        return image;
    }

    private static unsafe void PaintRectangle(IMemoryImage image, int x, int y, int width, int height,
        (byte r, byte g, byte b) colour)
    {
        using var imageLock = image.Lock(LockMode.ReadWrite, out var data);
        int x1 = Math.Min(data.w, x + width);
        int y1 = Math.Min(data.h, y + height);
        for (int row = Math.Max(0, y); row < y1; row++)
        {
            var rowPtr = data.ptr + data.stride * row;
            for (int column = Math.Max(0, x); column < x1; column++)
            {
                var pixel = rowPtr + data.bytesPerPixel * column;
                *(pixel + data.rOff) = colour.r;
                *(pixel + data.gOff) = colour.g;
                *(pixel + data.bOff) = colour.b;
            }
        }
    }
}
