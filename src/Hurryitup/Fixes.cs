using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace Hurryitup
{
    /// <summary>
    /// The patches the fixes need, installed whether or not Measurement is on. Each fix also checks
    /// its own setting on every call, so they can be switched live.
    /// </summary>
    internal static class Fixes
    {
        public static readonly List<string> Applied = new List<string>();
        public static readonly List<string> Failed = new List<string>();

        public static void Apply(Harmony harmony)
        {
            Try("TraderDealScreen.Show (which screen is open)", () =>
                harmony.Patch(AccessTools.Method(typeof(TraderDealScreen), nameof(TraderDealScreen.Show),
                        new[] { typeof(Trader), typeof(Profile), typeof(InventoryController), typeof(ETradeMode), typeof(ItemUiContext), typeof(QuestController), typeof(IEnumerable<Trader>) }),
                    prefix: new HarmonyMethod(typeof(Fixes), nameof(DealShowPrefix)) { priority = Priority.First }));

            Try("inventory change events", () => InventoryEvents.Apply(harmony));

            Try("FastTraderCells / SpreadStashCells (GridView.MagnifyIfPossible)", () =>
                harmony.Patch(AccessTools.Method(typeof(GridView), nameof(GridView.MagnifyIfPossible), new[] { typeof(Rect), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(FastCells), nameof(FastCells.Prefix))));

            Try("QuestPanelOncePerFrame (ItemView.SetQuestItemViewPanel)", () =>
                harmony.Patch(AccessTools.Method(typeof(ItemView), nameof(ItemView.SetQuestItemViewPanel)),
                    prefix: new HarmonyMethod(typeof(QuestPanelOnce), nameof(QuestPanelOnce.Prefix)) { priority = Priority.First - 1 }));

            string aqcWhyNot = AqcSupport.WhyNot();
            if (aqcWhyNot == null)
            {
                Try("AqcStashCountCache (AllQuestsCheckmarks 1.4.0)", () => AqcCompat.Apply(harmony));
                Try("AqcQuestIndex (AllQuestsCheckmarks 1.4.0)", () => AqcQuestIndex.Apply(harmony));
            }
            else
            {
                AqcCompat.Status = AqcQuestIndex.Status = aqcWhyNot;
                Applied.Add("AllQuestsCheckmarks fixes not installed: " + aqcWhyNot);
            }

            Try("FastIconRender (IconCreatorBase.CG_MoveNext.method_0)", () =>
                harmony.Patch(AccessTools.Method(typeof(IconCreatorBase<Item, ItemIcon>.CG_MoveNext), "method_0"),
                    prefix: new HarmonyMethod(typeof(FastRender), nameof(FastRender.Prefix))));
        }

        private static void Try(string what, Action patch)
        {
            try
            {
                patch();
                if (!Applied.Contains(what)) Applied.Add(what);
            }
            catch (Exception e)
            {
                Failed.Add(what + ": " + (e.InnerException?.Message ?? e.Message));
            }
        }

        private static void DealShowPrefix(TraderDealScreen __instance) => FastCells.DealScreen = __instance;
    }
}
