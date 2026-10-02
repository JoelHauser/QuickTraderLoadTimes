using Hurryitup;

public class SeriesTests
{
    private static Series Of(params double[] values)
    {
        Series s = new Series();
        foreach (double v in values) s.Add(v);
        return s;
    }

    [Fact]
    public void EmptySeriesReportsZeroes()
    {
        Series s = new Series();
        Assert.Equal(0, s.Percentile(50));
        Assert.Equal(0, s.Max);
        Assert.Equal(0, s.Mean);
        Assert.Equal("n=0", s.Describe());
    }

    [Theory]
    [InlineData(50, 2)]
    [InlineData(75, 3)]
    [InlineData(95, 4)]
    [InlineData(100, 4)]
    [InlineData(0, 1)]
    public void PercentileIsNearestRankOnSortedSamples(double p, double expected)
    {
        Assert.Equal(expected, Of(4, 1, 3, 2).Percentile(p));
    }

    [Fact]
    public void PercentileIsAlwaysOneOfTheSamples()
    {
        Series s = Of(16.7, 16.6, 120.4, 17.0, 16.9);
        Assert.Equal(16.9, s.Percentile(50));
        Assert.Equal(120.4, s.Percentile(95));
    }

    [Fact]
    public void CountOverIsStrictlyGreater()
    {
        Series s = Of(33, 33.1, 50, 51, 10);
        Assert.Equal(3, s.CountOver(33));
        Assert.Equal(1, s.CountOver(50));
    }

    [Fact]
    public void SumAndMean()
    {
        Series s = Of(1, 2, 3, 4);
        Assert.Equal(10, s.Sum);
        Assert.Equal(2.5, s.Mean);
        Assert.Equal("n=4 sum 10.0 avg 2.5 p50 2.0 p95 4.0 max 4.0", s.Describe());
    }
}

public class FormatTests
{
    [Fact]
    public void MillisecondsUseInvariantCulture()
    {
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1234.6", Fmt.Ms(1234.56));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void MissingTimesAreBlankOrLabelled()
    {
        Assert.Equal("", Fmt.MaybeMs(null));
        Assert.Equal("never", Fmt.MaybeMs(null, "never"));
        Assert.Equal("5.0", Fmt.MaybeMs(5));
    }

    [Fact]
    public void SignedMegabytes()
    {
        Assert.Equal("+1.5", Fmt.SignedMb(1572864));
        Assert.Equal("-2.0", Fmt.SignedMb(-2097152));
        Assert.Equal("+0.0", Fmt.SignedMb(0));
    }
}

public class CsvTests
{
    [Theory]
    [InlineData("Prapor", "Prapor")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData(null, "")]
    public void EscapesOnlyWhenNeeded(string? input, string expected)
    {
        Assert.Equal(expected, Csv.Escape(input!));
    }

    [Fact]
    public void RowJoinsEscapedFields()
    {
        Assert.Equal("a,\"b,c\",", Csv.Row(new[] { "a", "b,c", "" }));
    }
}
