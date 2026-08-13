# Hollowdeck Roadmap

## Where things stand

The run is complete and playable end to end: three acts of branching map, telegraphed-intent combat,
relics, potions, events, a shop, mid-run save/resume, a score-driven unlock track, 13 screens on one
pixel-art spec, 23 smoke suites, CI, and packaged exports for three platforms. Content stands at 101
cards (97 offerable), 36 enemies, 33 relics, 12 potions, 15 events, 10 blessings, 20 ascension rungs.

**The gating problem is no longer presentation, and it is no longer content volume. It is
mechanical vocabulary.** The previous roadmap correctly identified visual coherence as the ceiling
and spent five phases removing it; the sixth then pushed content from 33 cards to 84 and from 24
enemies to 36. What neither phase changed is the size of the vocabulary all of that is built out of,
and the content had saturated it — which is why the game read as a competent deckbuilder rather
than as this genre.

**Phase 7 closed the card half of that, and Phase 8 has since closed both of its own — the status
roster and the enemy behaviours.** Every row in the diagnosis table below is now struck. **Phase 9 is
closed**, and all seven of its items landed: the `?` node,
the potion pass, relic tiers — which closes the last row of the diagnosis table — the boss-relic
choice those tiers made worth having, the start-of-run screen, the card-reward skip streak, and the
map's width, which was the judgment call the phase held open longest and turned out to be arithmetic.
**Phase 10 is closed too** — the twenty-rung ascension ladder, which needed no new mechanical
vocabulary at all, because closing the four diagnosis rows above it is exactly what gave it eight
knobs to turn. What is open is Phase 11.

### The diagnosis

| Layer | Today | Consequence |
| --- | --- | --- |
| ~~`CardDefinition` fields~~ | ~~8, exactly one keyword (`Exhaust`)~~ | **Closed in Phase 7** — Retain, Innate, Ethereal and the `-1` X-cost sentinel |
| ~~`EffectScope`~~ | ~~`{ Target, Self }`~~ | **Closed in Phase 7** — `AllEnemies` and `RandomEnemy` |
| ~~`EffectRegistry` actions~~ | ~~10, every one of which moves an *existing* card or moves a number~~ | **Closed in Phase 7** — `add_card` is 11 |
| ~~`CardType`~~ | ~~`{ Attack, Skill, Power }`~~ | **Closed in Phase 7** — `Status` and `Curse`, unplayable |
| ~~`StatusType`~~ | ~~11~~ | **Closed in Phase 8** — `Artifact`, `Thorns`, `Intangible`, `Plating` make 15 |
| ~~Enemy AI types~~ | ~~3, all "pick a move off a list"~~ | **Closed in Phase 8** — `summon_enemy`, `onDeath`, escape, and `wake_on_damage` make four pickers |
| ~~`RelicDefinition`~~ | ~~no tier field~~ | **Closed in Phase 9** — `RelicTier` at 50/33/17 plus three source tiers, one tier set per grant site |
| ~~`PotionDefinition`~~ | ~~no rarity field, no combat drop~~ | **Closed in Phase 9** — `Rarity` at 65/25/10 across all four grant sites, and a per-act drop roll |

**The load-bearing row was the third**, and it is the one Phase 7 opened first. While no effect
could add a card to a pile, Curses and Status cards were unauthorable, "add a copy of this to your
discard" was unauthorable, and — the part that showed — **every event downside in the game had to be
HP or gold**, which is why events read as trades rather than as risks. One missing primitive cost an
entire design space; it is one `IEffect` and two `EffectSpec` fields.

This is the same finding the Phase 6 enemy pass made ("every enemy in the game was the same three
moves") and the card pass made ("33 cards out of 7 effect actions was already near-saturated"),
one level up. Both passes widened the vocabulary exactly enough for the batch in front of them and
stopped. It is the third time, so it is the rule rather than the incident:

> **Vocabulary before content.** Authoring against a vocabulary that is already full produces rows
> that are numerically distinct and mechanically identical, and they get rewritten later.

Everything below is sequenced by that.

## What shipped — Phases 0 through 7

Compressed. The decisions worth not relitigating, and nothing else; the full narrative is in git.

- **Pixel art is the single medium, and it is a spec rather than a taste.** `docs/ART_SPEC.md` plus
  `PixelSpec`: 32x32 creatures and icons, 64x64 tiles, integer scale only, Nearest filter
  everywhere, one 43-colour ramp. Rules can be smoke-tested; taste cannot, which is how the project
  once accumulated three palettes. **Do not move to a low-res viewport** — conventional for pixel
  art, wrong for a text-dense card game.
- **Content is data, effects are code, joined by string keys.** New cards are new rows in `data/`,
  not new classes. This is what `IScriptedEffect` and `RelicBehavior` exist as escape hatches
  *from*; both are deliberately unpopulated. It is also why Powers are statuses rather than a
  per-Power hook, and why all 33 relics are data rows driven by one factory.
- **The turn-start grant ordering trap.** Both combatants clear `Block` on their own turn, so a
  grant landing before that clear is wiped as it is given — hence `ApplyTurnStartGrants`.
  `Fervor`/`Foresight` are the same trap running the other way: energy and hand size are *assigned*
  in `BeginPlayerTurn`, so they are folded into the assignment instead of granted before it.
- **A telegraph is mostly derived.** Hit count comes from a run of identical `deal_damage` specs; a
  Buff's status name off the first `Self` spec. One authored number, pinned against its own effects
  for every move of every enemy. A telegraph that lies is the canonical bad bug in this genre.
- **`ScreenFade` lives on the `RunManager` autoload**, not in a scene — `ChangeSceneToFile` frees the
  current scene, so anything visible on both sides of a swap cannot live inside one.
- **The export premise was wrong, and finding that out was the value.** Godot 4 loads `.json`
  through a built-in resource loader, so the content files were always going to pack; the
  `include_filter` is insurance, not the mechanism. What is real is the failure *shape*: a
  content-less build boots to an empty menu and exits **0**, because Godot's .NET layer logs
  unhandled exceptions and carries on. That is why `tools/build-export.sh` greps the log rather than
  checking the exit code. Two macOS traps: Godot's built-in ad-hoc signer produces a signature AMFI
  rejects (use Apple's `codesign`, `3`), and an arm64 preset requires `import_etc2_astc=true`.
- **The balance analyser is static, not a simulator.** `CombatManager` paces the enemy turn on
  wall-clock timers, so real fights cannot cover enough ground inside the suite watchdog to say
  anything about a curve. `BalanceModel` reads the content databases instead — instant and exact.
  `EncounterCost` walks the fight turn by turn because damage-per-turn alone cannot see Poison, an
  enemy's own Vulnerable amplifying its later hits, or Strength accumulating through an enrage.
- **Measurement changed the answer more than once.** The act I spread, the starter deck's
  throughput, and two `RunScore` thresholds were all quoted as evidence and all wrong before
  `tools/balance-report.sh` existed. **Re-run it rather than trusting any snapshot, including the
  ones below.**

- **The card vocabulary, and the four traps in it.** `add_card` is the primitive everything else
  waited on. `Innate` is promoted from `StartCombat`, not the `PileManager` constructor, because
  `StartCombat` shuffles again afterwards — and `DrawHand` pops from the *end* of the pile, so
  "drawn first" means "moved last". `Retain` does not reduce the next draw, because
  `BeginPlayerTurn` assigns a hand size rather than topping one up (the `Fervor`/`Foresight`
  distinction again). `Ethereal` beats `Retain`, stated rather than emergent. And X-cost is a
  **per-spec multiplier** (`EffectSpec.PerX` + `EffectContext.AmountFor`), *not* the repeat count an
  earlier draft of this document implied: a repeat would fire relic hooks and damage numbers X times
  and could not express `"Deal X damage. Gain 3 Block."` at all. The accepted cost is that
  `"deal 6 damage X times"` is unauthorable.
- **`IsPlayable` is derived from `CardType`, never authored.** A Curse marked playable is
  unrepresentable rather than merely wrong. The exclusion that matters lives in `CardPool.Sample` —
  the single place "what may be offered" is decided — and the one that is easy to miss is
  `UpgradeRandomCardOutcome.Upgradable`, which the rest site's Smith and both upgrade events read;
  without it the picker shows a column whose button does nothing.
- **Card removal shipped with the Curses, not after them.** Adding a way to put dead cards in a deck
  without a way to take them out is punishment rather than design.

The curve as of today: encounter HP scales 1.43x then 1.44x per act, incoming damage 2.16x across
the run, player max HP only 1.32x — so **deck power has to cover 1.64x**, drawn from a mean of 16.6
three-card rewards. Elites span 1.09x–1.84x of an average normal fight and bosses 2.43x–3.06x, both
asserted as bands by `BalanceSmokeTest` — which reads them from `BalanceModel.EliteCost*`/`BossCost*`
rather than holding its own copy, so the report's printed header and the suite's flags cannot
disagree.

`BossCostLow` does a second job as of Phase 8's review pass: it is also the line a *normal*
encounter may not cross. Elites are banded against the **mean** normal, so a single Combat node
spiking past the whole elite pool averages away to nothing — which is exactly what shipped and what
the suite could not see. The costliest normal per act (2.04x / 1.77x / 1.89x) is printed in the
report for the same reason. Some overlap between the hardest normal and the hardest elite is what a
spread *is*, and predates Phase 8; a Combat node costing what a Boss node promises is not.

---

## Phase 7 — The card vocabulary — **shipped**

Landed as one branch, sequenced with X-cost last. `add_card` (11 effect actions), `CardType.Status`
and `CardType.Curse` with a derived `IsPlayable`, the `Retain`/`Innate`/`Ethereal` keywords,
`EffectScope.AllEnemies`/`RandomEnemy`, the `-1` X-cost sentinel with per-spec `PerX`, and card
removal at the shop for 75g. Content: 84 → 95 cards (4 unplayable), 15 → 16 event outcome keys, and
two events whose greediest choice now costs a card instead of only HP or gold.

The decisions worth not relitigating are folded into "What shipped" above and into `CLAUDE.md`'s
Architecture section. Two deferrals stand:

- **Runtime cost modification** ("costs 1 less this turn") still needs `CardInstance` to carry
  mutable per-instance state, which crosses the save boundary — risk 3.
- **An Ethereal card burning at end of turn does not animate as an exhaust.** `TryEndTurn` fires no
  per-card event, so `CombatScreen` has no hook for the ember tween it already owns. Cosmetic, and
  it belongs with Phase 11's feel work rather than here.

*Proven by:* a new `CardKeywordSmokeTest` (19 → 20 suites), plus moved assertions in
`EffectSmokeTest` (rarity coverage and the upgrade sweep now run over the offerable pool),
`CombatTargetingSmokeTest` (three rejection gates → four), `Phase4ContentSmokeTest` (no enemy move
may declare a card-only scope — the telegraph-honesty guard), `EventSmokeTest` and `ScreenSmokeTest`.

## Phase 8 — Enemies that do something other than damage

Phase 6 took the roster 24 → 36 by widening the *telegraph* vocabulary. The behaviour vocabulary was
not: all 36 were a list of moves picked by one of three pickers, and `EnemyDefinition` had no hook
for anything else, which is why 36 enemies felt like a dozen. That is now closed — an enemy can bring
in minions, react to its own death, leave a fight alive, and lie dormant until it is struck, and the
one picker that could still repeat itself indefinitely no longer can.

- ~~**`summon_enemy`**, **`onDeath: [EffectSpec]`** and **escape.**~~ **Shipped**, as one branch,
  because all three are the same change: `CombatManager.Enemies` mutating mid-fight. Effect actions
  11 → 13, intent types 4 → 6, and three re-authored act-1 enemies — `ward_acolyte` opens by calling
  a slime, `slime` bursts into Poison as it dies, `gaol_rat` steals 40 gold on turn 4 and leaves.
  Splitting is a capability rather than content: it falls out of `onDeath` + `summon_enemy`, but a
  half-HP copy needs a new sourced sprite or an HP override on the summon spec, and neither was worth
  buying to prove a point.

  The forecast held on the shape and missed on four things, all of which cost measurement rather than
  rework:
  - **The ordering that mattered was not the one this file named.** It called out `EnemyView`
    ordering and the hit test — both real, both cheap. The expensive one is that an `onDeath` can
    kill the player from the *player's* turn, which no resolution site checked: `ResolveCard` and
    `ResolvePotion` only ever tested for a Win. That is why the four repeated
    `RemoveDeadEnemies(); CombatantsChanged; if (Enemies.Count == 0) Win` triples collapsed into one
    `ResolveDeathsAndSettle`, and why **Lose is checked before Win** — the alternative silently
    no-ops every `onDeath` on the last enemy alive.
  - **A summon needs an intent type of its own, not a `Buff`.** The telegraph sweep resolves a Buff
    to a `Self`-scoped `apply_status`/`heal`, which a summon move has neither of. Six intent types,
    not five, and two new icons rather than one.
  - **The roster cap is a layout budget and it bit immediately.** `EnemyRow` is bounded by the relic
    bar and the pile counter strip, so widening it to fit a fourth enemy put it under the counters —
    `DeckViewSmokeTest` caught that. The fix is that `EnemyView`'s 220px minimum became a *maximum*,
    with `CombatScreen.FitEnemiesToTheRow` deriving the real width from the row.
    *Later correction:* a shrink-only rule needs a cap set by the widest case, not the narrowest.
    Carrying 220 over from the scene meant a lone boss got 220 of an 800px band and rendered as
    "CROWN REA"; the cap is `EnemyViewMaxWidth = 400` now, derived from the longest name in the
    content, and `TextFit` steps the font down a rung before `TrimChar` gets involved.
  - **`EncounterCost` moved act 1 and nothing else, and that took two measurements to establish.**
    Converting its parallel arrays to a growable list to model summons *also* silently stopped a
    dying enemy taking its last swing, which cost every encounter in the game 8–13% and would have
    put the branch's balance delta beyond attribution. Restored deliberately — acts 2 and 3 then came
    back byte-identical to `main`, and act 1's reference rose 42 → 48 purely from the summon, which
    is the honest number. The dying-swing question is real and belongs with the unmodelled
    `gain_block` moves as the next thing to do to that method, measured on its own.

  The review pass then found the thing all of that measuring had been reading past, and it is the
  fifth and most useful entry in this list:

  - **A moved *denominator* looks exactly like a moved numerator, and only one of them is the bug.**
    `possessed_armor` falling to 0.99x was read as the armour being too cheap and answered with
    113 → 120 HP. It was not: the summon had pushed two Combat nodes to 101 and 116 against a
    costliest elite of 77, which dragged act 1's mean normal cost 42 → 48 and deflated every elite
    and boss ratio in the act at once. The armour never changed. Every suite was green throughout,
    because nothing measured a normal encounter against anything — `BalanceReport` printed only
    elites and bosses, and `BalanceSmokeTest` banded them against the *mean*, which is precisely the
    statistic a single spike disappears into.

    Fixed at the source: `ward_acolyte` 40 → 24 HP, so the summoner is the genre's fragile caster
    hiding behind minions and acolyte-plus-slime lands where the acolyte alone used to. Act 1's
    ceiling returns to 90 — `cultist + cultist`, which held it before Phase 8 — and the 120 HP
    reverts to 113. Two assertions now stand where the argument was: no Combat node may reach
    `BossCostLow`, and the costliest normal per act is printed rather than left to be inferred from
    elites getting quietly cheaper.

    The same pass caught `snatch_and_flee` stealing exactly the 25 gold act 1's solo `gaol_rat` node
    pays out. Emptying the board scores as a Win however it emptied, so the escape handed over the
    full reward, cost the player nothing, and skipped 44 HP of fight — and `EncounterCost` is right
    that it only ever fires against a *below*-reference deck. The move existed to rescue the deck it
    was written to punish. Theft 25 → 40, and `BalanceSmokeTest` now asserts a theft exceeds the
    smallest reward any node the thief appears in can pay.
- ~~**A fourth AI type, `wake_on_damage`**~~ — **shipped**, structurally as forecast:
  `PhaseThresholdIntentPicker` inverted, reusing that file's shape rather than growing a flag on it.
  It reuses its *data* too — `EnrageMoves` is the second phase for both pickers now, because ten
  sweeps already walk `Moves.Concat(EnrageMoves)` and a third list would be ten places to forget.
  Intent types 6 → 7 (`Dormant`), one re-authored act-3 normal (`gilded_husk`), and no schema change
  at all. Four things the forecast did not contain:
  - **The picker was the cheap half; the *timing* was the design decision.** A picker-only wake is
    honest and invisible — the sleeper resolves its dormant move once more and wakes at its own turn
    boundary, which looks exactly like a sequential enemy with an opener. The wake has to re-telegraph
    *while the player still holds the turn* or the mechanic exists only in the rules. That is
    `CombatManager.RetelegraphChangedPhases` plus one defaulted `IIntentPicker.TryAdvancePhase`, and
    it is gated on `ResolvingCard`: an enemy woken during the enemy turn must resolve what it already
    advertised, which is the canonical bad bug approached from the other side.
  - **A sleeper needs an intent type for a reason the sweep does not enforce.** Unlike `Summon`, a
    dormant move *does* resolve to a `Self`-scoped grant, so `Buff` would have passed every
    assertion in the repo. It is still wrong: true about the effect, silent about the only thing the
    player needs to know. `PixelSpecSmokeTest` had already named this item as the case its
    intent-coverage assertion existed to catch, and it was right.
  - **The dormant grant may not be defensive, and that is a soft-lock rather than a balance rule.**
    HP loss is what wakes a sleeper, so Block accrued while dormant compounds; once it outgrows the
    player's per-hit damage the enemy cannot be woken, cannot be killed, and there is no flee.
    `TestNoDormantMoveGrantsBlock` refuses the authoring. Strength instead, which prices stalling
    without ever closing it off.
  - **The costliest normal in act 3 was one JSON number, and the report found it in one run.**
    Moving `gild` (Metallicize 4) from a one-time opener into a repeating three-move awake loop made
    the husk tanky enough that `gilded_husk + wailing_effigy` cost 522 — 2.16x the act mean, past
    every elite, and inside a rounding error of the `BossCostLow` line a normal may not cross. It
    also dragged act 3's mean 226 → 242 and deflated every elite and boss ratio in the act, which is
    the Phase 8 denominator trap repeating exactly. Metallicize 4 → 2 and the woken fist 16 → 22
    lands the whole encounter-cost table byte-identical to `main`, with the mechanic changed and the
    curve untouched.
- ~~**`Artifact`, plus three more statuses.**~~ **Shipped** — statuses 11 → 15, and six cards
  (`ward_sigil`, `reliquary_seal`, `bramble_mail`, `bramble_guard`, `scaled_hide`, `hollow_form`)
  plus two re-authored enemy moves that actually grant them. Landed as forecast: `Artifact` is one
  arm in `ApplyStatusEffect`, `Thorns` and `Plating` in `DealDamageEffect`, `Intangible` in
  `DamageMath`. Three things the forecast got wrong, all worth carrying forward:
  - The authoring cost is **six steps, not four**, and the two extra ones are the silent ones. A
    debuff must also be added to `StatusRow.IsDebuff` — since `Artifact` gates on that predicate it
    is now a resolution rule, and a debuff missing from it walks straight past `Artifact` with
    nothing thrown. A clock-decaying status must also be added to `CombatManager.DecayAtTurnEnd`.
    (The forecast also named `StatusRow.Describe` for the prose arm; it is `Keywords.Blurb`.)
  - **The two turn-end decay sites were two hand-written lists**, and `Intangible` was the first
    status to need adding to both. Folded into one array walked by `DecayTurnEndStatuses`, because
    the alternative is a status that wears off for the player and not the enemy while both sites
    keep compiling.
  - **`EncounterCost` needed re-measuring for a reason unrelated to `Artifact`.** Fight length was a
    closed form (`total HP / throughput`) computed up front, so nothing happening *during* a fight
    could change its length — which made the first Block model provably inert: `Plating` 3, 4 and 5
    all priced identically. It walks turn by turn now, draining HP and stopping when the group dies,
    which is what makes self-granted Block cost the player turns. Six enemies carried `Metallicize`
    and the analyser had been draining them as though they did not. The boss ceiling moved 3.2 →
    3.3 as a consequence of that accuracy, not of any content getting harder.

  Still unmodelled in `EncounterCost` and the next thing to do to it: one-off `gain_block` moves,
  i.e. every Defend intent in the game.
- ~~**Give `WeightedRandomIntentPicker` a run cap.**~~ **Shipped**, at `MaxRun = 2` — a move may
  repeat once, never twice — held as one constant that `BalanceModel` reads rather than an authored
  per-enemy field, since no content wanted the knob yet. No schema change, no new content row, no
  save version bump. Four things the forecast did not contain:
  - **A third rule was not added; two were removed.** The rules this replaced are the same rule at
    the ends of its range — excluding the last-played move is a cap of 1, and the two-move
    free-for-all is a cap of infinity — so the cap applies at every move count and both
    `moves.Count <= 2 ? Weighted(...) : AntiRepeatStationary(...)` branches in `BalanceModel`
    collapsed to one call. `Weighted` lost its last caller and went with them.
  - **`EnemyCombatant.LastMove` was dead the moment the picker got a run counter.** It existed only
    to feed the old rule, and keeping the move identity on the combatant while the run length lived
    on the picker would have been two sources for one fact. The picker owns both now, the way the
    other three own their cursors, and `AdvanceEnemyIntent` is one line.
  - **The chain's state is a pair, not a move.** "May I repeat?" is answerable from the run length
    alone, so `RunCappedStationary` power-iterates over `(move, runLength)` — six states for a
    3-move enemy at a cap of 2. The `n == 1` short-circuit is not decoration: the cap has nothing to
    exclude into there, and both the model and the picker have to yield rather than starve.
  - **The measured move came from the three-move enemies, not the two-move ones this bullet was
    about.** The cap loosens a 3-move enemy *toward* its authored weights, where the old rule forbade
    repeats outright: `possessed_armor` went .403/.339/.258 → .446/.325/.229 and its encounter cost
    48 → 53, which lifts a known-marginal elite 1.09x → 1.22x — off the floor of its band rather than
    out of it. Everything the bullet actually named moved by under 1%: act 1's mean normal cost 44 →
    43, incoming damage across the run 1.97x → 1.99x, and act 1's costliest normal flipping back to
    `cultist + cultist` on a tie at 90. No content number needed retuning.

  The genuinely useful outcome is the assertion, not the cap. `BalanceModel` mirrors the pickers
  rather than approximating them, and until now it was kept in step with them **by comment** — the
  bullet above had to say "in the same change or the analyser silently reports the old chain"
  precisely because nothing would have caught it. `BalanceSmokeTest.TestTheModelAgreesWithThePicker`
  now samples the real picker 20k times per weighted enemy and holds the frequencies against
  `BalanceModel.MoveDistribution`, which is the seam itself rather than one crossing of it.

*Proven by:* `Phase4ContentSmokeTest` (Summon, Escape and Dormant telegraphs, the wake picker's
latch, the wake landing on the player's turn and *not* mid-enemy-turn, every aiType resolving to its
own picker, no dormant move granting Block, every `summon_enemy` naming a
real enemy that does not itself summon, a summon arriving telegraphed but not acting that turn, an
escape leaving without a kill, `onDeath` firing before the fight is scored, no weighted move running
past the cap — nor the cap binding so hard it reverts to alternation — and a one-move picker
terminating), `CombatTargetingSmokeTest` (mid-fight `EnemyView` creation and `Instances` order, hit
test skipping a runaway as well as a corpse, the HUD clear of a full four-enemy row), `ActSmokeTest`
(no summon crossing an act), `EffectSmokeTest` (negative `gain_gold`, enemy-only actions off cards),
`DeckViewSmokeTest` (the slime picker still able to repeat), `BalanceSmokeTest` (a sleeper with no
awake phase, an awake phase that fails to out-damage its dormant one, and the analyser's move
distribution held against 20k samples of the real picker).

**Phase 8 is closed.** All five bullets shipped.

## Phase 9 — The map, and the run's texture

- ~~**The `?` node.**~~ **Shipped**, and the forecast above was wrong about the mechanism in a way
  worth keeping. It called for `MapNodeType.Unknown` resolved at *visit* time from `RngStreams.Map`,
  with the resolution recorded into `RunState`. What shipped is the same feature with the roll left
  where it already was: `MapNode.Concealed` is a bool beside `Type`, `MapGenerator` rolls the truth
  as it always did, and `MapScreen.EnterNode` clears the fog. Run save v3 → v4, no migration code,
  no `MapNodeType` member, no new icon.

  The two are indistinguishable to a player — nobody can observe when the die was cast — and the
  difference is entirely in what visit-time resolution would have cost:
  - **`BalanceModel` reads `Type` in a dozen places** (`Count`, `NodeGold`, `CardsAt`, `RelicsAt`,
    five `MaxAlong` sweeps). A node with no type yet is not an unknown to them, it is a *zero*: no
    fight, no gold, no reward card. Every printed curve number would have deflated silently, with
    every suite green — the Phase 8 denominator trap arriving through a third door.
  - **The re-roll window is real and the roadmap named the wrong fix for it.** Recording the
    resolution into `RunState` does not close it, because Combat is deliberately excluded from
    `RunManager.AutoSaveScreens` — a `?` that resolved to an Elite would never be persisted before
    the fight, so quitting mid-fight would re-roll it. Rolling at generation is what makes that
    unrepresentable rather than guarded.
  - `MapGenerator.MakeNode` already draws `EnemyIds` at generation, so visit-time resolution needed
    a second enemy draw at a second site. Concealment needed none.

  Three things the forecast did not contain:
  - **Where the weight comes from mattered more than what it was, and it took two passes to get
    right.** Carved out of the whole node-type table, an 18/128 `?` slot cost 1.1 reward picks and
    51 gold a run and took Encyclopedian from reachable on 23% of seeds to 15% — because a `?` comes
    back as a fight only one time in five, so paying for it proportionally taxes fights. Carving it
    out of Shop/Treasure/Rest/Event instead fixed that.

    **But leaving Combat and Elite's weights alone is not leaving their shares alone, and only one
    of the two was actually protected.** The table grew 110 → 119, so an unchanged weight is a
    smaller slice; Combat breaks even because the `?` table hands 20% back, and Elite — the one type
    a `?` may never be — does not. Elite frequency quietly fell 6.8%, 1.9 elites a run to 1.8 and
    the best path 10 to 8, and nothing in the repo would have said so: `BalanceSmokeTest` bands
    elite *cost ratios* and has never measured how often an elite is offered. Elite 14 → 15 is the
    fix, and the whole thing is the Phase 8 denominator lesson arriving in a table of weights rather
    than in a report.

    Landing: the encounter-cost, curve and boss tables byte-identical, fights 16.2 against 16.6,
    elites 1.9 against 1.9, Encyclopedian 23% against 23% — and Mystery Machine 95% against 83%,
    the one threshold this moves and it moves it the right way. Events go 1.6 → 2.1 per run, which
    is the texture the item was for.
  - **A `?` may not be an Elite**, and that is the only exclusion in `PickConcealedType` that is a
    design rule rather than structure. An unadvertised elite is a fight the player committed to
    without the one fact that decides whether to take it: an ambush, not a gamble.
    `MapSmokeTest` holds its own copy of the legal set, so widening the generator's table fails
    there and has to be argued rather than inherited.
  - **The type→screen router had no `default:` arm and could not be tested at all**, because it
    ended in `RunManager.ChangeScreen`. An unhandled `MapNodeType` advanced `CurrentNodeId` onto a
    node nothing routes from and changed no screen — a soft-lock rather than a crash. Split out as
    `MapScreen.EnterNode`, which is also where the reveal lives (not `BuildButtons`: revealing on
    render would show every `?` a floor early), and every enum member is now driven through it.
- ~~**Potion drops from combat.**~~ **Shipped**, and the forecast was right about the mechanism and
  wrong about where the work was. Rarity landed as forecast: `PotionDefinition.Rarity` reusing
  `CardDefinition`'s enum, weighted 65/25/10 across all four grant sites, with the tier-first draw
  *extracted* into `RarityPool` (now `TierPool`) rather than copied — the weights stay split (that is the
  `BlockMath`/`DamageMath` argument), the algorithm does not. Drop rates are per-act data beside the
  gold dials (`ActDefinition.PotionDropPercent`/`ElitePotionDropPercent`), rolled off a fifth RNG
  stream, `RngStreams.Drops`. No save-version bump: potions save by id, and the rates live in
  `acts.json`.

  Four things the forecast did not contain, in ascending order of how much they cost:

  - **The tier number that matters is per *row*, not per tier**, because tier-first sampling divides
    a tier's weight among its members. At 6/4/2 rows that is 10.8% / 6.3% / 5.0% — monotone, which is
    the whole point — but authoring two more Uncommon potions and nothing else would put an Uncommon
    *below* a Rare, and every assertion anyone would naturally write is about a tier's share and
    would stay green through it. That is one check, and it is the highest-value one in the batch.
  - **A drop rate defaulting to 0 is the one absent-is-zero field in the data layer whose default is
    wrong.** Everywhere else a missing key reads as "this act didn't have that", which is true; here
    it silently switches the feature off for that act with nothing thrown. `ActSmokeTest` asserts
    both keys are authored above zero, in range, and not transposed.
  - **The item uncovered a live double-grant bug that had nothing to do with potions.**
    `CombatScreen`'s Continue handler had a guard on the *stats* fold and none on the rewards below
    it, and two independent entry points (the button, and `hd_confirm`/`hd_end_turn`, which is
    deliberately not focus-based). `ScreenFade` holds the scene up long enough that a click plus an
    Enter awarded the gold twice and granted **two relics**. Fixed as `_continueResolved` over the
    whole handler.
  - **The presentation was the real work, and the first answer to it was wrong.** A potion tile
    beside the card fan needed a guard on the card pick and a retitled button, because taking a card
    called `Advance()` and whichever reward the player touched first forfeited the other. Those
    patches were the tell: the screen was a card fan pretending to be a reward list. It is an actual
    list now — gold, relic, potion and "add a card to your deck" are rows, claiming one removes it,
    the fan is a modal behind its own row, and one button leaves. Gold and the relic stopped being
    granted before the screen loaded, which is what made them rows rather than announcements.

    Two things fell out of that which are worth carrying: `MarkClaimed` has to **re-save**, because
    `RunManager` autosaves on *entering* a screen and the save taken when Reward opened has no claims
    in it; and **opening a modal is the one case `Regrab` cannot handle** — `RegrabNow` deliberately
    leaves an existing focus owner alone, so the row just pressed kept the ring behind the dim. Focus
    has to be *taken* into an overlay, and the list has to leave the focus chain rather than merely
    be dimmed. A 0.75 scrim also turned out not to be enough on its own: a column of gold headings
    read straight through it, the same failure the event picker had, and only a screenshot shows it.

  Landing: the encounter-cost, curve, band, boss and threshold tables byte-identical against `main`
  — the only lines that moved in `tools/balance-report.sh` are the two new ones. 4.5 expected drops
  a run against a three-slot belt, 8.1 on the best path, banded in `BalanceSmokeTest` at both ends
  because both ends are real failures.
- ~~**Relic tiers.**~~ **Shipped**, and the forecast was right about the enum and wrong about only
  one thing — that `RelicTier` is a rarity. `RelicTier { Common, Uncommon, Rare, Boss, Shop, Event }`
  landed verbatim, weighted 50/33/17 across the ladder, with the owned-and-unlocked filter collapsed
  out of four byte-identical LINQ copies into `RelicPool` (the `IsPlayable` argument, one content
  type over) and `ShopScreen`'s local uniform `Sample<T>` retired exactly as its own comment
  predicted. Content 27 → 33: the four rows that compound *per turn* became the Boss tier, and six
  new rows gave Boss, Shop and Event a real pool rather than a promoted leftover. No save bump —
  relics persist as ids, so a tier is re-resolved from the definition on load.

  Five things the forecast did not contain:
  - **"One pool per site" is the wrong decomposition; "which tiers may a site see" is the right
    one.** Written as one pool per site, Boss/Shop/Event each need their own sampler and their own
    exhaustion story. Written as a tier filter, `TierPool` already renormalises over whatever is in
    the pool it is handed, so "a boss draws the Boss tier alone" and "a shop draws the ladder plus
    its own tier" are the same function with a different argument. That is `RelicPool.TiersFor`, and
    it is the only place a site's pool is decided.
  - **`RelicTier` is two axes wearing one enum, which is why it is not `Rarity`.** Only the first
    three members are a power level; Boss, Shop and Event name a *source*. Reusing `Rarity` — which
    `PotionDefinition` correctly does — would have made "how likely is a Rare" and "where did this
    come from" the same question, and it would have silently under-covered the two hardcoded
    three-element `Rarity` sweeps in `EffectSmokeTest`. The relic sweep drives
    `Enum.GetValues<RelicTier>()` instead, so a seventh tier authored on nothing fails.
  - **`BossWeight` has to be positive and means nothing.** Boss is never mixed with another tier, so
    it is renormalised to the whole draw every time — but at `0`, `PickTier` sums a total weight of
    0, returns null, and the boss reward vanishes with no error. A constant that is load-bearing
    only in its sign is worth the comment it now has.
  - **Tier-scaled pricing was blocked on rendering, and `ShopScreen` had already written the rule
    down.** Its potion comment says a price moving with an attribute the tile does not render reads
    as a bug rather than as a tier — so the sub-label became `Relic - Rare` first and
    `RelicPriceFor` (110/150/210/170) followed it. The payoff is visible in one screenshot: at 129
    gold the shop offers an affordable Common and a greyed Uncommon, which is the slot being a
    decision for the first time.
  - **The mirror was the thing worth fixing, not the number.** `BalanceModel` held
    `ShopRelicPrice = 150` under a "mirrors ShopScreen" comment with *nothing asserting the mirror*
    — the `NodePotionPercent` hazard one file over, undetected. It reads `ShopScreen.RelicPriceFor`
    and `RelicPool.WeightOf` now and computes the expectation (146g), which cannot drift at all, and
    is strictly better than the assertion this was going to need. Landing: the encounter-cost,
    curve, band, boss and threshold tables byte-identical against `main` — the only lines that moved
    are the three new ones. `I Like Shiny` did not budge, because what a route collects is capped by
    how many Elite/Boss/Treasure/Shop nodes it can string together, not by how many relics exist.
- ~~**Tips under the reward list.**~~ **Shipped** alongside the tiers, off a playtest note rather
  than off this document. Fifteen rows in `data/tips/tips.json`, rendered in the band between the
  framed list and the Skip button — empty at every row count, because the list is centred in its own
  area. Three things worth carrying:
  - **It is a rotation, not a roll**, and adding a sixth `RngStreams` entry would have been free.
    Three reasons not to: a stream's position is not serialized and `Init` re-runs on load, so a
    draw outside the deterministic run pipeline replays differently after a resume; six `ScreenShot`
    fixtures re-render this screen; and a rotation visits every tip once before repeating, which a
    roll does not.
  - **A tip naming a key would have been the third place a binding could drift.** `{hd_pile_draw}`
    resolves through `ScreenKeyboardNav.ResolveKeyHints`, which is `KeyHint` for authored prose, and
    the suite refuses a token naming no real action.
  - **Hidden behind the card fan, not faded with the list.** It sits directly under the fan's Back
    button, and dimmed body text behind a modal reads as something the player failed to dismiss.
- ~~**The boss relic becomes a choice of three.**~~ **Shipped**, and the forecast was right that the
  reward screen had already been built for it — `RewardContext`'s own comment predicted "adds rows
  rather than fields", and it cost that type nothing. `RelicPool.Sample` already took a count, so the
  draw was a one-line change. Content unchanged, no save bump, and the encounter-cost, curve, band,
  boss and threshold tables byte-identical against `main`: three offers taken one at a time is still
  one relic per boss, so nothing in `BalanceModel` could move.

  The forecast was wrong about the shape in one place and silent about three others:
  - **"Reuses the `CardPicker` shape" was half right, and the wrong half is the reusable one.**
    `CardPicker.Populate` is card-typed and could not render a relic. What *is* reusable is
    `WireGridNavigation`, which already takes `IReadOnlyList<Control>` because `LibraryScreen` reuses
    it for relic tiles — so the picker is that call plus `LibraryScreen`'s tile, promoted to
    `ScreenChrome.FocusableFrame` on its second caller. The extraction is pixel-identical on
    `libraryrelics` and `libraryinspectrelic`, checked by shooting both branches.
  - **The offer had to stop being a nullable single, and the count is now the whole decision.**
    `GuaranteedRelic` → `RelicChoices`: one entry is an elite's and the row hands it over, more than
    one is a boss's and the row opens the picker. Keeping both fields would have made "an elite with
    three offers" representable, and nothing would have answered it.
  - **The exhaustion fallback was honest at one draw and silently wrong at three — and the first fix
    for it was wrong in a more interesting way than the bug.** `pool.Count == 0` became
    `pool.Count < count`, identical at count 1 so no existing site moved, and a Boss tier down to two
    rows can no longer offer two tiles with nothing thrown. But *topping up by concatenating the
    ladder into the pool* puts the ladder in the same tier roulette as Boss, where `BossWeight` is
    about half the total: measured, a boss with two of its own relics left came back with three
    Rares and Commons while both Boss relics sat unoffered. It also quietly promoted `BossWeight`
    from a number that only has to be positive into a live mixing ratio, contradicting the comment
    directly above it. Drawing the site's own tiers to exhaustion *first* and then filling only the
    shortfall keeps "Boss is never mixed with another tier" true. Caught in review, and the
    assertion this branch had explicitly declined to write ("a mixed draw can legitimately come back
    all-ladder") turned out to be exactly the one that pins it.
  - **Two overlays would have been five predicates.** The picker shares the card fan's overlay node
    rather than sitting beside it, because `RefreshRows` asks "is a modal open?" in five places.
  - **The relic name is the thing that grows, and the guard is not the one that looks like it.**
    Three tiles share an 800px band and `ScreenChrome.Heading` returns an unwrapped `Label` — whose
    minimum width is its whole string, so a long name widens its *tile* rather than overflowing it.
    Setting `AutowrapMode` is the whole fix; `TextFit` sits above it and buys legibility only
    (fewer wrapped lines), which one-line-at-a-time mutation established and a three-line mutation
    had obscured. Both are asserted, the second through the applied font size because that is the
    only thing about it anything can see. Third instance of Phase 11's "a constant that fits the
    worst case is not a constant that fits the best one", caught before a playtest this time — the
    longest Boss name today clears by three characters.
  - **A green suite was overwriting the developer's real `run_save.json`, and had been for two
    phases.** Claiming a reward row calls `MarkClaimed`, which re-saves by design; three
    `ScreenSmokeTest` tests do that outside a `RunSaveGuard`. `TestRestScreen`'s own guard could not
    help, because it runs last and so snapshots the already-clobbered file and faithfully restores
    the damage — which is why an in-progress run being eaten by a test run never looked like a test
    problem. One guard now wraps the whole `_Ready` sequence, so a test added later inherits it.
- ~~**A start-of-run choice.**~~ **Shipped**, and the forecast was right about both halves being one
  screen — three blessings and a typed seed field — and silent about what the choice would actually
  *offer*, which turned out to be the only question worth answering. The answer is that it needed no
  new mechanical vocabulary at all: `EventOutcomeRegistry` is already the non-combat registry (its
  own header says why — `EffectContext` requires a live `CombatManager`/`Combatant`), and its
  sixteen keys already spell every boon this genre offers. A `BlessingDefinition` is a label, a
  description and a list of the same `EventOutcomeSpec`s an event choice carries; `Begin(EventChoice)`
  widened to `Begin(IReadOnlyList<EventOutcomeSpec>, string)` and the choice overload delegates to
  it. Content 10 blessings, no save bump, no new RNG stream, no new `hd_*` action, no new icon.

  Seven things the forecast did not contain:
  - **The seed does not label the run, it *is* the run — so committing one rebuilds everything.**
    `StartNewRun` split into `BeginRun(int seed)` (seed, `RngStreams.Init`, `InitNewRun`, no screen
    change) and a `StartNewRun` that mints one and routes to RunSetup. Typing a seed re-runs
    `BeginRun`, so the map, the enemies and the three offers move together. A field that only
    relabelled the run would make every "reproduced" seed a different run, which is the one thing
    this item exists to prevent.
  - **Which makes claiming a blessing an ordering trap, and it is the same one Phase 8 kept
    finding.** `InitNewRun` resets `Deck`/`Relics`/HP, so a re-seed *after* a blessing resolves
    erases it silently. The seed controls leave play on the claim — read-only **and**
    `FocusModeEnum.None`, because Disabled is neither — and the real guard is a `_claimed` flag the
    commit path checks, since the lock is UI and the guard is a fact about the run.
  - **A screen that ignores `EventResolution.Pending` resolves the blessing to nothing.** Two of the
    sixteen keys come back pending, and "forbid pickers in blessings" was the cheaper option and the
    wrong one: it makes the registry's own contract conditional on the caller. `EventScreen`'s
    picker half transplanted verbatim, and `Unburdened` — remove one of the ten cards you start with
    — is the strongest row in the pool.
  - **It is the first `LineEdit` in the project, and `hollowdeck_theme.tres` has no entry for one.**
    An unstyled `LineEdit` inherits the default font and nothing else: stock Godot blue-grey box,
    blue caret, blue selection, which is the mixed-media seam the pixel-art commitment exists to
    close, arriving through the one control type nobody had used. `ChromeStyles.ApplyLineEditStyle`
    is the `ApplyFocusableSliderStyle` argument one control over.
  - **The starting position was written down in three places and asserted in none.**
    `BalanceModel.PlayerMaxHpByAct` defaulted to 50 and `Reachable` to 99, both copies of literals
    in `InitNewRun` — the `ShopRelicPrice = 150` hazard, undetected, two files over from where that
    one was found. Pricing the blessings needed the numbers, so they are `RunState.StartingMaxHp`
    and `StartingGold` now and the model reads them.
  - **Deck size is two axes wearing one column.** `BalanceModel` priced `Cards` as a signed count
    until the "every blessing offers something" assertion failed on `Unburdened` and then on
    `Borrowed Steel`, from opposite directions: a *smaller* deck is the gain in this genre, and a
    card named by the author is how a Curse is authored. Split into `Cards` (offered) and `Imposed`,
    so a Pain and two real cards stop printing as the same `+1`/`+2`. The `RelicTier`-is-not-`Rarity`
    lesson, in a report column.
  - **`ScreenSmokeTest`'s ordering is load-bearing and nothing said so.** `TestRestScreen` presses
    Leave, which puts the tree's current scene through `ChangeSceneToFile` and detaches the suite
    node — so a test added after it silently runs *outside* the tree: `AddChild` still succeeds,
    `_Ready` never fires, every assertion reads a screen that never built itself, and the first
    `await` on `GetTree()` hangs until the watchdog kills the suite. Cost one run to diagnose; the
    file says it now.

  One thing that only became *writable* here: `ScenePaths` covers every `ScreenState` for the first
  time, since RunSetup was the last unbuilt member. `RunSaveSmokeTest` asserts it, because
  `ChangeScreen` to an unregistered state pushes an error and returns — no crash, no screen change,
  and a fully built run left standing on the menu, which is exactly what the old `RunSetup` arm did.

  The review pass then found two things, and the first is the most useful entry here:

  - **Godot moves focus on mouse-*down*, which makes blur-to-commit unimplementable on this screen.**
    The seed field committed on `FocusExited` — the obvious reading of "the player is done typing" —
    so typing a seed and then *clicking a blessing* fired the commit between the press and the
    release. Committing rebuilds the offers, rebuilding frees the tiles, and the tile under the
    cursor was gone before the click resolved: nothing happened, and a different blessing had slid
    into its place for the second click. There is no ordering of "rebuild the run" and "claim from
    the run" that survives being interleaved with one click, so an uncommitted edit is now
    *discarded* on the way out and only Enter and Reroll commit — with the rule stated on screen,
    since a field that silently ignores what was typed into it is worse than one that asks for a
    keypress. Every suite was green throughout; only a synthetic press/release pair shows it.
  - **A hand-written set of a switch's case labels is a second copy, not a guard.**
    `BalanceModel.Price` has to end in a `default` arm, and an arm returning zero prices an unknown
    outcome as "changes nothing" — so the branch shipped a `PricedOutcomes` set beside it for the
    suite to check against. Measured: deleting the `add_card` arm while leaving its key in the set
    left all 22 suites green and Cursed Fortune's Pain priced at nothing. `Price` returns null for a
    key it does not handle now and `PricesOutcome` asks *it*, so there is one place that decides.
    The same shape as `BalanceModel` reading `ShopScreen.RelicPriceFor` rather than mirroring it, one
    phase on and one file over.

  Landing: the encounter-cost, curve, band, boss, threshold and upgrade tables byte-identical
  against `main` — the only lines that moved are the new blessing section. Max HP spans 44 to 70 and
  gold 0 to 279 across the pool, banded at both ends in `BalanceSmokeTest` against `RunState`'s own
  constants rather than against 50 and 99, which is the "no rung may make act I unwinnable"
  assertion Phase 10 was going to need arriving one phase early.
- ~~**Map width is a judgment call, not an obvious fix.**~~ **Shipped**, as 3–5 rather than the
  genre's seven, and the judgment turned out to be arithmetic rather than taste. `MapScreen` derives
  the vertical pitch from the band it has left (`availableHeight / (widest - 1)`), so width is spent
  directly out of the space *between* nodes: at 1152x648 a 6-wide floor puts 58px of pitch under 64px
  nodes once a run carries three rows of relics, and a 7-wide floor overlaps with no relics at all.
  Seven is unreachable without scrolling, which is the fill-one-canvas property Phase 4 built. Five is
  what the canvas holds. `MaxNodesPerFloor` 4 → 5, floor 0 pinned at 3 (every node on it is a Combat,
  so a wider opening is a wider choice between identical rooms), connectivity untouched, no schema
  change and no save bump.

  Four things the forecast did not contain:

  - **The cost does not land where this bullet said it would.** "Costs that layout" reads as the
    graph outgrowing the canvas; the band is spent in full at *any* width, so nothing outgrows
    anything. What width actually spends is the pitch, and the pitch's competitor is not the map at
    all — it is the run-status block, which grows 44px per relic row. The binding constraint on how
    wide this map can be is **how many relics the player is carrying**, which is not a fact about the
    map and is why no amount of staring at `MapGenerator` would have found it.
  - **The constant that broke first was the current-node ring**, `NodeSize + 20f` — the widest thing
    in the vertical stack and the one nobody had listed as width-dependent. It is derived from the
    pitch now. Measured, and worth recording because the tempting version of this story is false:
    it was **not** already broken at four wide. The worst case a run can reach there (20 relics, four
    rows) left an 85px pitch and the flat ring cleared it by a pixel. Widening is what made the
    derivation necessary — the fourth instance of Phase 11's caps-and-margins shape, but the first
    one that was genuinely latent rather than already shipping.
  - **What actually pays for the width is a relic grid capped at three rows on this screen**
    (`RelicColumnsForBand`, the trade `ShopScreen` already makes in the opposite direction). Without
    it, four relic rows at five wide leave a 63.75px pitch under 64px nodes — they overlap outright.
  - **Mutation testing is the only reason any of the above is stated correctly, and it cost this
    branch two wrong claims.** The first draft shipped four mechanisms; reverting the derived ring,
    the relic cap or the reclaimed `BottomMargin` *one at a time* left all 22 suites green. So the
    assertions were re-pointed at the mechanisms rather than at the overlap they prevent — ring
    against pitch, grid against row budget — and a fourth change, deriving the path bow from the
    pitch, turned out to move rendered output by 0.04px and was deleted.
    Then the *review* pass found the claim this bullet originally made — that `BottomMargin` 88 → 68
    and the column cap were "two halves of one fix, both needed" — was itself false: the 0.75px that
    priced the cap "alone" was computed against the flat ring the same branch had already replaced.
    With the cap and the derived ring in place the old 88px margin is fine, and reverting it alone is
    green. It stays, as measured headroom on a denser map (79.75px pitch against 74.75), and is
    labelled as headroom rather than as a fix. **A number that prices one change while holding
    another change's before-state is the arithmetic version of the moved-denominator trap this
    project has now hit in four different forms.**

  Landing: the encounter-cost, curve, band, boss, ramp, player-curve, card-reward-odds, blessing and
  upgrade tables **byte-identical** against `main` — nothing that does not read the map moved. Means
  wobble ≤0.2 (Combat 11.3 → 11.4, fights 16.2 → 16.4, gold 707 → 715), which is resampling rather
  than a shift: both samplers draw all three acts from one `Random` and a wider map consumes a
  different number of draws, so those rows are re-sampled, not re-computed. What moved for real is
  what should — best-any-path (Elite 9 → 11, Rest 13 → 14, Treasure 9 → 10) and reachability
  (Encyclopedian 23% → 26%, Librarian 97% → 99%, Mystery Machine 95% → 98%, relics 20 → 21). Nothing
  collapsed, so no threshold needed retuning.

  The last of those is the one worth carrying: **`BalanceSmokeTest` could not have told us if one
  had.** `TestScoreThresholdsAreReachable` asserts `Best >= needs`, which is one-sided — anything
  handing the player more map to route through can only make it greener, and the only signal a
  category had gone free was a `Sweep` row in the report that *disappears* when it collapses. It is
  banded now.
- ~~Smaller: a skip streak or rarity boost on card rewards, so skipping is a strategy rather than a
  shrug.~~ **Shipped**, and the "or" in that line was the decision worth making rather than a choice
  between two spellings of one feature. A flat rarity boost is a dial the player does not touch; a
  streak is a *bet* — each rung costs a card and pays odds on the next offer, which is what makes
  skipping a play rather than a shrug. `RunState.CardSkipStreak` counts consecutive declines, run
  save v4 → v5, no migration code (absent reads as 0, which is what a run saved before the feature
  had in fact skipped).

  The ladder is `CardPool.WeightOf(rarity, streak)`, and four things about its shape are load-bearing:

  - **The three steps sum to zero, so the total stays 100 at every rung.** That is not tidiness: a
    weight reads directly as its tier's percentage share in `CardPool`'s own comment, in the balance
    report, and *on the reward row the player reads*. A step set that did not cancel would leave all
    three quietly describing different ladders. `EffectSmokeTest` asserts the total rather than the
    steps.
  - **`MaxSkipStreak = 3` is where the ladder stops, and the bound is a correctness one.** At rung 4
    the Common weight reaches 0 — and `TierPool.PickTier` would leave Common *in the pool and
    unreachable*, a tier that exists and can never be drawn, with nothing thrown. `WeightOf` clamps
    rather than trusting its caller, because the number arrives from a save file.
  - **Uncommon leads at every rung, and Rare passing Common at the cap is intended.** Rare passing
    *Uncommon* would make the top rung a Rare dispenser rather than a richer pool, which is a
    different feature; the suite asserts the ordering, not the numbers.
  - **The streak is reward-only.** It is a per-draw offset the player moves, not a second authored
    table, and it applies to exactly one site. A shop that got richer because the player walked past
    two cards would be pricing something it had no part in — so the shop and the random-card event
    call the unchanged overload, pinned by an exact same-seed match against the rung-0 draw.

  Two traps the branch actually had to avoid, neither visible from the feature description:

  - **A press is not a skip, and `Advance` is not instant.** Skip is reachable from the button *and*
    from `hd_cancel`, and `ScreenFade` holds the scene up between the press and the swap. That window
    was harmless while the handler only changed screens; it stops being harmless the moment it moves
    a counter, and a click plus an Escape inside it built two rungs off one skip. `_skipResolved` is
    `CombatScreen._continueResolved` one screen over, for the same reason and against the same bug
    that shipped the double relic grant.
  - **The condition is the offer, never the button's label.** `RefreshExitButton` already retitles
    Skip as "Continue" once every row is taken, so a player who took the card leaves through the same
    handler having skipped nothing. `LeavingDeclinesACard` reads `RewardContext` instead.

  The row states the rule at rung 0 (`Skip to sharpen the next offer`) rather than appearing on the
  first skip, which was the cheaper option and the wrong one: a player who never happens to skip
  would never learn the mechanic exists, and a strategy nobody can see is not one they can adopt.
  The odds it prints are computed from `CardPool.WeightOf` rather than written beside it — a literal
  would be a second copy of the ladder, and the copy that shows the player a number.

  Landing: every existing table byte-identical against `main` — the only new lines are the card
  reward odds section, which prints 9% → 25% → 39% → 51% for a Rare somewhere in the three-card
  offer. `BalanceModel.CardsAt` is deliberately blind to the streak and says so: the streak moves
  what a reward card *is*, never how many are gained, and that model has no rarity axis for a deck
  at all.

*Proven by:* `MapSmokeTest` (concealed nodes actually appear, are never an Elite or the boss, never
land on a forced floor, still carry enemies when they are a Combat, roll more than one type, hide
their type in the tooltip as well as the icon while their neighbours still show theirs, and reveal
on entry — plus every `MapNodeType` routing to a screen through the extracted `EnterNode`),
`RunSaveSmokeTest` (a concealed node surviving a round trip in both directions, and a v3 save whose
map nodes omit the flag loading as visible).

For the map's width: `MapSmokeTest` (branching floors inside a 3–5 band with **both ends actually
occurring**, floor 0 pinned to the minimum, and — the first node-vs-node overlap check in this repo —
no two node rects closer than `MinNodeGap` at every relic count a run can reach, driven at the
longest act against a seed *searched for* rather than written down, since a hardcoded one tests
whatever width it happens to roll and would go quietly narrow as content moves — and the search
reports a miss as a red line rather than throwing out of `_Ready`, which would surface as a watchdog
TIMEOUT, with the check reading the search's *result* rather than re-running its predicate. Plus the two checks
aimed at the mechanisms rather than their consequence, which is what mutation testing established
were needed: the current-node ring never wider than the pitch, and the relic grid never past its row
budget — reverting either alone leaves every other assertion in the file green). And
`BalanceSmokeTest`, where `TestScoreThresholdsAreReachable` gained its other end: the hardest deck
tier must stay demanding, and the ladder as a whole must keep something worth routing for, because
`Best >= needs` can only get greener as the map grows.

For the start-of-run screen: a new `BlessingSmokeTest` (21 → 22 suites) — the database loading an
exact count, ids unique and ASCII, every outcome key registered *and* priced by `BalanceModel`
(the second half being the one nothing else covers, since the model's `default` arm prices an
unknown key as zero), a picker always the last spec and never inside a gamble, every `add_card`
naming a real card with a count, the offer coming back three distinct rows on 200 seeds and short
rather than looping when asked for more than the pool holds, the offer repeating for a seed and
differing across seeds, both picker blessings actually coming back pending against the *starting*
deck, and — the highest-value one — every row measurably changing `RunState` when it resolves, which
is the check a pool of prose-plus-string-keys most needs. Plus `ScreenSmokeTest` (three tiles from
the authored pool, the seed field showing the run seed, claiming granting exactly the chosen row and
nothing else, the claimed tile leaving the tree, a locked field refusing to rebuild the run, typing
a seed producing *that seed's own draw* rather than merely a different one and regenerating the map
with it, junk reverting, and the layout half driven with a label longer than anything authored —
three tiles inside their band, the same width, unclipped, stepping down a font rung, and clear of
the run-status block), `KeyboardSmokeTest` (the offer taking focus rather than the text field, focus
taken into the picker and returned to Begin, the seed controls leaving the focus chain behind the
claim, and — structurally, since what makes it true is which handler is overridden — the pile-view
keys being read from `_UnhandledKeyInput` so a focused text field consumes them first),
`RunSaveSmokeTest` (RunSetup excluded from `AutoSaveScreens`, and every `ScreenState` having a
scene) and `BalanceSmokeTest` (the pool able to fill an offer, and no row moving starting max HP or
gold out of band).

For the boss-relic choice: `ScreenSmokeTest` (the row asking rather than naming one of the three,
opening the picker granting nothing, every offer on a tile, a pick granting exactly the one chosen
and returning to the list rather than leaving the screen, the claimed row refusing further focus, a
re-opened picker paying nothing twice, the tip hidden behind this view as well as the fan, and — the
layout half, driven with a name longer than anything authored — three tiles inside their band, all
the same width, and none clipped by it), `KeyboardSmokeTest` (focus taken into the picker, the list
and Skip leaving the focus chain behind it, and both restored on Back, driven *after* the card fan in
the same visit so a stale focus owner from the freed CardViews would show), `EffectSmokeTest` (three
distinct Boss-tier offers, the tier carrying enough stock for every boss in a run, and the ladder
top-up at four of five Boss relics owned — the arm the old `== 0` fallback could not reach),
`ActSmokeTest` (a real boss win offering three, all Boss tier, none granted before the screen) and
`Phase4ContentSmokeTest` (an elite offering exactly one, so an elite drifting to three is not merely
"not null").

For the relic pass: `EffectSmokeTest` (every relic row *authoring* a tier, read out of `relics.json`
as text; every `RelicTier` member having stock, driven over the enum rather than a hand-written
list; per-row monotonicity across the ladder; every site drawing only from its own tiers over 400
samples, in both directions — no Boss relic in a chest, no Shop relic off a boss; each exclusive
tier actually reachable from its own site; and a boss falling back to the ladder with its own tier
owned out). For the tip line: the same suite (ASCII-only, unique ids, every `{hd_*}` token naming a
real action, a full lap repeating nothing and wrapping, a negative seed still indexing) plus
`ScreenSmokeTest` (a tip on screen and from the authored pool, the longest one clearing the Skip
button and fitting its line, the tip gone behind the card fan, and the tip advancing with the run).

For the potion pass: `EffectSmokeTest` (`TierPool`'s algorithm against a synthetic pool rather
than real content — drains every tier once, fixed order, renormalises an exhausted tier, and
actually reads the weight function; `PotionPool`'s rare share; every potion row *authoring* a
rarity, read out of `potions.json` as text because the enum has no null; and the per-row
monotonicity above), `ActSmokeTest` (both drop rates authored, in range, and not transposed),
`BalanceSmokeTest` (a boss never rolling, every act's fights actually rolling, and the per-run yield
banded), `ScreenSmokeTest` (the reward list: a row per offered reward and none for one not offered,
gold and the relic granted only on the claim, a claim that cannot repeat, the potion row refused but
still shown against a full belt, the card row opening the fan and a pick returning to the list
rather than leaving the screen), `KeyboardSmokeTest` (claiming the focused row leaving the ring
somewhere legal, and the modal taking focus while the list leaves the focus chain),
`Phase4ContentSmokeTest` (an elite reward *offered* rather than banked before the screen).

For the skip streak: `EffectSmokeTest` (the ladder's shape — the total held at every rung, every
tier still drawable at every rung, Uncommon leading each one, Rare rising rung over rung, and the
clamp at both ends — plus the two checks that shape alone cannot make, that a capped draw really
does come back richer than a flat one over 400 trials, and that the overload the shop and the event
call is byte-for-byte the rung-0 draw on the same seed; the first of those is the silent no-op
guard, since a weight function nobody passes to `TierPool` compiles, prints a perfect balance
report, and changes no card the player ever sees), `RunSaveSmokeTest` (a mid-ladder streak round
tripping, a v4 save without the key loading as 0 against a *poisoned* field, and an out-of-range
value clamping), `ScreenSmokeTest` (the row explaining the rule at rung 0 and naming a live streak
with the rung's real odds read out of `CardPool` rather than a literal, taking a card resetting it,
the three cases of `LeavingDeclinesACard` asserted directly, and two presses of Skip building
exactly one rung — that last driven with the fade deliberately left *on*, so `ChangeSceneToFile` is
deferred into a tween callback that never runs and the suite survives its own navigation), and
`BalanceSmokeTest` (every rung printed, the top one worth the three cards it costs, not so good it
makes Rares the norm, and the offer odds monotone — all asserted against the *printed* rows, so a
report computing them differently from the draw fails rather than shipping a table nobody can act
on).

## Phase 10 — Ascension — **shipped**

Twenty rungs of stacking modifiers: the reason a finished run is worth repeating, and the only thing
in this document aimed squarely at replayability rather than at a single run.

The ladder is earned rather than chosen. `MetaProgressionManager.AscensionLimit` rises only on a
**win at the current limit**, and `RunSetupScreen` offers one toggle — off, or your limit with every
rung below it stacked — hidden entirely until that limit is above 0. A stepper was the obvious
alternative and is a difficulty menu; a toggle makes each rung a thing you beat rather than a thing
you pick.

- ~~**Authored as data**, with a constrained modifier vocabulary, a rung-aware `BalanceModel`, meta
  save v2 → v3 and an ascension category in `RunScore`.~~ **Shipped**, all four bullets as one
  branch, and the forecast was right about every structural call: `AscensionDefinition`/
  `AscensionDatabase` mirror `ActDefinition`/`ActDatabase`, the nine knobs are exactly the ones that
  already existed, and no rung needed anything outside them. Content: 20 rungs, meta save v2 → v3,
  run save v5 → v6, one new suite (22 → 23), and no new effect action, RNG stream, `hd_*` action or
  icon.

  Seven things the forecast did not contain, in ascending order of how much they cost:

  - **The forecast named the four bullets and not the property that decides whether any of them
    worked: rung 0 must be identity.** `AscensionModifiers.None` and a defaulted parameter on every
    `BalanceModel` entry point are what make that structural rather than hoped for, and the landing
    check is the same one every phase since Phase 8 has used — all 200 lines of existing balance
    tables byte-identical, with the ascension section the only new content. Without it a ladder that
    quietly moved the rung-0 curve would have invalidated every band, threshold and encounter cost the
    last four phases measured, and nothing would have said so.
  - **The rows are per-rung *deltas* and the fold is a sum, which is what makes a forgotten key
    inert.** Authored as cumulative totals, a row that omitted a field would read as "this rung sets
    enemy HP back to 100%". Authored as multipliers rather than percent-deltas, an omitted field
    would default to 0 and zero the whole ladder. Both are silent; the delta form is the one where
    absent means "this rung does not touch that knob".
  - **A +5% damage rung is not a rung.** Enemy damage is authored in small integers (5–15), and
    round-half-up on +5% moves almost none of them — measured, rungs 1 and 2 of the first draft left
    act I's mean encounter cost *identical* to rung 0. Damage steps are +10%; HP steps are +4%,
    because HP is the far more sensitive lever (a single +4% moves the mean cost ~7%, since a tankier
    group is a longer fight that absorbs more turns). The two knobs are nothing like equally
    sensitive and the ladder cannot step them equally.
  - **The elite-frequency rung has to *move* weight out of Combat, not add it to Elite**, which is the
    Phase 9 `?`-node lesson arriving as a caller rather than as an edit: an unchanged denominator is
    not an unchanged share, and adding would have resilenced Shop/Treasure/Rest/Event, four types the
    rung is not about. It also only applies on floors ≥ 2, where Elite is actually in the table.
  - **The elite and boss cost *bands* are nearly rung-invariant by construction, and are therefore no
    evidence the ladder does anything.** They divide by `MeanNormalCost`, which an enemy-HP rung moves
    too — the moved-denominator trap, in its fifth form, this time as a check that cannot fail rather
    than one that failed. The assertion that means something is absolute: act I's mean normal cost
    against the rung's own starting max HP, which climbs 0.87 → 1.54 across the ladder.
  - **`RunScore` wanted a row, not a multiplier**, which the forecast's own word ("multiplier") would
    have got wrong. The run-end screen renders the breakdown as a column above a Total; a multiplier
    applied after that column makes the printed rows stop adding up to the printed total, which is the
    same class of thing as a drifted telegraph and is avoidable for free. As a row worth 5% of the
    rest per rung it is still a multiplier, and rung 0 drops out of the existing zero-row rule so the
    old exact-total test never moved.
  - **`Migrate()` was a single `>=` gate, which is only correct while there is one hop.** A v1 file
    reaching v3 would have run the shard fold and stamped itself v2. Split into a `< n` chain, whose
    v3 step converts nothing — it exists so the *next* bump does not inherit the bug.

  Two smaller things worth carrying. The screenshot harness restores the meta save only in a
  `finally` at the end of a session, so the new `runsetupascension` fixture — which seeds itself by
  writing a save that has climbed the whole ladder — leaked into every screen shot after it, turning
  plain `runsetup`'s absent row into "ASCENSION 20". That restore moved into `ResetRunState`, beside
  the fade cover and `RewardContext`, which is the third instance of that same leak and the first to
  reach disk. And the summary line under the toggle was authored at `UiTheme.Fonts.Small` — 8px, the
  bottom of the ART_SPEC ladder and the size the two-word ENTER APPLIES chip uses — which rendered
  the only place the player is told what a rung does as an unreadable 200-character rule. Only a
  screenshot shows that.

**One character, not four.** A second class is 60+ cards, a starter deck, new sprites and a full
rebalance of everything above — risk 6, and a different project. The ladder is where replayability
comes from here.

*Proven by:* a new `AscensionSmokeTest` (22 → 23 suites) — the ladder authored at an exact count with
contiguous levels and ASCII labels, rung 0 identity in its *methods* as well as its fields, every rung
measurably changing the fold (driven off `Effective` rather than the row, so a field added to the
definition and forgotten in the fold fails), the fold summing rather than overwriting, the ladder only
ever taking, clamping at both ends, every imposed card real *and unplayable*, and the rounding,
floors and weight-conservation rules. Plus `EffectSmokeTest` (the combat engine actually reading the
rung — enemy HP scaled at `EnemyFactory`, a boss leaning harder than a normal, enemy damage scaled
through `DamageMath` and the player's deliberately not, and a scaled hit landing scaled end to end;
this is the check every other green assertion in the branch is compatible with being false),
`BalanceSmokeTest` (`TestActCurveRises` driven at three rungs, no rung easier than the one below, the
top rung meaningfully harder, act I still playable there, no act-I normal costing boss money at the
top rung, potions still dropping, elites actually more common while the utility rooms survive, and a
shop still worth visiting), `MetaProgressionSmokeTest` (v2 → v3 with and without a prior win, v1
reaching v3 in one pass, both idempotent, and the limit rising only on a win *at* the limit — not on a
loss, not on a win below it, and never past the content file), `RunSaveSmokeTest` (a mid-ladder rung
round-tripping, a v5 save loading as 0 against a poisoned field, and an out-of-range value clamping),
`ScreenSmokeTest` (the row hidden until the ladder is unlocked, the toggle rebuilding the run rather
than recording it — asserted through the starting HP and the imposed cards, which is what a
field-assignment-only toggle would leave unmoved — the summary reporting the live modifiers rather
than a literal, its font rung, the row clearing the run-status block at its tallest, and the claim
locking the toggle both as UI and as a fact about the run) and `KeyboardSmokeTest` (the toggle not
stealing initial focus, reachable by Up from the offers and back down again, and leaving the focus
chain on a claim).

**Phase 10 is closed.**

## Phase 11 — Legibility and feel

**The art medium keeps its own backlog** in `docs/PIXEL_ART_ROADMAP.md` — pixel-specific work
(chrome 9-slices, the animated glow ring, one light direction, effect frames) sitting beside
`ART_SPEC.md` the way a backlog sits beside a rule set. It is not a phase and does not gate one.
Sprite frame animation was its first item and has shipped; it closed a live §2 violation rather than
adding polish, and the rest of that file is worth reading before touching anything visual.

Note that **card inspect's 1.15x hover bump is the same question, and this codebase has already
answered it once.** `CardView.cs`'s `FocusHaloSize` comment records killing a 1.08x scale tween on
the upgrade grid for exactly this: "a fractional scale over pixel art, so the 32px icon drawn at
`CardArtScale` 3 became 103.68px and the 16px bitmap text became 17.28 — both resampled, which is
precisely what `PixelSpec` exists to forbid." The halo replaced it there. The *hover* bump at
`CardView.cs:568` still scales the whole card by 1.15 and was left alone by the animation pass,
because a hover affordance needs a replacement rather than a deletion — which is what card inspect
is. `PixelSpecSmokeTest`'s transform guard is type-driven and would flag it, so `CardView.cs` is an
explicit named exception in that scan — as is `FloatingText.cs`, whose damage numbers punch in from
2.2x and hit ART_SPEC §7's design-em rule rather than §2's scale rule. Emptying that exception list
is the second half of this item; both entries need a replacement affordance, not a deletion.

- **Card inspect.** Cards are 176x240 with a 16px body face and hover does a 1.15x bump; the genre's
  hold-to-inspect is the highest-value UI item left. It has to work from the keyboard too, since
  combat is already fully keyboard-driven.
- **Status tooltips are mouse-only** — a real parity break against this project's own stated "fully
  playable without a mouse". `StatusRow` sets stock Godot `TooltipText`, while `CardView` and
  `EnemyView` both already route through the keyboard-aware `scripts/ui/HoverTooltip.cs`. Route
  `StatusRow` through the same widget.
- **Music is two tracks.** `ambient` and `combat`, selected by one ternary in
  `AudioManager.PlayMusicForState`, plus two stingers. No per-act music, no boss track. Because
  `AudioMusic` synthesises rather than loads, per-act and boss variants are parameter work, not an
  asset budget — the cheapest atmosphere available.
- **An animation-speed setting.** `ReduceMotion` is binary and gates only decorative motion (screen
  fade, reward flourishes, fog, one particle burst). The moment-to-moment card and combat juice is
  ungated, which is exactly where a speed control starts to matter around the tenth run.

**Already done — do not rebuild:** keyword tooltips over cards and intents, draw/discard/exhaust pile
inspection, and a damage preview that resolves live Strength and Weak *and* the aimed enemy's
Vulnerable. All three are good.

**Also done, from the first real playthrough — four text-and-layout fixes, all of the same shape.**
Every one was a box sized by a constant that stopped being true, and none was visible from any
committed screenshot, which is why they survived to a playtest:

- Enemy names cut to "CROWN REA" — a shrink-only width cap carried over from the crowded case
  (`EnemyViewMaxWidth`, now 400 and derived from the longest name in the content).
- The act-3 title painted through the gold chip — a centred label overflowing from both ends
  (`ScreenChrome.AddTitle`, now on the same `TextFit` ladder).
- Map nodes drawn under the relic grid from the seventh relic on — a band top fixed at one row's
  clearance (`MapScreen.BandTop`, now derived from the block's measured height).
- Card names lost in the fan — centred in a banner half of which sits behind the next card, so
  "Thunderclap" read as "Thunderc". Left-aligned, a name loses its tail rather than its middle.

The generalisable lesson, and the reason this list is here rather than in a commit message: **a
constant that fits the worst case is not a constant that fits the best one.** Three of the four were
caps or margins that were correct at the crowded end and silently wrong at the empty end. The
`mapfull` screenshot fixture and `MapSmokeTest`'s block-overlap check exist so the next one of these
is caught before a playthrough finds it.

## Phase 12 — Ship readiness

Deferred by choice, not dropped. Ordered by whether it blocks a player.

- ~~**The non-16:9 layout bug**~~ — **closed by letterboxing**, not by making the screens
  responsive. `window/stretch/aspect` is now `keep`, so the canvas is 1152x648 at every window size
  and every fixed offset in the codebase is right by construction; `docs/ART_SPEC.md` §4 states it
  and `PixelSpecSmokeTest.TestCanvasIsLetterboxedNotExpanded` pins it. The accepted cost is bars on
  an odd-shaped window. **Genuinely responsive screens remain open** and are the reason this entry
  is not simply deleted: they are what would let the game *use* a 16:10 display rather than mask it,
  and that is a much larger change than the setting was. Pair it with "Resolution and windowed-size
  options" below if it is ever picked up.
- **macOS notarization.** The build is ad-hoc signed, so downloaded from anywhere it carries
  `com.apple.quarantine`, and macOS 15 removed the Control-click bypass. Blocked on an Apple
  Developer Program membership — a purchase decision, which is why it sits here rather than earlier.
  The trap: a locally built `.app` has no quarantine attribute, so this never reproduces on the
  machine that made it.
- **An in-game credits screen.** `CREDITS.md` currently ships as a file beside the binary; the CC0
  sprite attribution deserves better than that.
- **A key-rebinding UI.** The `InputMap` layer exists — every binding is a named `hd_*` action — and
  the UI does not. Gamepad support is much cheaper than it was, for the same reason.
- **Resolution and windowed-size options.** Settings has a Fullscreen toggle and nothing else.
- **Play a packaged build end to end.** Still the last unchecked item on `CLAUDE.md`'s phase bar.
  `tools/build-export.sh` proves a build boots and quits clean; nobody has played a run inside one.

## Sequencing notes

- ~~**Phase 7 before Phase 9's `?` node.**~~ Satisfied, and spent: `add_card` shipped first, so the
  `?` node landed into a vocabulary where an unknown room can cost something other than HP.
- **Phase 8 and Phase 11 can run in parallel** — one is the combat model, the other is UI widgets.
- ~~**Phase 10 after 8 and 9**~~ Satisfied, and it was the right order: the ladder's nine knobs are
  eight things earlier phases built, and it needed no new mechanical vocabulary at all. A ladder
  stacked on a thin mechanic set would only have multiplied numbers.
- **Every count in this file is measured, not remembered**, and each phase updates `CLAUDE.md`'s
  "Current state" counts and its Verification table as it lands. Those counts are written as the
  single source of truth, so they have to stay one.

## Explicitly out of scope

- **A second character or class** — see Phase 10. Risk 6.
- **Localization.** One language, no abstraction layer anywhere in the tree, every string hardcoded
  in C# or in `data/`. A post-launch decision, and a large one.
- **A combat log.** The genre does not have one because the telegraph *is* the log; a scrolling
  history would be admitting the intent display failed.
- **An end-turn confirmation prompt.** The End Turn pulse already covers the case that matters, and a
  modal would fight the drag model (risk 5).

## Known cost, accepted

Carried forward from the art direction and still true: moving to a bitmap font gave up the Cinzel /
IM Fell English illuminated-manuscript character, and pixel type will not reproduce it. The gothic
identity is recovered through palette and ornament — heavy oxblood and bronze, ornate pixel borders,
drop caps — rather than through typeface. The alternative, pixel sprites under a high-res serif UI,
is a defensible hybrid that keeps a visible seam between art and text, which is the exact thing the
art phases existed to close.
