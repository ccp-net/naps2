using NAPS2.Pdf;
using Xunit;

namespace NAPS2.Lib.Tests.Pdf;

public class SavePdfOperationTests
{
    [Theory]
    [InlineData(1, 370_000L)]
    [InlineData(2, 870_000L)]
    [InlineData(5, 2_370_000L)]
    [InlineData(10, 4_870_000L)]
    [InlineData(100, 49_870_000L)]
    public void UnsignedPdfLimitReservesDigitalSignatureSpace(int pages, long expected)
    {
        Assert.Equal(expected, SavePdfOperation.GetMaxUnsignedPdfBytes(pages));
    }

    [Fact]
    public void CcpDpiLadderStopsAtTwoHundredDpi()
    {
        Assert.Equal(new[] { 300, 275, 250, 225, 200 }, SavePdfOperation.CCP_DPI_LEVELS);
        Assert.Equal(200, SavePdfOperation.CCP_DPI_LEVELS[^1]);
        Assert.Equal(500_000L, SavePdfOperation.CCP_MAX_BYTES_PER_PAGE);
        Assert.Equal(130_000L, SavePdfOperation.CCP_SIGNATURE_RESERVE_BYTES);
        Assert.Equal(40, SavePdfOperation.CCP_MIN_JPEG_QUALITY);
        Assert.Equal(92, SavePdfOperation.CCP_MAX_JPEG_QUALITY);
    }
}
