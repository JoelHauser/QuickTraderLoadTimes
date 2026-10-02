# Quick Trader Load Times

Trader screens and the flea market that open fast, for SPT 4.1.x.

Opening a trader used to show loading spinners across their items while they trickled in a few
at a time, and every switch to another trader froze the game for about half a second. With this
mod the first items appear in 0.2-0.4 s and everything in view, trader and stash, is there in
about half a second.

## What it does

- **Fills the trader's grid fast.** The game builds one item cell per frame; this builds as many
  as fit in a small per-frame time budget, top rows first.
- **No more freeze when switching traders.** The game rebuilt your stash's visible cells all in
  one frame on every switch; this spreads them over a few frames, filling at the same time as the
  trader's items.
- **Makes each item cell cheaper.** Every cell set up its quest checkmark three times in the same
  frame; now it's once.
- **AllQuestsCheckmarks, without the slowdown.** With AllQuestsCheckmarks installed, every item
  cell counted your entire stash and walked every active quest. That was most of the cost of a
  cell. This keeps the same answers but does that work once and shares it between cells.

It only acts on the **trader screen** and the **flea market**, and **never in a raid**.
Everywhere else, the game and your other mods run exactly as before. It doesn't change any
prices, stock or trader data, and it doesn't touch your saved icons or profile.

## Measured

On one heavily modded SPT 4.1.6 install, opening traders in the main menu:

| | Before | With the mod |
|---|---|---|
| Everything in view (trader + stash), most traders | 1.6-3.0 s | about 0.4-0.6 s |
| Peacekeeper, everything in view | 1.9 s | 0.5 s |
| First items visible | 0.4-0.8 s | 0.2-0.4 s |
| Longest single frame (the "freeze") | 0.36-0.92 s | 0.1-0.3 s |
| Cost of one item cell | 2.5-5 ms | 0.8-1.0 ms |

The first trader you open after launching the game is slower than the rest, because it also
waits for the server to send prices and stock. Your numbers will differ with your mods and PC.

## Install

Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`). It contains:

```
BepInEx/plugins/QuickTraderLoadTimes/QuickTraderLoadTimes.dll                 the mod
SPT_Runtime/user/mods/QuickTraderLoadTimes/QuickTraderLoadTimes.Server.dll    a startup banner
```

The BepInEx plugin does all the work. The server part only shows a banner when the SPT server
starts and lists the mod among the server's mods. It's optional.

To uninstall, delete both `QuickTraderLoadTimes` folders.

## Settings

`BepInEx/config/com.mybutthasarash.quicktraderloadtimes.cfg`, or the in-game configuration
manager (F12). The defaults are what was measured above, and changes take effect immediately.

- `CellBudgetMs` (default 12): milliseconds per frame spent building item cells. Higher fills
  the screen faster. For the fraction of a second it takes, the frame rate drops a little (16
  gave about 30-40 FPS on a 60 FPS menu). Lower it on a slower PC if the fill feels choppy.
- `FastTraderCells`, `SpreadStashCells`, `QuestPanelOncePerFrame`, `AqcStashCountCache`,
  `AqcQuestIndex`: the individual fixes, all on. Turn one off to get the game's (or
  AllQuestsCheckmarks') own behaviour back for that part.
- `FastIconRender` (off): renders brand-new item icons faster. It measured no faster in
  practice, so it stays off.
- `[Measurement]`: a timing tool for testing (see below). Leave `Enabled` off.

## Compatibility

- **SPT 4.1.x.** Tested on SPT 4.1.6.
- **AllQuestsCheckmarks 1.4.0:** supported, with the same checkmarks and tooltips. Its two
  fixes copy that version's logic, so with any other version of AllQuestsCheckmarks they stay
  off (the log says why) and the mod's own code runs.
- **Fika:** nothing in Fika 2.4.3 overlaps what this changes, and nothing here runs in raids,
  solo or co-op (Fika's raid check is the one used). It does nothing on a Fika headless client.
  Checked from Fika's code; not yet played with Fika installed.
- **UI mods:** tested alongside UI Fixes, UltrawideStash, UIScale.Reloaded, AdvancedStashSorting,
  QuickSell and many others.

## For developers

### How it works

The icons were never the problem: on a normal install almost every icon comes from memory or
EFT's own PNG cache. The time went into building item cells (`GridView.MagnifyIfPossible`
creates one and then waits a frame), into each cell's setup (`SetQuestItemViewPanel`, three times
per cell, and with AllQuestsCheckmarks a walk over every owned item and every quest), and into
the stash grid rebuilding in one frame on every trader switch. `CLAUDE.md` has the details,
the measurements behind each fix, and what was ruled out.

### Measurement harness

With `[Measurement] Enabled = true` (restart required), every trader open is timed until
everything in view is drawn and nothing has loaded for a second. A block starting
`===== Quick Trader Load Times: trader open #N (Trader) =====` goes to
`BepInEx/LogOutput.log`, and a row to `BepInEx/plugins/QuickTraderLoadTimes/measurements.csv`.
That covers the first item drawn, everything in view, where each icon came from, every main-thread
step, frame times, memory, and the stash's finish time. `ColdIconCache` points the icon cache at
a throwaway folder so every icon renders from scratch. Your real cache is never touched.
`scripts/compare-icons.ps1` compares two icon caches pixel by pixel.

### Build

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X            # build, test, pack dist\QuickTraderLoadTimes-<version>.zip
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install   # ...and copy both parts into that SPT install
```

The plugin compiles against the game's Assembly-CSharp, which has to be the copy the SPT
Launcher has already patched (start the game once on a new install before building). The
server part needs the .NET 10 SDK.

## License

MIT
