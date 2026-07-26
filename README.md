# Hollowdeck

A standalone desktop deckbuilder roguelike in the vein of Slay the Spire: traverse a branching
node map, fight turn-based card combats against enemies that telegraph their intents, collect
relics and potions, and unlock new content across runs by scoring well. Single-player, no
networking, desktop only (Windows/Mac/Linux).

## Status

The core loop is playable end-to-end — new run, map, combat, events, shop, rest, treasure,
rewards, bosses, run-end scoring, unlocks, and mid-run save/resume. Current content is three
acts: **30 cards, 22 relics, 12 potions, 24 enemies (6 of them bosses), 5 events**. Each act has
its own enemy pools and a two-boss pool the run seed picks from. See [ROADMAP.md](ROADMAP.md) for
what's still open.

## Stack

Godot **4.7**, C# (`Godot.NET.Sdk/4.7.1`, `net8.0`). The **Mono/.NET build** of the Godot editor
is required — the standard build won't load the project.

## Running it

Open the project in Godot 4.7 Mono and press play; the main scene is `scenes/MainMenu.tscn`.
Or from the command line:

```bash
dotnet build
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

C# is compiled ahead of time, so **`dotnet build` first** — otherwise you run the previous
binary. Godot is not on `PATH` here; the path above is the macOS default. `tools/run-smoke-tests.sh`
honours a `$GODOT` override, and the same binary path applies to every command below.

Save files live under Godot's user data directory (`~/Library/Application Support/Godot/app_userdata/Hollowdeck/`
on macOS): `meta_progression.json` for cross-run unlocks, `run_save.json` for a run in progress.

## Repo layout

```
scripts/
  run/        RunManager, RunState, save/load, MetaProgressionManager, RunScore,
              AudioManager, SettingsManager, RngStreams, PileManager, *Instance types
  combat/     CombatManager state machine, Combatant, EnemyFactory, intent pickers
  effects/    IEffect implementations + EffectRegistry, DamageMath
  relics/     RelicBehavior (7 hooks) + registry + SimpleHookEffectRelic + bespoke relics
  events/     IEventOutcome implementations + EventOutcomeRegistry
  map/        MapGenerator, MapNode, MapNodeType
  data/       Definition classes and the JSON databases that load them, EffectSpec
  ui/         Screen controllers, CardView, EnemyView, theming, layout helpers
  audio/      AudioSynth / AudioCues / AudioMusic — everything is synthesized at runtime
  debug/      14 smoke-test scenes + the screenshot harness
scenes/       11 screens + reusable CardView/EnemyView/PotionView/FloatingText
  debug/      smoke-test and screenshot scenes
data/         acts / cards / relics / potions / enemies / events — all JSON, the content layer
assets/       sprites, icons, fonts, backgrounds, themes (see CREDITS.md for licensing)
tools/        run-smoke-tests.sh
```

## Architecture

**Content is data, effects are code, joined by string keys.** A card is a row in
`data/cards/cards.json` holding a list of `EffectSpec`s; each spec's `action` keys into
`EffectRegistry`, a dictionary of `IEffect` implementations. New cards are new data rows, not new
classes. The same pattern drives relics, potions, enemy moves and event outcomes.

Four autoloads, declared in `project.godot` in this order — `AudioManager` must precede
`SettingsManager`, whose sliders address audio bus indices:

- **`RunManager`** — run state and scene transitions
- **`AudioManager`** — procedural SFX/music, Music/SFX bus split
- **`MetaProgressionManager`** — cross-run unlocks
- **`SettingsManager`** — volumes, fullscreen, reduce-motion

`CombatManager` is an explicit state machine (`Start`, `PlayerTurn`, `AwaitingTarget`,
`ResolvingCard`, `EnemyTurn`, `ResolvingEnemyIntent`, `CombatEnd`) rather than a set of boolean
flags — the sub-states are where input-during-animation bugs otherwise accumulate.

Two save files with separate lifetimes: `meta_progression.json` (versioned, with a v1→v2
migration) and `run_save.json` (autosaved on Map/Rest/Shop/Treasure/Reward/Event, deliberately
never mid-combat). Both store instance IDs referencing definitions, never embedded definitions,
so balance tweaks don't invalidate existing saves.

`RngStreams` splits randomness into four seeded streams derived from the run seed — `Combat`,
`EnemyAI`, `Shop`, `Map` — so drawing an extra card can't shift what the shop stocks. The `Map`
stream also picks which of an act's bosses a run gets, so that's reproducible from the seed too.

A run is three acts (`data/acts/acts.json`). Each act owns its floor count, encounter pools, boss
pool, backdrops and gold rewards; clearing a non-final boss calls `RunState.AdvanceAct`, which
regenerates the map for the next act and carries the deck, relics, gold and (raised) HP across.
Only the final act's boss ends the run.

[CLAUDE.md](CLAUDE.md) has the full version, including the conventions to follow when changing
any of this.

## Adding content

Most content needs no C# at all.

**A card** is a row in `data/cards/cards.json`:

```json
{
  "id": "strike",
  "name": "Strike",
  "cost": 1,
  "type": "Attack",
  "target": "SingleEnemy",
  "exhaust": false,
  "effects": [{ "action": "deal_damage", "amount": 6, "scope": "Target" }]
}
```

The seven `action` keys `EffectRegistry` currently knows: `deal_damage`, `gain_block`,
`apply_status`, `draw_cards`, `heal`, `gain_energy`, `lose_hp`. Statuses available to
`apply_status`: `Vulnerable`, `Weak`, `Strength`, `Poison`. A genuinely new mechanic means a new
`IEffect` in `scripts/effects/` registered in `EffectRegistry` — reach for that only when the
mechanic can't be composed from existing actions.

**A potion** (`data/potions/potions.json`) has the same `effects` shape. **An enemy**
(`data/enemies/enemies.json`) is HP plus a list of moves, each pairing a displayed `intent` with
the `effects` it will actually resolve; `aiType` picks the intent strategy (`sequential`,
weighted, or phase-threshold). **An event** (`data/events/events.json`) is text plus choices,
each naming one of `EventOutcomeRegistry`'s eight outcomes (`gain_gold`, `lose_gold`, `heal`,
`lose_hp`, `gain_random_card`, `gain_relic`, `lose_relic`, `none`).

**An act** (`data/acts/acts.json`) is a chapter of a run, in play order: `floorCount`, the
`normalEncounters` / `eliteEncounters` pools (each entry is one group, so `["slime","slime"]` is a
single two-slime fight), a `bossIds` pool the run seed picks one from, `mapBackground` /
`combatBackground` tiles with hex tints, the gold a fight pays, and the max-HP bonus plus heal
percentage granted for clearing it. Adding a fourth act is a row here plus the enemies it names —
`MapGenerator` reads all of it and `RunState.AdvanceAct` walks the list, so no code changes.

**A relic** (`data/relics/relics.json`) is data-only when it fires a single effect on
`OnCombatStart` or `OnTurnStart` — use `"behaviorId": "simple_hook_effect"` with a `hook` and an
`effect`. Anything else needs a `RelicBehavior` subclass in `scripts/relics/` overriding one of
the seven hooks (extending `SimpleHookEffectRelic` to the other five hooks is on the roadmap).

## Tests and verification

There's no test framework. Each `scenes/debug/*SmokeTest.tscn` runs assertions in `_Ready`,
prints `PASS`/`FAIL` per check and a `<Name>: N passed, M failed` summary, then exits nonzero if
anything failed. 14 suites, 332 checks:

```bash
tools/run-smoke-tests.sh                 # all of them; builds first, nonzero exit on any failure
tools/run-smoke-tests.sh MapSmokeTest    # a subset
```

Run them after touching anything under `scripts/` or any `.tscn`. Two suites print expected
noise that is not a regression: `MetaProgressionSmokeTest` emits a JSON parse warning from its
deliberate corrupt-save case, and `ScreenSmokeTest` / `Phase4ContentSmokeTest` each emit one
`Parent node is busy adding/removing children` error from a test clicking a button that changes
scene.

To look at a screen, render it — the harness instantiates the real `.tscn` with realistic data
seeded and a fixed RNG seed, so shots are reproducible:

```bash
dotnet build
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . \
    scenes/debug/ScreenShot.tscn -- shop reward unlocks
```

Screens: `combat` `reward` `shop` `map` `rest` `treasure` `event` `unlocks` `runend` `mainmenu`
`settings`. With no names it shoots all of them. PNGs land at
`~/Library/Application Support/Godot/app_userdata/Hollowdeck/shot_<name>.png`. **Never
`--headless` for screenshots** — the dummy renderer returns an empty viewport texture. (Headless
is correct for the smoke tests, which render nothing.)

Both workflows are also packaged as Claude Code skills in `.claude/skills/` — `smoke-test`
(which suite covers which subsystem) and `verify-screen`.

Beyond that, the bar is the same as it's always been: the game launches from the editor and from
a packaged export, the loop is playable end-to-end by hand, and no errors or warnings appear in
the Godot debugger during that playthrough.

## Docs

- [CLAUDE.md](CLAUDE.md) — architecture, conventions, and guidance for Claude Code
- [ROADMAP.md](ROADMAP.md) — what's still open
- [CREDITS.md](CREDITS.md) — asset licensing and attribution; **must ship with any build**
