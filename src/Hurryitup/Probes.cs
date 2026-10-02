using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Diz.Resources;
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
    /// Measurement-only Harmony patches, installed only when Measurement is on. None of them changes
    /// what the game does, with one opt-in exception (ColdIconCache, which points the item icon cache
    /// at a throwaway folder). The fixes' own patches are in Fixes.
    /// Timing prefixes run first and timing postfixes run last, so other mods' patches on the
    /// same method count inside the measured time.
    /// </summary>
    internal static class Probes
    {
        public struct RequestState
        {
            public bool Valid;
            public int Hash;
            public IconKind Kind;
            public long Start;
        }

        private static readonly List<string> Applied = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static readonly List<MethodBase> Watched = new List<MethodBase>();

        private static Type IconBase => typeof(IconCreatorBase<Item, ItemIcon>);

        public static void Apply(Harmony harmony)
        {
            if (HurryitupPlugin.ColdCacheActive)
            {
                Patch(harmony, "ItemIconCreator..ctor (cold cache)",
                    () => AccessTools.Constructor(typeof(ItemIconCreator), new[] { typeof(IEasyAssets), typeof(ObjectsFactory) }),
                    postfix: nameof(CreatorCtorPostfix));
            }

            // Where an open starts and ends, and the stock fetch.
            Patch(harmony, "TraderDealScreen.Show",
                () => AccessTools.Method(typeof(TraderDealScreen), nameof(TraderDealScreen.Show),
                    new[] { typeof(Trader), typeof(Profile), typeof(InventoryController), typeof(ETradeMode), typeof(ItemUiContext), typeof(QuestController), typeof(IEnumerable<Trader>) }),
                prefix: nameof(DealShowPrefix), postfix: nameof(DealShowPostfix));
            Patch(harmony, "TraderDealScreen.Close",
                () => AccessTools.Method(typeof(TraderDealScreen), nameof(TraderDealScreen.Close)),
                prefix: nameof(DealClosePrefix));
            Patch(harmony, "Trader.RefreshAssortment",
                () => AccessTools.Method(typeof(Trader), nameof(Trader.RefreshAssortment)),
                prefix: nameof(StartPrefix), postfix: nameof(RefreshAssortmentPostfix));
            Patch(harmony, "TradingGetMarketPricesOperation.ExecuteOnce",
                () => AccessTools.Method(typeof(TradingGetMarketPricesOperation), nameof(TradingGetMarketPricesOperation.ExecuteOnce)),
                prefix: nameof(StartPrefix), postfix: nameof(PricesPostfix));
            Patch(harmony, "TradingGetAssortmentOperation.ExecuteOnce",
                () => AccessTools.Method(typeof(TradingGetAssortmentOperation), nameof(TradingGetAssortmentOperation.ExecuteOnce)),
                prefix: nameof(StartPrefix), postfix: nameof(AssortmentRequestPostfix));

            // What the trader screen does on the main thread when it opens (the ~0.5 s frame).
            Patch(harmony, "TraderDealScreen.UpdateGridViews",
                () => AccessTools.Method(typeof(TraderDealScreen), nameof(TraderDealScreen.UpdateGridViews)),
                prefix: nameof(StartPrefix), postfix: nameof(UpdateGridViewsPostfix), watch: true);
            Patch(harmony, "TradingGridView.Show (trader, 6 args)",
                () => TradingGridShow(6),
                prefix: nameof(StartPrefix), postfix: nameof(TraderGridShowPostfix), watch: true);
            Patch(harmony, "TradingGridView.Show (stash, 5 args)",
                () => TradingGridShow(5),
                prefix: nameof(StartPrefix), postfix: nameof(StashGridShowPostfix), watch: true);
            Patch(harmony, "SimpleStashPanel.Show",
                () => AccessTools.Method(typeof(SimpleStashPanel), nameof(SimpleStashPanel.Show)),
                prefix: nameof(StartPrefix), postfix: nameof(StashPanelShowPostfix), watch: true);
            Patch(harmony, "GridView.CreateItemView",
                () => AccessTools.Method(typeof(GridView), nameof(GridView.CreateItemView)),
                prefix: nameof(StartPrefix), postfix: nameof(CreateItemViewPostfix), watch: true);
            Patch(harmony, "AutoExchange..ctor",
                () => AccessTools.Constructor(typeof(AutoExchange), new[] { typeof(InventoryController), typeof(IEnumerable<Trader>) }),
                prefix: nameof(StartPrefix), postfix: nameof(AutoExchangePostfix), watch: true);
            // Inside one cell (nested in "cell, ..." sections).
            Patch(harmony, "TradingItemView.NewTradingItemView",
                () => AccessTools.Method(typeof(TradingItemView), nameof(TradingItemView.NewTradingItemView)),
                prefix: nameof(StartPrefix), postfix: nameof(NewTradingItemViewPostfix), watch: true);
            Patch(harmony, "GridItemView.NewGridItemView",
                () => AccessTools.Method(typeof(GridItemView), nameof(GridItemView.NewGridItemView)),
                prefix: nameof(StartPrefix), postfix: nameof(NewGridItemViewPostfix), watch: true);
            Patch(harmony, "ItemView.NewItemView",
                () => AccessTools.Method(typeof(ItemView), nameof(ItemView.NewItemView)),
                prefix: nameof(StartPrefix), postfix: nameof(NewItemViewPostfix), watch: true);
            Patch(harmony, "ItemView.Init",
                () => AccessTools.Method(typeof(ItemView), nameof(ItemView.Init)),
                prefix: nameof(StartPrefix), postfix: nameof(InitPostfix), watch: true);
            Patch(harmony, "GridItemView.UpdateStaticInfo",
                () => AccessTools.Method(typeof(GridItemView), nameof(GridItemView.UpdateStaticInfo)),
                prefix: nameof(StartPrefix), postfix: nameof(UpdateStaticInfoPostfix), watch: true);
            Patch(harmony, "GridItemView.UpdateInfo",
                () => AccessTools.Method(typeof(GridItemView), nameof(GridItemView.UpdateInfo)),
                prefix: nameof(StartPrefix), postfix: nameof(UpdateInfoPostfix), watch: true);
            Patch(harmony, "ItemView.SetQuestItemViewPanel",
                () => AccessTools.Method(typeof(ItemView), nameof(ItemView.SetQuestItemViewPanel)),
                prefix: nameof(StartPrefix), postfix: nameof(QuestPanelPostfix), watch: true);
            Patch(harmony, "ItemView.UpdateCompoundItemInfo",
                () => AccessTools.Method(typeof(ItemView), nameof(ItemView.UpdateCompoundItemInfo)),
                prefix: nameof(StartPrefix), postfix: nameof(CompoundInfoPostfix), watch: true);

            Patch(harmony, "Canvas.ForceUpdateCanvases",
                () => AccessTools.Method(typeof(Canvas), nameof(Canvas.ForceUpdateCanvases)),
                prefix: nameof(StartPrefix), postfix: nameof(ForceUpdateCanvasesPostfix));

            // The icon pipeline.
            Patch(harmony, "ItemIconCreator.GetItemIcon",
                () => AccessTools.Method(typeof(ItemIconCreator), nameof(ItemIconCreator.GetItemIcon)),
                prefix: nameof(GetItemIconPrefix), postfix: nameof(GetItemIconPostfix), watch: true);
            Patch(harmony, "IconCreatorBase.LoadFromUserCacheAsync",
                () => AccessTools.Method(IconBase, nameof(ItemIconCreator.LoadFromUserCacheAsync)),
                prefix: nameof(StartPrefix), postfix: nameof(DiskLoadPostfix));
            Patch(harmony, "ImageConversion.LoadImage(Texture2D, byte[])",
                () => AccessTools.Method(typeof(ImageConversion), nameof(ImageConversion.LoadImage), new[] { typeof(Texture2D), typeof(byte[]) }),
                prefix: nameof(StartPrefix), postfix: nameof(LoadImagePostfix));
            Patch(harmony, "ItemIconCreator.RenderModel",
                () => AccessTools.Method(typeof(ItemIconCreator), nameof(ItemIconCreator.RenderModel)),
                prefix: nameof(StartPrefix), postfix: nameof(RenderPostfix));
            Patch(harmony, "ObjectsFactory.CreateCleanLootPrefabAsync",
                () => AccessTools.Method(typeof(ObjectsFactory), nameof(ObjectsFactory.CreateCleanLootPrefabAsync)),
                prefix: nameof(StartPrefix), postfix: nameof(PrefabPostfix));
            Patch(harmony, "ItemIconCreator.RequestMipZero",
                () => AccessTools.Method(typeof(ItemIconCreator), nameof(ItemIconCreator.RequestMipZero)),
                prefix: nameof(StartPrefix), postfix: nameof(MipPostfix));
            Patch(harmony, "IconCreatorBase.CaptureSpriteOfModel",
                () => AccessTools.Method(IconBase, nameof(ItemIconCreator.CaptureSpriteOfModel)),
                prefix: nameof(StartPrefix), postfix: nameof(CapturePostfix));
            Patch(harmony, "IconCreatorBase.SaveIconAsync",
                () => AccessTools.Method(IconBase, nameof(ItemIconCreator.SaveIconAsync)),
                prefix: nameof(StartPrefix), postfix: nameof(SavePostfix));
            Patch(harmony, "ItemView.IconChangedHandler",
                () => AccessTools.Method(typeof(ItemView), nameof(ItemView.IconChangedHandler)),
                prefix: nameof(StartPrefix), postfix: nameof(IconChangedPostfix), watch: true);

            HurryitupPlugin.Log.LogInfo($"measurement probes applied: {Applied.Count}; failed: {Failed.Count}" +
                (Failed.Count > 0 ? " -> " + string.Join("; ", Failed) : ""));
        }

        private static MethodBase TradingGridShow(int parameters) =>
            typeof(TradingGridView).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(m => m.Name == nameof(TradingGridView.Show) && m.DeclaringType == typeof(TradingGridView)
                    && m.GetParameters().Length == parameters);

        private static void Patch(Harmony harmony, string what, Func<MethodBase> target,
            string prefix = null, string postfix = null, bool watch = false)
        {
            try
            {
                MethodBase method = target();
                if (method == null) throw new MissingMethodException(what);
                HarmonyMethod pre = prefix == null ? null : new HarmonyMethod(typeof(Probes), prefix) { priority = Priority.First };
                HarmonyMethod post = postfix == null ? null : new HarmonyMethod(typeof(Probes), postfix) { priority = Priority.Last };
                harmony.Patch(method, prefix: pre, postfix: post);
                Applied.Add(what);
                if (watch) Watched.Add(method);
            }
            catch (Exception e)
            {
                Failed.Add(what + ": " + e.Message);
            }
        }

        /// <summary>
        /// Which other mods patch the methods on the trader screen's path. Read at the first trader
        /// open rather than at startup, because plugins that load after this one patch later.
        /// </summary>
        public static string OtherPatchers()
        {
            List<string> lines = new List<string>();
            foreach (MethodBase m in Watched)
            {
                Patches info = Harmony.GetPatchInfo(m);
                if (info == null) continue;
                IEnumerable<string> Owners(IEnumerable<HarmonyLib.Patch> ps, string kind) =>
                    ps.Where(p => p.owner != HurryitupPlugin.PluginGuid).Select(p => p.owner + " (" + kind + ")");
                List<string> owners = Owners(info.Prefixes, "prefix")
                    .Concat(Owners(info.Postfixes, "postfix"))
                    .Concat(Owners(info.Transpilers, "transpiler"))
                    .Concat(Owners(info.Finalizers, "finalizer"))
                    .Distinct().ToList();
                if (owners.Count > 0) lines.Add($"    {m.DeclaringType?.Name}.{m.Name}: {string.Join(", ", owners)}");
            }
            return lines.Count == 0 ? "    (no other mod patches these)" : string.Join("\n", lines);
        }

        private static double Since(long start) => TraderSession.Elapsed(start, Stopwatch.GetTimestamp());

        private static void Section(string name, long start)
        {
            double ms = Since(start);
            Recorder.With(s => s.Section(name, ms));
        }

        // ---- shared prefix: remember when the call started ----

        private static void StartPrefix(out long __state) => __state = Stopwatch.GetTimestamp();

        // ---- cold cache ----

        private static void CreatorCtorPostfix(ItemIconCreator __instance)
        {
            string dir = HurryitupPlugin.ColdCacheDir;
            __instance._cachePath = dir;
            __instance._indexPath = Path.Combine(dir, "index.json");
            HurryitupPlugin.Log.LogWarning("COLD icon cache: item icons for this launch read and write " + dir +
                " (the real cache is untouched). Turn ColdIconCache off and restart to go back.");
        }

        // ---- trader screen ----

        private static void DealShowPrefix(TraderDealScreen __instance, Trader trader, out long __state)
        {
            Recorder.Begin(__instance, trader);
            __state = Stopwatch.GetTimestamp();
        }

        private static void DealShowPostfix(long __state) => Section(Sections.DealShow, __state);

        private static void DealClosePrefix() => Recorder.EndIfOpen("closed");

        private static void RefreshAssortmentPostfix(Task __result, long __state) =>
            Recorder.TrackTask(__result, __state, (s, ms) => s.RefreshAssortmentWall.Add(ms));

        private static void PricesPostfix(Task __result, long __state) =>
            Recorder.TrackTask(__result, __state, (s, ms) => s.PricesRequestWall.Add(ms));

        private static void AssortmentRequestPostfix(Task __result, long __state) =>
            Recorder.TrackTask(__result, __state, (s, ms) => s.AssortmentRequestWall.Add(ms));

        private static void UpdateGridViewsPostfix(long __state) => Section(Sections.UpdateGridViews, __state);

        private static void TraderGridShowPostfix(long __state)
        {
            double ms = Since(__state);
            Recorder.With(s =>
            {
                s.Section(Sections.TraderGridShow, ms);
                s.GridShown();
            });
        }

        private static void StashGridShowPostfix(long __state) => Section(Sections.StashGridShow, __state);

        private static void StashPanelShowPostfix(long __state) => Section(Sections.StashPanelShow, __state);

        private static void CreateItemViewPostfix(GridView __instance, long __state)
        {
            double ms = Since(__state);
            Recorder.With(s => s.Section(s.IsTraderGrid(__instance) ? Sections.TraderCell : Sections.OtherCell, ms));
        }

        private static void NewTradingItemViewPostfix(long __state) => Section(Sections.CellNewTrading, __state);
        private static void NewGridItemViewPostfix(long __state) => Section(Sections.CellNewGrid, __state);
        private static void NewItemViewPostfix(long __state) => Section(Sections.CellNewItem, __state);
        private static void InitPostfix(long __state) => Section(Sections.CellInit, __state);
        private static void UpdateStaticInfoPostfix(long __state) => Section(Sections.CellStaticInfo, __state);
        private static void UpdateInfoPostfix(long __state) => Section(Sections.CellInfo, __state);
        private static void QuestPanelPostfix(long __state) => Section(Sections.CellQuestPanel, __state);
        private static void CompoundInfoPostfix(long __state) => Section(Sections.CellCompoundInfo, __state);

        private static void AutoExchangePostfix(long __state) => Section(Sections.AutoExchange, __state);

        private static void ForceUpdateCanvasesPostfix(long __state) => Section(Sections.ForceUpdateCanvases, __state);

        // ---- icons ----

        /// <summary>
        /// Repeats GetItemIcon's own decision (same hash, same memory/disk checks) to label the
        /// request before the original runs. Its cost is timed separately as probe overhead.
        /// </summary>
        private static void GetItemIconPrefix(ItemIconCreator __instance, Item item, bool forcedGeneration, out RequestState __state)
        {
            __state = default;
            if (!Recorder.Active || item == null) return;
            long probeStart = Stopwatch.GetTimestamp();
            try
            {
                int hash = IconsHash.GetItemHash(item);
                IconKind kind;
#pragma warning disable CS0618 // GetItemIcon itself reads InGameStatus.InRaid; the label must match its decision.
                if (!forcedGeneration && __instance.TryGetCachedIcon(hash, out ItemIcon cached)
                    && (InGameStatus.InRaid || !cached.IsGeneratedInRaid))
#pragma warning restore CS0618
                {
                    kind = cached.Sprite != null ? IconKind.Memory : IconKind.MemoryInFlight;
                }
                else if (!forcedGeneration && __instance.TryGetIconPath(hash, out _))
                {
                    kind = IconKind.Disk;
                }
                else
                {
                    kind = IconKind.Render;
                }
                long now = Stopwatch.GetTimestamp();
                double overhead = TraderSession.Elapsed(probeStart, now);
                Recorder.With(s => s.Section(Sections.ProbeOverhead, overhead));
                __state = new RequestState { Valid = true, Hash = hash, Kind = kind, Start = now };
            }
            catch (Exception e)
            {
                HurryitupPlugin.Log.LogWarning("GetItemIcon probe: " + e.Message);
            }
        }

        private static void GetItemIconPostfix(ItemIcon __result, RequestState __state)
        {
            if (!__state.Valid) return;
            double ms = Since(__state.Start);
            Recorder.With(s =>
            {
                s.Section(Sections.GetItemIcon, ms);
                s.IconRequested(__result, __state.Hash, __state.Kind, __state.Start);
            });
        }

        private static void DiskLoadPostfix(object __instance, Task __result, long __state)
        {
            if (!(__instance is ItemIconCreator)) return;
            Recorder.TrackTask(__result, __state, (s, ms) => s.DiskLoadWall.Add(ms));
        }

        private static void LoadImagePostfix(long __state) => Section(Sections.LoadImage, __state);

        private static void RenderPostfix(Task __result, long __state) =>
            Recorder.TrackTask(__result, __state, (s, ms) => s.RenderWall.Add(ms));

        private static void PrefabPostfix(Task __result, long __state) =>
            Recorder.TrackTask(__result, __state, (s, ms) => s.PrefabWall.Add(ms));

        private static void MipPostfix(Task<bool> __result, long __state) =>
            Recorder.TrackTask(__result, __state, (s, ms) =>
            {
                s.MipWall.Add(ms);
                if (__result.Status == TaskStatus.RanToCompletion && !__result.Result) s.MipTimeouts++;
            });

        private static void CapturePostfix(object __instance, long __state)
        {
            if (!(__instance is ItemIconCreator)) return;
            Section(Sections.Capture, __state);
        }

        private static void SavePostfix(object __instance, long __state)
        {
            if (!(__instance is ItemIconCreator)) return;
            Section(Sections.Save, __state);
        }

        private static void IconChangedPostfix(long __state) => Section(Sections.IconChanged, __state);
    }
}
