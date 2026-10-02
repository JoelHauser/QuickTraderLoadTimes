using EFT.InventoryLogic;
using HarmonyLib;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// A counter bumped on every item add, remove or refresh event any ItemController raises (the
    /// game routes all three through these Safe* methods). The fixes that share work between cells
    /// within a frame or a burst (QuestPanelOnce, AqcCompat, AqcQuestIndex) throw that work away when
    /// it changes. Over-counting (another player's controller in a Fika raid, say) only costs a
    /// recount, never a stale answer.
    /// </summary>
    internal static class InventoryEvents
    {
        public static int Version { get; private set; }

        public static void Apply(Harmony harmony)
        {
            HarmonyMethod bump = new HarmonyMethod(typeof(InventoryEvents), nameof(Bump));
            harmony.Patch(AccessTools.Method(typeof(ItemController), nameof(ItemController.SafeAddItemEventInvoke)), postfix: bump);
            harmony.Patch(AccessTools.Method(typeof(ItemController), nameof(ItemController.SafeRemoveItemEvent)), postfix: bump);
            harmony.Patch(AccessTools.Method(typeof(ItemController), nameof(ItemController.SafeRefreshItemEvent)), postfix: bump);
        }

        private static void Bump() => Version++;
    }
}
