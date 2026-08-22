---
name: run-structure
description: Hollowdeck's run scaffolding outside combat - the RunSetupScreen blessing-and-seed screen and its re-seed ordering trap, the twenty-rung ascension ladder and AscensionModifiers' nine knobs, the concealed ? map node, and the map-width budget that couples MapGenerator's MaxNodesPerFloor to MapScreen's layout pitch and the relic grid. Use when touching MapGenerator, MapScreen, RunSetupScreen, RunState, AscensionDatabase, BalanceModel's run walk, or data/acts, data/ascension, data/blessings.
---

# Run Structure

Moved out of the root `CLAUDE.md` so it loads on demand rather than in every session.
The rules here are load-bearing: they are invariants and failure contracts, not style notes.

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

**The ascension ladder is twenty rungs of stacking modifiers, and rung 0 is identity.** That last
clause is the load-bearing one and the thing the whole feature is checked against: every table in
`tools/balance-report.sh` came back byte-identical when it landed, and it has to keep doing so.
`AscensionDefinition` rows in `data/ascension/ascension.json` hold *that rung's own delta*;
`AscensionDatabase.Effective(level)` folds 1..level into an `AscensionModifiers`, cached at load
because it is read per enemy and per point of damage. `RunState.AscensionLevel` says which rung is in
play and `RunState.Ascension` resolves it.

**`AscensionModifiers` is the single place a modifier becomes a number**, and that is why it owns
methods (`EnemyHp`, `EnemyDamage`, `ShopPrice`, `PotionPercent`, `ClearHeal`, `StartingMaxHp`,
`EliteWeight`/`CombatWeight`) rather than being a bag of ints. Six of the nine knobs have two
readers — the game and `BalanceModel` — and this project has twice shipped a mirror between those
two that nothing asserted. The ladder starts on the right side of that.

The modifier vocabulary is **deliberately closed**: every field names a knob that existed before the
ladder did. A rung wanting anything else is new plumbing, not content.

Six things about it are expensive to rediscover:

- **The level is assigned by `RunManager.BeginRun(seed, ascension)`, before `InitNewRun`, and is not
  preserved across the rebuild.** `InitNewRun` reads it for the starting max HP and the imposed
  cards; `MapGenerator` reads it for the elite weight. `RunSetupScreen` calls `BeginRun` again on
  every reroll, every typed seed and every flip of the toggle, so a run is a pure function of
  (seed, rung) only because changing either rebuilds from both. Same ordering trap the seed and the
  blessing claim already turn on — and the toggle leaves play on a claim for the identical reason.
- **Enemy damage is scaled in `DamageMath.ComputeOutgoing`, before Strength, gated on
  `source is EnemyCombatant`.** That function is the one thing both `DealDamageEffect` and
  `EnemyView.LiveAttackAmount` call, so the telegraph and the hit scale together for free; scaling at
  resolution alone would make every enemy in the game telegraph a lie. Before Strength so a rung
  raises the authored move rather than compounding with the fight's accumulated buffs. The
  player-source gate is not optional — a ladder that scaled the player's cards would hand back what
  every other knob takes. Thorns is untouched: `DealDamageEffect` subtracts it directly so it cannot
  re-enter the effect system.
- **Enemy HP is scaled in `EnemyFactory.Create`, the only place an `EnemyCombatant`'s HP is set** —
  normals, elites, bosses and mid-fight summons in one edit. `isBoss` comes from
  `ActDatabase.IsBoss`, derived from `ActDefinition.BossIds`, because that list is already the one
  place the game decides what a boss is. In `BalanceModel`'s walk the enrage threshold must be
  checked against the *scaled* `WalkEnemy.MaxHp`; scaling one side of that comparison and not the
  other silently moves every phase flip in the game.
- **The elite rung moves weight from Combat into Elite, it does not add to Elite.** `MapGenerator`'s
  own comment records what an added weight cost last time — the `?` node grew the table 110 → 119 and
  Elite quietly lost 6.8% of its frequency. Moving holds the table total and the
  fights-versus-utility-rooms split constant. Only on floors ≥ 2, where Elite is actually in the
  table; below that the slot is a Combat and subtracting would shrink the table instead.
- **Shop prices go through one funnel, `ShopScreen.PriceFor`.** Each of the four prices is read three
  times — label, affordability gate, gold deduction — so a multiplier reaching the deduction alone
  makes the tile charge something other than what it prints. `RelicPriceFor` and `CardPrice` return
  the **base**, because `BalanceModel` applies its own rung to their output when pricing a shop at a
  rung the player is not on; folding the scale into them would double-apply there.
- **`RunScore` gains a bonus *row*, not a multiplier** (`AscensionBonusPercent`, 5% of the rest per
  rung, appended last). The run-end screen renders rows above a Total, and a multiplier applied after
  that column makes the printed rows stop adding up to the printed total — the same class of thing as
  a drifted telegraph. Rung 0 scores 0 and the existing drop-zero-rows rule removes it, which is what
  keeps the breakdown byte-for-byte what it was.

The ladder is earned rather than chosen: `MetaProgressionManager.AscensionLimit` (meta save v3) rises
only on a **Win at the current limit**, and `RunSetupScreen` offers a single toggle — off, or your
limit with every rung below it stacked — hidden entirely until the limit is above 0. `Migrate()` is a
version *chain* now rather than a single `>=` gate, because with two hops a v1 file has to run both
steps in order and land on the current version. Run save is v6 for `RunState.AscensionLevel`, clamped
on load like `ActIndex` and `CardSkipStreak`, and for the same reason: the number arrives from a file.

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
- **That cap is load-bearing; `BottomMargin`'s 20 reclaimed pixels are not**, and the distinction is
  worth stating because the tidy version ("two halves, both needed") is false and shipped once before
  mutation testing caught it. Without the cap, four relic rows at five wide leave a 63.75px pitch
  under 64px nodes. With it, the old 88px margin was already fine; the reclaimed 20px buys headroom
  (79.75px pitch against 74.75) and reverting it alone leaves every suite green. The assertions
  therefore point at the mechanisms that *are* load-bearing — ring against pitch, grid against row
  budget — rather than only at the overlap they prevent, because each one reverted alone slips past
  a check on the overlap itself.
- **The current-node ring is the widest thing in the vertical stack**, so it runs out of room before
  the nodes do and is derived from the pitch rather than being `NodeSize + 20f`. It was *not* already
  broken at four wide (20 relics leaves 85px and the flat ring cleared by one pixel) — widening is
  what made it live.
