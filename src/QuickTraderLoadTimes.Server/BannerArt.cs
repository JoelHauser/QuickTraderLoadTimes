namespace QuickTraderLoadTimes.Server;

/// <summary>
/// The startup line's colours, with no SPT or Spectre types so the test project can compile it.
/// </summary>
public static class BannerArt
{
    /// <summary>Amber to orange to hot pink to violet: "fast", left to right.</summary>
    public static readonly (byte R, byte G, byte B)[] Stops =
    {
        (0xFF, 0xC1, 0x07),
        (0xFF, 0x6F, 0x3C),
        (0xFF, 0x2E, 0x88),
        (0x8E, 0x5B, 0xFF),
    };

    /// <summary>The colour at t (0..1, clamped) along evenly spaced stops.</summary>
    public static (byte R, byte G, byte B) At(double t, (byte R, byte G, byte B)[] stops)
    {
        if (stops.Length == 1) return stops[0];
        t = Math.Clamp(t, 0, 1);
        double scaled = t * (stops.Length - 1);
        int i = Math.Min((int)scaled, stops.Length - 2);
        double f = scaled - i;
        (byte R, byte G, byte B) a = stops[i], b = stops[i + 1];
        return (Lerp(a.R, b.R, f), Lerp(a.G, b.G, f), Lerp(a.B, b.B, f));
    }

    private static byte Lerp(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);

    public static string Hex((byte R, byte G, byte B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
