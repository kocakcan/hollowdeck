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
(`DealDamageEffect`, `ApplyStatusEffect`, `DrawCardsEffect`, ...). `scope` is *per-effect*
targeting one level below `CardTargetType` — `Target`/`Self`/`AllEnemies`/`RandomEnemy` — which is
what lets one card hit its target and debuff the room. New cards are new data rows,
not new classes — a one-class-per-card approach becomes unmaintainable at hundreds-of-cards
scale. `IScriptedEffect` exists as an escape hatch for the rare card that doesn't decompose into
existing effects.

**A card carries two independent channels, and both are live.** `CardType`
(`Attack`/`Skill`/`Power`/`Status`/`Curse`) drives the frame fill; `Rarity`
(`Common`/`Uncommon`/`Rare`) drives the border *and* how often the card is offered — `CardPool` weights every reward, shop and event draw
60/37/3, so a Rare is an event rather than one row in a shuffled list. Playing a `Power` moves it
to `PileManager.Powers`, which is neither Discard (it would cycle back) nor Exhaust (a cost the HUD
renders as one).

The rest of the draw layer — the skip-streak ladder that shifts those weights, potion rarity at
65/25/10, `RelicTier`'s power-ladder-plus-source split and its per-site filters, the reward
screen's tip rotation, rewards as unclaimed *offers*, the boss's choice of three relics, and the
per-act potion drop roll — is in the **`rewards-and-pools` skill**.

The vocabulary built on top of that split — what a Power buys and why `Fervor`/`Foresight` are
kept out of `ApplyTurnStartGrants`, the fifteen-status roster and its three decay rules, the
`Retain`/`Ethereal`/`Innate` keyword layer, `IsPlayable`, the `add_card` primitive and X-cost —
is in the **`effects-and-statuses` skill**. Read it before adding a status: a new one has to
reach six places (an icon, `Keywords.Blurb`, `CardUpgrade.ShouldScale`, `StatusRow.IsDebuff` if
it is a debuff, `CombatManager.DecayAtTurnEnd` if it decays on a clock), and five of the six
fail silently.

**No pixel surface transforms, and what enforces that is derived rather than listed.** A hand card
lifts 18 snapped pixels and paints a halo; it does not scale, and the fan does not tilt. Damage
numbers step between two legal font rungs; they do not punch in from 2.2x. Reward cards breathe on
`modulate:a`; they do not sway on `rotation_degrees`. `PixelSpecSmokeTest`'s transform scan treats
`this` as a pixel surface in any file declaring a `TextureRect`, deriving from `Label`, or assigning
`Icon` from `ArtAssets` (thirteen files today), and treats instances of those classes as pixel
surfaces wherever they are declared — so a violation reaching in from another file is caught too. It
reads **code lines only**, because this file and its neighbours are dense prose about pixel art and
the scan matches source text: keyed to comments, coverage moved when a comment was reworded. A **static integer** scale is still legal and goes through
`PixelSpec.ApplyIntegerScale`, which takes an `int` so the scan does not have to evaluate one.
ART_SPEC §9; `docs/PIXEL_ART_ROADMAP.md` §1 carries the four holes that were found closing this, all
of them by mutating the guard rather than by reading it.

**Autoloads** (declared in `project.godot`, in this order — `AudioManager` must come before
`SettingsManager` because the settings sliders address audio bus indices):

| Autoload | Owns |
| --- | --- |
| `RunManager` | run state and scene transitions |
| `AudioManager` | procedural SFX/music playback, Music/SFX bus split |
| `MetaProgressionManager` | cross-run unlocks, persisted separately from run state |
| `SettingsManager` | volumes, fullscreen, reduce-motion |

**Screen flow:** `RunManager` drives MainMenu → RunSetupScreen → MapScreen → {Combat, Elite, Event,
Rest, Shop, Treasure} → RewardScreen → back to MapScreen → RunEnd → MetaProgressionScreen.
`ScenePaths` now covers every `ScreenState` — it used to document the *intended* flow with RunSetup
deliberately unbuilt, and `RunSaveSmokeTest.TestAutoSaveScreensSetIsCorrect` asserts the two stay in
agreement, because `ChangeScreen` to an unregistered state pushes an error and *returns*: no crash,
no screen change, and a run left standing wherever it was. Meta-progression
lives in its own save file (`user://meta_progression.json`) separate from in-run save state
(`user://run_save.json`) — different lifetimes and versioning needs, don't merge them. The meta
save carries a schema version and a v1→v2 migration; keep deserialization tolerant of unknown
fields.

The scaffolding around a run — the `RunSetupScreen` blessing-and-seed screen and the ordering trap
a re-seed carries, the twenty-rung ascension ladder and the nine knobs `AscensionModifiers` owns,
the concealed `?` node, and the map-width budget that couples `MapGenerator.MaxNodesPerFloor` to
`MapScreen`'s layout pitch — is in the **`run-structure` skill**.

**Acts:** a run is three acts, authored in `data/acts/acts.json` (`ActDefinition`/`ActDatabase`).
`RunState.ActIndex` says which one is in play; `RunState.MapNodes` only ever holds the *current*
act's graph. Killing a non-final act's boss goes RewardScreen-then-Map like any other fight, with
`RunState.AdvanceAct()` regenerating the map, resetting `CurrentNodeId`/`VisitedNodeIds` (node ids
repeat across acts), banking the cleared act's floors into `RunStats.FloorsInPreviousActs`, and
applying the act's max-HP bonus and heal. Only `IsFinalAct`'s boss routes to Victory. The run save
is v5 for `RunState.CardSkipStreak`, v4 for `MapNode.Concealed`; no bump carries migration code,
because every one of those fields' absent-is-false/zero default is already the right reading of an
older save (a v2 save loads as act 1, which is what it always was; a v3 map loads fully visible,
which is what it always was; a v4 run has declined nothing, which is what it always had).

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

The rest of the fight — how a telegraph is *derived* from a move's own `EffectSpec`s (a drifted
telegraph is the canonical bad bug in this genre), the `wake_on_damage` sleeper and its mid-turn
re-telegraph, `WeightedRandomIntentPicker`'s run cap, the `ResolveDeathsAndSettle` pass that owns
summon/escape/`onDeath`, and the `MaxEnemies` layout budget — is in the **`combat-loop` skill**.

**Input is two layers, and the game is fully playable without a mouse.** Every binding is a named
`hd_*` action in `project.godot`'s `[input]` — never a raw `Key.X` switch — so there is one place
to look and a future rebinding UI has something to rebind. Above that, the two surfaces are
navigated differently *on purpose*:

- **Non-combat screens use Godot's own focus system.** They are stock `Button`s, so Tab/arrow
  navigation comes free. `ScreenKeyboardNav.Attach` is the one line each screen adds, giving it an
  initial focus owner, a re-grab after it rebuilds controls, and `hd_cancel`.

  **Keeping the keyboard *off* an illegal choice is not free, and `Disabled` does not do it.**
  Measured on 4.7.1: `Disabled` excludes a control from neither Tab nor arrow navigation, and does
  not release focus it already holds — `BaseButton` keeps `FocusModeEnum.All` throughout. Only
  `FocusModeEnum.None` excludes *or* releases. The two screens with illegal-but-present choices set
  both together (`MapScreen.BuildButtons` on unreachable nodes, `ShopScreen.RefreshOffers` on
  unaffordable and sold offers); everywhere else every control on screen is a legal one, so
  `Disabled` alone is fine. This file, `ScreenKeyboardNav` and README all claimed the opposite for a
  long time, and the resulting bug — the focus ring parked on a map node the player could not walk
  to — shipped. `KeyboardSmokeTest.TestOnlyFocusModeExcludesAControlFromTheKeyboard` pins the engine
  behaviour so an upgrade that changes it is noticed here.
- **Combat drives its own `_UnhandledInput`.** Cards are fanned `Panel`s and targeting is a
  `CombatState` sub-state, so `EnemyView`, `PotionView`, End Turn and Continue all stay
  `FocusModeEnum.None` — focus navigation would fight the arrow-key card cycling. Don't "fix" those
  by making them focusable.

`ScreenKeyboardNav.KeyHint(action)` reads a binding back out of the `InputMap` for on-screen hints,
so a badge or tooltip can't drift from the key that actually fires.

**RNG** is split into separate seeded streams in `RngStreams` — `Combat`, `EnemyAI`, `Shop`,
`Map`, `Drops` — all derived from the run seed, so drawing an extra card can't shift what the shop
stocks and cosmetic jitter can never desync a deterministic run. The derivation is per-stream
(`runSeed * 397 + n`) rather than sequential, which is what makes appending one free: `Drops`
landed at `+4` and moved no existing seed's map, shop or shuffles. `Drops` owns the post-fight
potion roll alone — the shop's stock and the `gain_potion` event stay on `Shop`, which is where
every non-combat grant already draws from.

## Current state

An earlier shard *shop* was removed — don't reintroduce shard-purchase language.

There are **36 enemies** (7 normals + 3 elites per act, plus 6 bosses) across 3 acts. Card, relic,
event, potion, blessing, status and effect counts all live in `data/*/*.json` and the relevant
enums — read those rather than trusting a number here to stay current.

Those per-act counts are *enemy ids*, not encounter slots: each act's `eliteEncounters` also fields
one of its own **normals** as an escort — `rot_hound` (act 1), `ember_wisp` (act 2), `hollow_shade`
(act 3) — so a normal appearing in an elite group is intended, not a mis-authored row. Sharing
*across* acts is the thing that is forbidden, and that is the half `ActSmokeTest` asserts.
`summon_enemy` is a *second* route an enemy id reaches a fight by, and it bypasses the act pools
entirely — `ActSmokeTest.TestNoSummonCrossesAnAct` is what stops it being the one way act 3 content
turns up in an act 1 room.

**A backdrop is a room, not a texture, and that is the load-bearing half.** `ScreenBackground`
composes four bands — a wall, a colonnade in front of it, the plinth where the wall meets the
ground, and the floor — plus two haze layers drifting at two rates. `ActDefinition.Backdrop` names
the act's set (`ward`/`reach`/`throne`); its wall, plinth and pillar are *derived* from that prefix
while the three floors are authored, because those three are one room and an act that could pair
act I's wall with act III's plinth is expressing something nobody wants. A fifth piece, the **focal feature**, is placed once rather than tiled, and it is
the only thing back there that is a subject rather than a surface. **The arch is the act's and what
stands in it is the room's**: six interiors (`ScreenBackground.BackdropRoom` — Monument, Doorway,
Hearth, Stall, Shrine, Strongroom) across three acts, so a shop is not a rest site and act II's shop
is not act I's. A screen names the *kind of room it is*, never a tile — which is why the enum
argument on `AttachRoom` is not a violation of the call-site sweep. Placement is per surface, and
what is aimed at a screen's clear canvas is the arch's **opening** rather than its centre: the shop
has no 512px hole in it at all, so its frame goes behind the cards and its counter lands in the
strip right of them. It is 256x128 where every other backdrop asset is 64x64, which makes
`assets/backgrounds/` the first directory holding two asset classes; that is why
`PixelSpecSmokeTest`'s grid check calls `PixelSpec.IsLegalGrid` instead of comparing against
`TileGrid`. `ART_SPEC.md` §12 is the rule. What this replaced was one tile filling the whole canvas, which is wallpaper by construction —
and the expensive thing to know is that *generating better tiles for it changed nothing*, because
a tile repeated 9x5 has no horizon and nothing drawn in front of it acquires a position.

**Every screen goes through one of four argument-free entry points** — `AttachMap`, `AttachCombat`,
`AttachRoom`, `AttachMenu` — and no call site names a tile or a colour. Eleven of the thirteen used
to hardcode both, so act III's shop was act I's shop; `PixelSpecSmokeTest.TestNoScreenAuthorsItsOwnBackdrop`
sweeps the call sites, because a vocabulary two sites out of thirteen use looks exactly like one
nobody needs (the `UiTheme.Motion` lesson, one asset class over). Per-act identity lives in the art,
so the tints in `acts.json` are neutral greys carrying brightness alone.

**Icons are generated; sprites are sourced. Nothing in `assets/` is drawn by hand, and nothing is
an SVG.** Don't reintroduce SVG source art — `docs/ART_SPEC.md` fails an SVG anywhere under
`assets/`, and vector downscaled onto a 32x32 grid is mush rather than a sprite. Any non-integer
scale on a pixel asset is a bug, not a judgement call (ART_SPEC §2); alpha/`modulate` is the only
property a pixel asset may be tweened on. Enemy and player sprites are the one **sourced** class
(CC0 Dungeon Crawl tiles, palette-clamped, mapped act by act in `CREDITS.md`) — the backdrop tiles
were the second until §12, and their CC0 entry is now in that file's *Retired* section. There is **no
`artgen` on `PATH`** and no `tools/artgen` wrapper — always invoke it through `cargo run`.

The full pipeline — artgen's three subcommands and how to add an icon, the five combat FX frame
sets, the `chrome` 9-slice output-dir rule, the derived-light rule, creature sprite animation and
`GlowRing` — is in the **`pixel-art` skill**.

`ROADMAP.md` tracks what's genuinely still open. What's open is Phase 11's legibility and feel
work, of which **card inspect has shipped** — hold to peek at a card at 2x, on a mouse dwell or a
held `hd_inspect`, which is what let `CardView`'s 1.15x hover bump be replaced rather than deleted
and emptied `PixelSpecSmokeTest`'s transform-scan exception list. Don't treat this section as a to-do
list.

One open item is worth knowing *before* you touch the code it sits in, rather than when the roadmap
is next read:

- **Status tooltips are mouse-only**, a live parity break against this file's own "fully playable
  without a mouse". `StatusRow` sets stock Godot `TooltipText`, while `CardView` and `EnemyView`
  already route through the keyboard-aware `scripts/ui/HoverTooltip.cs`. Route `StatusRow` through
  the same widget rather than adding a second tooltip path.

**Non-16:9 windows are no longer a live bug, and the fix is a constraint rather than a repair.**
`window/stretch/aspect` is **`keep`** (ART_SPEC §4, pinned by
`PixelSpecSmokeTest.TestCanvasIsLetterboxedNotExpanded`), so the canvas is exactly 1152x648 at every
window size and the fixed offsets every screen is written against — `ScreenChrome`'s `DesignWidth`,
`MapScreen`'s `DesignHeight`, `CombatScreen.tscn`'s band offsets — are right by construction. It was
`expand` until a playtest, which grows the canvas along the window's long axis (1470x956 yields
1152x749) and left the extra 101px simply dead, invisibly, at any 16:9 size.

So the price is bars on an odd-shaped window, and the thing to *not* do is treat that as a reason to
reach back for `expand` — making the screens genuinely responsive is the alternative, and it is a
much larger change than the one-line setting suggests. Two consequences worth carrying: a
screenshot at the design size is now exactly what a player sees at any window size, and the window's
top-left corner no longer maps to canvas `(0, 0)` (see the mouse note in the `smoke-tests` skill).

## Key files

- `scripts/run/RunManager.cs` — autoload, run state + scene-transition orchestration
- `scripts/run/ScreenFade.cs` — the cross-screen fade, on a `CanvasLayer` parented to the
  `RunManager` autoload (a screen cannot own it: `ChangeSceneToFile` frees the current scene
  mid-transition). Gated on `SettingsManager.ReduceMotion`; declines rather than shortens, so the
  hard cut lives on exactly one path
- `scripts/run/RunState.cs`, `RunSaveManager.cs` — in-run state and its save/resume
- `scripts/run/MetaProgressionManager.cs` + `RunScore.cs` — score-driven unlock track, meta save
- `scripts/run/RngStreams.cs` — the five seeded RNG streams
- `scripts/run/TierPool.cs` — the tier-first weighted draw `CardPool`, `PotionPool` and `RelicPool`
  share (named `RarityPool` until relic tiers, and hard-typed to `Rarity` with it). Generic on
  purpose (unlike `BlockMath`/`DamageMath`): what is duplicated here is an algorithm whose every
  step is a correctness property, not a hazard worth two copies. The *weights* stay split, which is
  the `BlockMath` argument doing its job one level down
- `scripts/run/CardPool.cs` — rarity-weighted sampling; the single place "which cards does the
  player get offered" is decided (reward picks, shop stock, the random-card event outcome), and
  therefore the single place unplayable cards are excluded from being offered at all — that
  `IsPlayable` filter lives here rather than in `TierPool`, because it is a card rule. Also owns the
  skip-streak ladder (`MaxSkipStreak`, the three steps, and the `WeightOf`/`Sample` overloads that
  take a rung) — the reward site passes `RunState.CardSkipStreak`, the other two grant sites do not
- `scripts/run/PotionPool.cs` — the same for potions, at 65/25/10, across all four grant sites: the
  combat drop, the shop's two-potion stock, and the `gain_potion` event. No `IsPlayable` analogue —
  every potion in the database is offerable and none is unlock-gated
- `scripts/run/RelicPool.cs` — the same for relics, at 50/33/17 across the ladder, plus the
  per-site tier filter that is the actual feature (`RelicSite` → `TiersFor`). Holds the
  owned-and-unlocked filter that four grant sites each carried a copy of, which is the `IsPlayable`
  argument one content type over — and the ladder **top-up**, which is `pool.Count < count` rather
  than `== 0` so a boss offering three cannot quietly offer two
- `scripts/data/BlessingDatabase.cs` + `BlessingDefinition.cs` — the start-of-run pool and its
  `Offer(count, rng)` draw: distinct, uniform, and with no `Rarity` on the row, because the pool is
  small enough that every row is meant to be seen (`TierPool` is for the pools where that is false).
  There is no exhaustion story either — nothing is owned yet, so the pool is whole on every draw
- `scripts/ui/RunSetupScreen.cs` + `scenes/RunSetupScreen.tscn` — the screen between MainMenu and
  the map. Holds the ordering trap the whole feature turns on (a re-seed after a claim would erase
  the blessing) and the project's only `LineEdit`
- `scripts/data/TipDatabase.cs` — the reward screen's tip line. `ForVisit` is a *rotation* off the
  run seed rather than an `RngStreams` draw, and the comment there says why: a stream's position is
  not serialized, and six `ScreenShot` fixtures re-render that screen
- `scripts/combat/CombatManager.cs` — turn loop, intent telegraphing, targeting sub-state
- `scripts/effects/EffectRegistry.cs` + `IEffect.cs` — the composable effect system every
  card/relic/potion/enemy-move definition keys into
- `scripts/relics/RelicBehavior.cs` — the 7 relic hooks; `SimpleHookEffectRelic.cs` drives all 33
  relics off all 7, using the `target`/`condition`/`limit` vocabulary in
  `scripts/data/RelicTrigger.cs`. Subclassing `RelicBehavior` is the escape hatch and nothing
  currently uses it — `RelicRegistry` has one factory
- `scripts/events/EventOutcomeRegistry.cs` — the 16 non-combat outcome keys, shared by event choices
  and start-of-run blessings through one `Begin(IReadOnlyList<EventOutcomeSpec>, string)`. Fourteen resolve
  instantly; two (`remove_chosen_card`, `upgrade_chosen_card`) implement `ICardPickerOutcome` and
  come back from `Begin()` as *pending*, for `EventScreen` to open a card grid against. A picker
  must be the last spec in a choice and may not appear inside a `gamble` — both enforced by
  `EventSmokeTest`, not just documented
- `scripts/ui/CardPicker.cs` — the "choose one of these cards" grid, shared by the rest site's
  Smith, the two event picker outcomes, and the shop's 75g card-removal service (which reuses
  `RemoveChosenCardOutcome` rather than restating the deck floor)
- `scripts/effects/BlockMath.cs` — Dexterity/Frail, the exact mirror of `DamageMath`'s
  Strength/Weak, split for the same no-drift reason
- `scripts/map/MapGenerator.cs` — branching node DAG, per-act (floor count, encounter pools and
  boss pool all come from the `ActDefinition` passed in). Owns both weight tables: the node-type
  one, and `PickConcealedType`'s separate table for what is behind a `?`. Branching floors are
  **3–5 wide with floor 0 pinned at 3**, and `MaxNodesPerFloor` is **coupled to `MapScreen`'s
  layout** in a way neither file suggests on its own — see the map-width rule in Architecture.
  Length is per-act data (`FloorCount`); width is one constant shared by every act
- `scripts/data/ActDefinition.cs` + `ActDatabase.cs` — the three acts and what varies per act. Also
  owns `IsBoss`, derived from every act's `BossIds` rather than authored on `EnemyDefinition`, so
  "is this a boss" has one answer
- `scripts/data/AscensionDefinition.cs` + `AscensionDatabase.cs` + `AscensionModifiers.cs` — the
  twenty-rung ladder. The rows are per-rung deltas, `Effective(level)` is the cumulative fold, and
  `AscensionModifiers` is the one place a modifier becomes a number (the `ExpectedShopRelicPrice`
  argument, applied to nine knobs at once). `AscensionModifiers.None` is identity and every existing
  `BalanceModel` call site defaults to it
- `data/*/*.json` — the content layer; the schema is the data-vs-code split everything depends on
- `scripts/data/DataFile.cs` — the one place a content JSON is read off `res://`. The null guard
  here is what turns a mis-packed build from a bare `NullReferenceException` into a named error;
  it lives in one file rather than six because six copies of a guard is six places to forget it
- `export_presets.cfg` + `tools/build-export.sh` — the three export presets and the one command that
  builds and then *boots* them. The cfg is committed; nothing secret lives in it, because Godot
  routes codesign and notarization credentials to `.godot/export_credentials.cfg`, already gitignored
- `scripts/debug/BalanceModel.cs` + `tools/balance-report.sh` — the balance analyser and the command
  that prints it. Static analysis off the content databases, not a combat simulator: the enemy turn
  is wall-clock paced (0.35s per enemy action), so driving real fights cannot cover enough of them
  inside the suite watchdog to say anything about a curve. `BalanceReport` prints, `BalanceSmokeTest`
  asserts, and both read thresholds back out of `RunScore` rather than keeping a second copy.
  `EncounterCost` is the headline metric and the one to reach for: damage per turn alone is
  misleading, because it cannot see Poison (six enemies carry it, and Poison 5 is 15 damage over its
  life), an enemy's own Vulnerable amplifying its later hits, or Strength accumulating through an
  enrage phase. Costs compare *within* an act — the reference throughput does not grow act over act
  and a real deck does.

  **A ratio in this report has a denominator, and it is `MeanNormalCost`.** Every elite and boss row
  is a multiple of an average normal fight, so a *normal* encounter getting more expensive makes
  every elite and boss in that act look cheaper — and the suite reports that as the elite drifting
  out of band. Phase 8 shipped that exact misreading: a summon took two act-1 Combat nodes to 101 and
  116, the act's mean went 42 → 48, and `possessed_armor` was given 7 HP it did not need. Before
  moving a number an elite or boss row points at, check `EncounterProfile.Summoned` and the
  costliest-normal line for the same act. Two assertions now stand where that argument was: no normal
  encounter may reach `BossCostLow` (elites are banded against the *mean*, which is exactly the
  statistic one spiking group vanishes into), and an escape move must steal more than the cheapest
  node it can flee — otherwise emptying the board scores as a Win, pays out in full, and rewards the
  slow deck the move was written to punish
- `scenes/CombatScreen.tscn` — card drag/hover/targeting
- `scripts/ui/CombatFx.cs` — the six combat effect runs, and the single place one is positioned,
  sized and freed. Four burst in place; `swipe` and `gash` **travel** (`PlayTravelling` tweens
  `position` between two snapped endpoints while the frames play), which is what let the slash trail
  be one authored orientation instead of the eight-direction set the roadmap forecast — the attacker
  is pinned and the targets are one row above it, so the whole fight spans −13° to −49°. A travelling
  run therefore has its own cadence: for a burst the frame time is only a duration, for these it is
  also a speed. **The two blades are one drawn axis in two pigments**, because an axis is undirected:
  the enemy-to-player span is 131° to 167°, the same line read from the other end, and `fx.rs`
  asserts every axial frame is symmetric under a half turn. So the incoming blade needed a colour and
  not a silhouette — bone for the player's, oxblood for what is coming at them. **Which blade goes
  with which attacker is asserted directly** (`CombatScreen.BladeFor`), because the spawn scan reads
  whether a constant is *named* under `scripts/ui` and therefore catches an arm deleted but not the
  two exchanged — measured, swapping them left all 23 suites green, and with the geometry shared
  pigment is the entire channel telling the two apart. The registry
  (`CombatFx.All`) is what `PixelSpecSmokeTest` drives, so a seventh effect is one entry rather than
  a name retyped in a test. **A blade needs a cause, and there are two of them**: `PopupDelta` is a
  state diff, so `Combatant` carries `HitsTaken`/`LastAttacker` for a hit that reached HP and
  `HitsAbsorbed`/`LastAbsorbedAttacker` for one Block ate **any** of — the whole-absorb distinction
  is the *reader's* (`IsAbsorbedHit` demands `hpDelta == 0`), never the write's, so a partial absorb
  moves both pairs and draws one blade. Counter *and* name, always read
  together — either name alone survives every turn boundary and would throw a blade out of an enemy
  that did nothing. All four are written in `DealDamageEffect` and nowhere else; the other three ways
  HP falls (a Poison tick, `LoseHpEffect`, Thorns' direct subtraction) have no attacker to draw a
  blade from and deliberately get none
- `scripts/ui/SpriteAnimator.cs` — the one frame-swap driver, for creature clips and effect bursts
  alike. `Attach` resolves frames from a sprite id and needs an `idle`; `AttachOneShot` takes frames
  outright and holds the last one, which is what a burst needs and what frees it
- `scripts/ui/Motion.cs` — the eight named easing curves (`Jolt`/`Flash`/`Snap`/`Pop`/`Settle`/
  `Land`/`Fade`/`Drift`) and the only two builders any tween uses, `TweenTo` and `TweenPingPong`.
  A curve is a duration *and* a transition *and* an ease held together, because they are one
  decision — `MotionCurve.Over` changes the period and cannot change the shape, which is what lets
  a fog bank drift for 14 seconds without also picking its own easing. `Motion.Seconds` is the one
  place a period becomes a number, and therefore the one place an animation-speed setting
  multiplies — `Tween.Wait` routes bare delays through it too, since a stagger that did not scale
  with the sequence around it would invert the readout it exists to create. Two curves are `InOut`
  (`Fade`, `Drift`) and the rest are `Out`: a loop and a disappearance are the two cases where
  "arriving" is the wrong verb. ART_SPEC §11; `PixelSpecSmokeTest` fails a bare
  `TweenProperty`/`TweenInterval`/`SetTrans`/`SetEase` anywhere under `scripts/` (the exemption is
  the **property** `volume_db`, not the `AudioManager` file — the argument is that nothing on screen
  moves, so it has to be the predicate) and sweeps the other way for a curve nothing uses.
  **Renaming a tween builder means editing §9's transform regex too**, which is keyed to the
  spelling — that guard went silently dead when these call sites became `TweenTo`
- `scripts/ui/ScreenBackground.cs` — the four-band room behind every screen, and the four
  argument-free entry points that are the only way to attach one. Also the two drifting haze layers
  (`Motion.Drift`, ungated on `ReduceMotion` per the gate-the-flash-not-the-loop rule) and the dust
  motes, whose `CpuParticles2D` was a smooth-gradient §2/§3/§5 violation for six phases in the one
  spot neither the transform scan nor `artgen validate` can reach. **Four things have now shipped
  through that gap** — the hit spark, those motes, the two creature contact shadows and the slash
  trail — so it is a hole rather than a run of accidents, and
  `PixelSpecSmokeTest.TestNothingDrawsSmoothArt` is what now sits in it: a scan keyed to the
  *construct* (gradients, `Line2D`, `Polygon2D`, particle nodes, `antialiased: true`) across all of
  `scripts/`, whose only exemption is the marker `ART_SPEC-3-exception` on the line itself. Do not
  widen it to the canvas `Draw*` family — a hard-edged integer-width on-ramp stroke is pixel art,
  and banning the primitive failed `MapScreen`'s connectors on the commit that fixed them
- `tools/artgen/src/icons/light.rs` + `shapes.rs`'s `SHAPE_LIGHT` — ART_SPEC §10's one lamp, and the
  table saying which of its three classes each shape is in. The class used to be prose in four places
  — `shapes.rs`'s module header and `ART_SPEC.md` §10 (both carrying a drifted directional *nine*
  that omitted `tower_shield`), `light.rs`'s complement rule, and each shape's doc comment. It is
  data now, the two drifted lists are gone rather than corrected, and three Rust tests hold it — every shape classified, the table agreeing with the doc comment
  beside each shape, and **every `Directional` row actually measured**, found by a `LIGHT-ASSERTION`
  marker rather than a second list of test names. That last one found `sword` and `tower_shield`
  measured by nothing. What it does *not* close is §10's standing exemption: the ~186 hand-placed
  highlights in the category modules are still held by nothing
- `tools/artgen/src/icons/backgrounds.rs` — the twenty-one pieces, and the only category that breaks
  the grid, the location *and* the naming at once. Its Rust tests hold what `validate` is blind to:
  seam continuity per band, a pillar's transparency, and the per-band contrast budget
- `scripts/ui/ScreenChrome.cs` — the furniture every non-combat screen shares (title, HP/gold/relic
  status block, framed panel, art plinth), attached from `_Ready` like `ScreenBackground` and
  `DeckViewButtons`. Owns those node paths; `ScreenChrome.HpLabelPath` and friends are what the
  smoke tests address rather than literals. The relic grid wraps 6 to a row by default, which keeps
  the block clear of the centred title (x=296) — but a screen whose own content comes further left
  than that overrides it, and `ShopScreen` does: its four-card row starts at x=194, so six columns
  reaching x=280 painted relic icons over the first card. Trading width for height is the whole
  lever, and the trade runs the other way on the map, where `MapScreen.BandTop` pays for block
  height out of the node band

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
- **A data file with a non-resource extension needs a line in `export_presets.cfg`.** Godot 4 loads
  `.json` through a built-in resource loader, so `export_filter="all_resources"` packs the six
  content files on its own and a new `.json` under `data/` needs nothing (measured: blanking
  `include_filter` produces a byte-identical `.pck`). The `include_filter="data/*.json"` on all three
  presets is deliberate insurance, not the mechanism. A `data/foo.txt` or `.csv` is verifiably *not*
  packed and does need the filter widened. Keep it anchored to `data/` — Godot's glob crosses `/`,
  so a bare `*.json` would also pack several hundred Cargo fingerprint files from
  `tools/artgen/target/`.
- **`dotnet build` before running or testing anything** — C# is compiled ahead of time, so
  otherwise you exercise the previous binary.
- **Godot is not on `PATH` on this machine.** The Mono build is at
  `/Applications/Godot_mono.app/Contents/MacOS/Godot`. `tools/run-smoke-tests.sh`,
  `tools/balance-report.sh` and `tools/build-export.sh` all default `$GODOT` to exactly that, so
  they need no setup — `GODOT=/path/to/godot` is only for a machine where it lives elsewhere.
  Invoke the full path directly when running a scene by hand.

## Verification

There is no test framework. Each `scenes/debug/*SmokeTest.tscn` asserts in `_Ready`, prints
`PASS`/`FAIL` per check plus a `<Name>: N passed, M failed` summary, and exits nonzero on
failure.

```bash
tools/run-smoke-tests.sh                 # all 23; builds first, nonzero exit on any failure
tools/run-smoke-tests.sh MapSmokeTest    # a subset
```

Run this after touching anything under `scripts/` or any `.tscn`, before reporting work done —
when in doubt, run the full sweep, it takes well under a minute. For which suite covers what, how
a stall/`TIMEOUT` differs from a real failure, expected non-regression output, and the gotchas
around writing a new suite (headless mouse position, scene changes, save-state guards), see the
`smoke-tests` skill.

For anything visual — a `.tscn`, layout, colours, card/relic rendering, or a bug described as
"looks dimmed"/"overlaps"/"cut off" — use the `verify-screen` skill to render the real screen and
look at the PNG. Never `--headless` for screenshots: the dummy renderer returns an empty
viewport texture.

Before opening or merging a PR, the `hollowdeck-review` agent (`.claude/agents/`) reviews the
branch diff against `main`. It is aimed at what the suites structurally cannot catch and what this
codebase actually produces — **silent no-ops at the data/code seam** (a new key never registered, a
new status never added to `ShouldScale`), broken ordering invariants, duplicated content, and
assertions that cannot fail. A green sweep is not evidence against any of those.

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
