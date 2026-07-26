---
name: smoke-test
description: Run Hollowdeck's headless smoke tests and know which ones cover the change you just made. Use after editing anything under scripts/ or any .tscn, before reporting work as done, and whenever asked to "run the tests", "verify this", or "check nothing broke". Covers combat, effects, relics, map, events, screens, run saves, meta progression and audio.
---

# Smoke tests

There is no test framework here. Each `scenes/debug/*SmokeTest.tscn` is a scene that runs
assertions in `_Ready`, prints `PASS`/`FAIL` per check and `<Name>: N passed, M failed`, then
exits nonzero if anything failed.

```bash
tools/run-smoke-tests.sh                    # all 14, builds first, exits nonzero on any failure
tools/run-smoke-tests.sh MapSmokeTest       # a subset
```

One test on its own, when you want the full output:

```bash
dotnet build && /Applications/Godot_mono.app/Contents/MacOS/Godot \
    --headless --path . scenes/debug/MapSmokeTest.tscn
```

Always `dotnet build` first — C# is compiled ahead of time, so otherwise you test the old binary.

## What to run for what you changed

Baseline check counts as of the last full green run (332 total). A count that drops without you
deleting a test is a regression.

| Test | Covers | Run when you touch |
|---|---|---|
| `EffectSmokeTest` (28) | pile + effect resolution, generated card/potion description text | `scripts/effects/`, `PileManager`, `cards.json` |
| `CombatSmokeTest` (4) | `CombatScreen.tscn` boots and wires up | `CombatScreen`, `CombatManager` |
| `CombatTargetingSmokeTest` (5) | enemy target-lock glow | `EnemyView`, `CardView` drag/targeting |
| `RelicSmokeTest` (15) | relic hooks fire through combat | `scripts/relics/`, relic hooks, `relics.json` |
| `Phase4ContentSmokeTest` (9) | Poison, `lose_hp`, enrage picker, elite relic | intent pickers, statuses, elite rewards |
| `HandLayoutSmokeTest` (5) | hand fan spacing at 11+ cards, every card's text fits its box | `RefreshHand`, `HandFanLayout`, `CardView` text |
| `DeckViewSmokeTest` (12) | pile popups, combat-end z-order | `PileViewPopup`, `DeckViewButtons` |
| `MapSmokeTest` (30) | per-act DAG shape, boss pools, `MapScreen` renders and fits | `MapGenerator`, `MapScreen`, `MapNode`, `acts.json` |
| `EventSmokeTest` (17) | event DB, outcome keys, `EventScreen` | `scripts/events/`, `events.json` |
| `ScreenSmokeTest` (21) | Reward/Shop/Treasure/Rest load and populate | any non-combat screen or its `.tscn` |
| `ActSmokeTest` (33) | acts load, act progression, per-act content is distinct | `acts.json`, `ActDefinition`, `RunState.AdvanceAct` |
| `RunSaveSmokeTest` (26) | in-run save/load round-trip, save v2/v3 tolerance | `RunSaveData`, `RunSaveManager`, `RunState` |
| `MetaProgressionSmokeTest` (40) | meta save, v1→v2 migration, unlock gating, `RunScore` | `MetaProgressionManager`, `RunScore`, the unlock track |
| `AudioSmokeTest` (87) | stream construction, bus setup, volume round-trip | `scripts/audio/`, `AudioManager`, `SettingsManager` |

When in doubt run everything — the full sweep takes well under a minute.

## Restructuring a .tscn will break tests, on purpose

These tests assert on `GetNode` paths, so rewriting a scene's node tree fails them. That is the
alarm working, not a flaky test. `MetaProgressionSmokeTest` asserted on `ShardsLabel` and
`RelicUnlocksList` and correctly broke when that screen was rebuilt in containers. **Update the
assertion to the new path — never delete the check to get green.**

## Expected noise — not regressions

- `MetaProgressionSmokeTest` prints a JSON parse warning with a C# backtrace. That's its
  deliberate corrupt-save case proving `LoadFrom` falls back to defaults instead of throwing.
- `ScreenSmokeTest` and `Phase4ContentSmokeTest` each print one
  `Parent node is busy adding/removing children` engine error, from a test clicking a button that
  changes scene. Pre-existing and documented in both sources.

The `PASS`/`FAIL` lines and the summary are what count.

## Safety

Tests that touch persistence write only to `user://*_test.json` scratch paths and never the real
save — keep it that way when adding cases.

The trap is indirect writes. Any test that drives a screen far enough to *change scenes* hits
`RunManager.ChangeScreen`, and Map/Rest/Shop/Treasure/Reward/Event are all in
`RunManager.AutoSaveScreens` — so reaching one overwrites the real `user://run_save.json` with the
test's fixture. `ScreenSmokeTest`'s Rest-upgrade click did exactly that on every suite run until it
was fixed. If a test clicks a button that leaves the screen, open with:

```csharp
using var saveGuard = RunSaveGuard.Protect();   // scripts/debug/RunSaveGuard.cs
```

It snapshots `run_save.json` and `meta_progression.json` and restores them on scope exit (deleting
again anything that didn't exist before). Verify by hashing both files either side of a full run —
they must come back byte-identical.

The three visual scenes (`ArtScreenshot`,
`AnimationScreenshot`, `StyleReferenceScreen`) are not smoke tests, need a real renderer, and are
skipped by the runner; for looking at screens use the `verify-screen` skill.
