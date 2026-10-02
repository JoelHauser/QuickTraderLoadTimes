# Quick Trader Load Times

Faster trader screens for SPT 4.1.x. Opening a trader used to leave spinners across the
trader's items while they appeared a few at a time, and froze for about half a second on every
trader switch.

**Status: 0.7.0, release candidate, not yet released.** Fixes on by default; the measurement harness
is off by default. Measured on one heavily modded SPT 4.1.6 install (2026-10-01) with every fix
on and `CellBudgetMs = 16`: the first items appear about 0.2 s after opening a trader, and
everything in view (trader and stash) is there in about 0.5 s on most traders, down from
1.6-3.0 s. Each item cell costs ~0.8-1.0 ms, down from 2.5-5 ms. The icons were never the
problem: the trader grid built one cell per frame, each cell was slowed mostly by
AllQuestsCheckmarks, and the stash rebuilt in a single frame on every trader switch.

## Compatibility

- **AllQuestsCheckmarks:** two of the fixes stand in for parts of AllQuestsCheckmarks 1.4.0 with
  the same logic, and check their first answers against the mod's own. With any other version of
  AllQuestsCheckmarks they stay off and the log says so; without it they never install.
- **Fika:** Fika 2.4.3's client patches none of the methods this touches. On a Fika headless client
  (no graphics) the plugin installs nothing. The AllQuestsCheckmarks fixes step aside in raid,
  solo or co-op, so the mod's squad-quest marks are unaffected. Not yet run with Fika installed.
- Only the trader screen's two grids are rebuilt differently; every other screen uses the game's
  own grid code.

## What it measures

With `[Measurement] Enabled = true` (off by default, restart required), every time a trader's deal
screen opens the plugin follows that open until every icon visible
in the trader grid is drawn and nothing has finished loading for one second (or the screen
closes, or 30 seconds pass). It then writes:

- a block to `BepInEx\LogOutput.log`, starting `===== Quick Trader Load Times: trader open #N (Trader) =====`
- one row to `BepInEx\plugins\QuickTraderLoadTimes\measurements.csv`

| Metric | What it means |
|---|---|
| `first_visible_ms` | Time from opening the trader to the first icon drawn in the visible part of the trader grid |
| `all_visible_ms` | Time until every item in the visible part of the grid shows its icon |
| `all_icons_ms` | Time until every icon requested during the open has finished |
| `req_memory` / `req_inflight` / `req_disk` / `req_render` | Where each requested icon came from: already in memory, still loading from an earlier request, the PNG cache on disk, or rendered from the 3D model |
| `frame_p50` / `frame_p95` / `frame_max`, `frames_over_33/50/100` | Frame times during the open, next to `baseline_p50` / `baseline_p95` from the 3 seconds of menu before it |
| `icon_mb_open` / `icon_mb_end`, `mono_mb_delta`, `unity_mb_delta` | Icon texture memory and process memory before and after |
| `*_sync_sum` | Main-thread milliseconds spent in each step: PNG decode, render capture, PNG encode, cell refresh |
| `assort_request_wall`, `prices_request_wall` | Server round trips for the trader's stock and prices |

`probe_overhead_sum` is the plugin's own cost (it repeats the game's cache lookup to label
each request). Subtract it when comparing.

## Settings (`BepInEx\config\com.mybutthasarash.quicktraderloadtimes.cfg`)

**Fixes** (all on by default except `FastIconRender`; changes take effect immediately):

- `FastTraderCells`: fills the trader grid with as many cells per frame as `CellBudgetMs`
  allows (top rows first), instead of one per frame.
- `SpreadStashCells`: builds the trader screen's stash over several frames alongside the
  trader grid, instead of in one frame (the freeze on every trader switch).
- `CellBudgetMs` (default 12): per-frame time for building cells, split evenly between the grids
  filling at the time. Higher fills faster; for the fraction of a second it takes, the frame
  rate drops (16 gave about 30-40 FPS on a 60 FPS menu).
- `QuestPanelOncePerFrame`: sets up each new cell's quest checkmark once instead of three times
  in the same frame.
- `AqcStashCountCache` (AllQuestsCheckmarks 1.4.0): counts the stash once and shares the count
  between cells, instead of walking every owned item for every cell. The count is redone on any
  inventory change, and the first 20 answers are checked against the mod's own.
- `AqcQuestIndex` (AllQuestsCheckmarks 1.4.0): indexes the active quests once and answers each
  cell's quest lookup from the index, instead of walking every quest for every cell. Weapons and
  everything in raid use the mod's own lookup. The first 50 answers are checked against the mod's.
- `FastIconRender` / `RenderBudgetMs` (off): renders uncached icons within a per-frame budget
  instead of the game's one icon every two frames. Its icons match the game's own, but it
  measured no faster, because rendering waits on cell creation.

**Measurement** (for testing; restart required):

- `Enabled` (default off): the measurement harness described above.
- `RunLabel` (default `baseline`): written into every CSV row, to tell runs apart.
- `ColdIconCache` (default off): points the item icon cache at an empty throwaway folder
  (`cold-cache\<launch>-vanilla` or `-fast` beside the DLL; the newest 6 are kept), so every
  icon is rendered from scratch. **Your real icon cache is never read or written while it is on.**
- `MaxSessionSeconds`, `SettleSeconds`: when an open stops being measured.

## Checking that fast-rendered icons look right

`scripts\compare-icons.ps1` compares two icon caches pixel by pixel, matching icons by the
game's icon hash. Two vanilla renders of the same item are not always bit-identical (edge and
lighting noise), so compare a vanilla cold run and a fast cold run against the same reference
and look for a difference between them:

```
.\scripts\compare-icons.ps1 -B <cold-cache\...-vanilla>
.\scripts\compare-icons.ps1 -B <cold-cache\...-fast>
.\scripts\compare-icons.ps1 -A <cold-cache\...-vanilla> -B <cold-cache\...-fast>
```

## Measurement protocol

Run each scenario three times, from a fresh game start each time (the server can stay up):

1. **Cold launch, trader A:** start the game, go straight to Traders, open Peacekeeper.
2. **Reopen A:** leave the trader screen, come back, open Peacekeeper again.
3. **Trader B with overlapping items:** then open Mechanic.
4. **Cold cache:** set `ColdIconCache = true`, restart, open Peacekeeper. Set it back afterwards.

Peacekeeper is a good trader A: the game already loads his stock at startup (`AutoExchange`),
so the stock fetch is out of the way and the numbers are about icons.

## Build

```
dotnet build src/QuickTraderLoadTimes/QuickTraderLoadTimes.csproj -c Release -p:SPTPath=H:\SPT4.1.X -p:DeployToSPT=true
dotnet test tests/QuickTraderLoadTimes.Tests
```

The plugin compiles against the game's Assembly-CSharp, which has to be the copy the SPT
Launcher has already patched (start the game once on a new install before building).
