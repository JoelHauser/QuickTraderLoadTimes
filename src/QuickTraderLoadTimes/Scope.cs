using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Ragfair;
using UnityEngine;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// Where the fixes are allowed to act: while the trader deal screen or the flea market screen is
    /// open (the flea's Add Offer window sits on top of the flea screen, so it counts), and never in
    /// a raid, including the BTR driver's trader in raid. Everywhere else the game's and other mods'
    /// own code runs untouched.
    ///
    /// This is decided per screen, not per cell: a new cell comes from a pool and is only parented
    /// under its grid after it has been set up, so asking whether a cell sits under the trader screen
    /// would say no exactly when it matters. EFT shows one main screen at a time.
    /// </summary>
    internal static class Scope
    {
        /// <summary>The deal screen last shown (TraderDealScreen.Show prefix).</summary>
        public static TraderDealScreen DealScreen;

        /// <summary>The flea market screen last shown (RagfairScreen.Show prefix).</summary>
        public static RagfairScreen FleaScreen;

        /// <summary>Same check AllQuestsCheckmarks uses for "in raid".</summary>
        public static bool InRaid => Singleton<AbstractGame>.Instance?.InRaid ?? false;

        public static bool TraderScreenOpen => IsOpen(DealScreen);

        /// <summary>True only on the trader deal screen or the flea market, out of raid.</summary>
        public static bool Active => !InRaid && (IsOpen(DealScreen) || IsOpen(FleaScreen));

        private static bool IsOpen(Component screen) => screen != null && screen.gameObject.activeInHierarchy;
    }
}
