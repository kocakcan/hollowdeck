# Phase 8 behaviour half — review follow-up — **resolved**

Eight findings from the `hollowdeck-review` pass on PR #34 (`feat/enemy-behaviour-vocabulary`,
commit `eff7c14`). All eight are fixed. This file is kept as the record of *what the measurements
were*, because two of them are the kind that read as something else entirely, and because the plan
this file originally carried was wrong in two places that are worth not repeating.

CI was green and all 20 suites passed on `eff7c14`, which was the point: none of it was visible to
the suite as it stood. Six new assertions now stand where the arguments were.

---

## What each item was, and what fixed it

| # | Finding | Fix |
| --- | --- | --- |
| 1 | Two act-1 **Combat** nodes cost more than every act-1 **Elite** (116 and 101 vs 77) | `ward_acolyte` 40 → 24 HP; `possessed_armor` reverted 120 → 113 |
| 2 | A solo `gaol_rat` escape stole exactly the 25 gold its node paid out — net zero | Theft 25 → 40, `displayAmount` to match |
| 3 | Nothing mapped `IntentType`/`MapNodeType` to an icon; the `intents/unknown.png` fallback did not exist | New `PixelSpecSmokeTest` sweep driving `ArtAssets` itself; `intents/unknown.png` generated (icons 184 → 185) |
| 4 | `EncounterProfile.TotalHp`/`FlatDpt` were summon-blind, so the report called `ward_acolyte` act 1's *softest* fight | `EncounterProfile.Summoned`, folded into both; `Label` names the summoned members |
| 5 | The `BossCostHigh` comment claimed 3.20x/3.23x against an actual ceiling of 2.85x | Comment rewritten against the settled numbers |
| 6 | `MaxEnemies`'s comment said `EnemyRow` is 900px — it is 800 | Both that comment and `CombatTargetingSmokeTest`'s corrected |
| 7 | `EscapeEffect` had no `IsDead` guard, so a Thorns-killed escaper would never count as a kill | Guard in `EscapeEffect`, plus a synthetic hit-and-run test |
| 8 | The truncation ellipsis rendered as a solid 19x3 bar | `text_overrun_behavior` 3 → 1 (`TrimChar`) |

Items 4 and 7 also closed two latent model/engine mismatches: `ResolveOnDeath` now passes
`turn - 1` so a death-summon opens on the round it lands (an `onDeath` fires during the *player's*
turn, before `TryEndTurn` snapshots `_enemyTurnOrder`), and `PendingSummons` is keyed by summoned id
so an enemy with two different summon moves is no longer modelled summoning whichever the first move
named.

## Where this file's own plan was wrong

**The proposed assertion did not describe a property the game has ever had.** It said "no normal
encounter may cost more than the act's *cheapest* elite". Measured against `main`, that fails in all
three acts — act 2 has eight normal groups above its cheapest elite and act 3 has nine. The hardest
pair in a normal pool overlapping the softest elite is what a *spread* is.

Measured costliest-normal against costliest-elite, `main` → after the fix:

| act | `main` | `eff7c14` | now |
| --- | --- | --- | --- |
| 1 | 1.17x | **1.51x** | 1.17x |
| 2 | 0.96x | 0.96x | 0.96x |
| 3 | 1.10x | 1.10x | 1.10x |

So the shipped assertion is the one that states a real rule with a threshold that already existed:
**no Combat node may reach `BossCostLow`.** The summoner group hit 2.42x of an average normal fight,
inside the boss band; act 1's ceiling before and after is ~2.05x. It catches the bug, it uses a
constant nobody invented to fit, and it says why a Combat node is different — the map hands it to
you on the way past, with no relic for taking it.

**Item 2 called the two-enemy node fine.** `['gaol_rat', 'rot_hound']` pays 30 against a 25 theft, so
escaping it was worth **+5 gold**, not "fine". The assertion added for this (a theft must exceed the
smallest reward any node the thief appears in can pay) catches both nodes, which is why it was worth
writing as an assertion rather than a one-off number check.

## Item 1 is the one worth remembering

`possessed_armor` falling to 0.99x was read as the armour being too cheap and answered with a 7 HP
bump. It was not the armour. The summon had pushed two Combat nodes to 101 and 116, act 1's
`MeanNormalCost` rose 42 → 48, and **every elite and boss ratio in the act deflated at once** —
`rot_hound + rot_hound` 1.48x → 1.28x, `bog_troll` 1.72x → 1.49x, both bosses 3.20x → 2.77x. Every
one of those is a moved *denominator*.

Nothing in the report or the suite could say so, because neither measured a normal encounter against
anything: `BalanceReport` printed only elites and bosses, and `BalanceSmokeTest` banded them against
the mean — the exact statistic a single spiking group disappears into. The report now prints the
costliest normal per act, and `CLAUDE.md`'s `BalanceModel` entry says to check it before moving a
number an elite row points at.

## New assertions

- `BalanceSmokeTest.{act}_no_normal_costs_boss_money` — verified to fail at 2.40x on the pre-fix content
- `BalanceSmokeTest.{act}_{move}_escape_costs_more_than_{node}` — verified to fail on the shipped 25g theft, on both nodes
- `PixelSpecSmokeTest.{intents,map}_icons_cover_every_definition` / `_have_no_orphans` — verified to fail in both directions on a removed `ArtAssets` arm
- `Phase4ContentSmokeTest.TestDeathBeatsEscapeWhenBothLandInOneMove` — verified to fail with the guard removed (`EnemiesKilled=0` for a kill the player earned)

Each was run against the broken state before being kept. An assertion that has never failed is a
comment.

## Checked and clean — do not re-litigate

The reviewer verified these and found nothing: the `_enemyTurnOrder` snapshot keeping a summon from
acting on arrival; Lose-before-Win and its synthetic `test_lethal_burst`; `IsGone` applied at every
site that needs it; `ResolveDeaths`'s `OnDeathFired` loop terminating; `SummonEnemy`'s live-roster
cap in the split case; both new intent icons being distinguishable at 1x; `EnemyDefinition.AiType`
defaulting to `sequential`; and the escape/summon telegraphs and hover prose rendering correctly.
