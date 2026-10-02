using SPTarkov.Server.Core.Models.Spt.Mod;

namespace QuickTraderLoadTimes.Server;

/// <summary>
/// The mod's one piece of metadata. SPT's ModLoader runs SingleOrDefault over the types
/// implementing IModMetadata in a folder, so there is exactly one of these.
/// </summary>
public record ModMetadata : IModMetadata
{
    /// <summary>Shared with the BepInEx plugin's [BepInPlugin] so the two halves read as one mod.</summary>
    public string ModGuid { get; init; } = "com.mybutthasarash.quicktraderloadtimes";

    public string Name { get; init; } = "Quick Trader Load Times";

    public string Author { get; init; } = "JoelHauser";

    public List<string>? Contributors { get; init; }

    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");

    /// <summary>
    /// A hard gate: a mod outside the range loads nothing and logs nothing. "~4.1.0" is
    /// &gt;=4.1.0 &lt;4.2.0. The server half only prints a banner, so it is safe on any 4.1.x.
    /// </summary>
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }

    public string? Url { get; init; } = "https://github.com/JoelHauser/Hurryitup";

    public string License { get; init; } = "MIT";

    public bool HasPrepatcher { get; init; }
}
