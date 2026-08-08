---
name: smoke-tests
description: Reference for Hollowdeck's Godot smoke test suites (scenes/debug/*SmokeTest.tscn) - which suite covers what and when to run it, how a watchdog TIMEOUT/stall differs from a real failure, expected non-regression output, gotchas for writing a new suite (headless mouse position, scene changes, save-state guards), and the packaged-export boot check. Use when interpreting a smoke-test failure or TIMEOUT, deciding which suite to run for a change, or writing/modifying a *SmokeTest.tscn.
---

# Hollowdeck smoke tests

The script also runs artgen's `validate` before the engine suites (the `cargo run` form is in the
root `CLAUDE.md`'s Art section — there is no `artgen` on `PATH`) — the `docs/ART_SPEC.md` asset
rules (grid, ramp, hard alpha, no SVG) checked against the raw PNG bytes, which the C# side can't
do because `GD.Load` returns an imported texture. It's skipped with a warning if `cargo` isn't
installed.

Each suite runs under a 90-second watchdog (override with `SUITE_TIMEOUT`). A test that throws
inside `_Ready` — a `GetNode` against a path a restructured `.tscn` no longer has is the usual way
— never reaches its `GetTree().Quit()`, so Godot sits in an idle main loop and the sweep *stalls*
rather than failing. The watchdog reports that as `TIMEOUT` and exits nonzero.

A newly added PNG is the other way into that stall, and it looks nothing like an art problem:
until the editor has imported it there is no `.png.import` sidecar, `GD.Load` returns null, and
`PixelSpecSmokeTest` throws mid-`_Ready` and hangs. Run `Godot --headless --path . --import` after
dropping any asset into `assets/`, and commit the sidecars it writes.

`.github/workflows/ci.yml` runs this same script on every push to `main` and every PR. It imports
assets first (`.godot/` is gitignored, so a fresh checkout resolves no resources at all) and then
re-runs artgen's `generate` to check the committed art still matches its source. A red CI on
"Generated art is up to date" means an icon `fn` was edited without re-running the generator.

Restructuring a `.tscn` will break tests that assert on `GetNode` paths, on purpose — that's the
alarm working; update the assertion to the new path, never delete the check to get green.

| Test | Covers | Run when you touch |
|---|---|---|
| `EffectSmokeTest` | pile + effect resolution, generated card/potion description text, rarity coverage *over the offerable pool*, `CardPool` weighting and its unplayable exclusion, Power routing, every playable card's `+` actually changing something, a negative `gain_gold` stealing and clamping at zero, no card/potion/relic authoring the enemy-only `summon_enemy`/`escape`, and the four Phase 8 statuses: `Artifact` refused-iff-`IsDebuff` over the whole enum, `Thorns` billing a fully-blocked attack but not `lose_hp`, `Intangible` flooring after Vulnerable, `Plating` granting and eroding only on damage that gets through, and turn-end decay reaching both combatants | `scripts/effects/`, `PileManager`, `CardPool`, `cards.json`, `StatusType`, `StatusRow.IsDebuff`, `CombatManager.DecayAtTurnEnd` |
| `CardKeywordSmokeTest` | the Phase 7 vocabulary: Retain/Innate/Ethereal in `PileManager` (including Ethereal beating Retain, and no card declaring both), `add_card` into all three piles, the unplayable gate leaving the hand unchanged, `AllEnemies`/`RandomEnemy` through a real fight, X-cost spending everything and scaling only `PerX` specs | `PileManager` keywords, `AddCardEffect`, `CardType`, `EffectScope`, X-cost, `cards.json` |
| `CombatSmokeTest` | `CombatScreen.tscn` boots and wires up | `CombatScreen`, `CombatManager` |
| `CombatTargetingSmokeTest` | the drag/targeting layer (risk 5): target-lock glow, HUD never painting over an enemy, the intent tooltip staying off the hand, and the `CardView` drag path itself — the rejected-drop round trip, the reparent-before-resolve invariant, `TryPlayCard`'s four rejection gates leaving the hand *unchanged*, `_ExitTree` clearing the glow, the hit test skipping both a corpse *and* a runaway, a summon building an `EnemyView` mid-fight and appending to `Instances`, potion cancel/click, live description vs a Vulnerable target | `EnemyView`, `CardView` drag/targeting, `CombatManager` targeting sub-state, `CombatScreen.RefreshEnemies` |
| `RelicSmokeTest` | relic hooks fire through combat | `scripts/relics/`, relic hooks, `relics.json` |
| `Phase4ContentSmokeTest` | Poison, `lose_hp`, enrage picker, elite relic, every intent's telegraph against its effects (now including Summon and Escape), no enemy move using a card-only scope or `PerX`, the derived label shapes, turn-start grants on both sides (`Metallicize` for an enemy, `Fervor`/`Foresight` for the player), and the roster-mutating half: every `summon_enemy` naming a real enemy that does not itself summon, a summon arriving telegraphed but not acting that turn, an escape leaving without a kill, `onDeath` firing before the fight is scored | intent pickers, statuses, elite rewards, `EnemyView.FormatIntent`, `BeginPlayerTurn`, `CombatManager.ResolveDeathsAndSettle`/`SummonEnemy`, `enemies.json` |
| `HandLayoutSmokeTest` | hand fan spacing at 11+ cards, every card's text fits its box | `RefreshHand`, `HandFanLayout`, `CardView` text |
| `DeckViewSmokeTest` | pile popups, pile counters, combat-end z-order | `PileViewPopup`, `DeckViewButtons`, `PileCounterBar` |
| `MapSmokeTest` | per-act DAG shape, boss pools, `MapScreen` renders, fits *and fills* the canvas, and no node lands under the run-status block at a 13-relic haul | `MapGenerator`, `MapScreen`, `MapNode`, `ScreenChrome` block height, `acts.json` |
| `EventSmokeTest` | event DB, outcome keys, the `add_card` outcome and its authoring audit, `EventScreen` | `scripts/events/`, `events.json` |
| `ScreenSmokeTest` | Reward/Shop/Treasure/Rest load, populate and show their art; the shop's card-removal picker opening, hiding the shop beneath it, and cancelling for free; the keyword panel tracking hover and focus independently | any non-combat screen, `ScreenChrome`, `CardView`'s keyword tooltip, or its `.tscn` |
| `ActSmokeTest` | acts load, act progression, per-act content is distinct, no `summon_enemy` crossing an act | `acts.json`, `ActDefinition`, `RunState.AdvanceAct`, any `summon_enemy` spec |
| `RunSaveSmokeTest` | in-run save/load round-trip, save v2/v3 tolerance | `RunSaveData`, `RunSaveManager`, `RunState` |
| `MetaProgressionSmokeTest` | meta save, v1→v2 migration, unlock gating, `RunScore` | `MetaProgressionManager`, `RunScore`, the unlock track |
| `LibrarySmokeTest` | `LibraryScreen` loads with the full card/relic/potion roster (not just what's unlocked), per-category tile counts matching `CardDatabase`/`RelicDatabase`/`PotionDatabase`, locked card/relic dimming reading `MetaProgressionManager` live (a threshold crossed after the screen last opened un-dims on the next open), potions never dimmed, and the three category panes switching | `LibraryScreen`, `MetaProgressionManager`, `CardView`, `ChromeStyles.LockedTint` |
| `AudioSmokeTest` | stream construction, bus setup, volume round-trip, and a volume change leaving the window mode alone | `scripts/audio/`, `AudioManager`, `SettingsManager` |
| `TransitionSmokeTest` | cross-screen fade: overlay geometry/layer, the Reduce Motion gate, covered-action firing once | `ScreenFade`, `RunManager.ChangeScreen` |
| `PixelSpecSmokeTest` | asset grids, integer sprite scale, Nearest filter, the canvas letterboxing rather than expanding, font pair, palette ramp, icon- *and sprite*-to-definition coverage, every `IntentType`/`MapNodeType` resolving art of its own through `ArtAssets` (landing on the `unknown` fallback counts as uncovered), `artgen`'s ramp mirror | `docs/ART_SPEC.md`, `PixelSpec`, any sprite/tile/icon/font, `tools/artgen`, `project.godot` rendering *and* `[display]` |
| `KeyboardSmokeTest` | `hd_*` InputMap coverage and no duplicate keycodes, which control each screen focuses on load, that only `FocusMode` (never `Disabled`) excludes a control from the keyboard, the card picker's grid navigation (down a column, out to Cancel, back in), combat's card/potion keys, potion aiming, Continue at combat end | `project.godot` `[input]`, `ScreenKeyboardNav`, `CardPicker`, any screen's focus wiring, `CombatScreen._UnhandledInput` |
| `VictorySmokeTest` | the final act's boss win routing to `RunEndScreen` rather than another reward, and that screen reading VICTORY | `CombatScreen.OnContinuePressed`, `RunState.IsFinalAct`/`AdvanceAct`, `RunEndScreen` |
| `BalanceSmokeTest` | the difficulty curve rising act over act, every elite and boss encounter costing what its node type promises (bands, plus no boss cheaper than the act's costliest elite, **and no normal reaching `BossCostLow`**), every escape move stealing more than the cheapest node it can flee, every enrage phase out-hitting its own normal phase, every `RunScore` threshold being reachable by some seed, upgrade amounts matching the documented formula | `enemies.json`, `acts.json`, `RunScore` thresholds, `CardUpgrade`, `MapGenerator` weights |

Expected output that is **not** a regression: `MetaProgressionSmokeTest` prints a JSON parse
warning with a backtrace (its deliberate corrupt-save case, proving the loader falls back to
defaults), and `ScreenSmokeTest`, `Phase4ContentSmokeTest` and `KeyboardSmokeTest` each print one
`Parent node is busy adding/removing children` engine error from a test clicking a button that
changes scene.

**A headless Godot pins the mouse at `(0, 0)` and ignores both `Viewport.WarpMouse` and
`Input.WarpMouse`** — measured, not assumed. So anything hit-tested against
`GetGlobalMousePosition()` (`CardView.FindEnemyViewUnderMouse` is the one that matters) cannot be
tested by moving the cursor to the target; move the *target* over the mouse instead.

**But `(0, 0)` is the *window's* corner, not the canvas's, and those stopped being the same point
when the project went to `aspect="keep"`.** Letterboxing insets the canvas behind bars on a
non-16:9 window, so the window origin maps to a *negative* canvas coordinate, and a target pinned at
a literal `(-40, -40)` no longer has the mouse inside it. That is what it looks like when it breaks:
three green targeting checks went red on a one-line `project.godot` change, reporting "no enemy
matched — the setup is wrong, not the code under test", which is exactly what had happened. Ask a
`CanvasItem` for `GetGlobalMousePosition()` and place the target relative to *that* — both sides of
the real comparison are canvas-space, so this is correct under either setting. The suite's own class
is a plain `Node` and has no such method, which is why `SpawnEnemyOverTheMouse` asks the view.

Building the `EnemyView`s standalone rather than through `CombatScreen` also puts
`EnemyView.Instances` order under the test's control, which is the whole point of the corpse-skipping
check — the corpse has to be first in that list to be worth asserting about. See
`CombatTargetingSmokeTest.TestHitTestSkipsCorpsesAndIgnoresUntargetedCards`.

A suite whose last act changes scene must capture `GetTree()` into a local *before* it, and do
nothing asynchronous afterwards: `ChangeSceneToFile` replaces the tree's current scene, which is
the test itself, and `GetTree()` on the now-detached node comes back null — the run then hangs with
no summary and no `Quit()`, which the watchdog reports as a `TIMEOUT`. `ActSmokeTest` and
`KeyboardSmokeTest` both document this at their `_Ready`. That budget of one scene change per
suite is why `VictorySmokeTest` exists separately from `ActSmokeTest` at all.

`VictorySmokeTest` needs frames *after* its change, because the screen it lands on deletes the run
save and banks a score into the meta save from its own `_Ready` — during the deferred swap, i.e.
after a scoped `RunSaveGuard` in the test method has already restored. It buys them by handing the
`CurrentScene` title to an empty stand-in node first, so the swap deletes that instead of the
suite. Anything else that drives a screen far enough to reach `RunEndScreen` needs the same
treatment; without it the test eats the developer's in-progress run.

A suite that drives a button into `RunManager.ChangeScreen` also needs `HardCutGuard.Protect()`
alongside `RunSaveGuard`: the Phase 5 fade defers `ChangeSceneToFile` into a tween callback, so
without pinning Reduce Motion the suite behaves differently depending on the developer's
`user://settings.json` — including whether the documented "Parent node is busy" error appears.

Tests that touch persistence write only to `user://*_test.json` scratch paths, never the real
save. Any test that drives a screen far enough to change scenes hits `RunManager.ChangeScreen`,
and Map/Rest/Shop/Treasure/Reward/Event are all in `RunManager.AutoSaveScreens` — so reaching one
overwrites the real `user://run_save.json` with the test's fixture unless the test wraps the risky
section in `RunSaveGuard.Protect()` (`scripts/debug/RunSaveGuard.cs`), which snapshots and
restores both save files on scope exit.

Smoke tests are not full coverage. The phase-level bar remains: the game launches from the editor
and from a packaged export, the loop is playable end-to-end by hand, and no console
errors/warnings appear in the Godot debugger during that playthrough.

"And from a packaged export" now has a command behind it: `tools/build-export.sh`, which exports
and then **boots** the result headlessly, failing on any `ERROR:` line rather than on the exit code.
That distinction is measured, not assumed: an unhandled C# exception is logged by Godot's .NET layer
through `GD.PushError` and does *not* abort the process, so a build force-exported with `data/`
excluded boots to a menu with no cards, no enemies and no acts — and exits **0**. The boot is the
only check in the repo running against a `.pck` instead of the source tree. CI runs it as a separate
**Export** job (Linux only; the pack contents are platform-independent).

Two export-only failure modes worth knowing, both found by running the thing rather than reading
about it. macOS `codesign/codesign=1` (Godot's built-in ad-hoc signer) produces a signature
`codesign -vvv` calls valid and that AMFI then rejects with "failed parsing DER entitlements" — the
kernel `SIGKILL`s the app on launch, no stdout, no crash report. Use `3` (Apple's own `codesign`),
which needs a macOS host. And a `universal`/`arm64` macOS export requires
`textures/vram_compression/import_etc2_astc=true` in `project.godot` or Godot refuses the preset
outright.
