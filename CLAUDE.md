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

**What a Power buys is a status that pays out every turn.** `Metallicize` (Block), `Ritual`
(Strength) and `Regen` (HP) are granted in `CombatManager.ApplyTurnStartGrants` and never decay,
which is what no recurring Skill can offer. They are statuses rather than a `PowerBehavior` hook
deliberately: a hook would mean one C# class per Power, the one-class-per-card pattern the effect
system exists to avoid (risk 1), whereas a status keeps a Power an ordinary data row. The ordering
is load-bearing — both combatants clear `Block` on their own turn, so a grant that lands before
that clear is wiped the instant it is given; the player's clear is in `EndEnemyTurn`, the enemy's
is mid-loop. (`Regen` heals, so it is indifferent to that ordering; it lives with the other two
because it is the same *kind* of thing.)

`Fervor` (Energy) and `Foresight` (cards) are the same idea for the two resources a turn *assigns*
rather than accumulates, and that is why they are **not** in `ApplyTurnStartGrants`: energy and
hand size are set outright in `BeginPlayerTurn`, so a grant applied in the pass above would be
overwritten a line later — the Block ordering trap running the other way. They are folded into the
assignments themselves (`MaxEnergy + Fervor`, `BaseHandSize + Foresight`), which is what makes that
unable to happen. They are also the only two grants that are player-only: an enemy has neither
pool. Between them they are the pool's strongest upgrade delta, and the first thing the balance
pass should look at.

**The status roster is fifteen, and it now has three decay rules rather than two.**
`Strength`/`Weak` scale damage through `DamageMath`; `Dexterity`/`Frail` scale Block through
`BlockMath`, which is a deliberate copy of `DamageMath`'s shape rather than four more methods on it
— nothing applies Strength to Block, and keeping them apart is what stops a later edit reaching for
the wrong multiplier. `Vulnerable` and `Poison` sit on the target side; the five turn-start grants
above make up the rest of the original eleven.

How a status ends is the axis worth knowing, because the four added in Phase 8 did not all fit the
two rules that existed. Buffs (`Strength`, `Dexterity`, the grants, plus `Thorns`) never decay;
debuffs (`Weak`, `Vulnerable`, `Frail`) and `Intangible` wear off by 1 a turn; `Poison` decays as it
ticks, at the *start* of its holder's turn rather than the end. `Artifact` and `Plating` are the
third rule: they are **spent, not decayed** — a stack goes when something happens (a debuff refused,
an unblocked hit landed), never on a clock. A status whose lifetime is event-driven does not belong
in `DecayAtTurnEnd`, and putting it there would make it wear off twice.

That end-of-turn list is a single `CombatManager.DecayAtTurnEnd` array walked by
`DecayTurnEndStatuses`, not two hand-written sequences at the player and enemy decay sites. It was
two until `Intangible` needed adding to both, which is precisely the shape of bug worth a field: a
status added to one site and not the other decays for the player and not the enemy, both sites keep
compiling, and the asymmetry surfaces only as a fight that feels wrong.

`Artifact` is the load-bearing addition — before it, stacking `Vulnerable` was unconditionally
correct and there was no read to make — and it carries a trap with it. It gates on
`StatusRow.IsDebuff`, so that predicate is no longer a rendering detail but a resolution rule: **a
new debuff added to `StatusType` and forgotten in `IsDebuff` walks straight past `Artifact`, and
nothing throws.** `EffectSmokeTest.TestArtifactRefusesExactlyTheDebuffs` drives the whole enum for
that reason rather than a hand-picked few. Note also that `Artifact` refuses one *application*, not
one stack: a spec applying `Vulnerable 3` costs one stack and lands nothing.

`DamageMath.ApplyIncoming` (renamed from `ApplyVulnerable`) is the one place target-side modifiers
belong, which is what makes a new one reach the live damage preview and the enemy telegraph for
free. Order inside it is a rule, not an accident: Vulnerable amplifies first, `Intangible` floors
last, because flooring first would let Vulnerable multiply the floor back up.

A new status needs an icon in `tools/artgen/src/icons/misc.rs`, an arm in `Keywords.Blurb` (**not**
`StatusRow.Describe` — `StatusRow.cs` exists and is the obvious place to reach, but the prose lives
in `scripts/ui/Keywords.cs`), and — easy to forget, and silent when missed — an entry in
`CardUpgrade.ShouldScale`, or upgrading a card that grants it produces an identical `+`. That last
failure now has a sweep behind it rather than a warning:
`EffectSmokeTest.TestEveryCardUpgradeChangesSomething` fails any card whose `+` moves no number,
which is how a missing entry announces itself. If it is a *debuff*, it needs a fifth thing —
`StatusRow.IsDebuff` — and that one is silent in the resolution layer rather than the UI, per
`Artifact` above. If it decays on a clock, a sixth: `CombatManager.DecayAtTurnEnd`.

**The keyword layer is three bools and three enforcement sites, all in `PileManager`.** `Retain`
survives `DiscardHand`, `Ethereal` is exhausted by it, `Innate` is promoted to where the opening
draw finds it. Three things about them are expensive to rediscover:

- **`Innate` is promoted from `StartCombat`, not from the `PileManager` constructor.** `StartCombat`
  shuffles the draw pile itself, *after* the constructor already did, so any ordering established
  earlier is destroyed a line later. And `DrawHand` pops from the **end** of `DrawPile`, so
  "drawn first" means "moved last" — `PromoteInnate` appends.
- **`Retain` does not reduce the next draw.** `BeginPlayerTurn` *assigns* a hand size rather than
  topping one up, so a retained card makes turn two a six-card hand. Same assign-vs-accumulate
  distinction `Fervor`/`Foresight` turn on, and changing `DrawHand` into a top-up would silently
  change what `Foresight` means.
- **`Ethereal` beats `Retain`.** Nothing authors both (`CardKeywordSmokeTest` refuses it), and the
  winner is stated rather than left to branch order: a printed cost that another keyword on the same
  card can cancel is not a cost.

**`Status` and `Curse` are unplayable, and `IsPlayable` is derived from `CardType`** rather than
authored as a sixth bool — one source of truth, so a Curse marked playable is unrepresentable. Five
gates read it: a fourth rejection in `TryPlayCard`, an exclusion in `CardPool.Sample` (the single
place "what may be offered" is decided, so a later grant site inherits it), a refusal in
`CardUpgrade.Apply`, the same in `UpgradeRandomCardOutcome.Upgradable` (which the rest site's Smith
and both upgrade events read — without it the picker shows a column whose button does nothing), and
`CardView`, which dims the frame permanently and hides the cost badge entirely, because a `0` in
that badge reads as "free to play".

**Nothing could put a card into a pile at runtime until `add_card`.** That one missing primitive is
why Curses were unauthorable and why every event downside in the game had to be HP or gold.
`EffectSpec` carries `CardId` and a `CardPile` destination; `Draw` inserts at a random index out of
`RngStreams.Combat` rather than on top, because "shuffle it into your draw pile" is the genre's
meaning and the only one that makes a Curse a cost rather than one bad turn. `AddCardEffect`
resolves piles through `ctx.Combat.Player` rather than casting `ctx.Source`, so an enemy move can
use it too.

**X-cost is a per-spec multiplier, not a repeat count.** `Cost = -1` is the sentinel (`IsXCost`;
never compare against `-1` at a call site), `TryPlayCard` resolves the real cost into a local before
the energy gate — passing `Definition.Cost` on to `ResolveCard` would *grant* two energy — and
`EffectContext.AmountFor` is the one place `spec.Amount` becomes the amount that resolves. Every
`IEffect` reads it, so none can invent its own fallback. Opt-in per spec (`EffectSpec.PerX`) because
a blanket override cannot express `"Deal X damage. Gain 3 Block."`; the accepted cost is that
`"deal 6 damage X times"` is not expressible, and a card wanting it needs a new primitive rather
than a widening of this one. An X card at zero energy is refused outright — it would resolve for
nothing and be gone.

**`AllEnemies`/`RandomEnemy` are card-only scopes.** They resolve relative to the source
(`CombatManager.Opposition`), so an enemy authoring one is coherent rather than a crash — but
`EnemyView` derives a telegraph from these specs and can express neither a target chosen at
resolution nor an amount that does not exist yet. `Phase4ContentSmokeTest` refuses any enemy move
that declares them or `PerX`. That is an assertion rather than a comment because a drifted telegraph
is the canonical bad bug in this genre.

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

**A telegraph is mostly derived, and that is what keeps it honest.** An `EnemyIntent` is a type —
`Attack`/`Defend`/`Buff`/`Debuff`/`Summon`/`Escape` — plus a single authored `DisplayAmount`;
everything else in the
label `EnemyView.FormatIntent` builds comes from the move's own `EffectSpec`s. How many hits it is
(`4 x2`) is a run of identical `deal_damage` specs, counted through
`EffectDescriptionFormatter.SameEffect` so cards and intents can't disagree about what one hit is;
which status a Buff grants (`+2 Metal`, `+5 HP`) is read off the first `Self`-scoped spec, which is
what lets an enemy carry Metallicize/Ritual/Regen at all — the label used to be hardcoded `"+N
Str"`, and five of the nine statuses were unusable by enemies because of it. The one authored
number is pinned against its effects for every move of every enemy by
`Phase4ContentSmokeTest.TestEveryIntentTelegraphsWhatItResolves`; a drifted telegraph is the
canonical bad bug in this genre, because the player has already committed a turn against it.
`Debuff` exists so a move that only worsens the player's position doesn't have to be authored as an
Attack telegraphing 0. `Summon` and `Escape` are there for the same reason one level up: that sweep
resolves a `Buff` to a `Self`-scoped `apply_status`/`heal` and an `Attack` to a `deal_damage`, and a
move that changes *who is in the fight* has neither — so borrowing an existing type is a label that
lies and a red suite, in that order. Their derived halves are the summoned enemy's own `Name` and
the gold an escape takes, the latter read off a **negative `gain_gold`** and shown positive
(`-25g`): one action rather than one per sign, and `EnemyView.StolenGold` is the single place that
sign is turned back around.

**The roster mutates mid-fight, and everything about that lives in one settle pass.** `Enemies` was
fixed for the length of a fight until Phase 8's behaviour half — set once in `StartCombat`, only ever
shrunk by a dead-enemy sweep. Three things change it now: `summon_enemy` appends, `escape` removes an
enemy alive, and `EnemyDefinition.OnDeath` fires as one leaves. All three go through
`CombatManager.ResolveDeathsAndSettle`, which replaced the four near-identical
`RemoveDeadEnemies(); CombatantsChanged; if (Enemies.Count == 0) Win` triples the resolution sites
used to carry.

Four rules in it are load-bearing, and none is derivable from reading the call sites:

- **Lose is checked before Win.** An `onDeath` resolves *before* the fight is scored, so a dying
  enemy can still take the player with it. Win-first would silently no-op every `onDeath` on the last
  enemy alive — the one it matters most on. `Phase4ContentSmokeTest` drives a synthetic lethal burst
  rather than asserting about the branch, because no authored `onDeath` should be a parting blow the
  player cannot answer (every one in `enemies.json` is a Poison, which costs them a turn, not the
  run).
- **Escaping does not touch `EnemiesKilled`.** That omission *is* "escaping grants no reward" — the
  tally feeds `RunState.Stats.EnemiesSlain` and from there `RunScore`.
- **`ResolveDeaths` loops rather than making one pass**, because an `onDeath` can kill another enemy
  or summon one that immediately dies. `EnemyCombatant.OnDeathFired` is what terminates it.
- **A summon does not act on the turn it lands, and that is free rather than arranged.**
  `ResolveEnemyTurnAsync` walks `_enemyTurnOrder`, a snapshot taken in `TryEndTurn`, so a newcomer is
  simply not in the list being iterated. Rewriting that loop to walk `Enemies` directly would hit the
  player with a move they were never shown, and nothing else would notice.

`EnemyCombatant.IsGone` (`IsDead || HasEscaped`) is the predicate every "is this still in the fight"
site reads — the turn loop's two skips, `CardView`'s drag hit test, `SimpleHookEffectRelic`'s three
targeting arms. An escaped enemy is *alive*, so each of those walks straight past an `IsDead` check
while its view slides off the board; one predicate is what keeps a third exit from being four sites
to remember.

**`CombatManager.MaxEnemies` is a layout budget, not a design one.** `EnemyRow` is an 800px band
bounded by the relic bar on the left and the pile counter strip on the right, and four `EnemyView`s
have to share it. Widening the row is the wrong fix and
`DeckViewSmokeTest.pile_counter_strip_does_not_overlap_enemy_row` catches it. So
`CombatScreen.EnemyViewMaxWidth` is a *maximum* and `CombatScreen.FitEnemiesToTheRow` derives the
real width from the row's own size each refresh — shrink-only, `Min(max, available/count)`.

That maximum is **400**, and what it is derived from is the content: the longest names in
`enemies.json` are 20 characters, which is ~370px at Silkscreen-Bold 24 plus the display face's 4px
outline. It was `EnemyView.tscn`'s authored 220 for three phases, which made "the real width is
whatever the row can give" true of four enemies and a lie about one — 220 fits about eleven glyphs,
so a playtest found a lone boss captioned **"CROWN REA"** with 580px of the row empty beside it.
A cap that only ever binds at the crowded end is the trap; at one enemy the row could always afford
the whole name and nothing asked for it.

Two things then fit a name that is still too big, in order. `EnemyView` steps the font down a rung
(`TextFit`, Heading→Body — and there is no third rung, because ART_SPEC §6 puts every size on the
8px design em). Underneath that, `NameLabel` is set to **`TrimChar`**, still reachable at four
enemies (194px). Not `TrimEllipsis`, which was the first choice and is the wrong one here: the pixel
font has no usable ellipsis glyph at this size, so the three dots render as a solid 19x3 bar that
reads at 1x as an underscore or a redaction rather than as "there is more name here". A cleanly cut
name reads as a layout limit. Note that *scaling* the font is not available — ART_SPEC allows
integer scale only — which is why the rung ladder picks a legal size rather than fitting one.

`BalanceModel` reads the same `MaxEnemies` constant rather than keeping a copy — an analyser pricing
five enemies the screen will only ever show four of is pricing a fight nobody can have.

**`TextFit` is the shared half of that**, and it has a second caller: `ScreenChrome.AddTitle`, where
the same playtest put `"Act 3 of 3 — The Hollow Throne"` through the gold chip. A title is centred,
so an overflow spills from *both* ends and the left one is where the run-status block is. The ladder
is the caller's (what a label may shrink to is a design question per label); what is not is whether
a rung is legal, which `TextFit` checks itself — `PixelSpecSmokeTest`'s source scan only sees
literal `AddThemeFontSizeOverride` calls and cannot see a size arriving in a variable.

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
`Map` — all derived from the run seed, so drawing an extra card can't shift what the shop stocks
and cosmetic jitter can never desync a deterministic run.

## Current state

An earlier shard *shop* was removed — don't reintroduce shard-purchase language.

There are **36 enemies** (7 normals + 3 elites per act, plus 6 bosses) across 3 acts. Card, relic,
event, potion, status and effect counts all live in `data/*/*.json` and the relevant enums —
read those rather than trusting a number here to stay current.

Those per-act counts are *enemy ids*, not encounter slots: each act's `eliteEncounters` also fields
one of its own **normals** as an escort — `rot_hound` (act 1), `ember_wisp` (act 2), `hollow_shade`
(act 3) — so a normal appearing in an elite group is intended, not a mis-authored row. Sharing
*across* acts is the thing that is forbidden, and that is the half `ActSmokeTest` asserts.
`summon_enemy` is a *second* route an enemy id reaches a fight by, and it bypasses the act pools
entirely — `ActSmokeTest.TestNoSummonCrossesAnAct` is what stops it being the one way act 3 content
turns up in an act 1 room.

**Icons are generated; sprites are sourced. Nothing in `assets/` is drawn by hand, and nothing is
an SVG.** All icons are original work emitted by `tools/artgen`, one Rust `fn` per icon composing shapes onto
a 32x32 grid out of the single 43-colour ramp in `docs/ART_SPEC.md` §5. They therefore need **no
attribution**: the game-icons.net SVG set was retired in Phase 3 when the project committed to pixel
art as one medium (the generator replaced all 78 of them, and the set has grown since), and
`CREDITS.md` keeps that in a *Retired* section precisely so it doesn't get re-added. Don't
reintroduce SVG source art — `docs/ART_SPEC.md` fails an SVG anywhere under `assets/`, and vector
downscaled onto a 32x32 grid is mush rather than a sprite.

Enemy and player sprites are the one exception, **sourced rather than generated** — CC0 Dungeon
Crawl tiles, palette-clamped by `artgen clamp`, mapped act by act in `CREDITS.md`. Adding an enemy
means a row in `enemies.json`, a reference from exactly one act's pool, and a 32x32 PNG; the first
two are asserted by `ActSmokeTest`, the third by `PixelSpecSmokeTest`.

Adding an *icon* is a `fn` plus its registry line in `tools/artgen/src/icons/`, then `generate`,
then commit the PNGs **and the `.png.import` sidecars** — see the newly-added-PNG stall in
Verification, which is the same workflow and the usual way this bites. `PixelSpecSmokeTest` fails
both directions (a definition with no icon, an icon with no definition), and CI re-runs `generate`
to catch committed art drifting from its source. The one command, for all three subcommands:

```bash
cargo run --release --quiet --manifest-path tools/artgen/Cargo.toml -- generate
#   generate [cards|relics|potions|map|status|intents]   category optional; omitted = all 185
#   clamp [paths...]   snap sourced PNGs onto the ramp (this is what enemy sprites go through)
#   validate           what run-smoke-tests.sh calls; nonzero exit on failure
```

There is **no `artgen` on `PATH`** and no `tools/artgen` wrapper — the binary under
`tools/artgen/target/` is a gitignored build artifact, so always invoke it through `cargo run` as
above.

`ROADMAP.md` tracks what's genuinely still open. Packaged export, the card and enemy passes, the
balance retune, the *card* half of the vocabulary — keywords, per-effect targeting, the `add_card`
primitive, unplayable card types, X-cost — and now the *status* half — `Artifact`, `Thorns`,
`Intangible`, `Plating` — and the *behaviour* half — `summon_enemy`, `onDeath`, escape — have all
shipped. What's open of the enemy vocabulary is a `wake_on_damage` picker and the two-move
`WeightedRandomIntentPicker` collapse; after that, relic tiers, potion rarity and combat drops, the
`?` node, an ascension ladder. Don't treat this section as a to-do list.

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
- `scripts/run/RngStreams.cs` — the four seeded RNG streams
- `scripts/run/CardPool.cs` — rarity-weighted sampling; the single place "which cards does the
  player get offered" is decided (reward picks, shop stock, the random-card event outcome), and
  therefore the single place unplayable cards are excluded from being offered at all
- `scripts/combat/CombatManager.cs` — turn loop, intent telegraphing, targeting sub-state
- `scripts/effects/EffectRegistry.cs` + `IEffect.cs` — the composable effect system every
  card/relic/potion/enemy-move definition keys into
- `scripts/relics/RelicBehavior.cs` — the 7 relic hooks; `SimpleHookEffectRelic.cs` drives all 27
  relics off all 7, using the `target`/`condition`/`limit` vocabulary in
  `scripts/data/RelicTrigger.cs`. Subclassing `RelicBehavior` is the escape hatch and nothing
  currently uses it — `RelicRegistry` has one factory
- `scripts/events/EventOutcomeRegistry.cs` — the 16 event outcome keys. Fourteen resolve
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
  boss pool all come from the `ActDefinition` passed in)
- `scripts/data/ActDefinition.cs` + `ActDatabase.cs` — the three acts and what varies per act
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
tools/run-smoke-tests.sh                 # all 21; builds first, nonzero exit on any failure
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
