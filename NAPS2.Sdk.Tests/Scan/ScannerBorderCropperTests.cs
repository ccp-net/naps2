using NAPS2.Images.Bitwise;
using NAPS2.Scan.Internal;
using Xunit;

namespace NAPS2.Sdk.Tests.Scan;

public class ScannerBorderCropperTests : ContextualTests
{
    [Fact]
    public void RemovesGreyAdfSideStrips()
    {
        using var image = CreateImage(1000, 1400, 100, (245, 245, 245));
        PaintRectangle(image, 0, 0, 18, 1400, (74, 76, 75));
        PaintRectangle(image, 978, 0, 22, 1400, (92, 92, 94));
        PaintRectangle(image, 120, 180, 760, 2, (25, 25, 25));

        var result = ScannerBorderCropper.Detect(image);

        Assert.NotNull(result);
        Assert.InRange(result!.Transform.Left, 17, 20);
        Assert.InRange(result.Transform.Right, 21, 24);
        Assert.Equal(0, result.Transform.Top);
        Assert.Equal(0, result.Transform.Bottom);
    }

    [Fact]
    public void RemovesPartiallyLitScannerStrip()
    {
        using var image = CreateImage(1000, 1400, 100, (248, 248, 248));
        // A real ADF edge is often not solid black. Alternate dark and medium-grey blocks
        // emulate reflected light and dust while retaining a continuous edge strip.
        for (int y = 0; y < 1400; y += 20)
        {
            PaintRectangle(image, 985, y, 15, 14, (70, 72, 71));
            PaintRectangle(image, 985, y + 14, 15, 6, (175, 175, 175));
        }

        var result = ScannerBorderCropper.Detect(image);

        Assert.NotNull(result);
        Assert.InRange(result!.Transform.Right, 14, 18);
    }

    [Fact]
    public void PreservesDarkColouredDossierPage()
    {
        using var image = CreateImage(1000, 1400, 100, (45, 105, 65));
        PaintRectangle(image, 0, 0, 20, 1400, (28, 70, 42));

        var result = ScannerBorderCropper.Detect(image);

        Assert.Null(result);
    }

    [Fact]
    public void DoesNotCropNormalWhitePageWithTextNearEdge()
    {
        using var image = CreateImage(1000, 1400, 100, (250, 250, 250));
        for (int y = 100; y < 1300; y += 45)
        {
            PaintRectangle(image, 12, y, 850, 2, (25, 25, 25));
        }

        var result = ScannerBorderCropper.Detect(image);

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
