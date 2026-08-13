using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Prints the three-act difficulty curve. Everything it shows is computed by
// BalanceModel off the content databases; this file only formats.
//
// Deliberately not named *SmokeTest, so tools/run-smoke-tests.sh's
// scenes/debug/*SmokeTest.tscn glob does not pick it up - the same treatment
// ArtScreenshot/AnimationScreenshot/StyleReferenceScreen get, and for the same
// reason: a report has no pass/fail and belongs outside a sweep that exits on
// one. The invariants worth failing a build over live in BalanceSmokeTest.
//
//   tools/balance-report.sh
//
// (necessary because run-smoke-tests.sh captures each suite's stdout and
// echoes only the summary line, so the tables would be invisible through it.)
public partial class BalanceReport : Node
{
    private const int Seeds = 500;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        BlessingDatabase.LoadAll();

        var acts = BalanceModel.AllActs();
        var playerHp = BalanceModel.PlayerMaxHpByAct();
        double throughput = BalanceModel.Throughput(StarterDeck());
        var paths = BalanceModel.SampleRuns(Seeds);
        var reach = BalanceModel.Reachable(Seeds);

        Header("HOLLOWDECK BALANCE REPORT");
        // Offerable, not the raw database count. Curses and Status cards are
        // rows in cards.json but CardPool never offers one, so counting them
        // here would overstate how much a run can actually draw from - and the
        // first number in a balance report is the last place to be loose.
        int offerable = CardDatabase.All.Count(c => c.IsPlayable);
        int unplayable = CardDatabase.All.Count - offerable;
        GD.Print($"{acts.Count} acts, {EnemyDatabase.All.Count} enemies, "
                 + $"{offerable} offerable cards (+{unplayable} unplayable), "
                 + $"{Seeds} sampled runs. Damage figures are per turn.");

        PrintActCurve(acts, playerHp, throughput);
        PrintEncounterBands(acts);
        PrintBosses(acts);
        PrintOutliers(acts);
        PrintRamps();
        PrintPlayerCurve(acts, playerHp, throughput);
        PrintPaths(paths);
        PrintCardRewardOdds();
        PrintScoreReachability(reach);
        PrintBlessings();
        PrintUpgradeDeltas();
        PrintAscensionLadder(throughput);

        GD.Print("");
        GetTree().Quit(0);
    }

    // ------------------------------------------------------------------ acts

    private void PrintActCurve(List<BalanceModel.ActProfile> acts, List<int> playerHp, double throughput)
    {
        Header("THE CURVE - normal encounters against the player's own scaling");
        GD.Print("  act                enc HP     x      DPT     x     kill   die   player HP    x");

        for (int i = 0; i < acts.Count; i++)
        {
            var a = acts[i];
            double hp = a.MeanNormalHp;
            double dpt = a.MeanNormalDpt;
            int php = playerHp[i];

            GD.Print($"  {a.Act.Name,-19}"
                     + $"{hp,6:F0}  {Ratio(i, acts, x => x.MeanNormalHp),-6}"
                     + $"{dpt,6:F1}  {Ratio(i, acts, x => x.MeanNormalDpt),-6}"
                     + $"{BalanceModel.TurnsToKill((int)hp, throughput),6:F1} "
                     + $"{php / Math.Max(dpt, 0.01),6:F1}"
                     + $"{php,10}   {(i == 0 ? "-" : $"{php / (double)playerHp[i - 1]:F2}x"),-5}");
        }

        GD.Print("");
        GD.Print("  kill = turns to clear the group at starter throughput; die = turns the player");
        GD.Print("  survives at full HP with no Block. Both assume nothing improves, which is the");
        GD.Print("  point: the gap between them is what deck power has to cover.");
    }

    // Elite and boss nodes look identical on the map, so what they cost has to
    // be predictable. This is the table the retune was driven off: before it,
    // one act's elite pool ranged from 0.58x to 2.55x the cost of an average
    // normal fight, and the player had no way to tell which they were picking.
    private void PrintEncounterBands(List<BalanceModel.ActProfile> acts)
    {
        Header("WHAT AN ENCOUNTER COSTS - damage absorbed, against an average normal fight");
        GD.Print($"  Elites should land in {BalanceModel.EliteCostLow}-{BalanceModel.EliteCostHigh}x, "
            + $"bosses in {BalanceModel.BossCostLow}-{BalanceModel.BossCostHigh}x. Ratios are within an act:");
        GD.Print("  the reference throughput does not grow act over act but a real deck does.");

        foreach (var a in acts)
        {
            GD.Print("");
            GD.Print($"  {a.Act.Name} - an average normal fight costs {a.MeanNormalCost:F0}");

            // The costliest normal, printed because only the *mean* fed the
            // rows below it and a single spiking Combat node is invisible in a
            // mean. Phase 8 shipped two act-1 normals costing more than every
            // elite in the act and nothing in this report said so - the elite
            // rows just quietly got cheaper as the denominator moved.
            if (a.CostliestNormal is { } worst)
            {
                // Flagged against the *costliest* elite, not the cheapest. Some
                // overlap with the elite pool is what a spread is; outrunning
                // the whole of it means no opt-in detour in the act is as
                // expensive as a fight the map hands you for free.
                double dearestElite = a.Elites.Count == 0 ? 0 : a.Elites.Max(e => e.Cost);
                GD.Print($"    normal {worst.Cost,6:F0} ({a.CostRatio(worst),4:F2}x) hp={worst.TotalHp,4}"
                         + $"  {worst.Label}   <-- costliest normal"
                         + (worst.Cost > dearestElite
                             ? $", above every elite in the act ({dearestElite:F0})"
                             : ""));
            }

            foreach (var e in a.Elites.OrderBy(e => a.CostRatio(e)))
            {
                Band("elite", e.Cost, a.CostRatio(e), BalanceModel.EliteCostLow, BalanceModel.EliteCostHigh, e.Label, e.TotalHp);
            }
            foreach (var b in a.BossEncounters.OrderBy(e => a.CostRatio(e)))
            {
                Band("boss ", b.Cost, a.CostRatio(b), BalanceModel.BossCostLow, BalanceModel.BossCostHigh, b.Label, b.TotalHp);
            }
        }
    }

    private void Band(string kind, double cost, double ratio, double lo, double hi, string label, int hp)
    {
        bool ok = ratio >= lo && ratio <= hi;
        GD.Print($"    {kind} {cost,6:F0} ({ratio,4:F2}x) hp={hp,4}  {label}"
                 + (ok ? "" : ratio < lo ? "   <-- too cheap for its node type" : "   <-- spikier than its node type"));
    }

    private void PrintBosses(List<BalanceModel.ActProfile> acts)
    {
        Header("BOSSES - and whether the enrage phase is an escalation");
        GD.Print("  act                boss                    HP     DPT   enrage      x   at HP%");

        foreach (var a in acts)
        {
            foreach (var b in a.Bosses)
            {
                double ratio = b.EnrageFlatDpt / Math.Max(b.FlatDpt, 0.01);
                GD.Print($"  {a.Act.Name,-19}{b.Name,-21}{b.MaxHp,6}{b.FlatDpt,8:F1}"
                         + $"{b.EnrageFlatDpt,9:F1}{ratio,7:F2}x{b.EnrageHpPercent,10}%");
            }
        }
    }

    private void PrintOutliers(List<BalanceModel.ActProfile> acts)
    {
        Header("SPREAD WITHIN A POOL - two fights on the same floor, side by side");

        foreach (var a in acts)
        {
            var hardest = a.Normals.OrderByDescending(e => e.FlatDpt).First();
            var softest = a.Normals.OrderBy(e => e.FlatDpt).First();
            GD.Print($"  {a.Act.Name} ({a.Normals.Count} normal groups, "
                     + $"{hardest.FlatDpt / Math.Max(softest.FlatDpt, 0.01):F1}x spread)");
            GD.Print($"    hardest  {hardest.FlatDpt,5:F1} dpt  {hardest.TotalHp,4} hp   {hardest.Label}");
            GD.Print($"    softest  {softest.FlatDpt,5:F1} dpt  {softest.TotalHp,4} hp   {softest.Label}");
        }
    }

    // Every figure above this point is a flat steady-state mean, which is the
    // right number for comparing enemies but silently understates the ones
    // that buff themselves: a Ritual enemy re-grants Strength every turn, so
    // its ninth turn is nothing like its first. A long fight against one of
    // these is much worse than its DPT column suggests, and that is exactly
    // the fight an over-HP'd elite creates.
    private void PrintRamps()
    {
        Header("ENEMIES THAT RAMP - flat DPT is a lie for these");
        GD.Print("  enemy                   flat   turn 1   turn 5      x    ai");

        // Measured against the flat steady-state mean, not against turn 1.
        // Turn 1 is whatever the enemy opens with, and half the ramping
        // enemies in the game open with the buff itself - the Cultist's first
        // turn deals 0 - so a turn-1 baseline hides exactly the enemies this
        // section exists to find.
        var ramping = EnemyDatabase.All
            .Select(d => BalanceModel.Profile(d))
            .Where(p => p.FlatDpt > 0 && p.DptAtTurn5 > p.FlatDpt * 1.15)
            .OrderByDescending(p => p.DptAtTurn5 / Math.Max(p.FlatDpt, 0.01))
            .ToList();

        if (ramping.Count == 0)
        {
            GD.Print("  none");
            return;
        }

        foreach (var p in ramping.Take(10))
        {
            GD.Print($"  {p.Name,-20}{p.FlatDpt,8:F1}{p.DptAtTurn1,9:F1}{p.DptAtTurn5,9:F1}"
                     + $"{p.DptAtTurn5 / Math.Max(p.FlatDpt, 0.01),7:F2}x    {p.AiType}");
        }

        GD.Print("");
        GD.Print($"  {ramping.Count} of {EnemyDatabase.All.Count} enemies gain 15%+ by turn "
                 + $"{BalanceModel.RampTurns}.");
    }

    private void PrintPlayerCurve(List<BalanceModel.ActProfile> acts, List<int> playerHp, double throughput)
    {
        Header("THE PLAYER SIDE");
        GD.Print($"  starter deck throughput      {throughput,6:F1} damage/turn "
                 + "(greedy play, measured over 400 hands)");
        GD.Print($"  max HP by act                {string.Join(" -> ", playerHp)}");

        for (int i = 1; i < acts.Count; i++)
        {
            var act = acts[i - 1].Act;
            GD.Print($"  clearing {act.Name,-14} +{act.ClearMaxHpBonus} max HP, heal {act.ClearHealPercent}%");
        }

        double encounterGrowth = acts[^1].MeanNormalDpt / Math.Max(acts[0].MeanNormalDpt, 0.01);
        double playerGrowth = playerHp[^1] / (double)playerHp[0];
        GD.Print("");
        GD.Print($"  incoming damage act I -> III {encounterGrowth,6:F2}x");
        GD.Print($"  player max HP act I -> III   {playerGrowth,6:F2}x");
        GD.Print($"  deck power must cover        {encounterGrowth / playerGrowth,6:F2}x");
    }

    private void PrintPaths(BalanceModel.RunPaths paths)
    {
        Header($"WHAT A RUN CONTAINS - {paths.Seeds} seeds, uniform-random routing");
        GD.Print("  node type      mean per run    best any path");

        foreach (var (label, key, max) in new (string, string, int)[]
                 {
                     ("Combat", "combat", paths.Max.Combat),
                     ("Elite", "elite", paths.Max.Elite),
                     ("Boss", "boss", 0),
                     ("Rest", "rest", paths.Max.Rest),
                     ("Shop", "shop", paths.Max.Shop),
                     ("Treasure", "treasure", paths.Max.Treasure),
                     ("Event", "event", paths.Max.Event),
                 })
        {
            GD.Print($"  {label,-14}{paths[key],12:F1}{(max > 0 ? max.ToString() : "-"),17}");
        }

        GD.Print("");
        GD.Print($"  fights (= three-card reward picks)  {paths["fights"],6:F1} mean, {paths.Max.Fights} best");
        GD.Print($"  gold earned                         {paths["gold"],6:F0} mean, {paths.Max.Gold} best");

        // Drops only. Shop stock is deliberately not counted, for the same
        // reason the fights line above is captioned "three-card reward picks"
        // rather than "cards": this table is what the *nodes* pay, and what a
        // shop sells depends on gold the player may spend on something else.
        GD.Print($"  potion drops (expected)             {paths["potionpct"] / 100.0,6:F1} mean, "
                 + $"{paths.Max.PotionPercent / 100.0:F1} best   (drops only; shop stock not counted)");

        // Printed rather than left to be inferred from the line above, because
        // the two Phase 8 balance incidents and the Phase 9 node-weight one all
        // hid the same way: a data number moved, and only its consequence was
        // visible. An acts.json edit shows up here directly.
        var rates = string.Join("   ", ActDatabase.All.Select((act, i) =>
            $"act {new[] { "I", "II", "III" }.ElementAtOrDefault(i) ?? (i + 1).ToString()} "
            + $"{act.PotionDropPercent}/{act.ElitePotionDropPercent}"));
        GD.Print($"  drop rates normal/elite             {rates}");

        // Same argument as the drop rates above, one feature later. A relic's
        // price is now a function of its tier and the shop's expected price is
        // a function of RelicPool's weights, so two data numbers stand between
        // an edit and the reachability table below - print both rather than
        // leaving the reader to infer them from a moved "I Like Shiny" row.
        var tiers = RelicPool.TiersFor(RelicSite.Shop);
        GD.Print($"  relic prices by tier                "
                 + string.Join("  ", tiers.Select(t => $"{t} {ShopScreen.RelicPriceFor(t)}g")));
        GD.Print($"  shop relic expected price           "
                 + $"{BalanceModel.ExpectedShopRelicPrice(),6:F0}g over "
                 + string.Join("/", tiers.Select(t => RelicPool.WeightOf(t))));

        var byTier = RelicDatabase.All.GroupBy(r => r.Tier)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key} {g.Count()}");
        GD.Print($"  relic rows by tier                  {string.Join("  ", byTier)}");
    }

    private void PrintScoreReachability(BalanceModel.Reachability reach)
    {
        Header("RUNSCORE THRESHOLDS - can a player actually earn these?");
        GD.Print("  category            needs   typical   best    % of runs where routing for it works");

        // Thresholds come out of RunScore itself, so retuning one there is
        // immediately visible here rather than needing this file edited too.
        var gold = BalanceModel.GoldThresholds();
        var decks = BalanceModel.DeckSizeThresholds();

        Reach("Money Money", gold[^1], reach.Gold);
        Reach("Raining Money", gold[^2], reach.Gold);
        Reach("I Like Gold", gold[0], reach.Gold);
        Reach("I Like Shiny", RunScore.ShinyRelics, reach.Relics);
        Reach("Librarian", decks[^1], reach.DeckSize);
        Reach("Encyclopedian", decks[0], reach.DeckSize);
        Reach("Mystery Machine", RunScore.MysteryRooms, reach.EventRooms);

        // Only the thresholds that are not already comfortable get a sweep -
        // it disappears by itself once they are set sensibly, which is the
        // point of printing it rather than a fixed table.
        Sweep("Librarian", decks[^1], reach.DeckSize);
        Sweep("Encyclopedian", decks[0], reach.DeckSize);
        Sweep("Mystery Machine", RunScore.MysteryRooms, reach.EventRooms);

        GD.Print("");
        GD.Print("  'typical' is the median over seeds of the best a path can do; the last column is");
        GD.Print("  the share of seeds where that best clears the bar. Each is a witness path with");
        GD.Print("  the whole purse spent on that one category - gold buys cards or relics, never");
        GD.Print($"  both. Deck size counts fight rewards plus shop cards at {BalanceModel.ShopCardPrice}g; "
                 + $"on rewards alone the");
        GD.Print($"  median is {reach.DeckSizeNoPurchases.Median}, so the rest is bought.");
        GD.Print($"  Median best path: {reach.Shops.Median} shops, {reach.Elites.Median} elites, "
                 + $"{reach.Fights.Median} fights.");
    }

    private void Reach(string label, int needs, BalanceModel.Metric metric)
    {
        double share = metric.FractionAtLeast(needs);
        string verdict = share switch
        {
            0 => "  never - dead points",
            < 0.05 => "  a lottery, not an achievement",
            < 0.5 => "  demanding",
            _ => "",
        };
        GD.Print($"  {label,-20}{needs,5}{metric.Median,10}{metric.Best,7}{share,10:P0}{verdict}");
    }

    // What each nearby threshold value would actually cost, so a retune is a
    // choice between measured options rather than a guess that needs another
    // pass to check.
    private void Sweep(string label, int current, BalanceModel.Metric metric)
    {
        if (metric.FractionAtLeast(current) >= 0.95) return;

        var candidates = Enumerable.Range(Math.Max(1, metric.Median - 4), 9)
            .Where(v => v <= metric.Best + 2)
            .ToList();

        GD.Print("");
        GD.Print($"  {label} at each value: "
                 + string.Join("  ", candidates.Select(v =>
                     $"{v}={metric.FractionAtLeast(v):P0}{(v == current ? "*" : "")}")));
    }

    // ---------------------------------------------------------- card rewards

    // Printed rather than left as a consequence, per the rule the potion pass
    // wrote down and the node-weight pass repeated: both of those incidents hid
    // the same way, with a data number moving and only its effect visible. The
    // ladder here is exactly that kind of number - nothing else in this report
    // can see a rarity, so if these four rows are not on the page the only way
    // to find out what a skip is worth is to read CardPool.
    private void PrintCardRewardOdds()
    {
        Header("CARD REWARD ODDS - what a skip streak buys");
        GD.Print($"  A won fight offers {CombatScreen.RewardCardChoices} cards. Declining them all "
                 + "shifts the next offer's weights;");
        GD.Print("  taking one resets the streak. Shop stock and event grants stay on rung 0.");
        GD.Print("");
        GD.Print("  streak   common  uncommon    rare    a rare in the offer");

        foreach (var rung in BalanceModel.SkipStreakOdds())
        {
            string cap = rung.Streak == CardPool.MaxSkipStreak ? "  (max)" : "";
            GD.Print($"  {rung.Streak,6}   {rung.Common,6:P0}  {rung.Uncommon,8:P0}  {rung.Rare,6:P0}"
                     + $"    {rung.RareInAnOffer,6:P0}{cap}");
        }
    }

    // -------------------------------------------------------------- upgrades

    private void PrintUpgradeDeltas()
    {
        Header("UPGRADE DELTAS - what a '+' is worth, biggest first");
        GD.Print("  card                 cost  rarity     effect                 base -> +    x");

        var rows = new List<(CardDefinition Card, EffectSpec Before, EffectSpec After, double Ratio)>();
        foreach (var card in CardDatabase.All)
        {
            var upgraded = CardUpgrade.Apply(card);
            for (int i = 0; i < card.Effects.Count; i++)
            {
                var before = card.Effects[i];
                var after = upgraded.Effects[i];
                if (after.Amount == before.Amount) continue;
                rows.Add((card, before, after, after.Amount / (double)Math.Max(before.Amount, 1)));
            }
        }

        // Per-turn grants first: those are the deltas that compound over a
        // fight rather than paying out once, so a +1 there is not a +1.
        foreach (var row in rows
                     .OrderByDescending(r => IsPerTurnGrant(r.Before) ? 1 : 0)
                     .ThenByDescending(r => r.Ratio)
                     .ThenByDescending(r => r.After.Amount - r.Before.Amount)
                     .Take(12))
        {
            string effect = row.Before.Status ?? row.Before.Action;
            GD.Print($"  {row.Card.Name,-20}{row.Card.Cost,4}  {row.Card.Rarity,-10} {effect,-20}"
                     + $"{row.Before.Amount,5} ->{row.After.Amount,3}{row.Ratio,6:F2}x"
                     + (IsPerTurnGrant(row.Before) ? "   per turn, every turn" : ""));
        }
    }

    // The six statuses CombatManager pays out at the start of a turn - the four
    // in ApplyTurnStartGrants plus the two folded into BeginPlayerTurn's energy
    // and hand-size assignments.
    //
    // Hand-listed, and nothing fails if it falls behind ApplyTurnStartGrants:
    // a missing status just quietly stops being counted as a per-turn grant and
    // the report under-reports the pool's ceiling. Add here whenever a grant is
    // added there.
    private static bool IsPerTurnGrant(EffectSpec spec) =>
        spec.Action == "apply_status" && spec.Scope == EffectScope.Self
        && spec.Status is "Metallicize" or "Ritual" or "Regen" or "Fervor" or "Foresight"
            or "Plating";

    // ------------------------------------------------------------- utilities

    private static List<CardDefinition> StarterDeck()
    {
        // Built through RunState so it is the deck a run actually opens with,
        // rather than a second copy of the same three ids that could drift.
        RunState.InitNewRun();
        return new List<CardDefinition>(RunState.Deck);
    }

    private static string Ratio(int i, List<BalanceModel.ActProfile> acts, Func<BalanceModel.ActProfile, double> f) =>
        i == 0 ? "-" : $"{f(acts[i]) / Math.Max(f(acts[i - 1]), 0.01):F2}x";

    // Every run now takes exactly one of these before the first map, so this is
    // where the position every table above is measured from actually starts.
    // Printed rather than left as a consequence: both Phase 8 balance incidents
    // and the Phase 9 node-weight one hid the same way - a data number moved and
    // only its effect was visible.
    private void PrintBlessings()
    {
        var deltas = BalanceModel.BlessingDeltas();

        Header("START-OF-RUN BLESSINGS - what the curve's origin can be moved to");
        GD.Print($"  the run opens at {RunState.StartingMaxHp} max HP, {RunState.StartingGold} gold, "
                 + "10 cards, 1 relic. One blessing of three offered.");
        GD.Print("");
        GD.Print("  blessing              max HP    gold   cards  forced  relics potions   upg");

        foreach (var d in deltas.OrderByDescending(d => d.MaxHp).ThenByDescending(d => d.Gold))
        {
            GD.Print($"  {d.Label,-20}{Signed(d.MaxHp),8}{Signed(d.Gold),8}{Signed(d.Cards),8}"
                     + $"{Signed(d.Imposed),8}{Signed(d.Relics),8}{Signed(d.Potions),8}{Signed(d.Upgrades),6}");
        }

        GD.Print("");
        GD.Print($"  max HP spans {RunState.StartingMaxHp + deltas.Min(d => d.MaxHp):F0} to "
                 + $"{RunState.StartingMaxHp + deltas.Max(d => d.MaxHp):F0}; "
                 + $"gold {RunState.StartingGold + deltas.Min(d => d.Gold):F0} to "
                 + $"{RunState.StartingGold + deltas.Max(d => d.Gold):F0}.");
        GD.Print("  A gamble is priced as the mean over its alternatives, which is what it rolls.");
        GD.Print("  'cards' is what the player is offered (a draw up, a removal down); 'forced' is a");
        GD.Print("  card named by the author and put in the deck, which is how a Curse is authored.");
    }

    // ------------------------------------------------------------- ascension

    // Two halves, and the first one is the one this project keeps learning it
    // needs. Both Phase 8 balance incidents and the Phase 9 node-weight one hid
    // the same way: a data number moved and only its consequence was visible.
    // A twenty-row ladder is twenty such numbers, so the modifiers are printed
    // as modifiers before anything derived from them is.
    //
    // The second half is the derived one, and it is deliberately not all
    // twenty-one rungs of all eleven tables above - that is unreadable and it
    // is also slow. What it prints is the one measure that answers the question
    // the ladder actually raises: how much of the player's health an average
    // act-I fight takes, rung by rung. Sampled tables follow at the two ends.
    private void PrintAscensionLadder(double throughput)
    {
        int max = AscensionDatabase.MaxLevel;

        Header("THE ASCENSION LADDER - twenty rungs, and what each one is made of");
        GD.Print("  rung  what it adds                   enemy    dmg   boss    HP   heal   shop"
                 + "  elite potion");

        for (int level = 1; level <= max; level++)
        {
            var rung = AscensionDatabase.At(level);
            var e = AscensionDatabase.Effective(level);
            GD.Print($"  {level,4}  {rung.Label,-30}"
                     + $"{e.EnemyHpPercent,6}%{e.EnemyDamagePercent,6}%{e.BossHpBonusPercent,6}%"
                     + $"{Signed(e.StartingMaxHpDelta),6}{e.ClearHeal(100),6}%{e.ShopPricePercent,6}%"
                     + $"{Signed(e.EliteWeightDelta),7}{Signed(-e.PotionDropPercentDelta),7}");
        }

        GD.Print("");
        GD.Print("  Columns are cumulative, not per-rung: rung N is every rung up to it. 'enemy',");
        GD.Print("  'dmg', 'shop' are percentages of the authored number; 'boss' is extra HP on top");
        GD.Print("  of 'enemy'; 'heal' is what clearing an act restores; 'elite' is weight moved out");
        GD.Print("  of Combat into Elite, so the node table's total never changes.");

        GD.Print("");
        GD.Print("  rung   act I fight   your HP   pressure      x   costliest act I    imposed");

        double baseline = 0;
        for (int level = 0; level <= max; level++)
        {
            var asc = AscensionDatabase.Effective(level);
            var act1 = BalanceModel.Profile(ActDatabase.At(0), asc);
            int hp = asc.StartingMaxHp(RunState.StartingMaxHp);
            double mean = act1.MeanNormalCost;
            double worst = act1.CostliestNormal?.Cost ?? 0;
            double pressure = mean / Math.Max(hp, 1);
            if (level == 0) baseline = pressure;

            GD.Print($"  {level,4}{mean,14:F0}{hp,10}{pressure,11:F2}"
                     + $"{pressure / Math.Max(baseline, 0.0001),7:F2}{worst,18:F0}"
                     + $"{asc.StartingCurseIds.Count,11}");
        }

        GD.Print("");
        GD.Print("  'act I fight' is what an average act-I normal encounter throws at you at the");
        GD.Print($"  reference throughput of {throughput:F1} - damage dealt, before the player blocks any of it,");
        GD.Print("  so it is a pressure number and not a death count. 'pressure' divides it by the");
        GD.Print("  rung's own starting HP, which is the pair of knobs that decide whether act I is");
        GD.Print("  playable at all, and 'x' is that against rung 0.");
        GD.Print("");
        GD.Print("  It is an *absolute* measure on purpose. The elite and boss ratio bands above");
        GD.Print("  divide by the mean normal cost, which a rung moves too - so those bands are very");
        GD.Print("  nearly rung-invariant and are no evidence at all that the ladder does anything.");
    }

    // Blank rather than "0" for an axis a blessing does not touch: at seven
    // columns and ten rows, a grid of zeroes hides the two or three numbers per
    // line that are the whole content of the table.
    private static string Signed(double v) =>
        Math.Abs(v) < 0.005 ? "-" : (v > 0 ? $"+{v:0.##}" : $"{v:0.##}");

    private static void Header(string title)
    {
        GD.Print("");
        GD.Print(new string('=', 78));
        GD.Print($"  {title}");
        GD.Print(new string('=', 78));
    }
}
