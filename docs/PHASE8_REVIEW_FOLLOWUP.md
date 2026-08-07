# Phase 8 behaviour half — review follow-up

Eight findings from the `hollowdeck-review` pass on PR #34
(`feat/enemy-behaviour-vocabulary`, commit `eff7c14`). CI is green on that PR and all 20 suites
pass, which is the point: **none of this is visible to the suite as it stands.** Two of the eight
are missing assertions, and the rest are things the assertions were never asked about.

Ordered by severity. Each item states what is wrong, how it was established, and the fix. Work top
down — items 1, 2 and 4 interact (all three move `BalanceModel` numbers), so re-measure once at the
end rather than after each.

**Do not tune anything by eye.** `tools/balance-report.sh` on this branch and on `main` is what
established the numbers below, and act 2 and act 3 costs are currently byte-identical to `main` —
that identity is the control, and any fix that disturbs it has done something it did not intend.

---

## 1. Two act-1 *normal* encounters now cost more than every act-1 *elite*

**Severity: highest. This is a live difficulty bug, not a modelling one.**

`data/enemies/enemies.json` — `ward_acolyte`'s `call_the_faithful`.

Per-normal-group `EncounterCost`, `main` → this branch:

| group | main | branch |
| --- | --- | --- |
| `ward_acolyte` | 11.0 | **29.9** |
| `ward_acolyte + rot_hound` | 52.1 | **101.0** |
| `cultist + ward_acolyte` | 63.0 | **116.4** |

Act-1 elites on this branch cost 56 / 62 / 72 / 77 and the bosses 134 / 135. So
`cultist + ward_acolyte` — a **Combat** node — is 51% more expensive than `drowned_matron`, the
act's costliest **Elite**, and 86% of a boss fight. Concretely: 48 + 40 HP plus a 32 HP Acid Slime
from turn 1, three attackers splitting a starter deck's throughput, against 50 max HP.

This is also the *actual* cause of the `possessed_armor` 113 → 120 HP change already in the branch.
The act's mean normal cost rose 42 → 48 almost entirely because of these three groups (zeroing the
summon's `amount` drops the mean to 40), which deflated every elite and boss ratio in the act —
`rot_hound + rot_hound` 1.48x → 1.28x, `bog_troll` 1.72x → 1.49x, both bosses 3.20x → 2.77x.
`possessed_armor` fell to 0.99x for that reason and the HP bump treated the symptom. **Revisit that
bump once the denominator is fixed** — it may want reverting to 113.

### Fix

The summoned slime is 32 HP against the acolyte's own 40, so it roughly doubles every group it is
in. Two candidate directions, in preference order:

1. **Drop `ward_acolyte`'s own HP** (40 → ~24) so acolyte + slime lands near the old acolyte alone.
   Reads as the genre's fragile summoner hiding behind minions, scales all three groups
   proportionally, and keeps the mechanic exercised in every encounter it appears in.
2. Restrict the summoner to solo encounters — remove `ward_acolyte` from
   `['ward_acolyte', 'rot_hound']` and `['cultist', 'ward_acolyte']` in `data/acts/acts.json`.
   Costs act-1 group variety, and `ActSmokeTest` requires ≥6 distinct normals per act (currently
   satisfied with room, but check).

Measure option 1 first. Target: no normal group above the act's *cheapest* elite, per item 4's new
assertion.

## 2. A solo `gaol_rat` escape costs the player exactly nothing

`data/enemies/enemies.json` — `snatch_and_flee`; `scripts/ui/MapScreen.cs:366`.

Act 1 authors `normalGoldBase: 20`, `goldPerEnemy: 5`, and `MapScreen` computes
`GoldReward = NormalGoldBase + node.EnemyIds.Count * GoldPerEnemy`. The `['gaol_rat']` node has one
enemy, so its reward is **25 gold**. `snatch_and_flee` steals **25 gold**. The escape empties
`Enemies`, the fight scores as a Win, and `CombatScreen.OnContinuePressed` hands over the full 25
gold plus the three-card reward.

Net gold change: **zero.** The player also takes less damage than finishing the fight would have
cost and skips the rat's 44 HP. The only price is 2 `RunScore` points.

`EncounterCost` prices solo `gaol_rat` at 12.0 on both branches — identical — because at the 16.2
reference throughput it dies on turn 3 and never reaches the turn-4 escape. **That half is working
as designed**: the escape only fires for a below-reference deck. The bug is that it then *rescues*
that deck. ROADMAP's stated intent — "a fight you can lose by playing correctly but slowly" — is
inverted.

The two-enemy `['gaol_rat', 'rot_hound']` node is fine (reward 30 vs 25 stolen), and its cost
actually fell 67.0 → 46.0.

### Fix

Raise the theft above the node's own reward so escaping is a net loss: **25 → 40**. Leaves the solo
node at −15 net and the two-enemy node at −10 plus a rot_hound still to fight. `GainGoldEffect`
already clamps at zero, so a broke player is not driven negative.

Then update the authored `intent.displayAmount` to match (the telegraph sweep compares on
magnitude) and re-check the `-40g` label still fits the intent row at four enemies.

Consider also asserting in `BalanceSmokeTest` that an escape move's theft exceeds the smallest gold
reward any node it appears in can pay — that is the assertion whose absence let this through.

## 3. Nothing maps `IntentType` to an intent icon; a seventh type would ship as a blank telegraph

`scripts/ui/ArtAssets.cs:38`, `scripts/debug/PixelSpecSmokeTest.cs`.

Independently confirmed: `grep -n intents scripts/debug/PixelSpecSmokeTest.cs` returns exactly one
hit, and it is a comment. `TestEveryDefinitionHasAnIcon` sweeps `cards`, `relics`, `potions`,
`events`, `status` (enumerated straight off `Enum.GetValues<StatusType>()`) and `enemy_sprites` —
**not `intents`**, and not `map` either.

`ArtAssets.IntentIcon`'s `_ => "unknown"` fallback resolves to
`res://assets/icons/intents/unknown.png`, which **does not exist** (confirmed: the directory holds
only the six real icons; `assets/icons/map/unknown.png` *does* exist, which is what makes this easy
to assume away). `Load` returns null, `EnemyView.Refresh` sets `_intentIcon.Visible = false`, and
the telegraph renders as a bare label with no icon — with every suite green.

This branch added two intent types by hand and got away with it. The next two roadmap items both
walk into it: Phase 8's `wake_on_damage` and Phase 9's `MapNodeType.Unknown`.

### Fix

Extend `PixelSpecSmokeTest.TestEveryDefinitionHasAnIcon` with `intents` and `map` arms enumerated
off `Enum.GetValues<IntentType>()` / `Enum.GetValues<MapNodeType>()`, the way the `status` arm
already does — that arm's own comment names this exact failure shape ("a new StatusType ships
looking broken rather than not at all"). Both directions, as the other categories do: a type with
no icon, and an icon with no type.

## 4. `EncounterProfile.TotalHp`/`FlatDpt` are summon-blind, so the report contradicts itself

`scripts/debug/BalanceModel.cs` — `Encounter()`; `scripts/debug/BalanceReport.cs`.

`Encounter()` builds `TotalHp = defs.Sum(d => d.MaxHp)` and `FlatDpt = defs.Sum(FlatDpt)` from the
authored id list alone, while `Cost` now walks a roster that grows. In one printed report:

- `SPREAD WITHIN A POOL` names **`ward_acolyte` as act 1's *softest* fight, "2.8 dpt, 40 hp"**,
  while `EncounterCost` for that group went 11.0 → 29.9 and the fight actually carries 72 HP and a
  second attacker from turn 1.
- `THE CURVE`'s `enc HP` for act 1 stays 66 and `kill` stays 4.0 turns.

`BalanceSmokeTest.TestActCurveRises` (`MeanNormalHp`, `MeanNormalDpt`) and
`TestBossesOutweighTheirAct` (`MeanEliteHp > MeanNormalHp`) all read the summon-blind numbers, so
those curve assertions cover a fight the game no longer runs. Neither `BalanceReport.cs` nor
`BalanceSmokeTest.cs` was touched by this branch.

### Fix

Give `EncounterProfile` a `Summoned` list — walk each def's moves and `OnDeath` for `summon_enemy`,
capped at `CombatManager.MaxEnemies` total — and fold it into `TotalHp` and `FlatDpt`. Keep `Ids` as
authored so a report row still matches its `acts.json` entry, and have `Label` say which members
were summoned rather than silently absorbing them.

**Add the assertion item 1 needed and nobody had:** no normal encounter may cost more than the act's
*cheapest* elite. `TestEncounterCostsStayInTheirBand` only bands elites and bosses against the
*mean* normal, so a single spiking normal group is invisible to it, and `BalanceReport` prints only
elites and bosses per act — so the two numbers in item 1 appear nowhere in the report either. Print
the costliest normal group per act while you are in there.

Expect `TestActCurveRises` and `TestBossesOutweighTheirAct` to move once act 1's means include the
summon. **Re-measure them; do not re-assert them.**

### Two model/engine mismatches in the same method, both currently latent

- `ResolveOnDeath` gives a summon `joinedOnTurn: turn`, so it never acts on the turn it lands. In
  `CombatManager` an `onDeath` fires from `ResolveCard` during the **player's** turn, and
  `TryEndTurn` snapshots `_enemyTurnOrder` *after* that — so a death-summon *does* act that same
  round. Under-prices splitting, which is the stated next content use of `onDeath`.
- `SummonedDef(EnemyDefinition)` ignores which move produced the summon and returns the first
  `summon_enemy` spec found across all moves, while `SummonCount(move)` reads the specific move. An
  enemy with two different summon moves is modelled summoning the wrong creature.

## 5. The `BossCostHigh` comment misstates the model by 15%

`scripts/debug/BalanceModel.cs` — above `EliteCostLow`/`BossCostHigh`.

The comment claims *"The boss ceiling moved 3.2 -> 3.3 in Phase 8 … it prices them at 3.20x and
3.23x."* On this branch those two act-1 bosses price at **2.77x and 2.79x**, and the highest boss in
the game is 2.85x (`the_slag_maw`) — so `BossCostHigh = 3.3` now carries 0.45 of unreachable band
and no longer catches the drift it was calibrated to catch.

`ROADMAP.md`'s "curve as of today" paragraph *was* updated in this branch to "bosses 2.43x–2.85x";
this constant's own comment was not, and it is the single copy both the report header and the suite
flags read.

### Fix

Rewrite the comment against the numbers after items 1 and 4 land, and decide explicitly whether to
tighten `BossCostHigh`. Note that item 1's fix will *raise* these ratios again by lowering the
denominator, so this is the last thing to touch.

## 6. `MaxEnemies`'s justifying comment contradicts the scene, `CombatScreen` and `CLAUDE.md`

`scripts/combat/CombatManager.cs` — the `MaxEnemies` comment.

> `// EnemyRow is 900px wide and an EnemyView has a 220px minimum, so four fit`
> `// and five overflow the band into the relic bar.`

`scenes/CombatScreen.tscn` puts `EnemyRow` at `offset_left = 176.0`, `offset_right = 976.0` —
**800px**, not 900 — and four 220px views need 892px plus separation, so four do **not** fit at the
authored minimum. That is exactly why `FitEnemiesToTheRow` exists. `CombatScreen.cs`'s own comment
and `CLAUDE.md`'s new paragraph both say 800 correctly; only the comment defining the constant is
wrong, and it is wrong in the direction that makes `MaxEnemies = 5` look safe.

Straightforward provenance: the comment was written while the row *was* widened to 1076 (900 wide),
and the widening was reverted when `DeckViewSmokeTest` caught the pile-counter overlap. The comment
was not.

Same cause, same branch: `CombatTargetingSmokeTest`'s new comment claims the fourth column "pushed
`EnemyRow`'s right edge out to 1076". It did not; the edge is still 976.

### Fix

Correct both comments to 800px and to "four fit because `FitEnemiesToTheRow` narrows them, not
because the band is wide enough".

## 7. `EscapeEffect` has no liveness guard, and `ResolveDeaths` strips escaped *before* dead

`scripts/effects/EscapeEffect.cs`, `scripts/combat/CombatManager.cs`:

```csharp
Enemies.RemoveAll(e => e.HasEscaped);
EnemiesKilled += Enemies.RemoveAll(e => e.IsDead);
```

`EscapeEffect` sets `HasEscaped` unconditionally without checking `IsDead`. Author an escape move
that also deals damage (`deal_damage` then `escape` — the obvious hit-and-run thief) and give the
player `Thorns` from `bramble_mail`/`bramble_guard` or the `thorned_carapace` relic: the retaliation
resolves inside `ExecuteEffect` for the damage spec and kills the enemy, then the *next* spec in the
same move sets `HasEscaped`. `ResolveDeaths` fires the `onDeath`, then the `HasEscaped` sweep
removes it first — **`EnemiesKilled` is never incremented for an enemy the player killed**,
`RunScore` loses the points, and `CombatScreen` plays the escape tween. Which is the exact inversion
of the comment four lines above it ("Playing the death tween for a runaway would tell the player
they got a kill they did not get").

Not reachable with today's content — `snatch_and_flee` deals no damage — so this is latent.

### Fix

Death wins over escape. Either guard `EscapeEffect` on `IsDead`, or swap the two `RemoveAll` lines
so the kill tally claims the body first. Prefer the guard: it states the rule where the flag is set,
rather than leaving it as an ordering the next edit can silently reverse. Cover it with a
`CombatTargetingSmokeTest` or `Phase4ContentSmokeTest` case using a synthetic
damage-then-escape move, the way the lethal-`onDeath` case is already done.

## 8. A truncated enemy name renders as a solid bar, not an ellipsis

`scenes/EnemyView.tscn` — `NameLabel`'s `text_overrun_behavior = 3`.

Independently confirmed by cropping the `combatsummon` shot at 5x: at the derived 197px width,
"WARD ACO" is followed by a solid 19x3 bar. At 1x it reads as an underscore or a redaction, not as
"there is more name here" — the pixel font has no usable ellipsis glyph at this size. Visible in
every four-enemy fight, i.e. every `ward_acolyte` group after turn 1.

### Fix

`TrimChar` (`text_overrun_behavior = 1`): a cleanly cut name reads as a layout limit, where a solid
bar reads as a corrupt glyph. Font scaling is not an option — `docs/ART_SPEC.md` allows integer
scale only.

Worth knowing before over-engineering it: four-enemy fights only occur via a summon, only
`ward_acolyte` summons, and the only names at risk today are "Ward Acolyte" and "Acid Slime".

---

## Checked and clean — do not re-litigate

The reviewer verified these and found nothing:

- the `_enemyTurnOrder` snapshot correctly keeps a summon from acting on arrival, and
  `Phase4ContentSmokeTest` pins it with a check that can genuinely fail (the slime would deal 3–5
  damage on a turn the acolyte deals 0)
- Lose-before-Win is real, and the synthetic `test_lethal_burst` proves it
- `IsGone` is applied at every site that needs it (`Opposition`/`ResolveTargets` are unfiltered, but
  the settle pass always runs before the player can act again)
- `ResolveDeaths`'s `OnDeathFired` loop terminates
- `SummonEnemy`'s live-roster cap is correct for the split case
- both new intent icons are distinguishable at 1x from the existing four
- `EnemyDefinition.AiType` defaults to `sequential`, so the synthetic test definition logs no error
- the escape and summon telegraphs (`-25g`, `Acid Slime`) and the hover prose ("Steals 25 Gold.
  Flees the fight.") all render correctly

## Suggested order

1. **6** and **5**'s comment half, and **8** — comment and one-property fixes, no measurement.
2. **7** — guard plus a test.
3. **3** — the icon coverage sweep. Independent of everything else.
4. **4** — the model fix and the new "no normal above the cheapest elite" assertion, which is what
   makes item 1 checkable rather than argued.
5. **1** then **2** — content, measured against the assertion from step 4.
6. Re-run `tools/balance-report.sh`, revisit `possessed_armor`'s 120 HP, settle **5**'s band
   decision, and update `ROADMAP.md`'s curve paragraph and `CLAUDE.md`'s counts if anything moved.
7. Full sweep, plus a `verify-screen` pass on `combatsummon` for items 1 and 8.
