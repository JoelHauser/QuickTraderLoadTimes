using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using Diz.Jobs;
using HarmonyLib;
using UnityEngine;

namespace Hurryitup
{
    /// <summary>
    /// Phase 1: a measurement harness for trader icon loading. Every trader open is written to
    /// the BepInEx log as a "===== Hurryitup: trader open =====" block and appended as a row to
    /// measurements.csv beside this DLL.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class HurryitupPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.hurryitup";
        public const string PluginName = "Hurry It Up";
        public const string PluginVersion = "0.6.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<string> RunLabel;
        internal static ConfigEntry<bool> ColdIconCache;
        internal static ConfigEntry<float> MaxSessionSeconds;
        internal static ConfigEntry<float> SettleSeconds;
        internal static ConfigEntry<bool> FastIconRender;
        internal static ConfigEntry<float> RenderBudgetMs;
        internal static ConfigEntry<bool> FastTraderCells;
        internal static ConfigEntry<bool> SpreadStashCells;
        internal static ConfigEntry<float> CellBudgetMs;
        internal static ConfigEntry<bool> AqcStashCountCache;
        internal static ConfigEntry<bool> QuestPanelOncePerFrame;
        internal static ConfigEntry<bool> AqcQuestIndex;

        /// <summary>ColdIconCache as it was at startup; the icon creator only reads its path once.</summary>
        internal static bool ColdCacheActive { get; private set; }
        internal static string ColdCacheDir { get; private set; }

        private static bool _environmentLogged;

        private void Awake()
        {
            Log = Logger;
            string pluginDir = Path.GetDirectoryName(Info.Location);

            RunLabel = Config.Bind("Measurement", "RunLabel", "baseline",
                "Written into every CSV row, to tell before/after runs apart (e.g. baseline, prewarm-v1).");
            ColdIconCache = Config.Bind("Measurement", "ColdIconCache", false,
                "Restart required. Points the item icon cache at an empty throwaway folder (cold-cache\\<launch> beside " +
                "this DLL; the newest 6 are kept for scripts\\compare-icons.ps1) so every icon renders from scratch. " +
                "Your real icon cache is never touched.");
            MaxSessionSeconds = Config.Bind("Measurement", "MaxSessionSeconds", 30f,
                "A trader open stops being measured after this long even if icons are still loading.");
            SettleSeconds = Config.Bind("Measurement", "SettleSeconds", 1f,
                "A trader open is finished once every visible icon is drawn and no icon has finished for this long.");
            FastIconRender = Config.Bind("Fixes", "FastIconRender", false,
                "Render uncached item icons as fast as RenderBudgetMs per frame allows, instead of the game's " +
                "one-icon-per-two-frames pacing. Takes effect immediately.");
            RenderBudgetMs = Config.Bind("Fixes", "RenderBudgetMs", 8f,
                "With FastIconRender: milliseconds of icon capturing allowed per frame. The first capture in a " +
                "frame always runs.");
            FastTraderCells = Config.Bind("Fixes", "FastTraderCells", false,
                "Fill a trader's grid with as many cells per frame as CellBudgetMs allows, top rows first, instead " +
                "of one cell per frame. Takes effect immediately.");
            SpreadStashCells = Config.Bind("Fixes", "SpreadStashCells", false,
                "On the trader screen, build the stash grid over several frames (after the trader's grid) instead " +
                "of all in one frame, which is the half-second freeze on every trader switch. Takes effect immediately.");
            CellBudgetMs = Config.Bind("Fixes", "CellBudgetMs", 8f,
                "With FastTraderCells or SpreadStashCells: milliseconds of cell building allowed per frame. The " +
                "first cell in a frame is always built.");
            AqcStashCountCache = Config.Bind("Fixes", "AqcStashCountCache", false,
                "AllQuestsCheckmarks compatibility: count the stash once and share it between cells, instead of " +
                "walking every owned item for every cell. Recounted on any inventory change. Takes effect immediately.");
            QuestPanelOncePerFrame = Config.Bind("Fixes", "QuestPanelOncePerFrame", false,
                "A new item cell sets up its quest checkmark three times in the same frame (the game calls it from " +
                "Init and from both UpdateInfo calls). Do it once per cell per frame. Takes effect immediately.");
            AqcQuestIndex = Config.Bind("Fixes", "AqcQuestIndex", false,
                "AllQuestsCheckmarks compatibility: index the active quests once and answer each cell's quest lookup " +
                "from the index, instead of walking every quest for every cell. Weapons use the mod's own lookup. " +
                "Rebuilt on any inventory change; the first 50 answers are checked against the mod. Takes effect immediately.");

            string launchId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            ColdCacheActive = ColdIconCache.Value;
            string coldRoot = Path.Combine(pluginDir, "cold-cache");
            ColdCacheDir = Path.Combine(coldRoot, launchId + (FastIconRender.Value ? "-fast" : "-vanilla"));
            if (ColdCacheActive) PrepareColdCache(coldRoot);

            Recorder.Init(pluginDir, launchId);
            Probes.Apply(new Harmony(PluginGuid));
            Log.LogInfo($"{PluginName} {PluginVersion} measuring trader opens; run label '{RunLabel.Value}'" +
                (ColdCacheActive ? ", COLD icon cache" : "") + (FastIconRender.Value ? ", FastIconRender ON" : "") +
                (FastTraderCells.Value ? ", FastTraderCells ON" : "") + (SpreadStashCells.Value ? ", SpreadStashCells ON" : "") +
                (AqcStashCountCache.Value ? ", AqcStashCountCache ON" : "") +
                (QuestPanelOncePerFrame.Value ? ", QuestPanelOncePerFrame ON" : "") +
                (AqcQuestIndex.Value ? ", AqcQuestIndex ON" : ""));
        }

        /// <summary>
        /// One empty folder per cold launch. The 6 newest are kept (about 3 MB per 200 icons) so
        /// scripts\compare-icons.ps1 can diff a vanilla cold run against a fast one. Files left
        /// directly in cold-cache by 0.1.0 are not touched.
        /// </summary>
        private static void PrepareColdCache(string coldRoot)
        {
            try
            {
                Directory.CreateDirectory(ColdCacheDir);
                DirectoryInfo[] runs = new DirectoryInfo(coldRoot).GetDirectories("20*");
                Array.Sort(runs, (a, b) => string.CompareOrdinal(b.Name, a.Name));
                for (int i = 6; i < runs.Length; i++) runs[i].Delete(recursive: true);
            }
            catch (Exception e)
            {
                Log.LogError("could not prepare " + ColdCacheDir + ": " + e.Message);
            }
        }

        private void Update()
        {
            Recorder.Tick(Time.unscaledDeltaTime * 1000f);
        }

        /// <summary>Once per launch, at the first trader open: what the numbers depend on.</summary>
        internal static void LogEnvironmentOnce()
        {
            if (_environmentLogged) return;
            _environmentLogged = true;
            try
            {
                string cache = "(icon creator not created yet)";
                if (Singleton<ItemIconCreator>.Instantiated)
                {
                    ItemIconCreator c = Singleton<ItemIconCreator>.Instance;
                    cache = $"{c._cachePath} ({c._fileCacheIndex.Count} icons in index, {c._memoryCacheIndex.Count} in memory)";
                }
                string jobs = Singleton<JobScheduler>.Instantiated
                    ? $"{Singleton<JobScheduler>.Instance.FrameTicks / 10000.0:0.0} ms frame budget, slow frames {Singleton<JobScheduler>.Instance.SlowFrames}"
                    : "not created";
                Log.LogInfo($"environment: icon cache {cache}; JobScheduler {jobs}; targetFrameRate {Application.targetFrameRate}; " +
                    $"texture streaming {QualitySettings.streamingMipmapsActive}\n" +
                    "  other mods patching the trader screen path:\n" + Probes.OtherPatchers());
            }
            catch (Exception e)
            {
                Log.LogWarning("environment log failed: " + e.Message);
            }
        }
    }
}
