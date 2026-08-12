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

**60/37/3 is rung 0 of a ladder the player climbs by declining.** `RunState.CardSkipStreak` counts
card rewards skipped back to back, and `CardPool.WeightOf(rarity, streak)` shifts that turn's weights
out of Common — 45/46/9, 30/55/15, 15/64/21 — which takes a Rare somewhere in the three-card offer
from 9% to 51%. Taking a card resets it; `AdvanceAct` does not, because a streak carries an act
boundary the way the deck and gold do. Five things are load-bearing:

- **The three steps sum to zero, so the total is 100 at every rung.** A weight therefore reads
  directly as its tier's percentage share, which three separate readers depend on: `CardPool`'s own
  comment, `BalanceReport`, and the reward row shown to the player. A step set that did not cancel
  would leave all three describing different ladders.
- **`MaxSkipStreak = 3` is a correctness bound, not a taste one.** At rung 4 the Common weight hits
  0, and `TierPool.PickTier` would leave Common in the pool and *unreachable* — a tier that exists
  and can never be drawn, with nothing thrown. `WeightOf` clamps rather than trusting its caller,
  because the value reaches it from a save file.
- **Uncommon leads at every rung.** Rare passing Common at the cap is intended and paid for by three
  given-up cards; Rare passing *Uncommon* would make the top rung a Rare dispenser rather than a
  richer pool, which is a different feature.
- **The streak is reward-only** — a per-draw offset the player moves, not a second authored table.
  The shop and the random-card event call the streakless overload, because a shop that got richer
  because the player walked past two cards would be pricing something it had no part in.
- **It changes which cards come back and not how many `Next()` calls the draw spends**, so a boosted
  reward leaves `RngStreams.Shop` where an unboosted one would and a seed's later shop stock still
  reproduces. That is risk 2, and it would have been easy to break here.

Two traps live on the reward screen rather than in the pool. `RewardScreen._skipResolved` is
`CombatScreen._continueResolved` one screen over and for the same reason: Skip is reachable from the
button *and* `hd_cancel`, `ScreenFade` holds the scene up in between, and a click plus an Escape
inside that window built two rungs off one skip. And the condition is
`RewardScreen.LeavingDeclinesACard` — the offer and the claim set, **never the button's label**,
since `RefreshExitButton` already retitles Skip as "Continue" once every row is taken. The row states
the rule at rung 0 rather than appearing on the first skip (a strategy nobody can see is not one
anyone adopts), and computes its printed odds from `WeightOf` rather than restating them, which is
the drifted-telegraph shape this project refuses everywhere else.

`Rarity` is shared with `PotionDefinition`, and **the weight tables are not**. Cards are 60/37/3;
potions are 65/25/10 in `PotionPool`, because a card is a permanent deck slot and a potion is one
shot out of a three-slot belt — at the card weights a *named* Rare potion would sit under 1% against
a 12-row pool, i.e. authored and never seen. The tier-first draw itself is shared
(`TierPool.Sample`, which takes the weight function as a required parameter so a potion draw
silently running on the card table is unrepresentable); the `IsPlayable` filter stays in
`CardPool.Sample`, since it is a card rule. **The number that matters as content grows is per
*row*, not per tier** — a tier's weight is divided among its members, so authoring two more Uncommon
potions alone would put an Uncommon below a Rare. `EffectSmokeTest.TestEveryPotionDeclaresARarity`
watches for that, and reads `potions.json` as *text* to count `"rarity"` keys, because the enum has
no null and a forgotten tier is otherwise indistinguishable from an authored Common.

**A relic tier is two axes wearing one enum, and that is why it is not `Rarity`.** `RelicTier
{ Common, Uncommon, Rare, Boss, Shop, Event }` — the first three are a power ladder weighted
50/33/17 in `RelicPool`; the last three name a *source*. A site is therefore expressed as which
tiers it may see (`RelicPool.TiersFor`), and `TierPool` renormalises over whatever is actually in
the pool it is handed, which is what lets "a boss draws the Boss tier alone" and "a shop draws the
ladder plus its own tier" be the same function. Reusing `Rarity` would also have quietly
under-covered the two hardcoded three-element `Rarity` sweeps in `EffectSmokeTest`; the relic sweep
drives `Enum.GetValues<RelicTier>()` instead, so a seventh tier authored on nothing fails.

Four things around it:

- **Boss is the only site that leaves the ladder, and `BossWeight` only has to be positive.** It is
  never mixed, so it is always renormalised to the whole draw and says nothing about how a Boss
  relic compares to a Rare one — but at `0`, `PickTier` finds a total weight of 0, returns null, and
  the boss reward silently vanishes. Shop and Event *add* to the ladder rather than replacing it,
  because a shop stocking only its three exclusives would be empty by act II.
- **The empty-pool fallback is a design rule.** Own every Boss relic and a boss pays from the ladder
  rather than paying nothing — silence would deny the reward to precisely the player who earned it.
- **A tier-scaled price required rendering the tier first, and that order is not a preference.**
  `ShopScreen.cs` already says a price moving with an attribute the tile does not render reads as a
  bug rather than as a tier; so the sub-label became `Relic - Rare` and `RelicPriceFor` followed it.
  `BalanceModel` **reads** that function rather than mirroring it — the flat 150 it used to copy sat
  under a "mirrors ShopScreen" comment with nothing asserting the mirror held.
- **The owned-and-unlocked filter moved into `RelicPool`** from the four byte-identical LINQ copies
  at the grant sites, which is what retired `ShopScreen`'s local uniform `Sample<T>`.

**The reward screen carries a tip, and it is a rotation rather than a roll.** `data/tips/tips.json`
plus `TipDatabase.ForVisit(runSeed, RunState.VisitedNodeIds.Count)`, rendered into `TipLine` in
`RewardScreen.tscn` — the band between the framed list and the Skip button, which is empty at every
row count because the list is centred in its own area. Three reasons it does not draw from
`RngStreams`, and appending a sixth stream would have been free: a stream's position is not
serialized and `Init` re-runs on load, so a draw outside the deterministic run pipeline replays
differently after a resume; six `ScreenShot` fixtures re-render this screen; and a rotation visits
every tip once before repeating, which a roll does not. Tip text may carry one substitution —
`{hd_some_action}` resolved through `ScreenKeyboardNav.ResolveKeyHints`, so a tip naming a key
cannot drift from `project.godot`'s `[input]`. The line is **hidden**, not faded, while the overlay
is open — either view of it: it sits directly under that overlay's Back button, and dimmed body text
behind a modal reads as something the player failed to dismiss.

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

**The reward screen is a list you claim from, and every reward on it is an *offer*.** `RewardContext`
carries what a fight is offering — gold, a relic, a potion drop, three card choices — plus a
`HashSet<RewardKind> Claimed`; `RewardScreen` renders one row per offer and grants on the click.
Nothing in `CombatScreen.OnContinuePressed` touches `RunState` any more, which is a change from how
this worked for eight phases: gold was banked before the screen loaded and the relic was granted
inside the same call that picked it (`GrantRewardRelic` → `PickRewardRelics`), so two of the four
rows were things the player already had.

That was invisible while a card was the only thing being offered, and stopped being invisible the
moment a second claimable thing landed on the screen — whichever the player touched first silently
forfeited the other, because taking a card called `Advance()`. Four things about the shape that
replaced it:

- **A claim is idempotent, and the guard is `RewardContext.Claimed`, not the Button.** A row that is
  somehow still enabled cannot pay twice.
- **`MarkClaimed` re-saves.** `RunManager` autosaves on *entering* a screen (`AutoSaveScreens`), so
  the save taken when Reward opened has none of the claims in it — without the re-save, claiming
  gold and then quitting would return to a run that had never been paid. What is still *unclaimed*
  is still forfeited by quitting, which is the deal the card pick has always offered.
- **The overlay is a modal over the list, and opening it is the one case `Regrab` cannot handle.**
  `ScreenKeyboardNavListener.RegrabNow` deliberately leaves an existing focus owner alone, so the
  row that was just pressed keeps the ring and it sits on the list *behind* the dim. `ShowOverlay`
  grabs into the open view explicitly; `CloseOverlay` uses `Regrab`, which is correct there because
  the focused control has just been freed. The list also leaves the focus chain
  (`FocusModeEnum.None`, not `Disabled` — they are not illegal choices) and is faded to 0.3, because
  a 0.75 scrim alone left a column of gold headings perfectly legible behind the cards.
- **`CombatScreen._continueResolved` guards the whole Continue handler**, not just the stats fold it
  started life as. There are two independent ways in — the button's `Pressed` and
  `hd_confirm`/`hd_end_turn` in `_UnhandledInput`, which is deliberately not focus-based — and
  `ChangeScreen` is not instant because `ScreenFade` holds the scene up. A click plus an Enter inside
  that window used to award the gold twice and grant *two* relics.

**A boss offers three relics and the player takes one; an elite still offers one.** The offer is
`RewardContext.RelicChoices`, a list rather than the nullable single it replaced, and **the count is
what decides the interaction**: one entry and the Relic row *is* that relic (name, tier, rules text)
and hands it over on press; more than one and no single relic can be the row, so it reads
`Choose a boss relic` and opens a picker where the claim lands. Two fields — a guaranteed relic
*and* a choice list — would make "an elite with three offers" and "a boss with neither" both
representable, and neither has an answer. `CombatScreen.PickRewardRelics` is where the branch is,
and the boss is the only site that gets one because it is the only site with room: a boss never
rolls a potion, so the list has a free slot either way.

Four things around it:

- **The relic picker and the card fan share one overlay node, and that is the whole reason the modal
  rules are still written once.** `RefreshRows` asks `_overlay.Visible` in five places (the list
  fade, the tip hide, every row's `FocusMode`, Skip's, and `FirstClaimableControl`). Two sibling
  overlays would have made each of those "or the other one too" — five places to widen and five to
  forget, which is the shape `CombatManager.DecayAtTurnEnd` exists to refuse. Which *view* is open is
  read off the areas' own `Visible` rather than tracked in a field, so it cannot disagree with the
  tree. `ClearChoices` empties both areas unconditionally: a boss reward is the first fight that
  opens both views in one visit to the screen.
- **`RelicPool`'s exhaustion fallback is a top-up, and the shortfall is a *second draw*.** The
  comparison moved from `pool.Count == 0` to `pool.Count < count`, which at count 1 is the same
  condition — so every single-draw site is unchanged, while a Boss tier down to two rows can no
  longer hand over two tiles with nothing thrown. What is easy to get wrong is *how*: widening the
  first draw's pool with the ladder puts the ladder in the same tier roulette as Boss, where
  `BossWeight` is about half the total, so a boss with two of its own relics left offers Commons
  instead about one tile in two while both Boss relics sit unoffered — measured, that form returned
  three Rares/Commons. Drawing the site's own tiers to exhaustion first and then filling the
  remainder keeps **"Boss is never mixed with another tier"** true, which is what `BossWeight`'s own
  comment depends on.
- **A relic name is the one thing in a tile that can grow, and `AutowrapMode` is what holds it.**
  Three tiles share an 800px band, and `ScreenChrome.Heading` returns an *unwrapped* `Label` whose
  minimum width is its whole string — so a long name does not overflow its tile, it widens that tile
  and the row hangs off both ends of the band. A wrapping Label has a small minimum width instead,
  so the column keeps the 224 it was given. `TextFit` (Body→Small, the `EnemyView` ladder) sits above
  it and buys **legibility, not layout**: deleting it breaks no width assertion, it only decides how
  many lines a long name costs, and `GridContainer` levels every tile to the tallest. Both are
  asserted in `ScreenSmokeTest` against a name longer than anything authored — the width checks catch
  the autowrap, and a separate check on the applied font size is the only thing that can observe
  `TextFit` at all. The longest Boss name today clears by about three characters, which is the
  "constant that fits the best case" trap this project has shipped three times.
- **`ScreenChrome.FocusableFrame` is the tile**, moved out of `LibraryScreen` when this became its
  second caller. A focus ring drawn two ways is a seam the player sees and no suite can assert about.

**A won fight rolls for a potion, at a rate authored per act.** `ActDefinition.PotionDropPercent` /
`ElitePotionDropPercent` sit beside the gold dials in `data/acts/acts.json`, and
`CombatScreen.RollPotionDrop` reads them off `RngStreams.Drops`. **A boss never rolls** — it already
pays a choice of three relics and an act clear. Three things:

- **Both fields default to 0, and that is the wrong reading of an act that forgot the key.** Unlike
  every other absent-is-zero field in the data layer, a silent 0 disables the feature for that act
  with nothing thrown and every suite green. `ActSmokeTest` asserts both are authored above zero, in
  range, and that the elite rate is not *below* the normal one — that last catches the transposed
  pair, which is otherwise invisible.
- **`BalanceModel.NodePotionPercent` mirrors `RollPotionDrop`** and must keep mirroring it, or the
  report prices a drop table the game does not play. `BalanceSmokeTest` pins the boss arm rather
  than leaving a reader to trust a `_ =>`, and bands the yield at both ends: under one drop a run the
  belt is still empty (the problem, unfixed), over two beltfuls every extra drop meets a full belt
  and ships as a greyed row.
- **The rates are printed in `BalanceReport`, not just their consequence.** Both Phase 8 balance
  incidents and the Phase 9 node-weight one hid the same way — a data number moved and only its
  effect was visible.

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

**A run opens on a decision and a seed, and the two are the same fact.** `RunSetupScreen` sits
between MainMenu and the first map: three **blessings** drawn from `data/blessings/blessings.json`,
one taken, plus a typed seed field. A `BlessingDefinition` is a label, a description and a list of
`EventOutcomeSpec`s — the *same* spec an event choice carries, resolved through the *same*
`EventOutcomeRegistry.Begin`, which was widened from `Begin(EventChoice)` to
`Begin(IReadOnlyList<EventOutcomeSpec>, string)` with the choice overload delegating to it. So a
blessing needed **no new mechanical vocabulary at all**: that registry exists precisely because it
is the non-combat one (`EffectContext` requires a live `CombatManager`/`Combatant`, which no menu
screen has), and "an event choice offered before the map" is exactly what a blessing is. A
`BlessingDefinition.AsChoice()` adapter would also have worked and is worse — it makes one content
type impersonate another, where the overload leaves one implementation of the picker-is-pending
contract, the override-joining and the "a picker must be the last spec" rule.

Four things about it are load-bearing:

- **Re-seeding rebuilds the whole run, and that is the feature.** `RunManager.StartNewRun` split
  into `BeginRun(int seed)` — seed, `RngStreams.Init`, `RunState.InitNewRun`, *no screen change* —
  and a `StartNewRun` that mints a seed and routes to RunSetup. Typing a seed or pressing Reroll
  calls `BeginRun` again, so the map, the enemies and the three offers all move together. Anything
  less makes a reproduced seed not reproduce.
- **Which is why claiming a blessing takes the seed controls out of play.** `InitNewRun` resets
  `Deck`/`Relics`/HP, so a re-seed after a blessing resolves would silently erase it. The field goes
  read-only *and* `FocusModeEnum.None`, together, per the Disabled-is-not-enough rule below — and
  the real guard is `_claimed`, which `CommitSeed`/`ApplySeed` check, because the lock is UI and the
  guard is a fact about the run.
- **The seed field commits on Enter and *discards* on the way out, and blur-to-commit is not
  available here.** Godot moves focus on mouse-**down**, so committing on `FocusExited` fires
  between the press and the release of a click on a blessing tile — and committing rebuilds the
  offers, which frees the tile mid-click. Don't "fix" the field by making it commit on blur; the
  rule is stated on screen (`SeedHintLabel`) instead, and `ScreenSmokeTest` pins it.
- **The screen has to handle a pending card picker rather than forbid one.** `Begin` returns one for
  `remove_chosen_card`/`upgrade_chosen_card`, and a screen dropping a non-null `Pending` would
  resolve the blessing to *nothing* with no error. It is `EventScreen.ShowPicker` verbatim, down to
  the `Regrab` on both swaps. `Unburdened` (remove a card from the ten you start with) is the
  strongest row in the pool and the reason it was worth doing.
- **No new RNG stream and no save bump.** Offers draw from `RngStreams.Shop`, where every non-combat
  grant already draws; `RunSetup` is deliberately *not* in `AutoSaveScreens`, because `BeginRun` has
  already built a valid `RunState` by then and `TryContinueRun` jumps to Map — so a save here would
  let a player quit the setup screen and come back to a run whose blessing was never offered.

`BalanceModel.BlessingDeltas` prices the pool and `BalanceReport` prints it, per the rule the potion
pass wrote down: a data number that moves must be visible, not only its effect. Two traps in that
pricing. `BalanceModel.Price` returns **null** for an outcome key it does not handle, and
`PricesOutcome` asks that function rather than a list of case labels beside it — a `default` arm
returning zero prices an unknown key as "changes nothing" with every table green, and a hand-written
set does not close that (measured: deleting the `add_card` arm while leaving its key in the set left
all 22 suites green and a Curse priced free). And **deck size is two axes, not one**: `Cards` is what
the player is offered (a draw up, a *removal* down — a smaller deck is the gain in this genre) and
`Imposed` is a card named by the author, which is how a Curse is authored. One signed column would
print a Pain and two real cards as the same thing.

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

**A `?` node hides its type from the player and from nothing else.** `MapNode.Concealed` is a bool
beside `Type`, not a `MapNodeType.Unknown` in place of it: the roll happens in `MapGenerator` like
every other node's, and concealment only withholds the answer until `MapScreen.EnterNode` clears it.
Resolving at *visit* time — which is what the roadmap forecast — reads as the same feature and
breaks two things quietly. `BalanceModel` reads `Type` in a dozen places (`Count`, `NodeGold`,
`CardsAt`, `RelicsAt`, five `MaxAlong` sweeps) and would price an unrolled node as no fight, no
gold and no reward card, silently deflating every printed curve number; and because Combat is
deliberately *not* in `RunManager.AutoSaveScreens`, a resolution made on the player's click is never
persisted before the fight, so quitting mid-fight would re-roll a `?` that had turned out to be an
Elite. Rolling early costs nothing the player can observe and removes both.

Three things around it are load-bearing:

- **The weight is carved out of the utility rooms, not out of the whole table — and Elite still had
  to be paid.** A `?` comes back as a fight only one time in five, so paying for it proportionally
  taxes fights: measured, 1.1 reward picks and 51 gold a run, and `RunScore`'s Encyclopedian from
  reachable on 23% of seeds to 15%. Shop/Treasure/Rest/Event pay instead. But an unchanged *weight*
  is not an unchanged *share* once the table grows 110 → 119: Combat breaks even off the `?` table's
  20% Combat slice, and Elite — the one type a `?` may never be — has nothing handing it back, so it
  silently lost 6.8% of its frequency until its weight went 14 → 15. Nothing would have caught that;
  `BalanceSmokeTest` bands elite *cost ratios* and has never measured how often an elite is offered.
- **A `?` is never an Elite**, which is the one exclusion in `PickConcealedType` that is a design
  rule rather than structure. An unadvertised elite is a fight committed to without the fact that
  decides whether to take it — an ambush rather than a gamble. `MapSmokeTest` keeps its own copy of
  the legal set so widening the generator's table has to be argued for rather than inherited.
- **`MapScreen` has two leak sites and they are easy to miss**, because `Type` holds the truth the
  whole time: the tooltip and the no-art text fallback both read `NodeLabel(node.Type)` unguarded.
  The icon is the obvious one and the tooltip is the one that would ship.

`MapScreen.EnterNode` is where the reveal lives (not `BuildButtons` — revealing on render would show
every `?` a floor early), and it is the type→`ScreenState` router split out of `OnNodeChosen` so a
smoke test can drive it. It gained the `default:` arm that switch never had: an unhandled
`MapNodeType` used to advance `CurrentNodeId` onto a node nothing routes from and change no screen,
which is a soft-lock rather than a crash.

**How wide the map may be is a layout budget, and the thing it is spent against is the relic grid.**
Branching floors are 3–5 nodes (`MapGenerator.MinNodesPerFloor`/`MaxNodesPerFloor`), floor 0 pinned
at the minimum because every node on it is a Combat and a wider opening is a wider choice between
identical rooms. Five is not the genre's number — Slay the Spire is seven and *scrolls*, which is
the one canvas Phase 4 deliberately made this map fill. `MapScreen.BuildLayout` derives the vertical
pitch as `availableHeight / (widest - 1)`, so width comes straight out of the gap between nodes:
6-wide overlaps at three relic rows, 7-wide overlaps with no relics at all.

Four things follow, and none is visible from either file alone:

- **The generator's width and `MapScreen`'s pitch are coupled.** Raising `MaxNodesPerFloor` without
  re-deriving the layout draws nodes on top of each other and every suite stays green through it,
  which is why `MapSmokeTest` restates the 3–5 band rather than importing the constants — the same
  argument `LegalConcealedTypes` makes one feature over.
- **The competitor for the band is not the map.** The run-status block grows 44px per relic row and
  `BandTop` is derived from it, so the binding constraint on map width is *how many relics the run is
  carrying* — a fact about the player, not the graph. `MapScreen.RelicColumnsForBand` caps the grid at
  three rows by spending width instead, which is `ShopScreen`'s trade running the other way.
- **`BottomMargin` and that cap are two halves of one fix and both are load-bearing.** Priced
  separately: at five wide and four relic rows the old 88px margin leaves a 63.75px pitch under 64px
  nodes. Either alone clears `MinNodeGap`, neither clears it comfortably (4.75px and 0.75px
  respectively); together the pitch is 79.75. A revert of either one alone leaves every suite green,
  which is why the assertions point at the mechanisms — ring against pitch, grid against row budget —
  rather than only at the overlap they prevent.
- **The current-node ring is the widest thing in the vertical stack**, so it runs out of room before
  the nodes do and is derived from the pitch rather than being `NodeSize + 20f`. It was *not* already
  broken at four wide (20 relics leaves 85px and the flat ring cleared by one pixel) — widening is
  what made it live.

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
`Attack`/`Defend`/`Buff`/`Debuff`/`Summon`/`Escape`/`Dormant` — plus a single authored `DisplayAmount`;
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
sign is turned back around. `Dormant` is the third instance of the same argument and the only intent
that is not aimed at the player: it resolves to the same `Self`-scoped grant a `Buff` does, so
nothing in the sweep forced it, but a sleeper telegraphing `Buff +2 Str` is true about the effect and
silent about the only thing the player needs — that hitting it wakes it.

**An enemy can be asleep, and the wake is the one phase change that re-telegraphs mid-turn.**
`aiType: "wake_on_damage"` (`WakeOnDamageIntentPicker`) loops `Moves` while the enemy has lost no HP
and permanently loops `EnrageMoves` once it has — `PhaseThresholdIntentPicker` inverted, a separate
file for the reason `BlockMath` is a separate file from `DamageMath`. `EnrageMoves` is the *second
phase* now rather than the enrage phase, shared by both pickers, because ten sweeps across the debug
suites already walk `Moves.Concat(EnrageMoves)` and a third list would be ten places to forget.

Four things about it are worth more than re-reading the picker:

- **The wake re-telegraphs during the player's own turn.** `CombatManager.RetelegraphChangedPhases`
  runs inside the settle pass and asks `IIntentPicker.TryAdvancePhase`, so the sleeper's intent flips
  the instant the damage lands and the player can still answer it. Waiting for the enemy's turn
  boundary would make hitting a sleeper indistinguishable from hitting anything else.
- **It is gated on `State == CombatState.ResolvingCard`, and that gate is the safety half.** An enemy
  woken during the *enemy* turn — a Poison tick, a Thorns prick — must resolve what it already
  advertised; re-picking there is the canonical bad bug reached from the opposite direction. It wakes
  at its own `AdvanceEnemyIntent` a few lines later either way.
- **`PhaseThresholdIntentPicker` deliberately does not opt in.** A boss crossing its threshold holds a
  real move that still resolves truthfully, and flipping it early would change every boss fight and
  the curve measured under them. `TryAdvancePhase` defaults to false for exactly that reason.
- **A dormant move must grant something, and it must not be Block.** The first is the telegraph sweep
  (`Dormant` needs a `Self` grant behind it), so leaving a sleeper alone is never free. The second is
  `Phase4ContentSmokeTest.TestNoDormantMoveGrantsBlock`, and it is a soft-lock guard rather than
  balance: HP loss is what wakes a sleeper, so Block it accrues while dormant compounds, and once it
  passes the player's per-hit damage the enemy can never be woken, never be killed, and the fight has
  no exit.

`BalanceModel` steady-states a sleeper on its *awake* list (`SteadyMoves`) and wakes it in the walk
the turn it first loses HP, because the reference deck always attacks. Its dormant phase is therefore
unpriced by design — turns spent letting one sleep are a choice made against a visible Strength
counter, not a property of the curve.

**`WeightedRandomIntentPicker` will not play one move more than `MaxRun` (2) times running, and that
one rule replaced two that were each honest at a single move count.** Excluding the last-played move
outright is a cap of 1, and at exactly two moves it leaves one candidate — strict alternation, with
the excluded move's weight ignored every other turn. Disabling it below three moves fixed that and
made a two-move enemy unbounded i.i.d. sampling, free to play the same move four times running. A run
cap means something at every move count. Three things about it:

- **The picker owns its own memory** (`_lastMoveId`, `_run`), the way the other three own `_index` /
  `_enraged` / `_awake` — `EnemyFactory.Create` news one per enemy per combat, including each summon.
  `EnemyCombatant.LastMove` existed only to feed the old rule and is gone; splitting "which move" onto
  the combatant and "how long a run" onto the picker would be two sources for one fact.
- **The cap yields rather than starving.** A one-move enemy, or a move list whose `MoveId`s collide,
  would filter itself down to nothing; the empty-candidates fallback is what makes that terminate, and
  `Phase4ContentSmokeTest.TestAOneMovePickerTerminates` covers the arm no authored enemy reaches.
- **`BalanceModel.RunCappedStationary` solves the resulting chain, over `(move, runLength)` pairs
  rather than moves** — "may I repeat?" is answerable from the run length alone. It reads
  `WeightedRandomIntentPicker.MaxRun` rather than copying it, and `BalanceSmokeTest` samples the real
  picker 20k times per weighted enemy and holds the frequencies against the model, because a cap
  changed in the picker alone leaves every suite green while the whole curve is measured against a
  chain the game no longer plays.

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
#   generate [cards|relics|potions|map|status|intents]   category optional; omitted = all 192
#   clamp [paths...]   snap sourced PNGs onto the ramp (this is what enemy sprites go through)
#   validate           what run-smoke-tests.sh calls; nonzero exit on failure
```

There is **no `artgen` on `PATH`** and no `tools/artgen` wrapper — the binary under
`tools/artgen/target/` is a gitignored build artifact, so always invoke it through `cargo run` as
above.

`ROADMAP.md` tracks what's genuinely still open. Packaged export, the card and enemy passes, the
balance retune, the *card* half of the vocabulary — keywords, per-effect targeting, the `add_card`
primitive, unplayable card types, X-cost — and now the *status* half — `Artifact`, `Thorns`,
`Intangible`, `Plating` — and the *behaviour* half — `summon_enemy`, `onDeath`, escape, and now the
`wake_on_damage` picker, and the `WeightedRandomIntentPicker` run cap that closed it — have all
shipped, as has the `?` node that opened Phase 9 and the potion pass that followed it — rarity,
per-act combat drops, and the reward screen those drops forced — plus relic tiers and the
boss-relic choice those tiers made worth having, the start-of-run screen: blessings and typed
seed entry, the card-reward skip streak, and the map's width at 3–5. **That closes Phase 9**; what's
open is an ascension ladder. Don't treat this section as a to-do list.

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
tools/run-smoke-tests.sh                 # all 22; builds first, nonzero exit on any failure
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
