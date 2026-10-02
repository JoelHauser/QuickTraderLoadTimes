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

    [Fact]
    public void DiagonalRunsZeroToOne()
    {
        Assert.Equal(0, BannerArt.Diagonal(0, 0, 10, 3));
        Assert.Equal(1, BannerArt.Diagonal(9, 2, 10, 3));
        Assert.True(BannerArt.Diagonal(0, 1, 10, 3) > BannerArt.Diagonal(0, 0, 10, 3));
    }

    [Fact]
    public void TidyDropsBlankEdgesAndSharedIndent()
    {
        List<string> lines = BannerArt.Tidy("\r\n\n   ab  \r\n    c\n\n");
        Assert.Equal(new[] { "ab", " c" }, lines);
        Assert.Equal(2, BannerArt.Width(lines));
    }

    [Fact]
    public void TidyOfNothingIsEmpty()
    {
        Assert.Empty(BannerArt.Tidy("\n \n"));
        Assert.Equal(0, BannerArt.Width(BannerArt.Tidy("")));
    }
}
