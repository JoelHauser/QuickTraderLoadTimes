using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// Fix candidate 2: build grid cells within a per-frame time budget.
    ///
    /// Measured 2026-10-01 (0.2.0): with every icon already cached, a trader's grid still fills
    /// over 1.6-3.5 s, because GridView.MagnifyIfPossible creates one cell and then waits a frame
    /// (when the grid starts empty and _isAsyncAllowed is set, which the trader grid has), and
    /// each cell costs 2.5-5 ms. On every trader switch the stash grid (_isAsyncAllowed off)
    /// rebuilds its ~109 cells in a single frame: the 260-440 ms freeze.
    ///
    /// This is GridView.MagnifyIfPossible(Rect, bool) line for line, except:
    ///   - FastTraderCells: where the game would wait a frame after every cell, it waits only once
    ///     the frame's CellBudgetMs is used up (the first cell in a frame always goes ahead).
    ///   - Cells are created top row first, left to right, so the top of the grid fills first.
    ///   - SpreadStashCells: the trader screen's stash grid, which the game builds in one frame,
    ///     is built the same budgeted way, alongside the trader grid (0.5.0; 0.4.0 made it wait
    ///     for the trader grid). Grids filling at the same time split each frame's budget evenly.
    /// Only the trader screen's two grids are touched (0.7.0; before, any grid that builds
    /// gradually was). Every other grid, and every case the game does not spread (scrolling an
    /// existing grid), goes to the game's own method untouched.
    ///
    /// A grid stays ours until its build finishes: 0.3.0 let go after the first cell, so the
    /// game's next call (GridViewMagnifier forces one a frame after Show) found a non-empty grid
    /// and built every remaining stash cell in one frame, which moved the freeze instead of
    /// removing it.
    /// </summary>
    internal static class FastCells
    {
        private static readonly AccessTools.FieldRef<GridView, Rect> CachedRect =
            AccessTools.FieldRefAccess<GridView, Rect>("_cachedRect");
        private static readonly AccessTools.FieldRef<GridView, IntRect?> VisibleLocalRect =
            AccessTools.FieldRefAccess<GridView, IntRect?>("_visibleLocalRect");
        private static readonly AccessTools.FieldRef<GridView, CancellationTokenSource> Cancellation =
            AccessTools.FieldRefAccess<GridView, CancellationTokenSource>("_cancellationTokenSource");
        private static readonly AccessTools.FieldRef<GridView, ItemUiContext> UiContext =
            AccessTools.FieldRefAccess<GridView, ItemUiContext>("_itemUiContext");
        private static readonly AccessTools.FieldRef<GridView, Dictionary<string, ItemView>> Views =
            AccessTools.FieldRefAccess<GridView, Dictionary<string, ItemView>>("ItemViews");

        private static int _frame = -1;
        private static readonly Dictionary<GridView, double> SpentThisFrame = new Dictionary<GridView, double>();
        private static readonly Dictionary<GridView, bool> Filling = new Dictionary<GridView, bool>();

        public static int BudgetedBuilds;
        public static int SpreadStashBuilds;

        public static bool Prefix(GridView __instance, Rect rect, bool force, ref Task __result)
        {
            if (!QuickTraderLoadTimesPlugin.FastTraderCells.Value && !QuickTraderLoadTimesPlugin.SpreadStashCells.Value) return true;
            if (Scope.InRaid) return true;
            if (!__instance.IsMagnified || __instance.Grid == null) return true;

            if (Filling.TryGetValue(__instance, out bool fillingStash))
            {
                __result = Magnify(__instance, rect, force, fillingStash);
                return false;
            }

            bool startsEmpty = Views(__instance).Count <= 1;
            bool traderGrid = QuickTraderLoadTimesPlugin.FastTraderCells.Value && startsEmpty && __instance._isAsyncAllowed && IsDealScreenTrader(__instance);
            bool stashGrid = QuickTraderLoadTimesPlugin.SpreadStashCells.Value && startsEmpty && !__instance._isAsyncAllowed && IsDealScreenStash(__instance);
            if (!traderGrid && !stashGrid) return true;

            __result = Magnify(__instance, rect, force, stashGrid);
            return false;
        }

        private static bool IsDealScreenTrader(GridView grid)
        {
            TraderDealScreen screen = Scope.DealScreen;
            return Scope.TraderScreenOpen && ReferenceEquals(grid, screen._traderGridView);
        }

        private static bool IsDealScreenStash(GridView grid)
        {
            TraderDealScreen screen = Scope.DealScreen;
            return Scope.TraderScreenOpen && ReferenceEquals(grid, screen._stashGridView);
        }

        /// <summary>GridView.MagnifyIfPossible(Rect, bool) with budgeted waits; see the class summary.</summary>
        private static async Task Magnify(GridView grid, Rect rect, bool force, bool isStash)
        {
            CachedRect(grid) = rect;
            Vector3 vector = grid.transform.InverseTransformPoint(rect.min);
            Vector3 vector2 = grid.transform.InverseTransformPoint(rect.max) - vector;
            int x = Mathf.RoundToInt(vector.x / 63f);
            int num = Mathf.RoundToInt((0f - vector.y) / 63f);
            int width = Mathf.RoundToInt(vector2.x / 63f);
            int num2 = Mathf.RoundToInt(vector2.y / 63f);
            IntRect intRect = new IntRect(x, num - num2, width, num2);
            if (!force && VisibleLocalRect(grid).Equals(intRect))
            {
                return;
            }
            VisibleLocalRect(grid) = intRect;

            Dictionary<Item, LocationInGrid> inRect = grid.Grid.GetItemsInRect(intRect)
                .ToDictionary(elem => elem.Key, elem => elem.Value);

            Dictionary<string, ItemView> views = Views(grid);
            foreach (string key in new List<string>(views.Keys))
            {
                ItemView itemView = views[key];
                if (!itemView.BeingDragged && !inRect.ContainsKey(itemView.Item))
                {
                    views.Remove(itemView.Item.Id);
                    itemView.Kill();
                }
            }

            Cancellation(grid)?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            Cancellation(grid) = cts;
            CancellationToken token = cts.Token;

            if (isStash) SpreadStashBuilds++;
            else BudgetedBuilds++;
            Filling[grid] = isStash;
            try
            {
                ItemUiContext uiContext = UiContext(grid);
                foreach (KeyValuePair<Item, LocationInGrid> entry in inRect.OrderBy(e => e.Value.y).ThenBy(e => e.Value.x))
                {
                    if (views.ContainsKey(entry.Key.Id)) continue;

                    while (!MayBuildThisFrame(grid))
                    {
                        await Task.Yield();
                        if (token.IsCancellationRequested) return;
                    }
                    long start = Stopwatch.GetTimestamp();
                    grid.CreateItemView(entry.Key, entry.Value, uiContext);
                    SpentThisFrame.TryGetValue(grid, out double spent);
                    SpentThisFrame[grid] = spent + TraderSession.Elapsed(start, Stopwatch.GetTimestamp());
                    if (token.IsCancellationRequested) return;
                }
            }
            finally
            {
                // Let go unless a newer call replaced this build (it now owns the grid). A build
                // cancelled by the grid closing has no successor, so it lets go too.
                if (Cancellation(grid) == cts) Filling.Remove(grid);
            }
        }

        /// <summary>
        /// True while this grid's share of the frame's cell budget lasts (the budget split evenly
        /// between the grids filling right now). Each grid's first cell in a frame always goes ahead.
        /// </summary>
        private static bool MayBuildThisFrame(GridView grid)
        {
            int frame = Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                SpentThisFrame.Clear();
            }
            if (!SpentThisFrame.TryGetValue(grid, out double spent)) return true;
            return spent < QuickTraderLoadTimesPlugin.CellBudgetMs.Value / Math.Max(1, Filling.Count);
        }
    }
}
