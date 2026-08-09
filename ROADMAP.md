# Hollowdeck Roadmap

## Where things stand

The run is complete and playable end to end: three acts of branching map, telegraphed-intent combat,
relics, potions, events, a shop, mid-run save/resume, a score-driven unlock track, 13 screens on one
pixel-art spec, 20 smoke suites, CI, and packaged exports for three platforms. Content stands at 101
cards (97 offerable), 36 enemies, 27 relics, 12 potions, 15 events.

**The gating problem is no longer presentation, and it is no longer content volume. It is
mechanical vocabulary.** The previous roadmap correctly identified visual coherence as the ceiling
and spent five phases removing it; the sixth then pushed content from 33 cards to 84 and from 24
enemies to 36. What neither phase changed is the size of the vocabulary all of that is built out of,
and the content had saturated it — which is why the game read as a competent deckbuilder rather
than as this genre.

**Phase 7 closed the card half of that, and Phase 8 has since closed both of its own — the status
roster and the enemy behaviours.** The five struck rows below are done; the live ones are what the
rest of Phases 8 through 10 own.

### The diagnosis

| Layer | Today | Consequence |
| --- | --- | --- |
| ~~`CardDefinition` fields~~ | ~~8, exactly one keyword (`Exhaust`)~~ | **Closed in Phase 7** — Retain, Innate, Ethereal and the `-1` X-cost sentinel |
| ~~`EffectScope`~~ | ~~`{ Target, Self }`~~ | **Closed in Phase 7** — `AllEnemies` and `RandomEnemy` |
| ~~`EffectRegistry` actions~~ | ~~10, every one of which moves an *existing* card or moves a number~~ | **Closed in Phase 7** — `add_card` is 11 |
| ~~`CardType`~~ | ~~`{ Attack, Skill, Power }`~~ | **Closed in Phase 7** — `Status` and `Curse`, unplayable |
| ~~`StatusType`~~ | ~~11~~ | **Closed in Phase 8** — `Artifact`, `Thorns`, `Intangible`, `Plating` make 15 |
| ~~Enemy AI types~~ | ~~3, all "pick a move off a list"~~ | **Closed in Phase 8** — `summon_enemy`, `onDeath`, escape, and `wake_on_damage` make four pickers |
| `RelicDefinition` | no tier field | A boss grants from the same pool 150 gold buys from |
| `PotionDefinition` | no rarity field, no combat drop | The three-slot belt is nearly always empty |

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
  per-Power hook, and why all 27 relics are data rows driven by one factory.
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
in minions, react to its own death, leave a fight alive, and lie dormant until it is struck. What is
left is one honesty fix inside a picker that already exists.

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
- **Give `WeightedRandomIntentPicker` a run cap.** The wording this bullet carried for two phases was
  stale and worth correcting: the two-move *alternation* it described was fixed long ago, by
  disabling the anti-repeat rule below three moves. What is left is the other end of that trade — a
  two-move enemy is now unbounded i.i.d. sampling and can play the same move four times running. A
  per-move "not more than N times in a row" cap is honest at every move count, where both of the
  rules that have shipped so far are honest at one. `BalanceModel.AntiRepeatStationary` has to follow
  in the same change or the analyser silently reports the old chain.

*Proven by:* `Phase4ContentSmokeTest` (Summon, Escape and Dormant telegraphs, the wake picker's
latch, the wake landing on the player's turn and *not* mid-enemy-turn, every aiType resolving to its
own picker, no dormant move granting Block, every `summon_enemy` naming a
real enemy that does not itself summon, a summon arriving telegraphed but not acting that turn, an
escape leaving without a kill, `onDeath` firing before the fight is scored),
`CombatTargetingSmokeTest` (mid-fight `EnemyView` creation and `Instances` order, hit test skipping a
runaway as well as a corpse, the HUD clear of a full four-enemy row), `ActSmokeTest` (no summon
crossing an act), `EffectSmokeTest` (negative `gain_gold`, enemy-only actions off cards),
`BalanceSmokeTest` (a sleeper with no awake phase, and an awake phase that fails to out-damage its
dormant one).

One of the five bullets remains: the `WeightedRandomIntentPicker` run cap. It is picker-local and
shares no plumbing with what shipped.

## Phase 9 — The map, and the run's texture

- **The `?` node.** The most genre-defining map feature that is entirely absent — every node today
  renders its true type from generation onward, so the map is a plan rather than a gamble. Add
  `MapNodeType.Unknown`, resolved at *visit* time from `RngStreams.Map`. **The save implication is
  real:** the resolution has to be recorded into `RunState`, or a save/resume re-rolls the room the
  player already walked into (risk 3). Run save v3 → v4.
- **Potion drops from combat.** The three-slot belt is nearly always empty because potions are shop-
  and event-only — `RunState.Potions.Add` has exactly two call sites in the whole tree. A per-fight
  drop roll is what turns potions into a live combat resource instead of a shop line item. Ships with
  **potion rarity**, which `PotionDefinition` has no field for at all, weighted by copying
  `CardPool`'s draw-a-tier-then-an-item shape rather than reinventing it — that shape exists
  precisely so authoring more of one tier cannot silently retune the others.
- **Relic tiers.** `RelicDefinition` has no tier field, so all four grant sites (elite/boss reward,
  treasure, shop, event) draw one flat pool and a boss can hand over what 150 gold would have.
  `RelicTier { Common, Uncommon, Rare, Boss, Shop, Event }`, one pool per site.
- **The boss relic becomes a choice of three.** `CombatScreen.GrantRewardRelic` currently auto-grants
  one at random. The boss relic is where a run's identity gets decided in this genre, and being
  handed one is not the same event as choosing one. Reuses the `RewardScreen`/`CardPicker` shape.
- **A start-of-run choice.** The seam is already cut — `ScreenState.RunSetup` is declared in the enum
  and left unregistered on purpose, with `RunManager.cs:35` saying why. This is where it gets wired,
  and it is also where **seed entry** goes: the same screen, and the thing that makes Phase 10
  debuggable.
- **Map width is a judgment call, not an obvious fix.** The map is 3–4 nodes wide against the genre's
  seven, and non-scrolling because Phase 4 deliberately made it *fill* one canvas. Widening it buys
  real route planning and costs that layout. Decide it explicitly; do not drift into either.
- Smaller: a skip streak or rarity boost on card rewards, so skipping is a strategy rather than a
  shrug.

*Proven by:* `MapSmokeTest` (an Unknown resolves once, only once, and only at visit),
`RunSaveSmokeTest` (v3 → v4 tolerance, and a resolved Unknown surviving a round trip),
`ScreenSmokeTest` for the boss-relic choice and RunSetup, `EffectSmokeTest` for potion-tier weighting.

## Phase 10 — Ascension

Twenty rungs of stacking modifiers: the reason a finished run is worth repeating, and the only thing
in this document aimed squarely at replayability rather than at a single run.

- **Authored as data** — `data/ascension/ascension.json` with an
  `AscensionDefinition`/`AscensionDatabase` pair mirroring `ActDefinition`/`ActDatabase`. Twenty
  rungs must not become twenty C# classes; same argument that made relics data rows and Powers
  ordinary cards.
- **The modifier vocabulary is constrained to what already has a knob**, deliberately: enemy HP and
  damage multipliers, starting HP, act-clear heal percent, shop price multiplier, elite frequency in
  `MapGenerator`'s weights, boss HP, potion drop rate (Phase 9), and starting the run with a Curse in
  the deck (Phase 7). A rung wanting anything outside that list is asking for new plumbing, and
  should be recognised as such rather than smuggled in as content.
- **`BalanceModel` and `BalanceSmokeTest` take an ascension level.** Without it every curve assertion
  in the repo covers only rung 0 and the ladder ships unmeasured — exactly the failure the balance
  tooling was built to end.
- Meta save v2 → v3 for the highest rung reached, and `RunScore` gains an ascension multiplier so the
  ladder and the unlock track pull in the same direction rather than competing for the same run.

**One character, not four.** A second class is 60+ cards, a starter deck, new sprites and a full
rebalance of everything above — risk 6, and a different project. The ladder is where replayability
comes from here.

*Proven by:* `BalanceSmokeTest` parameterised across rungs — the curve must still rise act over act at
every rung, and no rung may make act I unwinnable at starter throughput — plus
`MetaProgressionSmokeTest` for v2 → v3 and `ActSmokeTest` for per-rung map weights.

## Phase 11 — Legibility and feel

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

- ~~**Phase 7 before Phase 9's `?` node.**~~ Satisfied: `add_card` ships, so an unknown room can now
  cost something other than HP. Phase 9's `?` node is unblocked.
- **Phase 8 and Phase 11 can run in parallel** — one is the combat model, the other is UI widgets.
- **Phase 10 after 8 and 9** (7 is done). A ladder of modifiers stacked on a thin mechanic set only multiplies
  numbers; the rungs are only interesting once there is something for them to change.
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
