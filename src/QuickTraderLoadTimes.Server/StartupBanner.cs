using System.Text;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using Spectre.Console;

namespace QuickTraderLoadTimes.Server;

/// <summary>
/// Says hello when the SPT server finishes loading: "QUICK TRADER" in ASCII-art letters on a
/// slanted amber-to-violet gradient, a "LOAD TIMES" speed line, whether the game-side plugin is
/// installed next to this server, and a line from a trader.
///
/// The art goes straight to Spectre.Console's AnsiConsole, the same console SPT's own console
/// logger writes to (SPTarkov.Common's ConsoleLogHandler calls AnsiConsole.MarkupLine), so it
/// lands in order: SPT's dispatcher is synchronous and its console format is just the message.
/// It does not go through ISptLogger because that escapes markup and allows one colour per line.
/// One plain line also goes through the logger, so the server log file records the mod too.
///
/// On a console narrower than the art, a one-line banner is used instead; where the console has
/// no colour, Spectre drops the colours by itself. Nothing here can stop the server: any failure
/// falls back to the plain line.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class StartupBanner(ISptLogger<StartupBanner> logger) : IOnLoad
{
    private const string Version = "1.0.0";

    private static readonly string[] Quips =
    {
        "Prapor already has the kettle on.",
        "Therapist has triaged your stash. It's stable.",
        "Skier swears the shelves were always this fast.",
        "Peacekeeper: time is money, and now you have more of both.",
        "Mechanic optimised the shelves. Twice.",
        "Ragman folded everything in advance.",
        "Jaeger heard you coming. The stock is already out.",
        "Fence did nothing. Fence never does anything. It's faster anyway.",
        "The flea market stopped dawdling.",
    };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        bool pluginFound = ClientPluginFound(out string pluginPath);
        try
        {
            Draw(pluginFound, pluginPath);
        }
        catch (Exception e)
        {
            logger.Debug("Quick Trader Load Times: banner skipped: " + e.Message);
        }
        logger.Info($"Quick Trader Load Times {Version} loaded" +
            (pluginFound ? "; game plugin found" : "; game plugin not found next to this server (BepInEx/plugins/QuickTraderLoadTimes)"));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Is the BepInEx plugin installed in the game folder this server sits in (SPT 4.x: the
    /// server is in SPT_Runtime, the game one folder up)? A server on another machine, a Fika
    /// dedicated host for example, won't find it, and that's fine.
    /// </summary>
    private static bool ClientPluginFound(out string path)
    {
        path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "BepInEx", "plugins",
            "QuickTraderLoadTimes", "QuickTraderLoadTimes.dll"));
        return File.Exists(path);
    }

    private static void Draw(bool pluginFound, string pluginPath)
    {
        IAnsiConsole console = AnsiConsole.Console;
        bool unicode = console.Profile.Capabilities.Unicode;
        List<string> art = Render(new FigletText("QUICK TRADER"));
        int width = BannerArt.Width(art);
        string streak = unicode ? "»" : ">";
        string streakBack = unicode ? "«" : "<";

        console.WriteLine();
        if (console.Profile.Width >= width + 4)
        {
            for (int row = 0; row < art.Count; row++)
            {
                console.MarkupLine("  " + Gradient(art[row], row, width, art.Count));
            }
            string subtitle = $"{Repeat(streak, 6)}  L O A D   T I M E S  {Repeat(streakBack, 6)}";
            int pad = Math.Max(0, (width - subtitle.Length) / 2);
            console.MarkupLine("  " + new string(' ', pad) + Gradient(subtitle, 0, subtitle.Length, 1)
                + $"  [grey]v{Version}[/]");
        }
        else
        {
            string line = $"{Repeat(streak, 3)} QUICK TRADER LOAD TIMES {Repeat(streakBack, 3)}";
            console.MarkupLine("  " + Gradient(line, 0, line.Length, 1) + $"  [grey]v{Version}[/]");
        }

        string tick = unicode ? "✔" : "+";
        console.MarkupLine(pluginFound
            ? $"  [#69F0AE]{tick}[/] [grey]game plugin found: traders and flea load fast[/]"
            : $"  [#FFD740]![/] [grey]game plugin not found next to this server: install the BepInEx part too[/]");
        if (!pluginFound)
        {
            console.MarkupLine("    [grey](fine if your game runs on another machine; looked for "
                + $"{Markup.Escape(Path.Combine("BepInEx", "plugins", "QuickTraderLoadTimes"))})[/]");
        }
        string quip = Quips[Random.Shared.Next(Quips.Length)];
        console.MarkupLine($"  [italic #B39DDB]{Markup.Escape(quip)}[/]");
        console.WriteLine();
    }

    /// <summary>Renders a widget to plain text on an off-screen console, then tidies the lines.</summary>
    private static List<string> Render(Spectre.Console.Rendering.IRenderable widget)
    {
        StringWriter writer = new StringWriter();
        IAnsiConsole offscreen = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        offscreen.Profile.Width = 400;
        offscreen.Write(widget);
        return BannerArt.Tidy(writer.ToString());
    }

    /// <summary>Each character in its own colour along the slanted gradient; spaces stay plain.</summary>
    private static string Gradient(string text, int row, int width, int rows)
    {
        StringBuilder sb = new StringBuilder();
        for (int x = 0; x < text.Length; x++)
        {
            char c = text[x];
            if (c == ' ')
            {
                sb.Append(' ');
                continue;
            }
            string hex = BannerArt.Hex(BannerArt.At(BannerArt.Diagonal(x, row, width, rows), BannerArt.Stops));
            sb.Append('[').Append(hex).Append(']').Append(Markup.Escape(c.ToString())).Append("[/]");
        }
        return sb.ToString();
    }

    private static string Repeat(string s, int n) => string.Concat(Enumerable.Repeat(s, n));
}
