# Hurry It Up

Faster trader screens for SPT 4.1.x. Opening a trader used to leave spinners across the
trader's items while they appeared a few at a time, and froze for about half a second on every
trader switch.

**Status: 0.5.0, a measurement harness with switchable fixes, all off by default.** Measured on
one heavily modded install (2026-10-01), every item in view now shows in 0.4-0.9 s, down from
1.6-3.0 s, and the worst frame is 100-220 ms, down from 360-920 ms. The icons were never the
problem: the trader grid built one cell per frame, each cell cost 2.5-5 ms (mostly
AllQuestsCheckmarks), and the stash rebuilt in a single frame on every switch.

## What it measures

Every time a trader's deal screen opens, the plugin follows that open until every icon visible
in the trader grid is drawn and nothing has finished loading for one second (or the screen
closes, or 30 seconds pass). It then writes:

- a block to `BepInEx\LogOutput.log`, starting `===== Hurryitup: trader open #N (Trader) =====`
- one row to `BepInEx\plugins\Hurryitup\measurements.csv`

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

## Settings (`BepInEx\config\com.mybutthasarash.hurryitup.cfg`)

- `RunLabel` (default `baseline`): written into every CSV row, to tell runs apart.
- `ColdIconCache` (default off, restart required): points the item icon cache at an empty
  throwaway folder (`cold-cache\<launch>-vanilla` or `-fast` beside the DLL; the newest 6 are
  kept), so every icon is rendered from scratch. **Your real icon cache is never read or written while it is on.**
- `MaxSessionSeconds`, `SettleSeconds`: when an open stops being measured.
- `FastTraderCells`: fills the trader grid with as many cells per frame as `CellBudgetMs`
  allows (top rows first), instead of one per frame.
- `SpreadStashCells`: builds the trader screen's stash over several frames alongside the
  trader grid, instead of in one frame (the freeze on every trader switch).
- `CellBudgetMs` (default 8): per-frame time for building cells, split evenly between the grids
  filling at the time.
- `AqcStashCountCache`: AllQuestsCheckmarks compatibility. It counts the stash once and shares
  the count between cells, instead of walking every owned item for every cell. The count is
  redone on any inventory change, and the first 20 answers are checked against the mod's own.
- `QuestPanelOncePerFrame`: sets up each new cell's quest checkmark once instead of three times
  in the same frame.
- `FastIconRender` / `RenderBudgetMs`: renders uncached icons within a per-frame budget instead
  of the game's one icon every two frames. Its icons match the game's own, but it gave no
  measurable gain, because rendering is fed by cell creation; leave it off.

All the fixes take effect immediately.

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
dotnet build src/Hurryitup/Hurryitup.csproj -c Release -p:SPTPath=H:\SPT4.1.X -p:DeployToSPT=true
dotnet test tests/Hurryitup.Tests
```

The plugin compiles against the game's Assembly-CSharp, which has to be the copy the SPT
Launcher has already patched (start the game once on a new install before building).
