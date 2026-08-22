---
name: rewards-and-pools
description: Hollowdeck's rarity/tier draw layer and the reward screen it feeds - the 60/37/3 card weights and the card-skip streak ladder, potion rarity at 65/25/10, RelicTier's power-ladder-plus-source split and per-site tier filters, the reward-screen tip rotation, rewards as unclaimed offers, the boss's choice of three relics, and per-act potion drop rates. Use when touching CardPool, PotionPool, RelicPool, TierPool, RewardScreen, ShopScreen, or the weights and rarities in data/cards, data/potions, data/relics, data/tips.
---

# Rewards And Pools

Moved out of the root `CLAUDE.md` so it loads on demand rather than in every session.
The rules here are load-bearing: they are invariants and failure contracts, not style notes.

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
