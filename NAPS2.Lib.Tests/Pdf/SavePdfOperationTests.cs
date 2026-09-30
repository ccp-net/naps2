using NAPS2.Pdf;
using Xunit;

namespace NAPS2.Lib.Tests.Pdf;

public class SavePdfOperationTests
{
    [Fact]
    public void WholeFilePdfLimitTargetsUnderNineteenMb()
    {
        Assert.Equal(19_000_000L, SavePdfOperation.CCP_MAX_UNSIGNED_PDF_BYTES);
        Assert.Equal(18_900_000L, SavePdfOperation.CCP_TARGET_UNSIGNED_PDF_BYTES);
        Assert.True(SavePdfOperation.CCP_TARGET_UNSIGNED_PDF_BYTES < SavePdfOperation.CCP_MAX_UNSIGNED_PDF_BYTES);
    }

    [Fact]
    public void WholeFileOptimizerKeepsCommonDpiFloorAtTwoHundred()
    {
        Assert.Equal(new[] { 300, 275, 250, 225, 200 }, SavePdfOperation.CCP_DPI_LEVELS);
        Assert.Equal(200, SavePdfOperation.CCP_DPI_LEVELS[^1]);
        Assert.Equal(40, SavePdfOperation.CCP_MIN_JPEG_QUALITY);
        Assert.Equal(92, SavePdfOperation.CCP_MAX_JPEG_QUALITY);
    }
}
