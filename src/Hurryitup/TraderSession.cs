using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Comfort.Common;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Hurryitup
{
    public enum IconKind
    {
        /// <summary>Already in the memory cache with a sprite: shows immediately.</summary>
        Memory = 0,
        /// <summary>In the memory cache but still loading or rendering (an earlier request owns it).</summary>
        MemoryInFlight = 1,
        /// <summary>Loaded from the PNG cache on disk.</summary>
        Disk = 2,
        /// <summary>Rendered from the 3D model.</summary>
        Render = 3,
    }

    /// <summary>Names of the timed main-thread sections, shared by the probes, the log and the CSV.</summary>
    internal static class Sections
    {
        public const string DealShow = "TraderDealScreen.Show";
        public const string UpdateGridViews = "TraderDealScreen.UpdateGridViews";
        public const string TraderGridShow = "trader grid Show";
        public const string StashGridShow = "stash grid Show";
        public const string StashPanelShow = "SimpleStashPanel.Show";
        public const string TraderCell = "cell, trader grid";
        public const string OtherCell = "cell, other grid";
        public const string AutoExchange = "AutoExchange ctor";
        public const string CellNewTrading = "cell part: NewTradingItemView";
        public const string CellNewGrid = "cell part: NewGridItemView";
        public const string CellNewItem = "cell part: NewItemView";
        public const string CellInit = "cell part: Init";
        public const string CellStaticInfo = "cell part: UpdateStaticInfo";
        public const string CellInfo = "cell part: UpdateInfo";
        public const string CellQuestPanel = "cell part: SetQuestItemViewPanel";
        public const string CellCompoundInfo = "cell part: UpdateCompoundItemInfo";
        public const string ForceUpdateCanvases = "Canvas.ForceUpdateCanvases";
        public const string GetItemIcon = "GetItemIcon";
        public const string LoadImage = "LoadImage (PNG decode)";
        public const string Capture = "icon capture";
        public const string Save = "icon save (PNG encode)";
        public const string IconChanged = "ItemView.IconChangedHandler";
        public const string ProbeOverhead = "probe overhead";
    }

    /// <summary>
    /// One trader open, from TraderDealScreen.Show until the trader's visible icons are all
    /// drawn and nothing is pending (or the screen closes, or MaxSessionSeconds passes).
    /// All times are milliseconds since Show.
    /// </summary>
    internal sealed class TraderSession
    {
        private struct Pending
        {
            public ItemIcon Icon;
            public long Start;
            public IconKind Kind;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        public readonly TraderDealScreen Screen;
        public readonly Trader Trader;
        public readonly string TraderId;
        public readonly string TraderName;
        public readonly int OpenIndex;
        public readonly int OpenOfThisTrader;
        public readonly bool AssortmentLoadedAtOpen;
        public readonly long T0;
        public readonly MemorySnapshot MemoryAtOpen;
        public readonly double BaselineP50;
        public readonly double BaselineP95;
        private readonly int _fastCapturesAtOpen;
        private readonly int _fastMultiFramesAtOpen;
        private readonly int _aqcScansAtOpen;
        private readonly int _aqcServedAtOpen;
        private readonly int _questSkippedAtOpen;
        private readonly StringBuilder _timeline = new StringBuilder();
        private int _lastTimelineCells = -1, _lastTimelineVisible = -1, _lastTimelineDrawn = -1;
        private bool _lastTimelineActive;
        private int _timelineEntries;

        public string EndReason;
        public MemorySnapshot MemoryAtEnd;

        private double? _tAssortmentReady;
        private double? _tGridShown;
        private double? _tFirstVisible;
        private double? _allVisibleSince;
        private double _tViewsComplete;
        private double _tLastIconDone;
        private bool _hadPending;
        private int _visibleAtAll;
        private int _maxVisible;
        private int _gridViews;
        private bool _magnified;
        private bool? _asyncBuild;
        private int _maxJobQueue;
        private int _maxRenderQueue;

        private readonly int[] _requests = new int[4];
        private readonly HashSet<int> _uniqueHashes = new HashSet<int>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Series[] _iconLatency = { new Series(), new Series(), new Series(), new Series() };
        private int _neverFinished;

        // Main-thread sections: totals for the whole open, and what ran since the last Tick so the
        // worst frame can be broken down.
        private readonly Dictionary<string, Series> _sections = new Dictionary<string, Series>();
        private readonly Dictionary<string, double> _sinceTick = new Dictionary<string, double>();
        private double _worstFrame;
        private double _worstFrameAt;
        private string _worstFrameSections = "";

        public readonly Series Frames = new Series();
        public readonly Series DiskLoadWall = new Series();
        public readonly Series RenderWall = new Series();
        public readonly Series PrefabWall = new Series();
        public readonly Series MipWall = new Series();
        public readonly Series RefreshAssortmentWall = new Series();
        public readonly Series PricesRequestWall = new Series();
        public readonly Series AssortmentRequestWall = new Series();
        public int MipTimeouts;

        public TraderSession(TraderDealScreen screen, Trader trader, int openIndex, int openOfThisTrader,
            double baselineP50, double baselineP95)
        {
            Screen = screen;
            Trader = trader;
            TraderId = trader?.Id ?? "?";
            TraderName = SafeName(trader);
            OpenIndex = openIndex;
            OpenOfThisTrader = openOfThisTrader;
            AssortmentLoadedAtOpen = trader?.CurrentAssortment != null;
            BaselineP50 = baselineP50;
            BaselineP95 = baselineP95;
            MemoryAtOpen = MemorySnapshot.Take();
            _fastCapturesAtOpen = FastRender.Captures;
            _fastMultiFramesAtOpen = FastRender.FramesWithMultipleCaptures;
            _aqcScansAtOpen = AqcCompat.Scans;
            _aqcServedAtOpen = AqcCompat.Served;
            _questSkippedAtOpen = QuestPanelOnce.Skipped;
            T0 = Stopwatch.GetTimestamp();
        }

        public double Now() => Elapsed(T0, Stopwatch.GetTimestamp());

        public static double Elapsed(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

        public void Section(string name, double ms)
        {
            if (!_sections.TryGetValue(name, out Series s)) _sections[name] = s = new Series();
            s.Add(ms);
            _sinceTick.TryGetValue(name, out double sum);
            _sinceTick[name] = sum + ms;
        }

        private Series SectionSeries(string name) => _sections.TryGetValue(name, out Series s) ? s : new Series();

        public void IconRequested(ItemIcon icon, int hash, IconKind kind, long start)
        {
            _requests[(int)kind]++;
            _uniqueHashes.Add(hash);
            if (kind == IconKind.Memory || icon == null)
            {
                _iconLatency[(int)kind].Add(0);
                return;
            }
            _hadPending = true;
            _pending.Add(new Pending { Icon = icon, Start = start, Kind = kind });
        }

        public void GridShown()
        {
            if (!_tGridShown.HasValue) _tGridShown = Now();
        }

        public bool IsTraderGrid(GridView grid) => Screen != null && ReferenceEquals(grid, Screen._traderGridView);

        /// <summary>Called once per frame from the plugin's Update. Returns true when the session is over.</summary>
        public bool Tick(float frameMs, double maxSeconds, double settleSeconds)
        {
            double now = Now();
            Frames.Add(frameMs);

            // Unity's deltaTime here is the frame that just ended; the sections recorded since the
            // last Tick are mostly that frame's (anything after this plugin's Update in the previous
            // frame, plus anything before it in this one), so the breakdown is approximate.
            if (frameMs > _worstFrame)
            {
                _worstFrame = frameMs;
                _worstFrameAt = now;
                _worstFrameSections = _sinceTick.Count == 0
                    ? "nothing timed"
                    : string.Join(", ", _sinceTick.Where(kv => kv.Value >= 1.0).OrderByDescending(kv => kv.Value)
                        .Select(kv => kv.Key + " " + Fmt.Ms(kv.Value)));
                if (_worstFrameSections.Length == 0) _worstFrameSections = "only sections under 1 ms";
            }
            _sinceTick.Clear();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (p.Icon.Sprite != null)
                {
                    _iconLatency[(int)p.Kind].Add(Elapsed(p.Start, Stopwatch.GetTimestamp()));
                    _tLastIconDone = now;
                    _pending.RemoveAt(i);
                }
            }

            _maxJobQueue = Math.Max(_maxJobQueue, Diz.Jobs.JobScheduler.QueueLength);
            if (Singleton<ItemIconCreator>.Instantiated)
            {
                _maxRenderQueue = Math.Max(_maxRenderQueue, Singleton<ItemIconCreator>.Instance._queueCount);
            }

            if (!_tAssortmentReady.HasValue && Trader != null && Trader.CurrentAssortment != null && !Trader.AssortmentLoading)
            {
                _tAssortmentReady = now;
            }

            int visible = ScanVisible(now);

            if (Screen == null || !Screen.gameObject.activeInHierarchy)
            {
                EndReason = "closed";
                return true;
            }
            if (now > maxSeconds * 1000.0)
            {
                EndReason = "timeout";
                return true;
            }

            double settleMs = settleSeconds * 1000.0;
            bool quiet = _pending.Count == 0
                && now - _tLastIconDone >= settleMs
                && now - _tViewsComplete >= settleMs
                && now >= settleMs;
            if (quiet && _allVisibleSince.HasValue)
            {
                EndReason = "settled";
                return true;
            }
            if (quiet && visible == 0 && _tAssortmentReady.HasValue && now > 5000)
            {
                EndReason = "settled, nothing visible";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Counts the trader grid's item views that overlap its scroll viewport, and how many of
        /// those already draw their icon. ItemView.IconChangedHandler is what turns MainImage on
        /// with the icon's sprite, so an active MainImage with a sprite is exactly "drawn".
        /// "All visible drawn" is the moment that became true for the last time: while the grid is
        /// still adding cells it can be briefly true for the few that exist.
        /// </summary>
        private int ScanVisible(double now)
        {
            TradingGridView grid = Screen != null ? Screen._traderGridView : null;
            ScrollRect scroll = Screen != null ? Screen._traderScroll : null;
            bool active = grid != null && grid.gameObject.activeInHierarchy;
            if (grid == null || scroll == null || !active)
            {
                Timeline(now, active, 0, 0, 0);
                return 0;
            }

            _magnified = grid.IsMagnified;
            _asyncBuild = grid._isAsyncAllowed;
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            Rect view = WorldRect(viewport);

            int views = 0, visible = 0, drawn = 0;
            foreach (ItemView itemView in grid.GridItemViews)
            {
                views++;
                if (itemView == null || !itemView.gameObject.activeInHierarchy) continue;
                if (!WorldRect((RectTransform)itemView.transform).Overlaps(view)) continue;
                visible++;
                Image image = itemView.MainImage;
                if (image != null && image.sprite != null && image.gameObject.activeSelf) drawn++;
            }

            Timeline(now, true, views, visible, drawn);
            if (views > _gridViews)
            {
                _gridViews = views;
                _tViewsComplete = now;
            }
            _maxVisible = Math.Max(_maxVisible, visible);
            if (visible > 0 && drawn > 0 && !_tFirstVisible.HasValue) _tFirstVisible = now;

            bool allDrawn = visible > 0 && drawn == visible;
            if (allDrawn && !_allVisibleSince.HasValue)
            {
                _allVisibleSince = now;
                _visibleAtAll = visible;
            }
            else if (!allDrawn)
            {
                _allVisibleSince = null;
            }
            else
            {
                _visibleAtAll = Math.Max(_visibleAtAll, visible);
            }
            return visible;
        }

        /// <summary>
        /// "t ms: cells/visible/drawn" whenever any of them changes, for the first 40 changes:
        /// shows when the trader grid becomes active, when its cells arrive, and when they draw.
        /// </summary>
        private void Timeline(double now, bool active, int cells, int visible, int drawn)
        {
            if (_timelineEntries >= 40) return;
            if (active == _lastTimelineActive && cells == _lastTimelineCells && visible == _lastTimelineVisible && drawn == _lastTimelineDrawn) return;
            _lastTimelineActive = active;
            _lastTimelineCells = cells;
            _lastTimelineVisible = visible;
            _lastTimelineDrawn = drawn;
            _timelineEntries++;
            if (_timeline.Length > 0) _timeline.Append("; ");
            _timeline.Append(Fmt.Ms(now)).Append(": ").Append(active ? $"{cells}/{visible}/{drawn}" : "grid inactive");
        }

        private static Rect WorldRect(RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            return Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
        }

        public void Finish()
        {
            _neverFinished = _pending.Count;
            MemoryAtEnd = MemorySnapshot.Take();
        }

        private int QuestPanelSkipped => QuestPanelOnce.Skipped - _questSkippedAtOpen;

        private int TotalRequests => _requests[0] + _requests[1] + _requests[2] + _requests[3];

        /// <summary>When the last icon that had to load finished; null if none had to, or one never did.</summary>
        private double? AllIconsDone => _hadPending && _neverFinished == 0 ? _tLastIconDone : (double?)null;

        private string AllIconsDoneText =>
            !_hadPending ? "nothing had to load" : _neverFinished > 0 ? "never" : Fmt.Ms(_tLastIconDone) + " ms";

        private string ViewsCompleteText => _gridViews == 0 ? "no cells" : Fmt.Ms(_tViewsComplete) + " ms";

        public string Report(string runLabel, string launchId, bool coldCache)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"===== Hurryitup: trader open #{OpenIndex} ({TraderName}, open {OpenOfThisTrader} of this trader this launch) =====");
            sb.AppendLine($"  run label '{runLabel}', launch {launchId}{(coldCache ? ", COLD icon cache" : "")}, ended: {EndReason} at {Fmt.Ms(Now())} ms");
            sb.AppendLine($"  assortment already loaded at open: {AssortmentLoadedAtOpen}; ready at {Fmt.MaybeMs(_tAssortmentReady, "never")} ms; trader grid shown at {Fmt.MaybeMs(_tGridShown, "not rebuilt")} ms; last cell added at {ViewsCompleteText}");
            sb.AppendLine($"  first visible icon drawn: {Fmt.MaybeMs(_tFirstVisible, "never")} ms; all visible drawn: {Fmt.MaybeMs(_allVisibleSince, "never")} ms ({_visibleAtAll} visible); all icons that had to load: {AllIconsDoneText}");
            sb.AppendLine($"  grid: {_gridViews} cells, magnified (only visible cells built): {_magnified}, async build (one cell per frame from empty): {(_asyncBuild.HasValue ? _asyncBuild.Value.ToString() : "?")}, max visible {_maxVisible}");
            sb.AppendLine($"  icon requests: {TotalRequests} ({_uniqueHashes.Count} distinct) = memory {_requests[0]}, memory in flight {_requests[1]}, disk {_requests[2]}, render {_requests[3]}; unfinished at end {_neverFinished}");
            if (_requests[(int)IconKind.Disk] > 0 && SectionSeries(Sections.LoadImage).Count == 0)
            {
                sb.AppendLine("  CHECK: disk loads happened but the LoadImage probe never fired (inlined?); decode cost is missing below");
            }
            sb.AppendLine($"  icon latency, request -> drawn: disk {_iconLatency[2].Describe()}");
            sb.AppendLine($"                                   render {_iconLatency[3].Describe()}");
            sb.AppendLine($"                                   in flight {_iconLatency[1].Describe()}");
            sb.AppendLine($"  worst frame: {Fmt.Ms(_worstFrame)} ms at {Fmt.Ms(_worstFrameAt)} ms; timed sections in it (nested, approximate): {_worstFrameSections}");
            sb.AppendLine("  main thread sections, whole open (nested: UpdateGridViews contains the grid and panel Shows, which contain cells):");
            foreach (KeyValuePair<string, Series> kv in _sections.OrderByDescending(kv => kv.Value.Sum))
            {
                sb.AppendLine($"    {kv.Key}: {kv.Value.Describe()}");
            }
            sb.AppendLine($"  async wall: disk load {DiskLoadWall.Describe()}");
            sb.AppendLine($"              render total {RenderWall.Describe()}");
            sb.AppendLine($"              prefab {PrefabWall.Describe()}; mip0 wait {MipWall.Describe()}, timeouts {MipTimeouts}");
            sb.AppendLine($"              RefreshAssortment {RefreshAssortmentWall.Describe()}; prices request {PricesRequestWall.Describe()}; assortment request {AssortmentRequestWall.Describe()}");
            sb.AppendLine($"  frames: {Frames.Count}, p50 {Fmt.Ms(Frames.Percentile(50))} p95 {Fmt.Ms(Frames.Percentile(95))} max {Fmt.Ms(Frames.Max)} ms; >33 ms {Frames.CountOver(33)}, >50 ms {Frames.CountOver(50)}, >100 ms {Frames.CountOver(100)}; baseline before open p50 {Fmt.Ms(BaselineP50)} p95 {Fmt.Ms(BaselineP95)}");
            sb.AppendLine($"  queues: JobScheduler max {_maxJobQueue}, icon render queue max {_maxRenderQueue}");
            sb.AppendLine($"  FastTraderCells: {(HurryitupPlugin.FastTraderCells.Value ? "ON" : "off")}, SpreadStashCells: {(HurryitupPlugin.SpreadStashCells.Value ? "ON" : "off")}, cell budget {Fmt.Ms(HurryitupPlugin.CellBudgetMs.Value)} ms");
            sb.AppendLine($"  trader grid timeline (ms: cells/visible/drawn): {_timeline}");
            sb.AppendLine($"  QuestPanelOncePerFrame: {(HurryitupPlugin.QuestPanelOncePerFrame.Value ? "ON" : "off")}; repeat calls skipped {QuestPanelSkipped}");
            sb.AppendLine($"  AqcStashCountCache: {(HurryitupPlugin.AqcStashCountCache.Value ? "ON" : "off")} ({AqcCompat.Status}); stash walks {AqcCompat.Scans - _aqcScansAtOpen} for {AqcCompat.Served - _aqcServedAtOpen} cell answers");
            sb.AppendLine($"  FastIconRender: {(HurryitupPlugin.FastIconRender.Value ? "ON, budget " + Fmt.Ms(HurryitupPlugin.RenderBudgetMs.Value) + " ms" : "off")}; its captures {FastRender.Captures - _fastCapturesAtOpen}, frames with 2+ captures {FastRender.FramesWithMultipleCaptures - _fastMultiFramesAtOpen}");
            sb.AppendLine($"  memory: icons in memory {MemoryAtOpen.Icons} -> {MemoryAtEnd.Icons} ({Fmt.Mb(MemoryAtOpen.IconBytes)} -> {Fmt.Mb(MemoryAtEnd.IconBytes)} MB incl. CPU copies), mono used {Fmt.SignedMb(MemoryAtEnd.MonoUsed - MemoryAtOpen.MonoUsed)} MB, Unity allocated {Fmt.SignedMb(MemoryAtEnd.UnityAllocated - MemoryAtOpen.UnityAllocated)} MB, Unity reserved {Fmt.Mb(MemoryAtEnd.UnityReserved)} MB");
            return sb.ToString();
        }

        public List<KeyValuePair<string, string>> CsvFields(string runLabel, string launchId, bool coldCache)
        {
            List<KeyValuePair<string, string>> f = new List<KeyValuePair<string, string>>();
            void Add(string k, string v) => f.Add(new KeyValuePair<string, string>(k, v));
            void Sum(string k, string section) => Add(k, Fmt.Ms(SectionSeries(section).Sum));
            void Max(string k, string section) => Add(k, Fmt.Ms(SectionSeries(section).Max));

            Add("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Add("plugin_version", HurryitupPlugin.PluginVersion);
            Add("run_label", runLabel);
            Add("launch", launchId);
            Add("cold_cache", coldCache ? "1" : "0");
            Add("fast_render", HurryitupPlugin.FastIconRender.Value ? "1" : "0");
            Add("render_budget_ms", Fmt.Ms(HurryitupPlugin.RenderBudgetMs.Value));
            Add("fast_captures", (FastRender.Captures - _fastCapturesAtOpen).ToString());
            Add("fast_multi_frames", (FastRender.FramesWithMultipleCaptures - _fastMultiFramesAtOpen).ToString());
            Add("fast_trader_cells", HurryitupPlugin.FastTraderCells.Value ? "1" : "0");
            Add("spread_stash_cells", HurryitupPlugin.SpreadStashCells.Value ? "1" : "0");
            Add("cell_budget_ms", Fmt.Ms(HurryitupPlugin.CellBudgetMs.Value));
            Add("aqc_cache", HurryitupPlugin.AqcStashCountCache.Value ? "1" : "0");
            Add("aqc_scans", (AqcCompat.Scans - _aqcScansAtOpen).ToString());
            Add("aqc_served", (AqcCompat.Served - _aqcServedAtOpen).ToString());
            Add("quest_once", HurryitupPlugin.QuestPanelOncePerFrame.Value ? "1" : "0");
            Add("quest_skipped", QuestPanelSkipped.ToString());
            Add("open_index", OpenIndex.ToString());
            Add("trader", TraderName);
            Add("trader_id", TraderId);
            Add("open_of_trader", OpenOfThisTrader.ToString());
            Add("end_reason", EndReason);
            Add("duration_ms", Fmt.Ms(Now()));
            Add("assort_loaded_at_open", AssortmentLoadedAtOpen ? "1" : "0");
            Add("assort_ready_ms", Fmt.MaybeMs(_tAssortmentReady));
            Add("grid_shown_ms", Fmt.MaybeMs(_tGridShown));
            Add("cells_complete_ms", _gridViews == 0 ? "" : Fmt.Ms(_tViewsComplete));
            Add("first_visible_ms", Fmt.MaybeMs(_tFirstVisible));
            Add("all_visible_ms", Fmt.MaybeMs(_allVisibleSince));
            Add("visible_count", _visibleAtAll.ToString());
            Add("all_icons_ms", Fmt.MaybeMs(AllIconsDone));
            Add("grid_cells", _gridViews.ToString());
            Add("magnified", _magnified ? "1" : "0");
            Add("async_build", _asyncBuild == true ? "1" : _asyncBuild == false ? "0" : "");
            Add("req_total", TotalRequests.ToString());
            Add("req_distinct", _uniqueHashes.Count.ToString());
            Add("req_memory", _requests[0].ToString());
            Add("req_inflight", _requests[1].ToString());
            Add("req_disk", _requests[2].ToString());
            Add("req_render", _requests[3].ToString());
            Add("unfinished", _neverFinished.ToString());
            Add("disk_latency_p50", Fmt.Ms(_iconLatency[2].Percentile(50)));
            Add("disk_latency_max", Fmt.Ms(_iconLatency[2].Max));
            Add("render_latency_p50", Fmt.Ms(_iconLatency[3].Percentile(50)));
            Add("render_latency_max", Fmt.Ms(_iconLatency[3].Max));
            Add("worst_frame_ms", Fmt.Ms(_worstFrame));
            Add("worst_frame_at_ms", Fmt.Ms(_worstFrameAt));
            Add("worst_frame_sections", _worstFrameSections);
            Sum("dealshow_sum", Sections.DealShow);
            Sum("updategridviews_sum", Sections.UpdateGridViews);
            Max("updategridviews_max", Sections.UpdateGridViews);
            Sum("tradergrid_show_sum", Sections.TraderGridShow);
            Sum("stashgrid_show_sum", Sections.StashGridShow);
            Sum("stashpanel_show_sum", Sections.StashPanelShow);
            Add("trader_cells", SectionSeries(Sections.TraderCell).Count.ToString());
            Sum("trader_cells_sum", Sections.TraderCell);
            Add("other_cells", SectionSeries(Sections.OtherCell).Count.ToString());
            Sum("other_cells_sum", Sections.OtherCell);
            Sum("autoexchange_sum", Sections.AutoExchange);
            Sum("cellpart_newtrading_sum", Sections.CellNewTrading);
            Sum("cellpart_newgrid_sum", Sections.CellNewGrid);
            Sum("cellpart_newitem_sum", Sections.CellNewItem);
            Sum("cellpart_init_sum", Sections.CellInit);
            Sum("cellpart_staticinfo_sum", Sections.CellStaticInfo);
            Sum("cellpart_info_sum", Sections.CellInfo);
            Sum("cellpart_questpanel_sum", Sections.CellQuestPanel);
            Sum("cellpart_compound_sum", Sections.CellCompoundInfo);
            Sum("forceupdatecanvases_sum", Sections.ForceUpdateCanvases);
            Sum("getitemicon_sync_sum", Sections.GetItemIcon);
            Sum("loadimage_sync_sum", Sections.LoadImage);
            Max("loadimage_sync_max", Sections.LoadImage);
            Sum("capture_sync_sum", Sections.Capture);
            Max("capture_sync_max", Sections.Capture);
            Sum("save_sync_sum", Sections.Save);
            Sum("iconchanged_sync_sum", Sections.IconChanged);
            Sum("probe_overhead_sum", Sections.ProbeOverhead);
            Add("prefab_wall_p50", Fmt.Ms(PrefabWall.Percentile(50)));
            Add("mip_wall_p50", Fmt.Ms(MipWall.Percentile(50)));
            Add("mip_timeouts", MipTimeouts.ToString());
            Add("refresh_assort_wall", Fmt.Ms(RefreshAssortmentWall.Max));
            Add("prices_request_wall", Fmt.Ms(PricesRequestWall.Max));
            Add("assort_request_wall", Fmt.Ms(AssortmentRequestWall.Max));
            Add("frames", Frames.Count.ToString());
            Add("frame_p50", Fmt.Ms(Frames.Percentile(50)));
            Add("frame_p95", Fmt.Ms(Frames.Percentile(95)));
            Add("frame_max", Fmt.Ms(Frames.Max));
            Add("frames_over_33", Frames.CountOver(33).ToString());
            Add("frames_over_50", Frames.CountOver(50).ToString());
            Add("frames_over_100", Frames.CountOver(100).ToString());
            Add("baseline_p50", Fmt.Ms(BaselineP50));
            Add("baseline_p95", Fmt.Ms(BaselineP95));
            Add("job_queue_max", _maxJobQueue.ToString());
            Add("render_queue_max", _maxRenderQueue.ToString());
            Add("icons_mem_open", MemoryAtOpen.Icons.ToString());
            Add("icons_mem_end", MemoryAtEnd.Icons.ToString());
            Add("icon_mb_open", Fmt.Mb(MemoryAtOpen.IconBytes));
            Add("icon_mb_end", Fmt.Mb(MemoryAtEnd.IconBytes));
            Add("mono_mb_delta", Fmt.SignedMb(MemoryAtEnd.MonoUsed - MemoryAtOpen.MonoUsed));
            Add("unity_mb_delta", Fmt.SignedMb(MemoryAtEnd.UnityAllocated - MemoryAtOpen.UnityAllocated));
            Add("unity_reserved_mb", Fmt.Mb(MemoryAtEnd.UnityReserved));
            return f;
        }

        private static string SafeName(Trader trader)
        {
            try
            {
                return trader?.LocalizedName ?? trader?.Id ?? "?";
            }
            catch
            {
                return trader?.Id ?? "?";
            }
        }
    }

    internal struct MemorySnapshot
    {
        public int Icons;
        public long IconBytes;
        public long MonoUsed;
        public long UnityAllocated;
        public long UnityReserved;

        /// <summary>
        /// Icon bytes count each icon texture's pixels once for the GPU and once more when the
        /// texture is still CPU-readable, which every cache-loaded and rendered icon is.
        /// (Process.WorkingSet64 reads 0 under the game's Mono, so Unity's reserved total stands in.)
        /// </summary>
        public static MemorySnapshot Take()
        {
            MemorySnapshot s = new MemorySnapshot();
            try
            {
                if (Singleton<ItemIconCreator>.Instantiated)
                {
                    foreach (ItemIcon icon in Singleton<ItemIconCreator>.Instance._memoryCacheIndex.Values)
                    {
                        s.Icons++;
                        Sprite sprite = icon?.Sprite;
                        if (sprite == null) continue;
                        Texture2D tex = sprite.texture;
                        if (tex == null) continue;
                        long bytes = (long)tex.width * tex.height * 4;
                        s.IconBytes += tex.isReadable ? bytes * 2 : bytes;
                    }
                }
                s.MonoUsed = Profiler.GetMonoUsedSizeLong();
                s.UnityAllocated = Profiler.GetTotalAllocatedMemoryLong();
                s.UnityReserved = Profiler.GetTotalReservedMemoryLong();
            }
            catch (Exception e)
            {
                HurryitupPlugin.Log.LogWarning("memory snapshot failed: " + e.Message);
            }
            return s;
        }
    }
}
