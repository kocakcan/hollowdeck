---
name: combat-loop
description: Hollowdeck's combat internals beyond the CombatState machine - how an enemy telegraph is derived from its own EffectSpecs (and why a drifted telegraph is the canonical bad bug), the wake_on_damage sleeper and its mid-turn re-telegraph, WeightedRandomIntentPicker's MaxRun cap, the ResolveDeathsAndSettle pass that owns summon/escape/onDeath, and the MaxEnemies/EnemyViewMaxWidth/TextFit layout budget. Use when touching scripts/combat/, EnemyView, CombatScreen's enemy row, an intent picker, or authoring/editing an enemy move in data/enemies/enemies.json.
---

# Combat Loop

Moved out of the root `CLAUDE.md` so it loads on demand rather than in every session.
The rules here are load-bearing: they are invariants and failure contracts, not style notes.

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
