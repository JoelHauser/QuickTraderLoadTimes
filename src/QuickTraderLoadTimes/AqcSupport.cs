using System;
using BepInEx.Bootstrap;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// The AllQuestsCheckmarks fixes (AqcCompat, AqcQuestIndex) stand in for that mod's own code with
    /// the same logic, read from version 1.4.0. Another version may count or match differently, so
    /// they only switch on for 1.4.0; anything else keeps the mod's own code and says so in the log.
    /// </summary>
    internal static class AqcSupport
    {
        public const string Guid = "com.zgfuedkx.allquestscheckmarks";
        public static readonly Version Supported = new Version(1, 4, 0);

        /// <summary>Null when the fixes may run; otherwise the reason they don't.</summary>
        public static string WhyNot()
        {
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out BepInEx.PluginInfo info))
            {
                return "AllQuestsCheckmarks not installed";
            }
            Version v = info.Metadata.Version;
            if (v.Major != Supported.Major || v.Minor != Supported.Minor || v.Build != Supported.Build)
            {
                return $"AllQuestsCheckmarks {v} found; these fixes were built for {Supported} and stay off";
            }
            return null;
        }
    }
}
