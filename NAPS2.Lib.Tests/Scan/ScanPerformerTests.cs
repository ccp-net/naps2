using NAPS2.Config;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

public class ScanPerformerTests
{
    // Note that these tests aren't meant to be comprehensive scanning tests. End-to-end scanning functionality is
    // verified through CommandLineIntegrationTests. These tests should focus on things that can't be verified or are
    // difficult to verify through those end-to-end tests.

    [Theory]
    [InlineData(ScanSource.Glass, false)]
    [InlineData(ScanSource.Feeder, false)]
    [InlineData(ScanSource.DuplexBook, false)]
    [InlineData(ScanSource.Duplex, true)]
    public void DuplexBindingMapsToExpectedBackPageRotation(ScanSource source, bool shouldFlip)
    {
        Assert.Equal(shouldFlip, ScanPerformer.ShouldFlipDuplexBackPages(source));
    }

    [Fact]
    public void DefaultProfileUsesRicohFi8250UPaperStreamFriendlySettings()
    {
        var profile = InternalDefaults.GetCommonConfig().DefaultProfileSettings;

        Assert.Equal("twain", profile.DriverName);
        Assert.Equal(TwainImpl.X64, profile.TwainImpl);
        Assert.False(profile.UseNativeUI);
        Assert.Equal(ScanSource.DuplexBook, profile.PaperSource);
        Assert.Equal(ScanBitDepth.C24Bit, profile.BitDepth);
        Assert.Equal(300, profile.Resolution.Dpi);
        Assert.Equal(ScanHorizontalAlign.Center, profile.PageAlign);
        Assert.True(profile.AutoPaperSize);
        Assert.True(profile.AutoDeskew);
        Assert.Equal(0, profile.Brightness);
        Assert.Equal(0, profile.Contrast);
        Assert.False(profile.BrightnessContrastAfterScan);
        Assert.False(profile.ExcludeBlankPages);
    }
}