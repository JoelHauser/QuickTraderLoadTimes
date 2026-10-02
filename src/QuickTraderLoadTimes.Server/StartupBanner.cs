using System.Text;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using Spectre.Console;

namespace QuickTraderLoadTimes.Server;

/// <summary>
/// One line when the SPT server finishes loading: "Quick Trader Load Times 1.0.0 loaded", the name
/// on an amber-to-violet gradient, with a short note on the same line if the game plugin isn't
/// installed next to this server. Kept to one line on purpose: a startup should not be loud.
///
/// The line goes straight to Spectre.Console's AnsiConsole, the console SPT's own console logger
/// writes to (SPTarkov.Common's ConsoleLogHandler calls AnsiConsole.MarkupLine), so it lands in
/// order: SPT's dispatcher is synchronous and its console format is just the message. It does not
/// go through ISptLogger, which escapes markup and allows one colour per line. If drawing fails for
/// any reason, the same line goes through the logger instead, in plain text.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class StartupBanner(ISptLogger<StartupBanner> logger) : IOnLoad
{
    private const string Version = "1.0.0";
    private const string Name = "Quick Trader Load Times";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        bool pluginFound = ClientPluginFound();
        try
        {
            AnsiConsole.Console.MarkupLine(Line(pluginFound));
        }
        catch (Exception)
        {
            logger.Info($"{Name} {Version} loaded" + (pluginFound ? "" : " (game plugin not found next to this server)"));
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Is the BepInEx plugin installed in the game folder this server sits in (SPT 4.x: the
    /// server is in SPT_Runtime, the game one folder up)? A server on another machine, a Fika
    /// dedicated host for example, won't find it, and that's fine.
    /// </summary>
    private static bool ClientPluginFound() =>
        File.Exists(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "BepInEx", "plugins",
            "QuickTraderLoadTimes", "QuickTraderLoadTimes.dll")));

    private static string Line(bool pluginFound) =>
        Gradient(Name) + $" [grey]{Version} loaded[/]" +
        (pluginFound ? "" : " [grey](game plugin not found next to this server)[/]");

    /// <summary>Each character in its own colour along the gradient; spaces stay plain.</summary>
    private static string Gradient(string text)
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
            double t = text.Length <= 1 ? 0 : (double)x / (text.Length - 1);
            sb.Append('[').Append(BannerArt.Hex(BannerArt.At(t, BannerArt.Stops))).Append(']')
                .Append(Markup.Escape(c.ToString())).Append("[/]");
        }
        return sb.ToString();
    }
}
