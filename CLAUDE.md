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

**Godot 4.7 with C#** (`Godot.NET.Sdk/4.7.1`, `net8.0`). The Mono/.NET build of the Godot editor
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

**RNG** is split into separate seeded streams in `RngStreams` — `Combat`, `EnemyAI`, `Shop`,
`Map` — all derived from the run seed, so drawing an extra card can't shift what the shop stocks
and cosmetic jitter can never desync a deterministic run.

## Current state

The originally planned phases are all built, not stubbed: the data-driven effect system, the
`CombatManager` state machine, the branching map generator, relics/potions/gold, the
meta-progression save, and a content-and-polish pass. Later passes added mid-run save/resume,
Event nodes, turn pacing, a themed art/typography pass, and a fully procedural audio layer.

- 11 screens, all wired to real data: MainMenu, Map, Combat, Event, Rest, Shop, Treasure, Reward,
  RunEnd, MetaProgression, Settings.
- Content: **three acts** (`data/acts/acts.json`), 30 cards (Attack/Skill), 22 relics, 12
  potions, 24 enemies (12 normal, 6 elite, 6 boss), 5 events. Each act has its own encounter
  pools, backdrops and gold scaling plus a **two-boss pool** the run seed picks from, so which
  boss ends an act varies per run. Enemy pools don't overlap between acts (asserted in
  `ActSmokeTest`).
- Effects: `deal_damage`, `gain_block`, `apply_status`, `draw_cards`, `heal`, `gain_energy`,
  `lose_hp`. Statuses: Vulnerable, Weak, Strength, Poison.
- Meta-progression is a **score-driven unlock track** (`RunScore` grades a finished run; the
  track's 14 rungs unlock 10 cards and 4 relics). An earlier shard *shop* was removed — don't
  reintroduce shard-purchase language.
- Audio is synthesized in-engine at runtime, no sampled assets.

`ROADMAP.md` tracks what's genuinely still open (CI, packaged export, `InputMap` actions, Power
card type, rarity in data, wider status roster, a balance pass over the three-act curve). Don't
treat this section as a to-do list.

## Key files

- `scripts/run/RunManager.cs` — autoload, run state + scene-transition orchestration
- `scripts/run/RunState.cs`, `RunSaveManager.cs` — in-run state and its save/resume
- `scripts/run/MetaProgressionManager.cs` + `RunScore.cs` — score-driven unlock track, meta save
- `scripts/run/RngStreams.cs` — the four seeded RNG streams
- `scripts/combat/CombatManager.cs` — turn loop, intent telegraphing, targeting sub-state
- `scripts/effects/EffectRegistry.cs` + `IEffect.cs` — the composable effect system every
  card/relic/potion/enemy-move definition keys into
- `scripts/relics/RelicBehavior.cs` — the 7 relic hooks; `SimpleHookEffectRelic.cs` is the
  data-only path (currently `OnCombatStart`/`OnTurnStart` only)
- `scripts/events/EventOutcomeRegistry.cs` — the 8 event outcome keys
- `scripts/map/MapGenerator.cs` — branching node DAG, per-act (floor count, encounter pools and
  boss pool all come from the `ActDefinition` passed in)
- `scripts/data/ActDefinition.cs` + `ActDatabase.cs` — the three acts and what varies per act
- `data/*/*.json` — the content layer; the schema is the data-vs-code split everything depends on
- `scenes/CombatScreen.tscn` — card drag/hover/targeting

## Conventions

- **Content changes belong in `data/`, not in new C# classes.** Reach for a new `IEffect` only
  when a genuinely new mechanic is needed, and register it in `EffectRegistry`.
- **Save instance IDs referencing definitions, never embedded definitions**, so balance tweaks
  don't break existing saves (`CardInstance` vs `CardDefinition`). Deserialization ignores
  unknown fields on purpose.
- **`dotnet build` before running or testing anything** — C# is compiled ahead of time, so
  otherwise you exercise the previous binary.
- Godot is not on `PATH` on this machine; use the full path to the Mono build, or `$GODOT`.

## Verification

There is no test framework. Each `scenes/debug/*SmokeTest.tscn` asserts in `_Ready`, prints
`PASS`/`FAIL` per check plus a `<Name>: N passed, M failed` summary, and exits nonzero on
failure. 14 suites, 332 checks at the last full green run.

```bash
tools/run-smoke-tests.sh                 # all 14; builds first, nonzero exit on any failure
tools/run-smoke-tests.sh MapSmokeTest    # a subset
```

Run these after touching anything under `scripts/` or any `.tscn`, before reporting work done.
The `smoke-test` skill catalogues which suite covers which subsystem.

For anything visual — a `.tscn`, layout, colours, card/relic rendering, or a bug described as
"looks dimmed"/"overlaps"/"cut off" — use the `verify-screen` skill to render the real screen and
look at the PNG. Never `--headless` for screenshots: the dummy renderer returns an empty
viewport texture.

Expected output that is **not** a regression: `MetaProgressionSmokeTest` prints a JSON parse
warning with a backtrace (its deliberate corrupt-save case, proving the loader falls back to
defaults), and `ScreenSmokeTest` and `Phase4ContentSmokeTest` each print one `Parent node is busy
adding/removing children` engine error from a test clicking a button that changes scene.

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
