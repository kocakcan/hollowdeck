# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Hollowdeck is a standalone desktop deckbuilder roguelike in the vein of Slay the Spire:
node-based map traversal, turn-based card combat with telegraphed enemy intents, relics/potions,
and cross-run meta-progression. Desktop-only (Windows/Mac/Linux), single-player, no networking.
The builder is an experienced general-purpose programmer (Rust background) who came to this new
to full game engines — which is why the project leans on engine-provided UI and animation tooling
rather than hand-rolling it.

See `README.md` for the developer orientation (layout, how to run, how to add content) and
`ROADMAP.md` for what's still open.

## Stack

**Godot 4.7 with C#**. The Mono/.NET build of the Godot editor
is required. Scenes/nodes map directly onto the genre's screens (map, combat, shop, reward);
`Control` nodes with anchors/containers handle card layout/hover/drag; built-in
`Tween`/`AnimationPlayer` handle animation ("juice") without third-party libraries.

Rejected alternatives, kept here so the decision isn't relitigated: **Unity C#** — most mature 2D
UI tooling and the most deckbuilder tutorials, but a heavier editor footprint and no real
advantage over Godot 4 for a solo desktop project. **Rust + Bevy** — tempting for language
continuity, but `bevy_ui` and tweening (needs `bevy_tweening`) are the weakest parts of Bevy, and
they're exactly what this genre leans on; it also stacks learning ECS on top of learning an
engine. **Rust + macroquad-style immediate mode** — a canvas library, not an engine, so the scene
stack, UI layout/hit-testing, tweening and drag targeting all get hand-rolled. **TypeScript +
Electron/Tauri** — React/Framer Motion animate cards well, but it still isn't an engine; the
combat state machine and effect system get hand-built anyway, plus desktop packaging overhead.

The explicit tradeoff: giving up Rust continuity in exchange for a scene editor, built-in UI
layout and built-in tweening. Worth it here; would not be for a combat-only prototype.

## Architecture

**Content is data, effects are code, connected by string keys.** This is the single most
load-bearing decision. Acts/cards/relics/enemies/potions/events are authored as JSON under `data/`;
each `CardDefinition` holds a list of `EffectSpec`s (e.g. `{action: "deal_damage", amount: 6,
scope: "Target"}`) that key into `EffectRegistry`, a dictionary of `IEffect` implementations
(`DealDamageEffect`, `ApplyStatusEffect`, `DrawCardsEffect`, ...). New cards are new data rows,
not new classes — a one-class-per-card approach becomes unmaintainable at hundreds-of-cards
scale. `IScriptedEffect` exists as an escape hatch for the rare card that doesn't decompose into
existing effects.

**A card carries two independent channels, and both are live.** `CardType`
(`Attack`/`Skill`/`Power`) drives the frame fill; `Rarity` (`Common`/`Uncommon`/`Rare`) drives the
border *and* how often the card is offered — `CardPool` weights every reward, shop and event draw
60/37/3, so a Rare is an event rather than one row in a shuffled list. Playing a `Power` moves it
to `PileManager.Powers`, which is neither Discard (it would cycle back) nor Exhaust (a cost the HUD
renders as one).

**What a Power buys is a status that pays out every turn.** `Metallicize` (Block), `Ritual`
(Strength) and `Regen` (HP) are granted in `CombatManager.ApplyTurnStartGrants` and never decay,
which is what no recurring Skill can offer. They are statuses rather than a `PowerBehavior` hook
deliberately: a hook would mean one C# class per Power, the one-class-per-card pattern the effect
system exists to avoid (risk 1), whereas a status keeps a Power an ordinary data row. The ordering
is load-bearing — both combatants clear `Block` on their own turn, so a grant that lands before
that clear is wiped the instant it is given; the player's clear is in `EndEnemyTurn`, the enemy's
is mid-loop. (`Regen` heals, so it is indifferent to that ordering; it lives with the other two
because it is the same *kind* of thing.)

**The status roster is nine, in mirrored pairs.** `Strength`/`Weak` scale damage through
`DamageMath`; `Dexterity`/`Frail` scale Block through `BlockMath`, which is a deliberate copy of
`DamageMath`'s shape rather than four more methods on it — nothing applies Strength to Block, and
keeping them apart is what stops a later edit reaching for the wrong multiplier. `Vulnerable` and
`Poison` sit on the target side; the three turn-start grants above make up the rest. Buffs
(`Strength`, `Dexterity`, and the grants) never decay; debuffs (`Weak`, `Vulnerable`, `Frail`)
wear off by 1 a turn at the two `DecayStatus` sites, and `Poison` decays as it ticks. A new status
needs an icon in `tools/artgen/src/icons/misc.rs`, an arm in `StatusRow.Describe`, and — easy to
forget, and silent when missed — an entry in `CardUpgrade.ShouldScale`, or upgrading a card that
grants it produces an identical `+`.

**Autoloads** (declared in `project.godot`, in this order — `AudioManager` must come before
`SettingsManager` because the settings sliders address audio bus indices):

| Autoload | Owns |
| --- | --- |
| `RunManager` | run state and scene transitions |
| `AudioManager` | procedural SFX/music playback, Music/SFX bus split |
| `MetaProgressionManager` | cross-run unlocks, persisted separately from run state |
| `SettingsManager` | volumes, fullscreen, reduce-motion |

**Screen flow:** `RunManager` drives MainMenu → MapScreen → {Combat, Elite, Event, Rest, Shop,
Treasure} → RewardScreen → back to MapScreen → RunEnd → MetaProgressionScreen. Meta-progression
lives in its own save file (`user://meta_progression.json`) separate from in-run save state
(`user://run_save.json`) — different lifetimes and versioning needs, don't merge them. The meta
save carries a schema version and a v1→v2 migration; keep deserialization tolerant of unknown
fields.

**Acts:** a run is three acts, authored in `data/acts/acts.json` (`ActDefinition`/`ActDatabase`).
`RunState.ActIndex` says which one is in play; `RunState.MapNodes` only ever holds the *current*
act's graph. Killing a non-final act's boss goes RewardScreen-then-Map like any other fight, with
`RunState.AdvanceAct()` regenerating the map, resetting `CurrentNodeId`/`VisitedNodeIds` (node ids
repeat across acts), banking the cleared act's floors into `RunStats.FloorsInPreviousActs`, and
applying the act's max-HP bonus and heal. Only `IsFinalAct`'s boss routes to Victory. The run save
is v3 for `ActIndex`; a v2 save loads as act 1, which is what it always was.

**Combat loop** (`CombatManager`) is an explicit `CombatState` enum machine — `Start`,
`PlayerTurn`, `AwaitingTarget`, `ResolvingCard`, `EnemyTurn`, `ResolvingEnemyIntent`, `CombatEnd`
— not loose booleans. Sub-states like "awaiting target" and "animation playing (input locked)"
are exactly where these games accumulate input-during-animation bugs if left implicit:

1. Combat start → shuffle/draw opening hand → `OnCombatStart` relic triggers → enemies pick and
   **display** their first intents.
2. Player turn: play cards (targeting sub-state for single-target cards) → resolve `EffectSpec`s
   → fire relic triggers.
3. End turn: discard non-retained hand, resolve end-of-turn statuses.
4. Enemy turn: execute the **already-telegraphed** intent, then pick and display the *next*
   intent.
5. Repeat to victory/defeat → RewardScreen.

**Input is two layers, and the game is fully playable without a mouse.** Every binding is a named
`hd_*` action in `project.godot`'s `[input]` — never a raw `Key.X` switch — so there is one place
to look and a future rebinding UI has something to rebind. Above that, the two surfaces are
navigated differently *on purpose*:

- **Non-combat screens use Godot's own focus system.** They are stock `Button`s, so Tab/arrow
  navigation and the skipping of `Disabled` controls come free — which is why the map needs no
  navigation code at all: unreachable nodes are already disabled. `ScreenKeyboardNav.Attach` is the
  one line each screen adds, giving it an initial focus owner, a re-grab after it rebuilds
  controls, and `hd_cancel`.
- **Combat drives its own `_UnhandledInput`.** Cards are fanned `Panel`s and targeting is a
  `CombatState` sub-state, so `EnemyView`, `PotionView`, End Turn and Continue all stay
  `FocusModeEnum.None` — focus navigation would fight the arrow-key card cycling. Don't "fix" those
  by making them focusable.

`ScreenKeyboardNav.KeyHint(action)` reads a binding back out of the `InputMap` for on-screen hints,
so a badge or tooltip can't drift from the key that actually fires.

**RNG** is split into separate seeded streams in `RngStreams` — `Combat`, `EnemyAI`, `Shop`,
`Map` — all derived from the run seed, so drawing an extra card can't shift what the shop stocks
and cosmetic jitter can never desync a deterministic run.

## Current state

An earlier shard *shop* was removed — don't reintroduce shard-purchase language.

Content stands at **58 cards** (24 Common / 23 Uncommon / 11 Rare), **15 events**, 22 relics, 12
potions, 24 enemies, 3 acts. Nine statuses, nine effect actions, fifteen event outcome keys.

`ROADMAP.md` tracks what's genuinely still open (packaged export, the rest of the way to 80–120
cards, more enemies per act, the remaining five relic hooks, a balance pass over the three-act
curve). Don't treat this section as a to-do list.

## Key files

- `scripts/run/RunManager.cs` — autoload, run state + scene-transition orchestration
- `scripts/run/ScreenFade.cs` — the cross-screen fade, on a `CanvasLayer` parented to the
  `RunManager` autoload (a screen cannot own it: `ChangeSceneToFile` frees the current scene
  mid-transition). Gated on `SettingsManager.ReduceMotion`; declines rather than shortens, so the
  hard cut lives on exactly one path
- `scripts/run/RunState.cs`, `RunSaveManager.cs` — in-run state and its save/resume
- `scripts/run/MetaProgressionManager.cs` + `RunScore.cs` — score-driven unlock track, meta save
- `scripts/run/RngStreams.cs` — the four seeded RNG streams
- `scripts/run/CardPool.cs` — rarity-weighted sampling; the single place "which cards does the
  player get offered" is decided (reward picks, shop stock, the random-card event outcome)
- `scripts/combat/CombatManager.cs` — turn loop, intent telegraphing, targeting sub-state
- `scripts/effects/EffectRegistry.cs` + `IEffect.cs` — the composable effect system every
  card/relic/potion/enemy-move definition keys into
- `scripts/relics/RelicBehavior.cs` — the 7 relic hooks; `SimpleHookEffectRelic.cs` is the
  data-only path (currently `OnCombatStart`/`OnTurnStart` only)
- `scripts/events/EventOutcomeRegistry.cs` — the 15 event outcome keys. Thirteen resolve
  instantly; two (`remove_chosen_card`, `upgrade_chosen_card`) implement `ICardPickerOutcome` and
  come back from `Begin()` as *pending*, for `EventScreen` to open a card grid against. A picker
  must be the last spec in a choice and may not appear inside a `gamble` — both enforced by
  `EventSmokeTest`, not just documented
- `scripts/ui/CardPicker.cs` — the "choose one of these cards" grid, shared by the rest site's
  Smith and by the two event picker outcomes
- `scripts/effects/BlockMath.cs` — Dexterity/Frail, the exact mirror of `DamageMath`'s
  Strength/Weak, split for the same no-drift reason
- `scripts/map/MapGenerator.cs` — branching node DAG, per-act (floor count, encounter pools and
  boss pool all come from the `ActDefinition` passed in)
- `scripts/data/ActDefinition.cs` + `ActDatabase.cs` — the three acts and what varies per act
- `data/*/*.json` — the content layer; the schema is the data-vs-code split everything depends on
- `scenes/CombatScreen.tscn` — card drag/hover/targeting
- `scripts/ui/ScreenChrome.cs` — the furniture every non-combat screen shares (title, HP/gold/relic
  status block, framed panel, art plinth), attached from `_Ready` like `ScreenBackground` and
  `DeckViewButtons`. Owns those node paths; `ScreenChrome.HpLabelPath` and friends are what the
  smoke tests address rather than literals.

## Conventions

- **Content changes belong in `data/`, not in new C# classes.** Reach for a new `IEffect` only
  when a genuinely new mechanic is needed, and register it in `EffectRegistry`.
- **Save instance IDs referencing definitions, never embedded definitions**, so balance tweaks
  don't break existing saves (`CardInstance` vs `CardDefinition`). Deserialization ignores
  unknown fields on purpose.
- **New keys are `hd_*` actions in `project.godot`, checked with `IsActionPressed`.** Never a raw
  keycode compare. `IsActionPressed` defaults `exact_match` to false, so a modifier binding
  (`Shift+1`) also matches its unmodified action (`hd_card_1`) unless you pass `exactMatch: true` —
  which is why the potion keys are `Z`/`X`/`C`.
- **`dotnet build` before running or testing anything** — C# is compiled ahead of time, so
  otherwise you exercise the previous binary.
- Godot is not on `PATH` on this machine; use the full path to the Mono build, or `$GODOT`.

## Verification

There is no test framework. Each `scenes/debug/*SmokeTest.tscn` asserts in `_Ready`, prints
`PASS`/`FAIL` per check plus a `<Name>: N passed, M failed` summary, and exits nonzero on
failure.

```bash
tools/run-smoke-tests.sh                 # all 17; builds first, nonzero exit on any failure
tools/run-smoke-tests.sh MapSmokeTest    # a subset
```

The script also runs `tools/artgen validate` before the engine suites — the `docs/ART_SPEC.md`
asset rules (grid, ramp, hard alpha, no SVG) checked against the raw PNG bytes, which the C# side
can't do because `GD.Load` returns an imported texture. It's skipped with a warning if `cargo`
isn't installed.

Each suite runs under a 90-second watchdog (override with `SUITE_TIMEOUT`). A test that throws
inside `_Ready` — a `GetNode` against a path a restructured `.tscn` no longer has is the usual way
— never reaches its `GetTree().Quit()`, so Godot sits in an idle main loop and the sweep *stalls*
rather than failing. The watchdog reports that as `TIMEOUT` and exits nonzero.

`.github/workflows/ci.yml` runs this same script on every push to `main` and every PR. It imports
assets first (`.godot/` is gitignored, so a fresh checkout resolves no resources at all) and then
re-runs `artgen generate` to check the committed art still matches its source. A red CI on
"Generated art is up to date" means an icon `fn` was edited without re-running the generator.

Run these after touching anything under `scripts/` or any `.tscn`, before reporting work done.

| Test | Covers | Run when you touch |
|---|---|---|
| `EffectSmokeTest` | pile + effect resolution, generated card/potion description text, rarity coverage, `CardPool` weighting, Power routing | `scripts/effects/`, `PileManager`, `CardPool`, `cards.json` |
| `CombatSmokeTest` | `CombatScreen.tscn` boots and wires up | `CombatScreen`, `CombatManager` |
| `CombatTargetingSmokeTest` | enemy target-lock glow | `EnemyView`, `CardView` drag/targeting |
| `RelicSmokeTest` | relic hooks fire through combat | `scripts/relics/`, relic hooks, `relics.json` |
| `Phase4ContentSmokeTest` | Poison, `lose_hp`, enrage picker, elite relic | intent pickers, statuses, elite rewards |
| `HandLayoutSmokeTest` | hand fan spacing at 11+ cards, every card's text fits its box | `RefreshHand`, `HandFanLayout`, `CardView` text |
| `DeckViewSmokeTest` | pile popups, pile counters, combat-end z-order | `PileViewPopup`, `DeckViewButtons`, `PileCounterBar` |
| `MapSmokeTest` | per-act DAG shape, boss pools, `MapScreen` renders, fits *and fills* the canvas | `MapGenerator`, `MapScreen`, `MapNode`, `acts.json` |
| `EventSmokeTest` | event DB, outcome keys, `EventScreen` | `scripts/events/`, `events.json` |
| `ScreenSmokeTest` | Reward/Shop/Treasure/Rest load, populate and show their art | any non-combat screen, `ScreenChrome`, or its `.tscn` |
| `ActSmokeTest` | acts load, act progression, per-act content is distinct | `acts.json`, `ActDefinition`, `RunState.AdvanceAct` |
| `RunSaveSmokeTest` | in-run save/load round-trip, save v2/v3 tolerance | `RunSaveData`, `RunSaveManager`, `RunState` |
| `MetaProgressionSmokeTest` | meta save, v1→v2 migration, unlock gating, `RunScore` | `MetaProgressionManager`, `RunScore`, the unlock track |
| `AudioSmokeTest` | stream construction, bus setup, volume round-trip | `scripts/audio/`, `AudioManager`, `SettingsManager` |
| `TransitionSmokeTest` | cross-screen fade: overlay geometry/layer, the Reduce Motion gate, covered-action firing once | `ScreenFade`, `RunManager.ChangeScreen` |
| `PixelSpecSmokeTest` | asset grids, integer sprite scale, Nearest filter, font pair, palette ramp, icon-to-definition coverage, `artgen`'s ramp mirror | `docs/ART_SPEC.md`, `PixelSpec`, any sprite/tile/icon/font, `tools/artgen`, `project.godot` rendering |
| `KeyboardSmokeTest` | `hd_*` InputMap coverage and no duplicate keycodes, which control each screen focuses on load, combat's card/potion keys, potion aiming, Continue at combat end | `project.godot` `[input]`, `ScreenKeyboardNav`, any screen's focus wiring, `CombatScreen._UnhandledInput` |

When in doubt run everything — the full sweep takes well under a minute. Restructuring a
`.tscn` will break tests that assert on `GetNode` paths, on purpose — that's the alarm working;
update the assertion to the new path, never delete the check to get green.

For anything visual — a `.tscn`, layout, colours, card/relic rendering, or a bug described as
"looks dimmed"/"overlaps"/"cut off" — use the `verify-screen` skill to render the real screen and
look at the PNG. Never `--headless` for screenshots: the dummy renderer returns an empty
viewport texture.

Expected output that is **not** a regression: `MetaProgressionSmokeTest` prints a JSON parse
warning with a backtrace (its deliberate corrupt-save case, proving the loader falls back to
defaults), and `ScreenSmokeTest`, `Phase4ContentSmokeTest` and `KeyboardSmokeTest` each print one
`Parent node is busy adding/removing children` engine error from a test clicking a button that
changes scene.

A suite whose last act changes scene must capture `GetTree()` into a local *before* it, and do
nothing asynchronous afterwards: `ChangeSceneToFile` replaces the tree's current scene, which is
the test itself, and `GetTree()` on the now-detached node comes back null — the run then hangs with
no summary and no `Quit()`, which the watchdog reports as a `TIMEOUT`. `ActSmokeTest` and
`KeyboardSmokeTest` both document this at their `_Ready`.

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

## Genre-specific risks to keep in mind

Code comments cite these by number — keep the numbering stable.

1. **Effect system must be composable, not hardcoded per card** — the `EffectSpec`/
   `EffectRegistry` pattern is what keeps content authorable as data. Adding a bespoke class per
   card would undo it.
2. **RNG determinism** — separate seeded streams for map generation, combat (shuffle/draw),
   enemy AI, and shop stock, so visual jitter never desyncs a deterministic run. New systems that
   need randomness get their own stream rather than borrowing an existing one.
3. **Save/run-state serialization** — save instance IDs referencing definitions, not embedded
   definitions, so balance tweaks don't break old saves. Deserialization stays tolerant of
   unknown fields.
4. **Combat sub-state explicitness** — see the state machine above; new sub-states go in the
   enum, not into a new boolean flag.
5. **UI drag/targeting feel** — cards target by drag with a target-lock glow; potions target via
   `AwaitingTarget` + click. Two interaction models coexist deliberately; changing one means
   checking the other still reads consistently.
6. **Content scope creep** — the initial content target is capped (~80–120 cards, one class,
   one act) rather than chasing full parity with Slay the Spire. More content is
   post-launch/ongoing.
