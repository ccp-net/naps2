using NAPS2.Pdf;
using Xunit;

namespace NAPS2.Lib.Tests.Pdf;

public class SavePdfOperationTests
{
    [Fact]
    public void WholeFileBudgetLeavesRoomBelowTwentyMb()
    {
        Assert.Equal(20_000_000L, SavePdfOperation.CCP_MAX_SIGNED_PDF_BYTES);
        Assert.Equal(19_000_000L, SavePdfOperation.CCP_TARGET_UNSIGNED_PDF_BYTES);
        Assert.Equal(130_000L, SavePdfOperation.CCP_SIGNATURE_RESERVE_BYTES);
        Assert.True(
            SavePdfOperation.CCP_TARGET_UNSIGNED_PDF_BYTES + SavePdfOperation.CCP_SIGNATURE_RESERVE_BYTES
            < SavePdfOperation.CCP_MAX_SIGNED_PDF_BYTES);
    }

    [Fact]
    public void PerPageBudgetLeavesSignatureHeadroomBelowOneMb()
    {
        Assert.Equal(1_000_000L, SavePdfOperation.CCP_MAX_SIGNED_PAGE_BYTES);
        Assert.Equal(860_000L, SavePdfOperation.CCP_TARGET_UNSIGNED_PAGE_BYTES);
        Assert.Equal(130_000L, SavePdfOperation.CCP_SIGNATURE_RESERVE_BYTES);
        Assert.True(
            SavePdfOperation.CCP_TARGET_UNSIGNED_PAGE_BYTES + SavePdfOperation.CCP_SIGNATURE_RESERVE_BYTES
            < SavePdfOperation.CCP_MAX_SIGNED_PAGE_BYTES);
    }

    [Fact]
    public void CcpDpiLadderStopsAtTwoHundredDpi()
    {
        Assert.Equal(new[] { 300, 275, 250, 225, 200 }, SavePdfOperation.CCP_DPI_LEVELS);
        Assert.Equal(200, SavePdfOperation.CCP_DPI_LEVELS[^1]);
        Assert.Equal(40, SavePdfOperation.CCP_MIN_JPEG_QUALITY);
        Assert.Equal(92, SavePdfOperation.CCP_MAX_JPEG_QUALITY);
    }
}
