# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state of this repo

This repo is **pre-implementation**. It currently contains only `hollowdeck.md`, a tech-stack and
architecture plan — no Godot project, no source code, and no build/test tooling exist yet. There
are no build/lint/test commands to document because nothing has been scaffolded.

When asked to start implementing, treat `hollowdeck.md` as the source of truth for stack and
architecture decisions below, and scaffold the Godot project per "Build Sequencing" (Phase 0
first). Update this CLAUDE.md with real commands (Godot CLI invocations for headless
export/test runs, GUT test commands, etc.) once the project exists.

## Project

Hollowdeck is a standalone desktop deckbuilder roguelike in the vein of Slay the Spire:
node-based map traversal, turn-based card combat with telegraphed enemy intents, relics/potions,
and cross-run meta-progression. Desktop-only (Windows/Mac/Linux), single-player, no networking.
Target builder: experienced general-purpose programmer (Rust background) who is new to full game
engines (scene editors, UI layout systems, tweening) — so the plan front-loads engine-provided UI
and animation tooling instead of hand-rolling it.

## Stack

**Godot 4.x with C#.** Scenes/nodes map directly onto the genre's screens (map, combat, shop,
reward); `Control` nodes with anchors/containers handle card layout/hover/drag; built-in
`Tween`/`AnimationPlayer` handle animation ("juice") without third-party libraries. GDScript is
the documented fallback if the C# toolchain proves painful (spend ~1 day validating this in
Phase 0 before committing).

Rejected alternatives (see `hollowdeck.md` for full reasoning): Unity C# (no real advantage over
Godot 4 here), Rust+Bevy (`bevy_ui`/tweening immature, plus stacks ECS-learning on
engine-learning), Rust immediate-mode canvas libs (means hand-rolling scene stack/UI/tweening —
exactly the risk this plan avoids), TypeScript+Electron/Tauri (not an engine; state machine and
effects still hand-built, plus packaging overhead).

## Architecture

**Content is data, effects are code, connected by string keys.** This is the single most
load-bearing decision. Cards/relics/enemies/potions are authored as JSON (or Godot `.tres`
Resources); each `CardDefinition` holds a list of `EffectSpec`s (e.g.
`{action: "deal_damage", amount: 6, ...}`) that key into a code-side `EffectRegistry` of `IEffect`
implementations (`DealDamageEffect`, `ApplyStatusEffect`, `DrawCardsEffect`, ...). New cards are
new data rows, not new classes — a one-class-per-card approach becomes unmaintainable at
hundreds-of-cards scale. Reserve a small `IScriptedEffect` escape hatch for the rare card that
doesn't decompose into existing effects.

Intended layout once scaffolded:
```
/data/cards/*.json /relics/*.json /enemies/*.json /potions/*.json
/scripts/effects/    — IEffect implementations + EffectRegistry
/scripts/run/         — RunManager (autoload), deck/pile management
/scripts/combat/       — CombatManager: turn loop, intent telegraphing, targeting
/scripts/map/            — map generation, node graph
/scenes/{MapScreen,CombatScreen,ShopScreen,RewardScreen,MetaProgressionScreen}.tscn
```

**State machine:** `RunManager` (autoload) owns run state and drives scene transitions:
MainMenu → RunSetup → MapScreen → {Combat, Elite, Event, Rest, Shop, Treasure} → RewardScreen →
back to MapScreen → Victory/Defeat → MetaProgressionScreen. Meta-progression (unlocks/currency)
lives in a separate save file/autoload (`MetaProgressionManager`) from in-run save state —
different lifetimes and versioning needs, don't merge them.

**Combat loop** (`CombatManager`) must be an explicit state machine (enum + transitions), not
loose booleans — sub-states like "awaiting target", "animation playing (input locked)", "enemy
turn executing" are exactly where these games accumulate input-during-animation bugs if left
implicit:
1. Combat start → shuffle/draw opening hand → `OnCombatStart` relic triggers → enemies pick and
   **display** their first intents.
2. Player turn: play cards (targeting sub-state for single-target cards) → resolve `EffectSpec`s
   → fire relic triggers.
3. End turn: discard non-retained hand, resolve end-of-turn statuses.
4. Enemy turn: execute the **already-telegraphed** intent, then pick and display the *next*
   intent.
5. Repeat to victory/defeat → RewardScreen.

## Build sequencing

Follow this order — each phase's "Done when" is the exit criterion before moving on:

- **Phase 0 — Skeleton.** New Godot project, verify C# support works (try GDScript too before
  committing), stub `RunManager`/`MetaProgressionManager` autoloads, minimal scene-nav stack, git
  repo. Done: navigate two empty screens, render one clickable card that responds to hover/drag.
- **Phase 1 — Vertical slice.** One map, full combat turn loop, ~10 cards (Attack/Skill), 1–2
  enemy types with simple intent patterns, HP/energy/draw/discard/exhaust piles, win/lose screens.
  No relics/potions/meta yet. Test a **multi-enemy** fight here so targeting isn't reworked later.
  Done: play 3 sequential fights start to finish, win or die, restart.
- **Phase 2 — Relics & potions.** Event-bus relic triggers, ~10–15 relics across different hooks,
  potions, Shop screen, Treasure rooms. Done: gold economy works and relics measurably change
  outcomes.
- **Phase 3 — Meta-progression.** Persistent save (separate from run save, tolerant
  deserialization), unlock currency/conditions, seed logging, unlock screen. Done: run outcomes
  change what's available in future runs, persisting across restarts.
- **Phase 4 — Content breadth & polish.** Expand toward a capped target (~80–120 cards / one
  class initially — not full 250+ card parity), elite fights, act bosses, branching map
  complexity, animation/juice pass, settings.

## Genre-specific risks to keep in mind

1. **Effect system must be composable, not hardcoded per card** — get the `EffectSpec`/
   `EffectRegistry` pattern right in Phase 1; retrofitting after 50+ hardcoded cards is expensive.
2. **RNG determinism** — use separate seeded streams for map generation, combat (shuffle/draw),
   enemy AI, and cosmetic-only effects, so visual jitter never desyncs a deterministic run.
3. **Save/run-state serialization** — save instance IDs referencing definitions, not embedded
   definitions, so balance tweaks during dev don't break old saves. Use tolerant deserialization
   (ignore unknown fields).
4. **Combat sub-state explicitness** — see state machine above.
5. **UI drag/targeting feel** — prototype in Phase 0/1, not deferred to polish; reveals
   Control-node layering/z-index issues early.
6. **Content scope creep** — cap Phase 4's initial target explicitly rather than chasing full
   parity with Slay the Spire; treat more content as post-launch/ongoing.

## Critical first files (Phase 0–1)

- `scripts/run/RunManager.cs` — autoload, run state + scene-transition orchestration
- `scripts/combat/CombatManager.cs` — turn loop, intent telegraphing, targeting sub-state
- `scripts/effects/EffectRegistry.cs` + `scripts/effects/IEffect.cs` — the composable effect
  system every card/relic/potion definition keys into
- `data/cards/*.json` schema — establishes the data-vs-code split the whole content pipeline
  depends on
- `scenes/CombatScreen.tscn` — where card drag/hover/targeting feel gets prototyped first

## Verification

Since this is greenfield, "verification" at each phase means: the game launches from the Godot
editor and a packaged export, the phase's "Done when" criterion is actually playable end-to-end
by hand, and no console errors/warnings appear in the Godot debugger during that playthrough. No
automated test suite is proposed for Phase 0–1; consider adding GUT (Godot Unit Test) coverage
for the `EffectRegistry` and combat resolution logic once Phase 1's shape stabilizes.
