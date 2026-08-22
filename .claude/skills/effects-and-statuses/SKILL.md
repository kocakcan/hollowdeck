---
name: effects-and-statuses
description: Hollowdeck's card and status vocabulary on top of the EffectSpec/EffectRegistry split - what a Power buys and why Fervor/Foresight are excluded from ApplyTurnStartGrants, the fifteen-status roster and its three decay rules (including spent-not-decayed Artifact/Plating), the six sites a new status must be added to, the Retain/Ethereal/Innate keyword layer in PileManager, IsPlayable, the add_card primitive, and X-cost. Use when adding a status, an effect, a card keyword, or authoring cards in data/cards/cards.json.
---

# Effects And Statuses

Moved out of the root `CLAUDE.md` so it loads on demand rather than in every session.
The rules here are load-bearing: they are invariants and failure contracts, not style notes.

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
