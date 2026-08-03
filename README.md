# Hollowdeck

[![CI](https://github.com/kocakcan/hollowdeck/actions/workflows/ci.yml/badge.svg)](https://github.com/kocakcan/hollowdeck/actions/workflows/ci.yml)

A standalone desktop deckbuilder roguelike in the vein of Slay the Spire: traverse a branching
node map, fight turn-based card combats against enemies that telegraph their intents, collect
relics and potions, and unlock new content across runs by scoring well. Single-player, no
networking, desktop only (Windows/Mac/Linux).

## Status

The core loop is playable end-to-end — new run, map, combat, events, shop, rest, treasure,
rewards, bosses, run-end scoring, unlocks, and mid-run save/resume. Current content is three
acts: **84 cards, 27 relics, 12 potions, 36 enemies (6 of them bosses), 15 events**. Each act has
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

## Controls

Everything is playable with the mouse or entirely with the keyboard; neither is a second-class
path. Bindings are named `hd_*` actions in `project.godot`'s `[input]` section.

**In combat**

| Key | Does |
| --- | --- |
| `1`–`9`, `0` | Select the card in that hand slot (the badge on each card is its key) |
| `←` `→` | Cycle the selection — or, once a card is aimed, cycle the target |
| `Space` | Play the selected card. A single-target card aims first, so it's Space twice |
| `Z` `X` `C` | Drink the potion in that belt slot, then aim it the same way |
| `Enter` | End turn — and, at the end of a fight, Continue |
| `Esc` | Cancel aiming (right-click does the same) |
| `D` `Q` `W` `E` | Inspect the deck / draw / discard / exhaust pile; `Esc` closes |

**Everywhere else** — `Tab` and the arrow keys move between controls, `Space`/`Enter` activates,
`Esc` goes back. Only legal choices are reachable: unreachable map nodes and unaffordable shop
offers are disabled and get skipped. Whatever the keyboard is on carries a bright gold ring (a
choice card grows slightly instead).

## Repo layout

```
scripts/
  run/        RunManager, RunState, save/load, MetaProgressionManager, RunScore,
              AudioManager, SettingsManager, RngStreams, PileManager, *Instance types
  combat/     CombatManager state machine, Combatant, EnemyFactory, intent pickers
  effects/    IEffect implementations + EffectRegistry, DamageMath
  relics/     RelicBehavior (7 hooks) + registry + SimpleHookEffectRelic, which every relic uses
  events/     IEventOutcome implementations + EventOutcomeRegistry
  map/        MapGenerator, MapNode, MapNodeType
  data/       Definition classes and the JSON databases that load them, EffectSpec
  ui/         Screen controllers, CardView, EnemyView, theming, layout helpers
  audio/      AudioSynth / AudioCues / AudioMusic — everything is synthesized at runtime
  debug/      17 smoke-test scenes + the screenshot harness
scenes/       11 screens + reusable CardView/EnemyView/PotionView/FloatingText
  debug/      smoke-test and screenshot scenes
data/         acts / cards / relics / potions / enemies / events — all JSON, the content layer
assets/       sprites, icons, fonts, backgrounds, themes (see CREDITS.md for licensing)
tools/        run-smoke-tests.sh
  artgen/     Rust asset tool — generates the 161 icons, palette-clamps art, validates ART_SPEC
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

The ten `action` keys `EffectRegistry` currently knows: `deal_damage`, `gain_block`,
`apply_status`, `draw_cards`, `heal`, `gain_energy`, `lose_hp`, `discard_cards`, `exhaust_hand`,
`gain_gold`. The eleven statuses available to `apply_status`: `Vulnerable`, `Weak`, `Strength`,
`Poison`, `Dexterity`, `Frail`, `Metallicize`, `Ritual`, `Regen`, `Fervor`, `Foresight` — the last
five pay out every turn and never decay, which is what a `Power` card buys. A genuinely new
mechanic means a new `IEffect` in `scripts/effects/` registered in `EffectRegistry` — reach for
that only when the mechanic can't be composed from existing actions.

**A potion** (`data/potions/potions.json`) has the same `effects` shape. **An enemy**
(`data/enemies/enemies.json`) is HP plus a list of moves, each pairing a displayed `intent` with
the `effects` it will actually resolve; `aiType` picks the intent strategy (`sequential`,
weighted, or phase-threshold). An intent is one of `Attack` / `Defend` / `Buff` / `Debuff` plus a
`displayAmount`, and that number is the *only* authored part of the telegraph — the hit count and
the name of the status a Buff grants are derived from the move's own effects, so a move can't
promise something it doesn't do. A new enemy also needs a 32x32 sprite at
`assets/sprites/enemies/<id>.png` (sourced and clamped, see `CREDITS.md`) and a reference from
some act's pool; both are asserted. **An event** (`data/events/events.json`) is text plus choices,
each naming one of `EventOutcomeRegistry`'s fifteen outcomes (`gain_gold`, `lose_gold`, `heal`,
`lose_hp`, `gain_max_hp`, `lose_max_hp`, `gain_random_card`, `gain_relic`, `lose_relic`,
`gain_potion`, `upgrade_random_card`, `gamble`, `remove_chosen_card`, `upgrade_chosen_card`,
`none`).

**An act** (`data/acts/acts.json`) is a chapter of a run, in play order: `floorCount`, the
`normalEncounters` / `eliteEncounters` pools (each entry is one group, so `["slime","slime"]` is a
single two-slime fight), a `bossIds` pool the run seed picks one from, `mapBackground` /
`combatBackground` tiles with hex tints, the gold a fight pays, and the max-HP bonus plus heal
percentage granted for clearing it. Adding a fourth act is a row here plus the enemies it names —
`MapGenerator` reads all of it and `RunState.AdvanceAct` walks the list, so no code changes.

**Art** follows the same convention as everything else: `ArtAssets.cs` resolves it by definition
id, so a card named `strike` picks up `assets/icons/cards/strike.png` with no schema or code
change. Those icons are generated — add a `fn` and one registry line in `tools/artgen/src/icons/`,
then `cargo run --release --manifest-path tools/artgen/Cargo.toml -- generate`. See
`tools/artgen/README.md`; `PixelSpecSmokeTest` fails if a definition has no icon or an icon has no
definition.

**A relic** (`data/relics/relics.json`) is a data row like everything else. All 27 declare
`"behaviorId": "simple_hook_effect"`; none is a C# class. A relic is a `hook` — any of
`RelicBehavior`'s seven — plus the `effect` it fires, narrowed by three optional keys:

```json
{
  "id": "ledger_of_ruin",
  "behaviorId": "simple_hook_effect",
  "hook": "OnCardPlayed",
  "target": "Self",
  "condition": { "cardType": "Attack" },
  "limit": { "oncePerCombat": true },
  "effect": { "action": "apply_status", "status": "Strength", "amount": 1 }
}
```

- **`target`** — `Self` (the default), `Attacker` (OnDamageTaken's), `FirstEnemy`, `RandomEnemy`,
  `AllEnemies`. This is the relic's own selector, *not* `EffectSpec.Scope`: a relic has no card
  targets to inherit, so `scope` is meaningless on a relic effect and is left off.
- **`condition`** — `cardType`, `outcome` (`"Win"`/`"Lose"`), `minEnergy`, `minHpPercent`,
  `targetKilled`. Each is checked only when present, and only makes sense on some hooks.
- **`limit`** — `oncePerTurn`, `oncePerCombat`, `everyNth`. The per-turn counters reset on the
  player's turn start, so a hit taken during the enemy turn counts against the player turn that
  follows it.

A `RelicBehavior` subclass in `scripts/relics/` is still the escape hatch for a mechanic none of
that reaches, but nothing in the game currently needs one — same standing as `IScriptedEffect`.

## Tests and verification

There's no test framework. Each `scenes/debug/*SmokeTest.tscn` runs assertions in `_Ready`,
prints `PASS`/`FAIL` per check and a `<Name>: N passed, M failed` summary, then exits nonzero if
anything failed. 17 suites, 770 checks:

```bash
tools/run-smoke-tests.sh                 # all of them; builds first, nonzero exit on any failure
tools/run-smoke-tests.sh MapSmokeTest    # a subset
```

The script also runs `tools/artgen validate` first, which enforces `docs/ART_SPEC.md` against the
raw PNG bytes — grid, palette, hard alpha, no SVG. It's skipped with a warning if `cargo` isn't
installed; the game itself never needs a Rust toolchain.

Each suite runs under a 90-second watchdog (`SUITE_TIMEOUT`). A test that throws inside `_Ready` —
a `GetNode` against a path a `.tscn` no longer has is the usual way — never reaches its
`GetTree().Quit()`, so Godot drops into an idle main loop and the whole sweep stalls instead of
failing. The watchdog turns that back into a `TIMEOUT` line and a nonzero exit.

The same script is what CI runs (`.github/workflows/ci.yml`) on every push to `main` and every
pull request, against the pinned 4.7.1-stable mono build. It imports assets first — `.godot/` is
gitignored, so a fresh checkout has no imported resources and every `ResourceLoader.Exists()`
returns false — and afterwards re-runs `artgen generate` to check the committed art still matches
the code that produces it.

Run them after touching anything under `scripts/` or any `.tscn`. Some suites print expected
noise that is not a regression: `MetaProgressionSmokeTest` emits a JSON parse warning from its
deliberate corrupt-save case, and `ScreenSmokeTest` / `Phase4ContentSmokeTest` / `KeyboardSmokeTest`
each emit one `Parent node is busy adding/removing children` error from a test clicking a button
that changes scene.

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
