# Quick Trader Load Times: notes for the next session

Renamed from "Hurry It Up" to **Quick Trader Load Times** on 2026-10-01, before any release: plugin
GUID com.mybutthasarash.quicktraderloadtimes, DLL and plugin folder QuickTraderLoadTimes. The
local folder (H:\SPTMods\Hurryitup) and the GitHub repo (JoelHauser/Hurryitup) still have the
old name. Joel's old DLL and config are in H:\SPTMods\plugin-backups6-10-01-hurryitup-rename.

Goal: make trader item icons appear sooner on the first trader visit after launch, without
stutters, extra memory, stale icons or any change to live trader data (prices, stock, limits,
unlocks). Joel wants a **measured** improvement: no fix ships without before/after rows from
`measurements.csv`.

## Phase plan

1. **Measure (0.1.0, this build).** Probes only, no behaviour change. Run the README protocol.
2. **Decide from the numbers.** The candidates:
   - **Render path dominates** (many `req_render`, gradual icons): fill the per-frame time
     budget instead of waiting fixed frames, render visible items first, and move PNG encoding
     off the main thread. This removes work rather than moving it.
   - **Disk path dominates** (one big hitch): prewarm the traders whose stock the game already
     loads at startup, a few icons per frame while the menu is idle. This moves work earlier and
     doesn't remove any. Possibly also a faster on-disk format.
   - Either way, add cache invalidation (below).
3. Re-run the same protocol with a new `RunLabel` and compare.

## Measured (0.1.0, 2026-10-01, 13 normal + 7 cold-cache opens; CSV in the install)

- **Normal cache: icons are not the bottleneck.** First visible icon 450-840 ms (Prapor's first
  open 1.4 s). The stock request takes 455-770 ms and the price request 742 ms; prices come
  first on a trader's first open. Disk loads are cheap: Prapor had 223 of them with 31 ms of
  decoding in total.
- **Every open has one frozen frame of 365-980 ms**, even when every icon came from memory.
  With the stock already loaded the grid is up at 70-125 ms, but the first icon only appears
  after that frame (~450-530 ms), so this freeze directly delays the first icon. The cold log
  places it inside TraderDealScreen.Show (440-605 ms when the stock is already loaded); the
  trader grid accounts for only 7-90 ms of it. 0.1.1+ probes break it down (UpdateGridViews, the
  stash grid and panel, cells, AutoExchange, ForceUpdateCanvases, the worst frame's sections).
- **Cold renders are held back by pacing, not work:** about one icon per 2 frames (30-35/s at
  60 FPS) against ~5.6 ms of main-thread work each (capture 5.0, PNG save 0.6). Prapor cold:
  121 renders, 7.3 s until every visible icon showed. FastIconRender (0.2.0) targets this.
- The trader grid only builds the cells you can see (60-93 of them). Stash icons share the
  render queue.
- 0.1.0 metric bugs fixed in 0.1.1: "all visible" fired while the grid was still adding cells;
  working set read 0 under Mono (now Unity reserved memory); "all icons done" read 0.0 when
  nothing had to load.
- Cache correctness (compare-icons.ps1, vanilla cold renders vs the real cache): of 390 icons
  in both, 75 were identical, 289 within tolerance and 26 different. Real problems among them: a
  magazine cached at 64x127 that now renders at 64x190 (a mod resized it), and 2 of 5,053 cached
  icons are completely blank (hashes 210800287 from 8/21 and 1160038103 from 9/18). Both will
  stay wrong until the cache gets invalidation. The rest of the differences are render-to-render
  noise.

## Measured (0.2.0, 2026-10-01: launches freeze / cold-vanilla / cold-fast)

- **Icons only appear as cells get created, and cells are what's slow.** The trader grid has
  `_isAsyncAllowed` set, so `GridView.MagnifyIfPossible` creates one cell and then waits a frame
  (Task.Yield). Even with every icon cached, the last cell lands 1.6-3.5 s after opening (e.g.
  Peacekeeper: grid at 99 ms, last cell at 1,994 ms, all visible at 2,011 ms). Each cell costs
  2.5-5 ms on the main thread.
- **The freeze is the stash grid rebuild.** UpdateGridViews -> stash grid Show -> ~109 stash
  cells at ~2.5 ms each = 260-440 ms in one frame, on every trader switch (ShouldUpdateStashGrid
  compares the trader's hash). Only UltrawideStash, UIFixes (SyncStashScroll, OpenSearch) and
  StashPanelAdPatch patch SimpleStashPanel.Show, and that whole method costs only 8-43 ms.
- **FastIconRender made no difference** (Prapor cold: 6,968 ms all visible vs 6,815 vanilla),
  even with 36 frames capturing 2 or more icons. Renders are fed by cell creation, and Prapor's
  first open is held up by first-time bundle loading (render wall avg 3.9 s against ~4 ms of
  capture). Its icons are correct: vanilla vs fast, 282 of 283 identical or within tolerance;
  the one difference is the camo pattern's placement on a weapon.
- 0.3.0 adds FastTraderCells / SpreadStashCells / CellBudgetMs (FastCells.cs, a line-for-line
  copy of MagnifyIfPossible with budgeted waits), plus "cell part:" probes to find what inside
  a cell costs 2.5-5 ms.

## Measured (0.3.0, 2026-10-01: cells-off vs a launch with SpreadStashCells + FastIconRender on)

- Joel's second launch had FastTraderCells OFF and FastIconRender ON (a mix-up), so only the stash
  spreading was tested.
- **SpreadStashCells 0.3.0 moved the freeze instead of removing it.** UpdateGridViews dropped from
  ~345-387 ms to 60-138 ms, but the worst frame (300-600 ms) now held all ~109 stash cells: the
  game's next MagnifyIfPossible call (GridViewMagnifier forces one a frame later) saw a non-empty
  grid and built everything synchronously. 0.4.0 keeps the grid until its build finishes.
- **The cost of a cell is AllQuestsCheckmarks 1.4.0.** "cell part" sums per open: SetQuestItemViewPanel
  370-745 ms, about 70% of all cell time (~2-3.5 ms per cell). Its QuestItemViewPanel.Show prefix
  replaces the game's method for every item; out of raid, StashHelper.GetItemsInStash walks every
  owned item (Inventory.GetPlayerItems()) once per cell. Vanilla returns early for ordinary items.
  0.4.0's AqcCompat (AqcStashCountCache) answers it from one shared count, which is thrown away on any
  ItemController add/remove/refresh event, after 2 idle frames, or after 1 s, and self-checks its
  first 20 answers against the mod's own count. The proper fix belongs upstream (ZGFueDkx).
- Also from these rows: NewTradingItemView (trading setup, inside the cell) is the next biggest cost
  at ~2 ms per trader cell.

## Measured (0.4.0 all-on, 2026-10-01; Joel: "the items are loading so fast now")

- All visible drawn: 0.5-1.4 s on a trader's first open (0.3.0 fixes off: 1.6-3.0 s); 0.4-0.6 s
  on reopens. Worst frame mostly 100-210 ms (was 360-920).
- AqcCompat: 1-3 stash walks per open instead of ~200, and the self-check matched 20 of 20.
- Still left: SetQuestItemViewPanel runs 3 times per new cell (Init plus two UpdateInfo calls;
  603 calls for 201 cells, ~0.5 ms each with AllQuestsCheckmarks): 0.5.0 QuestPanelOncePerFrame.
  TraderDealScreen.Show itself takes ~170 ms on reopens. Reopens report first visible = all
  visible (~600 ms) although the grid is up at 60-95 ms, which isn't explained yet; 0.5.0 logs a
  timeline (ms: cells/visible/drawn).
- Joel asked for the stash to fill at the same time as the trader: 0.5.0 drops the wait and
  splits each frame's CellBudgetMs evenly between the grids filling.

## Measured (0.5.0 "together", 2026-10-01) and 0.6.0

- Corrected "all items in view" (later of all_visible_ms and cells_complete_ms; 0.5.0's
  all_visible_ms fired once the first few cells, already drawn, were all there was): Peacekeeper
  1,945 ms with fixes off -> 1,059 (0.4.0) -> 650 first open / 573 reopen (0.5.0). Most traders
  0.4-0.9 s. 0.6.0 resets all_visible when a new cell arrives, and logs stash_complete_ms.
- Per cell ~1.35 ms: AQC's single remaining quest-panel call ~0.57 ms (QuestsHelper.
  GetActiveQuestsWithItem walks every started quest's every condition), cell setup ~0.2 ms,
  UpdateInfo x2 + UpdateStaticInfo + Init ~0.25 ms, pool residual ~0.3 ms. The two UpdateInfo
  calls (one from BindEvent(AssortmentUpdated) inside NewTradingItemView, one from Init) can't
  safely be cut: UpdateStaticInfo runs between them and IsSearched gates the icon.
- 0.6.0 AqcQuestIndex: one index of template -> quest matches per burst (same rules as the mod;
  weapons go to the mod), the prefix closed over the mod's internal CurrentQuest with
  MakeGenericMethod so the out parameters match exactly, and the first 50 answers checked. Joel's
  test config: CellBudgetMs 16 (option 1), RunLabel fast16.

## Measured (0.6.0 "fast16", 2026-10-01; Joel: "its incredibly fast now wow")

- Self-checks: AQC stash count 20/20, AQC quest index 50/50, probes 34 applied, 0 failed.
- Per cell 1.35 -> ~0.8-1.0 ms; the quest panel per open 100-300 -> 20-100 ms.
- Clean opens: Skier first open, all in view 476 ms (0.5.0: 754; fixes off: 1,992); first icon
  ~170-250 ms on most traders; stash done 50-460 ms. Prapor as the first trader after launch:
  1,515 ms, waiting on ~0.7 s of server requests.
- Cost: frame p95 28-60 ms during the fill (0.5.0: 17-27) with CellBudgetMs 16; Joel didn't
  notice it. Several rows include scrolling and tab switching (many disk loads, 128 new renders on
  Peacekeeper), so their all_visible/cells_complete aren't open-to-done times.

## 0.7.0, release prep (2026-10-01, not yet run in game)

- Measurement off by default ([Measurement] Enabled, restart required); when off, no probes are
  installed. The fixes' patches live in Fixes.cs and are always installed; each fix checks its own
  setting per call.
- Defaults: all fixes on except FastIconRender; CellBudgetMs 12 (Joel's cfg keeps 16).
- FastTraderCells only touches TraderDealScreen._traderGridView (it used to touch any grid that
  builds gradually).
- AllQuestsCheckmarks fixes install only for 1.4.0 exactly (AqcSupport; soft BepInDependency so
  the version is known). InventoryEvents (the change counter) is always installed, so
  QuestPanelOnce no longer depends on AQC being present. AqcQuestIndex now also steps aside in raid.
- Fika: Fika 2.4.3 (Fika.Core.dll, EFT 0.16.9.40743) was decompiled into a scratchpad; none of its
  patches touch our targets (TraderDealScreen, grids, item views, quest panel, icon creator,
  ItemController events). AQC's Fika code (SquadQuests, FikaBridge) is outside what we replace.
  The plugin does nothing when Application.isBatchMode or there is no graphics device (headless).
  Fika is not installed on Joel's machine, so this is static analysis only.

## How the game loads icons (from the 4.1.x client, decompiled 2026-10-01)

- Each cell calls `ItemView.RefreshIcon` -> `ItemViewFactory.LoadItemIcon` ->
  `ItemIconCreator.GetItemIcon(item, in size, forcedGeneration)`, which is keyed by
  `IconsHash.GetItemHash(item)`:
  - **Memory**: `_memoryCacheIndex[hash]`, never evicted during a session.
  - **Disk**: `_fileCacheIndex[hash]` -> `<n>.png`, loaded by `LoadFromUserCacheAsync`. The read is
    async; `Texture2D.LoadImage` (the decode) runs on the main thread. Nothing throttles it, so a
    batch of disk loads all finish within a frame or two.
  - **Render**: `FillIconWithNewSpriteAsync` -> `RenderModel` (load the bundles, create the prefab
    with `CreateCleanLootPrefabAsync`, wait for mip 0 with `RequestMipZero`), then
    `CaptureSpriteOfModel`, which renders, blits and `ReadPixels` on the main thread. Captures run
    one at a time behind `_isIconCreating`, with `JobScheduler.Yield()` both before and after. The
    PNG is saved only if mip 0 loaded (`requireZeroMip: true`), and the `EncodeToPNG` runs on the
    main thread. `index.json` is rewritten whenever the render queue empties.
- `Diz.Jobs.JobScheduler` runs continuations in `LateUpdate`, only while the frame so far is under
  `FrameTicks` (1000 / lobby FPS limit) and for at most half of that. After `SlowFrames` starved
  frames it runs them anyway.
- Hash: template ID, plus each child's slot name, its parent's template ID, grid location and
  stack index, plus toggle, fold and magazine-fill state. This is deterministic across launches
  (`MongoID.GetHashCode` is arithmetic). Mods that change it (7Bpencil WeaponCamo and
  MaterialEditor, Tyfon.WeaponCustomizer, COTI) only touch items with decals, custom materials,
  customizations or the COTI device; fresh trader stock is unaffected.
- **Cache location under SPT:** `SPT_Runtime\user\sptappdata\live\` (SPT redirects
  `Application.temporaryCachePath`; `live` is `MatchingVersion`; the item creator's subfolder is
  empty). It is **not** under `%TEMP%\Battlestate Games`. On 2026-10-01 it held 5,037 icons,
  69 MB on disk and ~180 MB decoded; the index and the files matched exactly.
- **The cache never invalidates.** The folder key is the constant `live`, so a mod that changes
  a model without changing the item ID keeps its old icon. COTI ships `CotiIconCacheInvalidator`
  for exactly that case. A fix: record each new icon's bundle checksums (SPT keeps
  `SPT_Runtime\user\cache\bundleHashCache.json`) plus the client build, and drop icons whose
  files changed.
- Trader screen: `TraderDealScreen.Show` -> `UpdateGridViews` rebuilds the trader grid only when the
  trader changed (`trader != _lastTrader`) or the stock's hash changed, so reopening the same trader
  normally reuses the views. `Trader.RefreshAssortment` fetches prices, then the stock, one after
  the other. `AutoExchange` loads Peacekeeper's and Skier's stock at startup.
- Whether the trader grid only builds views for visible items (`GridViewMagnifier`, a setting in
  the prefab) can't be read from code; the measurement logs `magnified`.

## Joel's install, for reading the numbers

`Graphics.ini` (in `SPT_Runtime\user\sptSettings`): LobbyFramerate 60 (a 16.7 ms JobScheduler
frame budget), MipStreaming false (so `RequestMipZero` should return almost at once), VSync off,
Reflex OnAndBoost. `H:` is NVMe.

## Build notes

- Compiles against the patched `Assembly-CSharp` (the csproj refuses an unpatched one). Joel's
  other client mods resolve by name at runtime instead; this is a harness for his own install.
- Patch targets were checked offline with `System.Reflection.MetadataLoadContext`: all 15 resolve
  to exactly one method, and the parameter names match. Patches on the generic
  `IconCreatorBase<Item, ItemIcon>` (`LoadFromUserCacheAsync`, `CaptureSpriteOfModel`,
  `SaveIconAsync`) are shared with the clothing and player icon creators; the probes filter on
  `__instance is ItemIconCreator`.
- Each probe is applied separately; the log line `probes applied: N; failed: M -> ...` names
  any that failed.
