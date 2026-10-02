using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Utils;
using UnityEngine;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// Fix candidate 3 (off unless AqcStashCountCache is on): AllQuestsCheckmarks 1.4.0 compatibility.
    ///
    /// Measured 2026-10-01 (0.3.0): building one item cell costs 2.5-5 ms, and ~70% of that is
    /// ItemView.SetQuestItemViewPanel. AllQuestsCheckmarks replaces QuestItemViewPanel.Show for every
    /// item, and out of raid its StashHelper.GetItemsInStash(templateId) walks every item the player
    /// owns (Inventory.GetPlayerItems()) to count that one template: once per cell, ~200 cells per
    /// trader open.
    ///
    /// This answers GetItemsInStash out of raid from one shared count of every template, built by
    /// the same walk the mod does (GetPlayerItems(), StackObjectsCount, split by
    /// MarkedAsSpawnedInSession). The shared count is thrown away and rebuilt when:
    ///   - any ItemController raises an add, remove or refresh item event (InventoryEvents),
    ///   - more than 2 frames pass without a request (the cells being built have stopped), or
    ///   - it is more than 1 second old (a backstop in case a change raises no event).
    /// In raid, the mod's own method runs unchanged. The first 20 answers are also computed the
    /// mod's way and compared; the result is logged.
    /// </summary>
    internal static class AqcCompat
    {
        private const int VerifyCalls = 20;

        private static Type _countType;
        private static FieldInfo _fir;
        private static FieldInfo _nonFir;

        private static Dictionary<MongoID, int[]> _counts;
        private static int _builtVersion = -1;
        private static int _lastFrame = -100;
        private static float _builtAt;
        private static int _verifyLeft = VerifyCalls;
        private static int _verifyMismatches;

        public static int Scans;
        public static int Served;
        public static string Status = "not patched";

        public static void Apply(Harmony harmony)
        {
            Type helper = AccessTools.TypeByName("AllQuestsCheckmarks.Helpers.StashHelper");
            if (helper == null)
            {
                Status = "AllQuestsCheckmarks not installed";
                return;
            }
            _countType = AccessTools.Inner(helper, "ItemsCount");
            _fir = AccessTools.Field(_countType, "Fir");
            _nonFir = AccessTools.Field(_countType, "NonFir");
            MethodInfo target = AccessTools.Method(helper, "GetItemsInStash", new[] { typeof(MongoID) });
            if (_countType == null || _fir == null || _nonFir == null || target == null)
            {
                throw new MissingMemberException("AllQuestsCheckmarks StashHelper.GetItemsInStash / ItemsCount has changed");
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(AqcCompat), nameof(Prefix)));
            Status = "patched";
        }


        private static bool Prefix(MongoID itemId, ref object __result)
        {
            if (!QuickTraderLoadTimesPlugin.AqcStashCountCache.Value) return true;
            if (Singleton<AbstractGame>.Instance?.InRaid ?? false) return true;

            Inventory inventory = ClientAppUtils.GetClientApp()?.GetClientBackEndSession()?.Profile?.Inventory;
            if (inventory == null) return true;

            Dictionary<MongoID, int[]> counts = Current(inventory);
            counts.TryGetValue(itemId, out int[] c);
            int fir = c?[0] ?? 0, nonFir = c?[1] ?? 0;

            if (_verifyLeft > 0) Verify(inventory, itemId, fir, nonFir);

            object result = Activator.CreateInstance(_countType);
            _fir.SetValue(result, fir);
            _nonFir.SetValue(result, nonFir);
            __result = result;
            Served++;
            return false;
        }

        private static Dictionary<MongoID, int[]> Current(Inventory inventory)
        {
            int frame = Time.frameCount;
            float now = Time.realtimeSinceStartup;
            bool valid = _counts != null
                && _builtVersion == InventoryEvents.Version
                && frame - _lastFrame <= 2
                && now - _builtAt < 1f;
            _lastFrame = frame;
            if (valid) return _counts;

            Dictionary<MongoID, int[]> counts = new Dictionary<MongoID, int[]>();
            foreach (Item item in inventory.GetPlayerItems())
            {
                if (!counts.TryGetValue(item.TemplateId, out int[] c)) counts[item.TemplateId] = c = new int[2];
                c[item.MarkedAsSpawnedInSession ? 0 : 1] += item.StackObjectsCount;
            }
            _counts = counts;
            _builtVersion = InventoryEvents.Version;
            _builtAt = now;
            Scans++;
            return counts;
        }

        /// <summary>The mod's own way, for the first few answers.</summary>
        private static void Verify(Inventory inventory, MongoID itemId, int fir, int nonFir)
        {
            int expectFir = 0, expectNonFir = 0;
            foreach (Item item in inventory.GetPlayerItems())
            {
                if (item.TemplateId != itemId) continue;
                if (item.MarkedAsSpawnedInSession) expectFir += item.StackObjectsCount;
                else expectNonFir += item.StackObjectsCount;
            }
            if (expectFir != fir || expectNonFir != nonFir)
            {
                _verifyMismatches++;
                QuickTraderLoadTimesPlugin.Log.LogWarning($"AQC stash count MISMATCH for {itemId}: shared {fir}/{nonFir}, mod's way {expectFir}/{expectNonFir}");
            }
            if (--_verifyLeft == 0)
            {
                QuickTraderLoadTimesPlugin.Log.LogInfo($"AQC stash count check: {VerifyCalls - _verifyMismatches} of {VerifyCalls} answers matched the mod's own count");
            }
        }
    }
}
