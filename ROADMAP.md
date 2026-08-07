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

**Phase 7 has since closed the card half of that, and Phase 8's status half is now in.** The five
struck rows below are done; the live ones are what the rest of Phases 8 through 10 own.

### The diagnosis

| Layer | Today | Consequence |
| --- | --- | --- |
| ~~`CardDefinition` fields~~ | ~~8, exactly one keyword (`Exhaust`)~~ | **Closed in Phase 7** — Retain, Innate, Ethereal and the `-1` X-cost sentinel |
| ~~`EffectScope`~~ | ~~`{ Target, Self }`~~ | **Closed in Phase 7** — `AllEnemies` and `RandomEnemy` |
| ~~`EffectRegistry` actions~~ | ~~10, every one of which moves an *existing* card or moves a number~~ | **Closed in Phase 7** — `add_card` is 11 |
| ~~`CardType`~~ | ~~`{ Attack, Skill, Power }`~~ | **Closed in Phase 7** — `Status` and `Curse`, unplayable |
| ~~`StatusType`~~ | ~~11~~ | **Closed in Phase 8** — `Artifact`, `Thorns`, `Intangible`, `Plating` make 15 |
| Enemy AI types | 3, all "pick a move off a list" | No minions, no split, no escape, no on-death |
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

The curve as of today: encounter HP scales 1.49x then 1.44x per act, incoming damage 2.10x across
the run, player max HP only 1.32x — so **deck power has to cover 1.59x**, drawn from a mean of 16.6
three-card rewards. Elites span 1.13x–1.84x of an average normal fight and bosses 2.43x–3.23x, both
asserted as bands by `BalanceSmokeTest` — which reads them from `BalanceModel.EliteCost*`/`BossCost*`
rather than holding its own copy, so the report's printed header and the suite's flags cannot
disagree.

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
never widened: all 36 are a list of moves picked by one of three pickers, and `EnemyDefinition` has
no hook for anything else. This is why 36 enemies feel like a dozen.

- **`summon_enemy`.** Lets a move bring in minions. The effect is the easy part; the real cost is
  that `CombatManager.Enemies` must grow mid-fight and `CombatScreen` must build an `EnemyView`
  mid-fight, which touches `EnemyView.Instances` ordering and the corpse-skipping hit test that
  `CombatTargetingSmokeTest` covers. Expect those assertions to move — that is the alarm working.
- **`onDeath: [EffectSpec]` on `EnemyDefinition`**, authored as data like relics and Powers rather
  than a C# class per enemy (risk 1). **Splitting falls out of this** — a split is an `onDeath` that
  summons two half-HP copies, not a mechanic of its own — and so does "on death, poison whoever
  killed me".
- **Escape.** A fifth `IntentType` and a move that removes an enemy *alive*, granting no reward. A
  thief that leaves with your gold is a fight you can lose by playing correctly but slowly, which is
  a shape the game currently cannot express.
- **A fourth AI type, `wake_on_damage`** — structurally `PhaseThresholdIntentPicker` inverted
  (transition on damage taken rather than an HP threshold), reusing that file's shape the way
  `BlockMath` reuses `DamageMath`'s.
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
- **Fix the two-move `WeightedRandomIntentPicker` collapse.** Its anti-repeat rule only engages at
  three or more moves, so a two-move enemy strictly alternates and its authored `Weight` values are a
  lie the content cannot see. Replace it with a per-move "not more than N times in a row" cap, which
  is honest at every move count. `BalanceModel`'s stationary-distribution correction has to follow in
  the same change or the analyser silently reports the old chain.

*Proven by:* `Phase4ContentSmokeTest` (every intent still telegraphs what it resolves, now including
summons and escapes), `CombatTargetingSmokeTest` (mid-fight `EnemyView` creation, hit test with a
summon appended). Note that summons change `EncounterCost`, so `BalanceSmokeTest`'s act bands need
**re-measuring, not re-asserting** — as the status half already did.

The status bullet above is done; the five behaviour bullets are what remains of this phase.

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

## Phase 12 — Ship readiness

Deferred by choice, not dropped. Ordered by whether it blocks a player.

- **The non-16:9 layout bug**, filed during the export work and still open.
  `window/stretch/aspect="expand"` grows the canvas past 1152 units, while `ScreenChrome` centres a
  fixed 1152-wide panel and `MapScreen.cs:135` lays out against a literal `1152f`. Identical at any
  16:9 size, so it is invisible on the developer's display and appears the first time anyone
  maximises on a 16:10 laptop.
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
