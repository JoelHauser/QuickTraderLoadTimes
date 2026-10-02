# Quick Trader Load Times: notes for the next session

**Status: 1.0.0 released 2026-10-02** (GitHub release v1.0.0, Latest).

- Repo: https://github.com/JoelHauser/QuickTraderLoadTimes (renamed from `Hurryitup` on
  2026-10-02; GitHub redirects the old URLs). Local clone: `H:\SPTMods\Hurryitup` (the folder
  kept the old name).
- Plugin GUID `com.mybutthasarash.quicktraderloadtimes`, the same for both halves. It was renamed
  from "Hurry It Up" before release. Joel's pre-rename DLL and config are in
  `H:\SPTMods\plugin-backups\2026-10-01-hurryitup-rename`.
- Forge description: Joel pastes it himself (header, the before/after GIF
  `https://i.imgur.com/FYzIqI6.gif` with the MP4 linked, What it does / Results / Good to know /
  Install). The Forge renders images but not video, and censors the word "Tarkov".

## What it is

Trader screens and the flea market that open fast. The icons were never the bottleneck. The
time went into building item cells (one per frame), into each cell's setup (mostly
AllQuestsCheckmarks), and into the stash grid rebuilding in one frame on every trader switch.
Measured on Joel's install: everything in view went from 1.6-3.0 s to about 0.4-0.6 s, and the
worst frame from 0.36-0.92 s to 0.1-0.3 s. The History section below has the numbers behind
every step.

| File | What |
|---|---|
| `src/QuickTraderLoadTimes/Scope.cs` | Every fix acts only while the TraderDealScreen or RagfairScreen is open, and never when `AbstractGame.InRaid`. |
| `FastCells.cs` | `FastTraderCells` / `SpreadStashCells` / `CellBudgetMs`: a line-for-line copy of `GridView.MagnifyIfPossible` with per-frame budgeted waits, top rows first, for the deal screen's two grids only. A grid stays ours until its build finishes. |
| `QuestPanelOnce.cs` | `QuestPanelOncePerFrame`: a cell's `SetQuestItemViewPanel` runs 3 times in one frame (Init + 2 UpdateInfo); repeats for the same cell, item and inventory version are skipped. |
| `AqcCompat.cs`, `AqcQuestIndex.cs`, `AqcSupport.cs` | AllQuestsCheckmarks 1.4.0 only: a shared stash count and an index of active quests, replacing per-cell walks. Same answers (self-checked with Measurement on); weapons and anything outside Scope use the mod's own code. |
| `InventoryEvents.cs` | A counter bumped on every ItemController add/remove/refresh event; the shared work above is thrown away when it changes. |
| `FastRender.cs` | `FastIconRender` (off): budgeted icon capture. Its icons are correct, but it measured no faster. |
| `Probes.cs`, `Recorder.cs`, `TraderSession.cs`, `Measure.cs` | The measurement harness (`[Measurement] Enabled`, off by default; nothing installed when off). |
| `src/QuickTraderLoadTimes.Server/` | One line at server start, "Quick Trader Load Times 1.0.0 loaded", plus the mod's entry in the server's mod list. |
| `scripts/pack.ps1`, `scripts/compare-icons.ps1` | Release packaging; pixel diff of two icon caches. |

## Conventions

- Commit as `-c user.name="Joel Hauser" -c user.email=jhauser@bostonlightsource.com`; the
  global git identity on this machine has no name.
- The server line stays **one line**. Joel tried a 5-line ASCII-art banner and called it
  "kinda obnoxious".
- The release is quiet: no Info-level logging from the plugin unless Measurement is on.
- Nothing may act outside the trader screen and the flea market, or in any raid (Joel's call).
- Measure before claiming: turn `[Measurement] Enabled` on in Joel's cfg for a test round, read
  `BepInEx\plugins\QuickTraderLoadTimes\measurements.csv` (it survives restarts;
  LogOutput.log doesn't), and turn it off again. Check that a config file exists and was read
  before rewriting it: an unchecked PowerShell edit once blanked Joel's.

## Not verified / open

- The one-line server message has only been previewed offline (a recording console rendered to
  PNG), not seen in a running SPT server.
- Fika: checked statically against Fika 2.4.3 (no overlapping patches; Fika patches
  `AbstractGame.InRaid` to `is CoopGame`, which Scope relies on), never run with Fika installed.
- Flea market: covered by Scope, but not measured (the harness follows trader opens only). Opening
  Add Offer builds the visible stash in one frame, the same freeze the trader screen had; the
  stash spreading could cover `AddOfferWindow._gridView` after measuring it.
- Tested on SPT 4.1.6 only.
- Ideas not built: fetch each trader's stock while idle in the menu (the first visit waits
  0.2-0.7 s on the server; the game already does this for Peacekeeper and Skier through
  AutoExchange); shorten TraderDealScreen.Show's own ~130-200 ms first frame; icon-cache
  invalidation (bundle checksums from `SPT_Runtime\user\cache\bundleHashCache.json`) for the 2
  blank and 1 stale-size cached icons found on Joel's install.
- AllQuestsCheckmarks upstream (ZGFueDkx): the per-cell stash walk and quest walk deserve a fix
  in the mod itself; no report has been sent yet.

## History (newest last)

### The original plan (0.1.0; the measurements changed it)

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

### Measured (0.1.0, 2026-10-01, 13 normal + 7 cold-cache opens; CSV in the install)

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

### Measured (0.2.0, 2026-10-01: launches freeze / cold-vanilla / cold-fast)

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

### Measured (0.3.0, 2026-10-01: cells-off vs a launch with SpreadStashCells + FastIconRender on)

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

### Measured (0.4.0 all-on, 2026-10-01; Joel: "the items are loading so fast now")

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

### Measured (0.5.0 "together", 2026-10-01) and 0.6.0

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

### Measured (0.6.0 "fast16", 2026-10-01; Joel: "its incredibly fast now wow")

- Self-checks: AQC stash count 20/20, AQC quest index 50/50, probes 34 applied, 0 failed.
- Per cell 1.35 -> ~0.8-1.0 ms; the quest panel per open 100-300 -> 20-100 ms.
- Clean opens: Skier first open, all in view 476 ms (0.5.0: 754; fixes off: 1,992); first icon
  ~170-250 ms on most traders; stash done 50-460 ms. Prapor as the first trader after launch:
  1,515 ms, waiting on ~0.7 s of server requests.
- Cost: frame p95 28-60 ms during the fill (0.5.0: 17-27) with CellBudgetMs 16; Joel didn't
  notice it. Several rows include scrolling and tab switching (many disk loads, 128 new renders on
  Peacekeeper), so their all_visible/cells_complete aren't open-to-done times.

### 0.7.0, release prep (2026-10-01; ran in game as part of 0.8.0)

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

### 0.8.0, scope (2026-10-01; Joel: "it runs flawlessly")

Joel: "this should only effect traders and the flea market too ... nothing in raid". Scope.cs:
every fix acts only while the TraderDealScreen or RagfairScreen is open (activeInHierarchy;
RagfairScreen.Show tracked like TraderDealScreen.Show) and never when AbstractGame.InRaid. Before
this, QuestPanelOnce ran on every cell everywhere (raids included), and the AQC fixes on every
screen out of raid. The gate is per screen, not per cell, because a new cell comes from a pool
and is only parented under its grid after it is set up. The flea's AddOfferWindow is
ItemUiContext's shared window, not a child of RagfairScreen; it is covered because RagfairScreen
stays open under it. The BTR driver's trader in raid is excluded by the raid check. Fika patches
AbstractGame.InRaid to `is CoopGame`, so the check is right under Fika too (true in Fika raids,
false in the hideout). The flea market is covered but not measured: the measurement harness only
follows trader opens.

### 1.0.0 (2026-10-01)

- Joel ran 0.8.0 ("it runs flawlessly"): no errors from the mod, 29/29 probes applied, AQC
  self-checks 20/20 and 50/50. Everything in view on 0.42-0.61 s for most traders; worst frame
  0.1-0.27 s (0.47 s for Prapor, the first trader after launch).
- Release logging: the startup summary is Debug level; only fixes that failed to install log a
  warning. The AQC self-checks run only with Measurement on, and their failure warnings log once.
  Measurement is off by default, and Joel's cfg is set back to off.
- New server half (src/QuickTraderLoadTimes.Server, net10.0, SPTarkov.Server.Core 4.1.2): one
  line at server start, "Quick Trader Load Times 1.0.0 loaded", the name on an amber-to-violet
  gradient, plus "(game plugin not found next to this server)" on the same line when the BepInEx
  plugin isn't one folder up. It's written straight to Spectre's AnsiConsole (SPT's
  ConsoleLogHandler uses AnsiConsole.MarkupLine, its dispatcher is synchronous, and the console
  format is %message%, so it lands in order); if that throws, the same line goes through
  ISptLogger in plain text. History: Joel first asked for "something fun", got a 5-line FigletText
  banner with a trader quip, saw it in his server, called it "kinda obnoxious" and asked for one
  line, because a mod shouldn't be loud at startup. **Keep it to one line.**
- scripts/pack.ps1: checks that all five version numbers agree, builds, tests (25), writes
  dist\QuickTraderLoadTimes-<v>.zip with forward-slash entries, and installs with -Install.
- A PowerShell edit blanked Joel's cfg once (the file was momentarily missing and the failed
  read was written back); it was restored from known values (CellBudgetMs 16, measurement off).

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

- `scripts\pack.ps1 [-SPTPath H:\SPT4.1.X] [-Install]` is the only way to build a release: it
  refuses to pack unless all five version numbers agree (plugin csproj, PluginVersion, server csproj,
  ModMetadata, StartupBanner.Version), builds both halves, runs the tests (22), and writes
  `dist\QuickTraderLoadTimes-<v>.zip` with forward-slash entries (PS 5.1's Compress-Archive writes
  backslashes). Run it from PowerShell, not Bash. `dist\` is gitignored; release notes are drafted
  there as `release-notes-<v>.md`.
- The plugin compiles against the SPT-patched `Assembly-CSharp` (the csproj refuses an unpatched
  one). Joel's other client mods resolve by name at runtime instead. Every SPT install runs the
  patched copy at runtime anyway, so this only matters for building: start the game once on a new
  install first.
- The server half needs the user-local .NET 10 SDK (`%USERPROFILE%\.dotnet\dotnet.exe`; the one
  on PATH is SDK 8).
- Patch targets were checked offline with `System.Reflection.MetadataLoadContext` (game methods,
  AllQuestsCheckmarks 1.4.0's StashHelper/QuestsHelper, spt-reflection's ClientAppUtils): each
  resolves to exactly one method and the parameter names match. Patches on the generic
  `IconCreatorBase<Item, ItemIcon>` are shared with the clothing and player icon creators; the
  probes and FastRender filter on `__instance is ItemIconCreator`.
- Each fix and probe is applied separately (Fixes.cs, Probes.cs). A fix that fails to install
  logs one warning at startup; the rest still work.
