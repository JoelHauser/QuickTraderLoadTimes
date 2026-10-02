using QuickTraderLoadTimes.Server;

public class BannerArtTests
{
    [Fact]
    public void GradientEndsAreTheFirstAndLastStops()
    {
        Assert.Equal(BannerArt.Stops[0], BannerArt.At(0, BannerArt.Stops));
        Assert.Equal(BannerArt.Stops[^1], BannerArt.At(1, BannerArt.Stops));
    }

    [Fact]
    public void GradientClampsOutOfRange()
    {
        Assert.Equal(BannerArt.Stops[0], BannerArt.At(-3, BannerArt.Stops));
        Assert.Equal(BannerArt.Stops[^1], BannerArt.At(7, BannerArt.Stops));
    }

    [Fact]
    public void GradientBlendsBetweenStops()
    {
        (byte, byte, byte)[] stops = { (0, 0, 0), (200, 100, 50) };
        Assert.Equal(((byte)100, (byte)50, (byte)25), BannerArt.At(0.5, stops));
    }

    [Fact]
    public void HexIsUpperCaseRgb()
    {
        Assert.Equal("#FFC107", BannerArt.Hex((0xFF, 0xC1, 0x07)));
    }
}
