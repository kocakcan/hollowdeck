# Hollowdeck — Tech Stack & Architecture Plan

## Context

A new, standalone desktop game meant to capture the feel of Slay the
Spire — node-based map traversal, turn-based card combat with telegraphed
enemy intents, relics/potions, and cross-run meta-progression. This is a
separate project/repo, unrelated to any prior codebase. The intended
builder is an experienced general-purpose programmer (comfortable in Rust)
but new to full game engines (scene editors, built-in UI layout,
tweening). Confirmed scope: desktop-only (Windows/Mac/Linux), full game
(not just a combat prototype), single-player, no networking. This plan
picks a stack and sequences the build so someone new to engines isn't also
building UI/animation infrastructure from scratch while learning the tool.

## Recommendation: Godot 4.x with C#

Godot's scene/node model maps directly onto this genre's screens (map,
combat, shop, reward are just scenes), `Control` nodes with
anchors/containers solve card layout/hover/drag largely for free, and the
built-in `Tween`/`AnimationPlayer` cover "juice" (card fly-in, hit flash,
screen shake) without pulling in a third-party library. C# keeps
static-typing/refactor safety close to what a Rust background provides;
GDScript remains a lower-friction fallback if the C# toolchain causes setup
pain.

**Why not the alternatives:**
- **Unity (C#)** — most mature 2D/UI tooling and the most deckbuilder
  tutorials, but heavier editor footprint and no real advantage over Godot
  4 for a solo desktop project. Keep as a documented fallback if Godot's C#
  integration proves rocky.
- **Rust + Bevy** — tempting for language continuity, but `bevy_ui` and
  tweening (needs `bevy_tweening`) are the weakest parts of Bevy today,
  exactly the tooling this genre leans on most. Also stacks a new paradigm
  (ECS) on top of a new engine simultaneously — general Rust fluency
  doesn't transfer to ECS architecture decisions.
- **Rust + macroquad-style immediate mode** — an immediate-mode canvas
  library, not an engine. Means hand-rolling a scene stack, UI
  layout/hit-testing, tweening, and drag targeting — the opposite of what
  reduces engine-ramp risk here.
- **TypeScript + Electron/Tauri** — React/Framer Motion is genuinely good
  for card animation, but still isn't a game engine; the combat state
  machine and effect system get hand-built anyway, plus desktop packaging
  overhead, with no net reduction in risk vs. picking an actual engine.

**Explicit tradeoff:** giving up Rust continuity and Bevy's compile-time
ECS purity, in exchange for a scene editor + built-in UI layout + built-in
tweening — worth it for a UI-heavy, full-scope, engine-newcomer project.
Would *not* be worth it for a small combat-only prototype.

## Architecture

**Content is data, effects are code, connected by string keys.** This is
the single most load-bearing decision — with hundreds of cards, a
one-class-per-card approach becomes unmaintainable. Author
cards/relics/enemies/potions as JSON (or Godot `.tres` Resources); each
`CardDefinition` holds a list of `EffectSpec`s (`{action: "deal_damage",
amount: 6, ...}`) that key into a code-side `EffectRegistry` of `IEffect`
implementations (`DealDamageEffect`, `ApplyStatusEffect`,
`DrawCardsEffect`, ...). New cards are new data rows, not new classes.
Reserve a small `IScriptedEffect` escape hatch for the handful of truly
unique cards that don't decompose.

Suggested layout:
```
/data/cards/*.json /relics/*.json /enemies/*.json /potions/*.json
/scripts/effects/    — IEffect implementations + EffectRegistry
/scripts/run/         — RunManager (autoload), deck/pile management
/scripts/combat/       — CombatManager: turn loop, intent telegraphing, targeting
/scripts/map/            — map generation, node graph
/scenes/{MapScreen,CombatScreen,ShopScreen,RewardScreen,MetaProgressionScreen}.tscn
```

**State machine:** `RunManager` (autoload) owns run state and drives scene
transitions: MainMenu → RunSetup → MapScreen → {Combat, Elite, Event, Rest,
Shop, Treasure} → RewardScreen → back to MapScreen → Victory/Defeat →
MetaProgressionScreen. Keep meta-progression (unlocks/currency) in a
separate save file/autoload (`MetaProgressionManager`) from in-run save
state — different lifetimes and versioning needs.

**Combat loop** (`CombatManager`), as an explicit state machine (enum +
transitions), not loose booleans — sub-states like "awaiting target",
"animation playing (input locked)", "enemy turn executing" are exactly
where these games accumulate input-during-animation bugs if left implicit:
1. Combat start → shuffle/draw opening hand → `OnCombatStart` relic
   triggers → enemies pick and **display** their first intents.
2. Player turn: play cards (targeting sub-state for single-target cards) →
   resolve `EffectSpec`s → fire relic triggers.
3. End turn: discard non-retained hand, resolve end-of-turn statuses.
4. Enemy turn: execute the **already-telegraphed** intent, then pick and
   display the *next* intent.
5. Repeat to victory/defeat → RewardScreen.

## Build Sequencing

- **Phase 0 — Skeleton.** New Godot project, verify C# support works
  (spend ~1 day trying GDScript too before committing), stub
  `RunManager`/`MetaProgressionManager` autoloads, minimal scene-nav stack,
  git repo. Done: navigate two empty screens, render one clickable card
  that responds to hover/drag.
- **Phase 1 — Vertical slice.** One map, full combat turn loop, ~10 cards
  (Attack/Skill), 1–2 enemy types with simple intent patterns, HP/energy/
  draw/discard/exhaust piles, win/lose screens. No relics/potions/meta yet.
  Test a **multi-enemy** fight here so targeting isn't reworked later.
  Done: play 3 sequential fights start to finish, win or die, restart.
- **Phase 2 — Relics & potions.** Event-bus relic triggers, ~10–15 relics
  across different hooks, potions, Shop screen, Treasure rooms. Done: gold
  economy works and relics measurably change outcomes.
- **Phase 3 — Meta-progression.** Persistent save (separate from run save,
  tolerant deserialization), unlock currency/conditions, seed logging,
  unlock screen. Done: run outcomes change what's available in future runs,
  persisting across restarts.
- **Phase 4 — Content breadth & polish.** Expand toward a capped target
  (recommend ~80–120 cards / one class initially, not full 250+ card
  parity), elite fights, act bosses, branching map complexity, animation/
  juice pass, settings.

## Genre-Specific Risks

1. **Effect system must be composable, not hardcoded per card** — get the
   `EffectSpec`/`EffectRegistry` pattern right in Phase 1; retrofitting
   after 50+ hardcoded cards is expensive.
2. **RNG determinism** — separate seeded streams for map generation, combat
   (shuffle/draw), enemy AI, and cosmetic-only effects, so visual jitter
   never desyncs a deterministic run. Sets up reproducible bug reports and
   a future seed-sharing feature cheaply.
3. **Save/run-state serialization** — save instance IDs referencing
   definitions, not embedded definitions, so balance tweaks during dev
   don't break old saves. Tolerant deserialization (ignore unknown fields).
4. **Combat sub-state explicitness** — see state machine above; this is
   where "played a card mid-animation" bugs come from if skipped.
5. **UI drag/targeting feel** — prototype in Phase 0/1, not deferred to
   polish; reveals Control-node layering/z-index issues early.
6. **Content scope creep** — cap Phase 4's initial target explicitly rather
   than chasing full parity with Slay the Spire itself; treat more content
   as post-launch/ongoing.

## Critical First Files (Phase 0–1)

- `scripts/run/RunManager.cs` — autoload, run state + scene-transition
  orchestration
- `scripts/combat/CombatManager.cs` — turn loop, intent telegraphing,
  targeting sub-state
- `scripts/effects/EffectRegistry.cs` + `scripts/effects/IEffect.cs` — the
  composable effect system every card/relic/potion definition keys into
- `data/cards/*.json` schema — establishes the data-vs-code split the
  whole content pipeline depends on
- `scenes/CombatScreen.tscn` — where card drag/hover/targeting feel gets
  prototyped first

## Verification

Since this is greenfield, "verification" at each phase means: the game
launches from the Godot editor and a packaged export, the phase's "Done
when" criterion is actually playable end-to-end by hand (map → combat →
win/lose → restart for Phase 1; full run loop with relics for Phase 2/3),
and no console errors/warnings appear in the Godot debugger during that
playthrough. No automated test suite is proposed for Phase 0–1; consider
adding GUT (Godot Unit Test) coverage for the `EffectRegistry` and combat
resolution logic once Phase 1's shape stabilizes.
