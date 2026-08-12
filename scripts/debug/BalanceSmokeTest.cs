using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// The pass/fail half of the balance tooling. BalanceReport prints the whole
// curve for a human to read; this asserts the parts of it that are invariants
// rather than tuning, so a content edit that breaks one fails a build instead
// of waiting to be noticed in a playthrough.
//
// What is deliberately NOT asserted: the anomalies the report exists to show.
// Elites hitting softer per turn than normal encounters, and the flat boss
// enrage curve, are real problems - but pinning them either lands the suite
// red on a known-open item or forces a bound so loose it could never fail,
// and an assertion that cannot fail is worse than no assertion. Those get
// reported and retuned; these get asserted.
//
// No scene changes and no persistence, so this needs neither RunSaveGuard nor
// HardCutGuard.
public partial class BalanceSmokeTest : Node
{
    // Enough seeds for the reachability sample to be stable run to run, few
    // enough to stay far inside the 90s suite watchdog (the whole sweep here
    // is graph walks and arithmetic - no combat, no frames).
    private const int Seeds = 200;

    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        BlessingDatabase.LoadAll();

        TestTheModelAgreesWithThePicker();
        TestEveryEnemyCanFight();
        TestActCurveRises();
        TestEnrageIsAnEscalation();
        TestWakingIsAnEscalation();
        TestBossesOutweighTheirAct();
        TestEncounterCostsStayInTheirBand();
        TestAnEscapeCostsMoreThanTheNodeItLeaves();
        TestScoreThresholdsAreReachable();
        TestPotionDropYield();
        TestSkipStreakIsWorthTheCardsItCosts();
        TestBlessingsStayInTheirBand();
        TestUpgradeGrantsAreWhatTheDocsClaim();

        GD.Print($"BalanceSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // Every damage number in this file rests on BalanceModel's claim about what
    // an enemy does on an average turn, and for weighted_random that claim is a
    // solved Markov chain rather than a reading of the weights - the picker's
    // run cap makes it one. Two hand-written mirrors of one rule is the seam
    // this codebase actually produces: change the cap in the picker alone and
    // every suite here stays green while the whole curve is measured against a
    // chain the game no longer plays. So drive the real picker and hold the
    // frequencies against the model.
    //
    // 20k samples puts the standard error near 0.0035, so a 2pt tolerance is
    // ~6 sigma of sampling noise and comfortably inside the smallest real
    // disagreement (the cap moves slime's 60/40 by 6.7pt).
    private void TestTheModelAgreesWithThePicker()
    {
        const int samples = 20000;
        const double tolerance = 0.02;

        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All.Where(d => d.AiType == "weighted_random"))
        {
            var picker = new WeightedRandomIntentPicker();
            var enemy = new EnemyCombatant { Name = def.Name, Definition = def };

            var counts = new Dictionary<string, int>();
            for (int i = 0; i < samples; i++)
            {
                string id = picker.PickNext(enemy).MoveId;
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }

            foreach (var (move, p) in BalanceModel.MoveDistribution(def))
            {
                double observed = counts.GetValueOrDefault(move.MoveId) / (double)samples;
                if (System.Math.Abs(observed - p) > tolerance)
                    problems.Add($"{def.Id}:{move.MoveId} model {p:F3} vs picker {observed:F3}");
            }
        }

        Check("balance_model_agrees_with_the_picker", problems.Count == 0, Join(problems));
    }

    // An enemy that never deals damage, or an encounter group that sums to no
    // HP, is an authoring slip that no other suite would see: ActSmokeTest
    // checks the ids resolve, Phase4ContentSmokeTest checks the telegraph
    // matches the effects. Neither notices a moveset that cannot threaten
    // anyone.
    private void TestEveryEnemyCanFight()
    {
        var toothless = EnemyDatabase.All
            .Where(d => BalanceModel.Profile(d) is { FlatDpt: <= 0, EnrageFlatDpt: <= 0 })
            .Select(d => d.Id)
            .ToList();
        Check("every_enemy_deals_damage", toothless.Count == 0, $"never attack: {Join(toothless)}");

        var broken = new List<string>();
        foreach (var act in ActDatabase.All)
        {
            foreach (var group in act.NormalEncounters.Concat(act.EliteEncounters))
            {
                var e = BalanceModel.Encounter(group);
                if (e.TotalHp <= 0 || e.FlatDpt <= 0) broken.Add($"{act.Id}:{e.Label}");
            }
        }
        Check("every_encounter_has_hp_and_damage", broken.Count == 0, Join(broken));
    }

    // The whole point of three acts. Both halves have to rise: HP alone makes
    // a longer fight, damage alone makes a swingier one.
    private void TestActCurveRises()
    {
        var acts = BalanceModel.AllActs();

        for (int i = 1; i < acts.Count; i++)
        {
            var prev = acts[i - 1];
            var act = acts[i];

            Check($"act{i + 1}_normal_hp_above_act{i}",
                act.MeanNormalHp > prev.MeanNormalHp,
                $"{act.MeanNormalHp:F0} vs {prev.MeanNormalHp:F0}");
            Check($"act{i + 1}_normal_dpt_above_act{i}",
                act.MeanNormalDpt > prev.MeanNormalDpt,
                $"{act.MeanNormalDpt:F1} vs {prev.MeanNormalDpt:F1}");
            Check($"act{i + 1}_elite_hp_above_act{i}",
                act.MeanEliteHp > prev.MeanEliteHp,
                $"{act.MeanEliteHp:F0} vs {prev.MeanEliteHp:F0}");
            Check($"act{i + 1}_elite_dpt_above_act{i}",
                act.MeanEliteDpt > prev.MeanEliteDpt,
                $"{act.MeanEliteDpt:F1} vs {prev.MeanEliteDpt:F1}");
        }
    }

    // An enrage phase that does not hit harder than the phase before it is a
    // content bug wearing a mechanic's name: the player watches the boss drop
    // below the threshold, braces, and nothing happens. This is the one thing
    // PhaseThresholdIntentPicker cannot check for itself - it swaps move lists
    // without any opinion about what is in them.
    private void TestEnrageIsAnEscalation()
    {
        var enraging = EnemyDatabase.All
            .Select(BalanceModel.Profile)
            .Where(p => p.HasEnrage)
            .ToList();

        Check("enrage_phases_exist", enraging.Count > 0, "no enemy has an enrage phase");

        var weak = enraging.Where(p => p.EnrageFlatDpt <= p.FlatDpt).ToList();
        Check("every_enrage_hits_harder_than_its_normal_phase", weak.Count == 0,
            Join(weak.Select(p => $"{p.Id} {p.FlatDpt:F1} -> {p.EnrageFlatDpt:F1}")));

        // The other half of the same trap: a phase_threshold enemy authored
        // with no EnrageMoves silently never changes behaviour at all.
        var empty = EnemyDatabase.All
            .Where(d => d.AiType == "phase_threshold" && d.EnrageMoves.Count == 0)
            .Select(d => d.Id)
            .ToList();
        Check("no_phase_threshold_enemy_lacks_enrage_moves", empty.Count == 0, Join(empty));
    }

    // The same two traps, one trigger along. A sleeper shares EnrageMoves with
    // the enrage picker, so it inherits both failure modes and neither of the
    // checks above sees it: HasEnrage gates on EnrageHpPercent, which a
    // wake_on_damage enemy leaves at 0.
    //
    // The escalation half means something slightly different here and is worth
    // more, not less. An enrage that fails to escalate is an anticlimax; a
    // sleeper whose awake phase does not out-damage its dormant one is a fight
    // the player is actively rewarded for never touching, which the win
    // condition then forces them to touch anyway.
    private void TestWakingIsAnEscalation()
    {
        var sleepers = EnemyDatabase.All.Where(d => d.AiType == "wake_on_damage").ToList();
        Check("sleepers_exist", sleepers.Count > 0, "no enemy uses wake_on_damage");

        var empty = sleepers.Where(d => d.EnrageMoves.Count == 0).Select(d => d.Id).ToList();
        Check("no_sleeper_lacks_an_awake_phase", empty.Count == 0,
            Join(empty) + " - with no EnrageMoves a sleeper never wakes at all");

        // Dormant damage read off the dormant list directly: BalanceModel.Profile
        // steady-states a sleeper on its *awake* moves, which is the right
        // reading everywhere else and would compare a number against itself here.
        var weak = sleepers
            .Where(d => d.EnrageMoves.Count > 0)
            .Select(d => (d.Id,
                Dormant: BalanceModel.MeanMoveDamage(d.Moves),
                Awake: BalanceModel.MeanMoveDamage(d.EnrageMoves)))
            .Where(x => x.Awake <= x.Dormant)
            .ToList();
        Check("every_awake_phase_hits_harder_than_its_dormant_one", weak.Count == 0,
            Join(weak.Select(x => $"{x.Id} {x.Dormant:F1} -> {x.Awake:F1}")));
    }

    // A boss the act's own elites out-stat is not a climax. Compared against
    // the toughest elite group rather than the mean, because the player meets
    // one specific elite, not an average of them.
    private void TestBossesOutweighTheirAct()
    {
        foreach (var act in BalanceModel.AllActs())
        {
            int hardestElite = act.Elites.Max(e => e.TotalHp);
            var weakest = act.Bosses.OrderBy(b => b.MaxHp).First();

            Check($"{act.Act.Id}_boss_hp_above_hardest_elite",
                weakest.MaxHp > hardestElite,
                $"{weakest.Id} {weakest.MaxHp} vs elite group {hardestElite}");

            Check($"{act.Act.Id}_elites_tougher_than_normals",
                act.MeanEliteHp > act.MeanNormalHp,
                $"{act.MeanEliteHp:F0} vs {act.MeanNormalHp:F0}");
        }
    }

    // The node types have to mean something. An Elite node and a Combat node
    // look identical on the map apart from the icon, so what they cost the
    // player is the only thing distinguishing them - and before the retune one
    // act's elite pool ranged from 0.58x to 2.55x the cost of an average
    // normal fight, which is not a difficulty tier, it is a coin flip.
    //
    // Bands rather than exact values, because these are tuning decisions and
    // the point is to catch a *drift out of the tier*, not to freeze numbers.
    // Cost is compared within an act only - see BalanceModel.EncounterCost.
    //
    // The numbers live on BalanceModel so this suite and the report that prints
    // them cannot disagree about what is in tier.
    private void TestEncounterCostsStayInTheirBand()
    {
        const double eliteLow = BalanceModel.EliteCostLow, eliteHigh = BalanceModel.EliteCostHigh;
        const double bossLow = BalanceModel.BossCostLow, bossHigh = BalanceModel.BossCostHigh;

        foreach (var act in BalanceModel.AllActs())
        {
            Check($"{act.Act.Id}_normal_fights_cost_something", act.MeanNormalCost > 0,
                "an average normal fight deals no damage at all");

            foreach (var elite in act.Elites)
            {
                double r = act.CostRatio(elite);
                Check($"{act.Act.Id}_elite_in_band_{Slug(elite.Label)}",
                    r >= eliteLow && r <= eliteHigh,
                    $"{elite.Label} costs {r:F2}x an average normal fight, outside {eliteLow}-{eliteHigh}x");
            }

            foreach (var boss in act.BossEncounters)
            {
                double r = act.CostRatio(boss);
                Check($"{act.Act.Id}_boss_in_band_{Slug(boss.Label)}",
                    r >= bossLow && r <= bossHigh,
                    $"{boss.Label} costs {r:F2}x an average normal fight, outside {bossLow}-{bossHigh}x");
            }

            // The tier ordering itself, which the bands imply but do not state:
            // no elite may be cheaper than the average normal fight it sits
            // beside, and no boss cheaper than the act's costliest elite.
            var dearestElite = act.Elites.Max(e => e.Cost);
            Check($"{act.Act.Id}_every_boss_outweighs_every_elite",
                act.BossEncounters.All(b => b.Cost > dearestElite),
                $"costliest elite is {dearestElite:F0}, cheapest boss "
                + $"{act.BossEncounters.Min(b => b.Cost):F0}");

            // The end of that ordering the bands above genuinely cannot see.
            // They measure each *elite* against the *mean* normal, so a single
            // Combat node spiking past the whole elite pool averages away to
            // nothing: the four cheap groups beside it pull the mean back and
            // every check in this file stays green.
            //
            // Not hypothetical. Phase 8's summoner took two act-1 Combat nodes
            // to 101 and 116 against a costliest elite of 77, and the suite
            // said nothing - the elite rows just quietly got cheaper as the
            // denominator moved under them.
            //
            // BossCostLow rather than a threshold invented for this check, and
            // not "no normal above any elite" either. Measured: the costliest
            // normal has always run a little past the costliest elite (act 1
            // 1.17x, act 3 1.10x, both predating Phase 8), because the hardest
            // pair in a normal pool overlapping the softest elite is what a
            // spread *is*. What is never acceptable is a Combat node - the one
            // the map hands you on the way past, with no relic for it - costing
            // what a Boss node promises. The summoner group hit 2.42x, inside
            // the boss band; the act's own ceiling before and after is ~2.05x.
            var bossPriced = act.Normals.Where(n => act.CostRatio(n) >= bossLow).ToList();
            Check($"{act.Act.Id}_no_normal_costs_boss_money",
                bossPriced.Count == 0,
                $"a Boss node starts at {bossLow}x an average normal fight; these Combat "
                + "nodes reach it: "
                + Join(bossPriced.OrderByDescending(n => n.Cost)
                    .Select(n => $"{n.Label} {n.Cost:F0} ({act.CostRatio(n):F2}x)")));
        }
    }

    // An escape has to be a loss, and nothing was checking that it was.
    //
    // Emptying the board scores as a Win however it happened, so a fight the
    // player failed to finish still pays out its node's full gold and card
    // reward. The only price is the theft on the way out - so if a thief steals
    // less than the node pays, escaping is *profit*: the same reward, less
    // damage taken, and the enemy's HP never had to be chewed through.
    //
    // Shipped exactly that. Act 1 pays 20 + 5/enemy, so the solo ['gaol_rat']
    // node pays 25 and snatch_and_flee stole 25 - a net gold change of zero.
    // Worse, EncounterCost is right that the escape only ever fires against a
    // *below*-reference deck (a deck at 16.2 dpt kills the rat on turn 3 and
    // never sees turn 4), so the move existed solely to rescue the deck it was
    // written to punish. ROADMAP's stated intent - "a fight you can lose by
    // playing correctly but slowly" - ran exactly backwards.
    //
    // Checked against the *smallest* reward any node the thief appears in can
    // pay, because that is the node where the margin is thinnest.
    private void TestAnEscapeCostsMoreThanTheNodeItLeaves()
    {
        foreach (var act in ActDatabase.All)
        {
            foreach (var group in act.NormalEncounters)
            {
                int reward = act.NormalGoldBase + group.Count * act.GoldPerEnemy;

                foreach (var id in group)
                {
                    var def = EnemyDatabase.Get(id);
                    foreach (var move in def.Moves.Concat(def.EnrageMoves))
                    {
                        if (move.Effects.All(e => e.Action != "escape")) continue;

                        // Authored negative and shown positive - EnemyView
                        // .StolenGold is the one place that sign is turned back
                        // around, so read it the same way here.
                        int stolen = -move.Effects
                            .Where(e => e.Action == "gain_gold" && e.Amount < 0)
                            .Sum(e => e.Amount);

                        Check($"{act.Id}_{move.MoveId}_escape_costs_more_than_{Slug(string.Join(" + ", group))}",
                            stolen > reward,
                            $"{def.Id}'s {move.MoveId} steals {stolen} from a node that pays "
                            + $"{reward} - escaping is worth {reward - stolen:+#;-#;0} gold to the "
                            + "player, so the fight they failed to finish rewards them for it");
                    }
                }
            }
        }
    }

    // A stable, filename-safe check name, so a renamed encounter shows up as a
    // renamed check rather than silently matching an old one.
    private static string Slug(string label) =>
        label.Replace(" + ", "_and_").Replace(" ", "_").ToLowerInvariant();

    // The general form of the Mystery Machine bug. A threshold no seed can
    // reach is not a hard category, it is points that silently never award -
    // and nothing in the game says so, because RunScore just omits the row.
    //
    // The bar is deliberately "some seed", not "most seeds": how *hard* a
    // category should be is a design call, but zero is always wrong. The
    // report prints the share of seeds for tuning it.
    private void TestScoreThresholdsAreReachable()
    {
        var reach = BalanceModel.Reachable(Seeds);
        var deckTiers = BalanceModel.DeckSizeThresholds();
        var goldTiers = BalanceModel.GoldThresholds();

        // Every threshold is read out of RunScore rather than repeated here.
        // A copy would let this suite go on passing against numbers the game
        // no longer uses, which is the exact failure the file is about.
        Check("score_tier_tables_are_readable", deckTiers.Count == 2 && goldTiers.Count == 3,
            $"deck tiers {deckTiers.Count}, gold tiers {goldTiers.Count}");

        var categories = new List<(string Label, int Needs, BalanceModel.Metric Metric)>
        {
            ("i_like_shiny", RunScore.ShinyRelics, reach.Relics),
            ("mystery_machine", RunScore.MysteryRooms, reach.EventRooms),
        };
        categories.AddRange(goldTiers.Select((t, i) => ($"gold_tier_{i}", t, reach.Gold)));
        categories.AddRange(deckTiers.Select((t, i) => ($"deck_tier_{i}", t, reach.DeckSize)));

        foreach (var (label, needs, metric) in categories)
        {
            Check($"score_{label}_is_reachable", metric.Best >= needs,
                $"needs {needs}, best over {Seeds} seeds is {metric.Best}");
        }
    }

    // The potion drop yield. Potions were shop- and event-only until Phase 9,
    // which is why the three-slot belt was almost always empty and why this
    // number is worth a band rather than just a printed line.
    private void TestPotionDropYield()
    {
        // A boss is the one fight type that never rolls, and it is expressed as
        // the default arm of a switch - so it is pinned here rather than left
        // to a reader trusting `_ => 0`. Driven over every act, since the arm
        // is per-act data.
        var bossRolls = ActDatabase.All
            .Where(act => BalanceModel.NodePotionPercent(
                new MapNode { Type = MapNodeType.Boss }, act) != 0)
            .Select(act => act.Id)
            .ToList();
        Check("a_boss_never_drops_a_potion", bossRolls.Count == 0, Join(bossRolls));

        // And the rooms that do roll actually do, per act - the mirror of
        // ActSmokeTest's authoring check, one layer down, so a rate that landed
        // in acts.json but never reached the model still fails something.
        var silentActs = ActDatabase.All
            .Where(act => BalanceModel.NodePotionPercent(new MapNode { Type = MapNodeType.Combat }, act) <= 0
                || BalanceModel.NodePotionPercent(new MapNode { Type = MapNodeType.Elite }, act) <= 0)
            .Select(act => act.Id)
            .ToList();
        Check("fights_actually_roll_for_a_potion_in_every_act", silentActs.Count == 0, Join(silentActs));

        // The yield itself, banded at both ends because both ends are real
        // failures. Below one drop a run the belt is still empty and the whole
        // item did nothing; above two beltfuls every extra drop arrives at a
        // full belt and ships as a greyed-out row, which reads as broken rather
        // than as generous.
        double drops = BalanceModel.SampleRuns(Seeds)["potionpct"] / 100.0;
        Check("expected_potion_drops_per_run_is_worth_the_belt",
            drops is >= 1.0 && drops <= RunState.MaxPotionSlots * 2,
            $"{drops:F1} drops per run against a {RunState.MaxPotionSlots}-slot belt");
    }

    // The skip streak has to pay for what it costs. Every rung is a card given
    // up, and this is the only place in the suite that can say whether the odds
    // bought back are worth one - EffectSmokeTest checks the ladder's shape,
    // and shape is not payoff.
    private void TestSkipStreakIsWorthTheCardsItCosts()
    {
        var rungs = BalanceModel.SkipStreakOdds();

        // The ladder is the whole ladder. A rung missing from the report is a
        // rung the player can reach and nobody has measured.
        Check("skip_streak_report_covers_every_rung",
            rungs.Count == CardPool.MaxSkipStreak + 1,
            $"{rungs.Count} rungs printed, cap is {CardPool.MaxSkipStreak}");

        // Worth the card. Three skips costs three cards, so if the top rung does
        // not make a Rare in the offer better than even the streak is a strictly
        // worse play than taking whatever was on the table - which is the shrug
        // this item exists to remove, arrived at from the other direction.
        var top = rungs[^1];
        Check("the_top_rung_is_worth_the_cards_it_costs", top.RareInAnOffer >= 0.4,
            $"a rare in the offer at rung {top.Streak} is {top.RareInAnOffer:P0}");

        // And not so good it replaces the pool. Rung 0 is what almost every
        // reward in a run is drawn on; a top rung that made a Rare the *likely*
        // outcome would make skipping the default play rather than a bet.
        Check("the_top_rung_does_not_make_rares_the_norm", top.Rare < 0.5,
            $"rare share at the cap is {top.Rare:P0}");

        // Monotone in the thing the player actually sees, and led by Uncommon at
        // every rung. Both are asserted against the *printed* rows rather than
        // against CardPool, so a report that computed them differently from the
        // draw fails here rather than shipping a table nobody can act on.
        for (int i = 1; i < rungs.Count; i++)
        {
            Check($"skip_streak_offer_odds_rise_at_rung_{rungs[i].Streak}",
                rungs[i].RareInAnOffer > rungs[i - 1].RareInAnOffer,
                $"{rungs[i - 1].RareInAnOffer:P0} -> {rungs[i].RareInAnOffer:P0}");
        }

        var ledByRare = rungs.Where(r => r.Rare >= r.Uncommon).Select(r => r.Streak).ToList();
        Check("uncommon_leads_the_ladder_at_every_rung", ledByRare.Count == 0,
            Join(ledByRare.Select(s => s.ToString()).ToList()));
    }

    // Every run takes exactly one blessing before the first map, so the pool
    // decides where the curve's origin can be - which is the position every
    // other table in this suite is measured against. Bands rather than exact
    // figures, because the numbers are content and are meant to move; what may
    // not move is the *range*.
    //
    // The floors are the load-bearing half. A run opening at 30 max HP is one
    // act-1 elite from over at starter throughput, and a run opening with no
    // gold cannot use the first shop it walks into - both are the "no rung may
    // make act I unwinnable" assertion Phase 10 will need, arriving early.
    private void TestBlessingsStayInTheirBand()
    {
        var deltas = BalanceModel.BlessingDeltas();

        Check("the_blessing_pool_can_fill_an_offer", deltas.Count >= 3,
            $"{deltas.Count} rows against an offer of 3");

        // Nothing may cost more than a third of the opening HP, and nothing may
        // grant more than half of it. Stated against RunState's own constants
        // rather than 50, so moving the starting position moves the band with
        // it instead of quietly invalidating it.
        var hpOffenders = deltas
            .Where(d => d.MaxHp < -RunState.StartingMaxHp / 3.0 || d.MaxHp > RunState.StartingMaxHp / 2.0)
            .Select(d => $"{d.Id} {d.MaxHp:+0.#;-0.#}")
            .ToList();
        Check("no_blessing_moves_starting_max_hp_out_of_band", hpOffenders.Count == 0, Join(hpOffenders));

        // Gold floors at 0 (LoseGoldOutcome), so the real question at the low
        // end is whether a row can empty the purse *and* nothing else - a
        // blessing that is purely a cost is not an offer.
        var goldOffenders = deltas
            .Where(d => RunState.StartingGold + d.Gold < 0
                        || d.Gold > BalanceModel.ShopCardPrice * 5)
            .Select(d => $"{d.Id} {d.Gold:+0.#;-0.#}")
            .ToList();
        Check("no_blessing_moves_starting_gold_out_of_band", goldOffenders.Count == 0, Join(goldOffenders));

        // The one that is not about magnitude: a row offering nothing on any
        // axis is a punishment wearing a reward's tile. Priced rather than
        // resolved, so a cost paired with a *random* grant (a relic, a potion)
        // still counts as an offer.
        //
        // A whitelist of gains rather than the obvious "every axis <= 0" test,
        // because deck size is not signed the way the others are: Cards moves
        // *either* way as a reward (a card offered, or a card removed - the
        // latter being the strongest row in the pool), which is why the model
        // keeps authored cards on Imposed instead and Imposed is absent here.
        var allCost = deltas
            .Where(d => !(d.MaxHp > 0 || d.Gold > 0 || d.Relics > 0 || d.Potions > 0
                          || d.Upgrades > 0 || d.Hp > 0 || d.Cards != 0))
            .Select(d => d.Id)
            .ToList();
        Check("every_blessing_offers_something", allCost.Count == 0, Join(allCost));
    }

    // Pins the two upgrade deltas CardUpgrade's comment describes, by id and
    // by amount. The drift this catches is exactly the one that comment used
    // to carry: Apply's Mathf.Max(amount + 1, amount * 1.4) floor means a
    // grant of 2 upgrades to 3, so the docs said +2 while the data produced
    // +3 for two full content passes and nothing noticed.
    private void TestUpgradeGrantsAreWhatTheDocsClaim()
    {
        CheckGrant("deep_focus", "Foresight", 2, 3);
        CheckGrant("bloodpact", "Fervor", 1, 2);

        // And the general rule the two are instances of, so a third card on a
        // per-turn grant is covered without being named here.
        var wrong = new List<string>();
        foreach (var card in CardDatabase.All)
        {
            var upgraded = CardUpgrade.Apply(card);
            for (int i = 0; i < card.Effects.Count; i++)
            {
                var before = card.Effects[i];
                var after = upgraded.Effects[i];
                if (before.Amount == after.Amount) continue;

                int expected = Mathf.Max(before.Amount + 1, Mathf.RoundToInt(before.Amount * 1.4f));
                if (after.Amount != expected) wrong.Add($"{card.Id} {before.Action} {after.Amount}!={expected}");
            }
        }
        Check("every_scaled_amount_follows_the_documented_formula", wrong.Count == 0, Join(wrong));
    }

    private void CheckGrant(string cardId, string status, int baseAmount, int upgradedAmount)
    {
        var card = CardDatabase.Get(cardId);
        var spec = card.Effects.FirstOrDefault(e => e.Status == status && e.Scope == EffectScope.Self);
        if (spec is null)
        {
            Check($"{cardId}_grants_{status}", false, $"{cardId} has no self-scoped {status} effect");
            return;
        }

        var upgraded = CardUpgrade.Apply(card).Effects
            .First(e => e.Status == status && e.Scope == EffectScope.Self);

        Check($"{cardId}_grants_{status}_{baseAmount}", spec.Amount == baseAmount, $"got {spec.Amount}");
        Check($"{cardId}_upgraded_grants_{status}_{upgradedAmount}",
            upgraded.Amount == upgradedAmount, $"got {upgraded.Amount}");
    }

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _pass++;
            GD.Print($"PASS {name}");
        }
        else
        {
            _fail++;
            GD.Print($"FAIL {name}: {detail}");
        }
    }
}
