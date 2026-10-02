using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using UnityEngine;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// Fix candidate 4 (off unless QuestPanelOncePerFrame is on).
    ///
    /// Measured 2026-10-01 (0.4.0): SetQuestItemViewPanel ran 603 times for 201 new cells. ItemView.Init
    /// calls it, and so does GridItemView.UpdateInfo, which runs twice while a cell is set up. In vanilla
    /// that's cheap; with AllQuestsCheckmarks every call rebuilds the full quest tooltip (~0.5 ms).
    ///
    /// Only on the trader deal screen and the flea market, out of raid (Scope).
    ///
    /// The repeat is skipped when it is the same cell, showing the same item, in the same frame, with
    /// no inventory add/remove/refresh event since the first call: nothing the panel reads can have
    /// changed in between. Any later frame or inventory change runs it again as normal.
    /// </summary>
    internal static class QuestPanelOnce
    {
        private struct Seen
        {
            public Item Item;
            public int InventoryVersion;
        }

        private static int _frame = -1;
        private static readonly Dictionary<ItemView, Seen> SeenThisFrame = new Dictionary<ItemView, Seen>();

        public static int Skipped;

        public static bool Prefix(ItemView __instance)
        {
            if (!QuickTraderLoadTimesPlugin.QuestPanelOncePerFrame.Value || !Scope.Active) return true;

            int frame = Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                SeenThisFrame.Clear();
            }

            Item item = __instance.Item;
            int version = InventoryEvents.Version;
            if (SeenThisFrame.TryGetValue(__instance, out Seen seen)
                && ReferenceEquals(seen.Item, item)
                && seen.InventoryVersion == version)
            {
                Skipped++;
                return false;
            }
            SeenThisFrame[__instance] = new Seen { Item = item, InventoryVersion = version };
            return true;
        }
    }
}
