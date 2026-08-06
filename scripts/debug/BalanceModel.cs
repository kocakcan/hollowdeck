using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Reads the three-act difficulty curve straight off the content databases:
// how much HP an encounter has, how hard it hits, how many fights and rewards
// a path through the map actually contains. BalanceReport prints it,
// BalanceSmokeTest asserts the parts of it that must not regress.
//
// This is a static analyser, not a simulator, and that is a deliberate choice
// rather than a shortcut. CombatManager paces the enemy turn on wall-clock
// timers (PreActionDelaySec + PostActionDelaySec, 0.35s per enemy action), so
// driving real fights costs seconds per fight and the 90s suite watchdog caps
// a sweep at a couple of hundred enemy actions - far too few to say anything
// about a curve. Reading the numbers off enemies.json/acts.json is instant and
// exact, and needs no production code bent to a test's convenience.
//
// Everything here is derived. Nothing in this file re-authors a number that
// lives in data, and the damage math is DamageMath's own rather than a second
// copy of it - a balance tool that disagrees with the game about what 6 damage
// plus 3 Strength comes to is worse than no tool.
public static class BalanceModel
{
    // How many turns the ramped figures average over. Five is roughly how long
    // a normal encounter lasts at starter throughput (see TurnsToKill below),
    // so it is the window in which an enemy's Strength ramp actually gets to
    // pay out.
    public const int RampTurns = 5;

    // ---------------------------------------------------------------- enemies

    public sealed record EnemyProfile(
        string Id,
        string Name,
        int MaxHp,
        string AiType,
        // The steady-state loop mean of raw move damage, ignoring statuses.
        // This is the comparable, at-a-glance number: it is what an enemy hits
        // for on an average turn if nothing about it changes.
        double FlatDpt,
        // The same enemy with its own Strength ramp applied - a move that
        // grants Strength to Self makes every later hit land harder, and a
        // Ritual grant re-applies that every turn. For anything without a
        // self-buff DptAtTurn5 equals FlatDpt, and the gap between them is
        // what BalanceReport's ramp table sorts on.
        double DptAtTurn1,
        double DptAtTurn5,
        int EnrageHpPercent,
        // 0 for the 28 enemies with no enrage phase.
        double EnrageFlatDpt)
    {
        public bool HasEnrage => EnrageHpPercent > 0 && EnrageFlatDpt > 0;
    }

    public static EnemyProfile Profile(EnemyDefinition def)
    {
        var enrage = def.EnrageMoves.Count > 0
            ? Mean(def.EnrageMoves.Select(m => (double)MoveDamage(m)))
            : 0.0;
        return new EnemyProfile(
            def.Id, def.Name, def.MaxHp, def.AiType,
            FlatDpt: FlatDpt(def),
            DptAtTurn1: DptAtTurn(def, 1),
            DptAtTurn5: DptAtTurn(def, RampTurns),
            def.EnrageHpPercent,
            enrage);
    }

    public static EnemyProfile Profile(string id) => Profile(EnemyDatabase.Get(id));

    // Damage a single move deals, summed over its deal_damage specs rather
    // than read off intent.DisplayAmount - the telegraph carries damage *per
    // hit*, so a move authored as three 4s displays 4 and deals 12. Same
    // relationship Phase4ContentSmokeTest pins from the other direction.
    private static int MoveDamage(EnemyMove move) =>
        move.Effects.Where(e => e.Action == "deal_damage").Sum(e => e.Amount);

    private static int MoveHits(EnemyMove move) =>
        move.Effects.Count(e => e.Action == "deal_damage");

    private static int SelfStatusGain(EnemyMove move, string status) =>
        move.Effects
            .Where(e => e.Action == "apply_status" && e.Scope == EffectScope.Self && e.Status == status)
            .Sum(e => e.Amount);

    private static int TargetStatusGain(EnemyMove move, string status) =>
        move.Effects
            .Where(e => e.Action == "apply_status" && e.Scope == EffectScope.Target && e.Status == status)
            .Sum(e => e.Amount);

    // The steady-state distribution over moves, i.e. what the enemy does on an
    // average turn once the opening sequence is behind it. Mirrors the pickers
    // in scripts/combat/ rather than approximating them:
    //
    //  - sequential loops Moves[LoopFromIndex..], so the moves before that
    //    index are openers and do not belong in a steady-state mean.
    //  - phase_threshold loops all of Moves until it enrages (and then all of
    //    EnrageMoves - reported separately, since when it flips depends on
    //    player throughput and so is not a property of the enemy alone).
    //  - weighted_random is its Weight distribution, except that for 3+ move
    //    enemies WeightedRandomIntentPicker excludes the last move played,
    //    which makes it a Markov chain rather than i.i.d. sampling. That chain
    //    is solved below rather than waved at: three enemies have 3 moves
    //    (possessed_armor, pyre_warden, silent_judge) and their weights are
    //    lopsided enough that the difference is real.
    private static IReadOnlyList<(EnemyMove Move, double P)> SteadyState(EnemyDefinition def)
    {
        var moves = def.Moves;
        if (moves.Count == 0) return Array.Empty<(EnemyMove, double)>();

        if (def.AiType == "weighted_random")
        {
            return moves.Count <= 2
                ? Weighted(moves)
                : AntiRepeatStationary(moves);
        }

        // phase_threshold wraps to 0; sequential wraps to LoopFromIndex.
        int from = def.AiType == "phase_threshold" ? 0 : Math.Clamp(def.LoopFromIndex, 0, moves.Count - 1);
        var loop = moves.Skip(from).ToList();
        return loop.Select(m => (m, 1.0 / loop.Count)).ToList();
    }

    private static IReadOnlyList<(EnemyMove Move, double P)> Weighted(IReadOnlyList<EnemyMove> moves)
    {
        double total = moves.Sum(m => m.Weight);
        return total <= 0
            ? moves.Select(m => (m, 1.0 / moves.Count)).ToList()
            : moves.Select(m => (m, m.Weight / total)).ToList();
    }

    // Stationary distribution of "pick by weight from everything except what
    // you just played". Power iteration on a 3-4 state chain converges in a
    // few dozen steps; there is no need to be cleverer than this.
    private static IReadOnlyList<(EnemyMove Move, double P)> AntiRepeatStationary(IReadOnlyList<EnemyMove> moves)
    {
        int n = moves.Count;
        var p = Enumerable.Repeat(1.0 / n, n).ToArray();

        for (int step = 0; step < 200; step++)
        {
            var next = new double[n];
            for (int last = 0; last < n; last++)
            {
                double total = 0;
                for (int j = 0; j < n; j++) if (j != last) total += moves[j].Weight;
                if (total <= 0) continue;
                for (int j = 0; j < n; j++)
                {
                    if (j == last) continue;
                    next[j] += p[last] * moves[j].Weight / total;
                }
            }
            p = next;
        }

        return moves.Select((m, i) => (m, p[i])).ToList();
    }

    public static double FlatDpt(EnemyDefinition def) =>
        SteadyState(def).Sum(s => s.P * MoveDamage(s.Move));

    // Expected damage on turn t, carrying the Strength this enemy has granted
    // itself on turns 1..t-1. Strength is added per hit (DamageMath applies it
    // inside each deal_damage), and a Ritual grant re-adds Strength at the
    // start of every turn the way CombatManager.ApplyTurnStartGrants does.
    //
    // Turn 1 uses the enemy's actual opening move where it has one; from turn
    // 2 the steady-state distribution takes over. That is a simplification for
    // sequential enemies with long openers, and it is stated rather than
    // hidden - the flat number above is the one to compare across enemies.
    public static double DptAtTurn(EnemyDefinition def, int turn)
    {
        if (def.Moves.Count == 0) return 0;

        double strength = 0;
        double ritual = 0;
        double damage = 0;

        for (int t = 1; t <= turn; t++)
        {
            strength += ritual;

            var dist = t == 1
                ? new[] { (Move: def.Moves[0], P: 1.0) }.ToList()
                : SteadyState(def).ToList();

            damage = dist.Sum(s => s.P * (MoveDamage(s.Move) + MoveHits(s.Move) * strength));
            strength += dist.Sum(s => s.P * SelfStatusGain(s.Move, "Strength"));
            ritual += dist.Sum(s => s.P * SelfStatusGain(s.Move, "Ritual"));
        }

        return damage;
    }

    // ------------------------------------------------- what a fight costs you

    // Damage per turn is the wrong headline number on its own, and believing
    // it caused two wrong conclusions before this existed. It ignores Poison,
    // which is authored on six enemies and deals N + (N-1) + ... + 1 over its
    // life - `corrosive_tide`'s Poison 5 is 15 damage, more than the 13 the
    // move telegraphs. It ignores that an enemy applying Vulnerable amplifies
    // its *own* later hits by 1.5x. And it ignores Strength accumulating
    // through an enrage phase, which is most of what an act-1 boss does.
    //
    // So the metric that decides fights is the total damage a group lands over
    // a fight of realistic length, and that is what this computes: a
    // turn-by-turn walk of the enemy side only. Still static analysis - no
    // engine, no player, no frames - just an honest accounting of the rules in
    // DamageMath and CombatManager.ApplyPoisonTick.
    //
    // Fight length comes from the group's HP against a reference throughput,
    // so a tankier group is a longer fight and absorbs more turns of damage.
    // Compare costs *within* an act: across acts the player's real throughput
    // has grown and this reference has not, which inflates later fights.
    public static double EncounterCost(IReadOnlyList<string> ids, double throughput)
    {
        if (ids.Count == 0 || throughput <= 0) return 0;

        var defs = ids.Select(EnemyDatabase.Get).ToList();
        int turns = Math.Max(1, (int)Math.Ceiling(defs.Sum(d => d.MaxHp) / throughput));

        var strength = new double[defs.Count];
        var ritual = new double[defs.Count];
        var enraged = new bool[defs.Count];
        var hp = defs.Select(d => (double)d.MaxHp).ToArray();

        // Player-side state the enemies themselves create.
        double vulnerable = 0, poison = 0, total = 0;
        double sharedThroughput = throughput / defs.Count;

        for (int turn = 1; turn <= turns; turn++)
        {
            // Poison bypasses Block and decays as it ticks - see
            // CombatManager.ApplyPoisonTick.
            if (poison > 0)
            {
                total += poison;
                poison = Math.Max(0, poison - 1);
            }

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                hp[i] -= sharedThroughput;

                if (!enraged[i] && def.EnrageHpPercent > 0 && def.EnrageMoves.Count > 0
                    && hp[i] * 100 <= def.MaxHp * def.EnrageHpPercent)
                {
                    enraged[i] = true;
                }

                // Ritual re-grants Strength at the start of every turn, the
                // way CombatManager.ApplyTurnStartGrants does.
                strength[i] += ritual[i];

                foreach (var (move, p) in Cadence(def, turn, enraged[i]))
                {
                    double raw = MoveDamage(move) + MoveHits(move) * strength[i];
                    if (vulnerable > 0) raw *= DamageMath.VulnerableMultiplier;
                    total += p * raw;

                    strength[i] += p * SelfStatusGain(move, "Strength");
                    ritual[i] += p * SelfStatusGain(move, "Ritual");
                    poison += p * TargetStatusGain(move, "Poison");
                    vulnerable += p * TargetStatusGain(move, "Vulnerable");
                }
            }

            vulnerable = Math.Max(0, vulnerable - 1);
        }

        return total;
    }

    // What the enemy is expected to do on a given turn. Mirrors the three
    // pickers in scripts/combat/ rather than approximating them - including
    // that a sequential enemy plays its opening moves once and only then
    // wraps to LoopFromIndex, which a steady-state mean cannot express.
    private static IReadOnlyList<(EnemyMove Move, double P)> Cadence(
        EnemyDefinition def, int turn, bool enraged)
    {
        var moves = enraged ? def.EnrageMoves : def.Moves;
        if (moves.Count == 0) return Array.Empty<(EnemyMove, double)>();

        // PhaseThresholdIntentPicker resets its index on transition and wraps
        // to 0, ignoring LoopFromIndex.
        if (enraged || def.AiType == "phase_threshold")
        {
            return new[] { (moves[(turn - 1) % moves.Count], 1.0) };
        }

        if (def.AiType == "weighted_random")
        {
            return moves.Count <= 2 ? Weighted(moves) : AntiRepeatStationary(moves);
        }

        int index = turn - 1;
        if (index < moves.Count) return new[] { (moves[index], 1.0) };

        int loopFrom = Math.Clamp(def.LoopFromIndex, 0, moves.Count - 1);
        int span = moves.Count - loopFrom;
        return new[] { (moves[loopFrom + (index - moves.Count) % span], 1.0) };
    }

    // ------------------------------------------------------------- encounters

    // The throughput every encounter cost is measured against. A fixed
    // reference rather than a per-act guess: nothing in the repo can measure
    // what an act-3 deck actually does, and inventing a number per act would
    // put an unmeasured assumption underneath every comparison. Costs are
    // therefore comparable *within* an act and not across one.
    public const double ReferenceThroughput = 16.2;

    public sealed record EncounterProfile(
        IReadOnlyList<string> Ids,
        int TotalHp,
        double FlatDpt,
        double Cost)
    {
        public string Label => string.Join(" + ", Ids);
    }

    public static EncounterProfile Encounter(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        var defs = list.Select(EnemyDatabase.Get).ToList();
        return new EncounterProfile(
            list,
            defs.Sum(d => d.MaxHp),
            defs.Sum(FlatDpt),
            EncounterCost(list, ReferenceThroughput));
    }

    // ------------------------------------------------------------------- acts

    public sealed record ActProfile(
        ActDefinition Act,
        IReadOnlyList<EncounterProfile> Normals,
        IReadOnlyList<EncounterProfile> Elites,
        IReadOnlyList<EnemyProfile> Bosses,
        IReadOnlyList<EncounterProfile> BossEncounters)
    {
        public double MeanNormalHp => Mean(Normals.Select(e => (double)e.TotalHp));
        public double MeanNormalDpt => Mean(Normals.Select(e => e.FlatDpt));
        public double MeanEliteHp => Mean(Elites.Select(e => (double)e.TotalHp));
        public double MeanEliteDpt => Mean(Elites.Select(e => e.FlatDpt));

        // The yardstick every other encounter in the act is measured against:
        // what an average normal fight takes out of you.
        public double MeanNormalCost => Mean(Normals.Select(e => e.Cost));

        public double CostRatio(EncounterProfile e) =>
            MeanNormalCost <= 0 ? 0 : e.Cost / MeanNormalCost;
    }

    public static ActProfile Profile(ActDefinition act) => new(
        act,
        act.NormalEncounters.Select(Encounter).ToList(),
        act.EliteEncounters.Select(Encounter).ToList(),
        act.BossIds.Select(Profile).ToList(),
        act.BossIds.Select(b => Encounter(new[] { b })).ToList());

    public static List<ActProfile> AllActs() => ActDatabase.All.Select(Profile).ToList();

    // The player's side of the same curve, so the two can be read against each
    // other: max HP at the start of each act, from acts.json's clear bonuses
    // and heal rather than from a number typed here.
    public static List<int> PlayerMaxHpByAct(int startingMaxHp = 50)
    {
        var result = new List<int> { startingMaxHp };
        int hp = startingMaxHp;
        foreach (var act in ActDatabase.All.Take(ActDatabase.Count - 1))
        {
            hp += act.ClearMaxHpBonus;
            result.Add(hp);
        }
        return result;
    }

    // ------------------------------------------------------- player throughput

    // Damage per turn from a deck, measured by dealing real hands out of the
    // real PileManager and spending energy greedily on the biggest hit
    // affordable. "A starter deck does about 18 a turn" is one of the
    // hand-computed figures this tool exists to replace, so it gets measured.
    //
    // Greedy is not optimal play, and it is the right reference anyway: it is
    // reproducible, it needs no card-specific knowledge, and every encounter is
    // measured against the same yardstick.
    public static double Throughput(IReadOnlyList<CardDefinition> deck, int turns = 400, int maxEnergy = 3,
        int seed = 0)
    {
        if (deck.Count == 0) return 0;

        // PileManager shuffles out of the global RngStreams.Combat stream, so
        // the measurement would otherwise depend on whatever else in the
        // process had drawn from it first. Pinning the run seed here makes the
        // reported throughput reproducible; nothing else in a report or a
        // smoke test cares what state the streams are left in.
        RngStreams.Init(seed);
        var piles = new PileManager(deck);
        var player = new PlayerCombatant { Name = "Reference", MaxHp = 999, CurrentHp = 999 };
        double total = 0;

        for (int t = 0; t < turns; t++)
        {
            // A fresh dummy target each turn: Vulnerable from a card like Bash
            // pays out within the turn that applied it, but carrying one
            // target across 400 turns would stack it into nonsense.
            var target = new EnemyCombatant { Name = "Dummy", MaxHp = 9999, CurrentHp = 9999 };
            piles.DrawHand(CombatManager.BaseHandSize);
            int energy = maxEnergy;

            while (true)
            {
                var best = piles.Hand
                    // Both guards are load-bearing rather than defensive.
                    // An unplayable card would be "played" for zero damage,
                    // which makes the yardstick blind to deck pollution -
                    // the exact thing Curses exist to do. And an X card's
                    // Cost is the -1 sentinel, so it passes the affordability
                    // test at any energy and then *adds* one on the subtract
                    // below, so `energy` climbs and the greedy loop empties
                    // the whole hand every turn - silently inflating the
                    // reference throughput every act band is measured against.
                    // This model deliberately does not simulate X.
                    .Where(c => c.Definition.IsPlayable && !c.Definition.IsXCost
                        && c.Definition.Cost <= energy)
                    .OrderByDescending(c => CardDamage(c.Definition, player, target))
                    .ThenByDescending(c => c.Definition.Effects.Any(e => e.Status == "Vulnerable"))
                    .FirstOrDefault();
                if (best is null) break;

                energy -= best.Definition.Cost;
                total += CardDamage(best.Definition, player, target);
                foreach (var spec in best.Definition.Effects)
                {
                    if (spec.Action == "apply_status" && spec.Scope == EffectScope.Target
                        && Enum.TryParse<StatusType>(spec.Status, out var status))
                    {
                        target.AddStatus(status, spec.Amount);
                    }
                }
                piles.Hand.Remove(best);
                piles.Discard.Add(best);
                if (energy <= 0) break;
            }

            piles.DiscardHand();
        }

        return total / turns;
    }

    private static int CardDamage(CardDefinition card, Combatant source, Combatant target) =>
        card.Effects
            .Where(e => e.Action == "deal_damage")
            .Sum(e => DamageMath.ApplyVulnerable(DamageMath.ComputeOutgoing(e.Amount, source), target));

    public static double TurnsToKill(int hp, double throughput) => throughput <= 0 ? 0 : hp / throughput;

    // ------------------------------------------------------------------ paths

    // What one walk from floor 0 to the boss actually contains. Node counts,
    // not floors: a run's difficulty and its rewards are both counted in
    // fights, and a path through a 10-floor act visits 10 nodes regardless of
    // how wide the act is.
    public readonly record struct PathCounts(
        int Combat, int Elite, int Boss, int Rest, int Shop, int Treasure, int Event, int Gold)
    {
        public int Fights => Combat + Elite + Boss;

        public static PathCounts operator +(PathCounts a, PathCounts b) => new(
            a.Combat + b.Combat, a.Elite + b.Elite, a.Boss + b.Boss, a.Rest + b.Rest,
            a.Shop + b.Shop, a.Treasure + b.Treasure, a.Event + b.Event, a.Gold + b.Gold);
    }

    // A category is reachable if *some* path reaches it, so each of these is
    // maximised independently - a player routing for events is not also
    // routing for gold, and asking whether they could is the wrong question.
    public readonly record struct PathMaxima(
        int Combat, int Elite, int Rest, int Shop, int Treasure, int Event, int Gold, int Fights);

    // Means are fractional on purpose: "1.6 event rooms" is the whole point of
    // the Mystery Machine finding, and an integer PathCounts would report 1.
    public sealed record RunPaths(
        int Seeds,
        IReadOnlyDictionary<string, double> Mean,
        PathMaxima Max)
    {
        public double this[string key] => Mean.TryGetValue(key, out var v) ? v : 0;
    }

    // Generates all three acts from one Random the way a real run does (a
    // single Map stream, drawn act after act), walks a uniform-random path
    // through each, and averages over `seeds` runs. The maxima come from a DP
    // over the same graphs, in the same pass.
    public static RunPaths SampleRuns(int seeds = 500)
    {
        var sums = new Dictionary<string, double>
        {
            ["combat"] = 0, ["elite"] = 0, ["boss"] = 0, ["rest"] = 0,
            ["shop"] = 0, ["treasure"] = 0, ["event"] = 0, ["gold"] = 0, ["fights"] = 0,
        };
        var max = new PathMaxima();

        for (int s = 0; s < seeds; s++)
        {
            var rng = new Random(s);
            var runMax = new PathMaxima();

            foreach (var act in ActDatabase.All)
            {
                var nodes = MapGenerator.Generate(rng, act);

                var c = Walk(nodes, act, rng);
                sums["combat"] += c.Combat;
                sums["elite"] += c.Elite;
                sums["boss"] += c.Boss;
                sums["rest"] += c.Rest;
                sums["shop"] += c.Shop;
                sums["treasure"] += c.Treasure;
                sums["event"] += c.Event;
                sums["gold"] += c.Gold;
                sums["fights"] += c.Fights;

                runMax = new PathMaxima(
                    runMax.Combat + MaxAlong(nodes, n => n.Type == MapNodeType.Combat ? 1 : 0),
                    runMax.Elite + MaxAlong(nodes, n => n.Type == MapNodeType.Elite ? 1 : 0),
                    runMax.Rest + MaxAlong(nodes, n => n.Type == MapNodeType.Rest ? 1 : 0),
                    runMax.Shop + MaxAlong(nodes, n => n.Type == MapNodeType.Shop ? 1 : 0),
                    runMax.Treasure + MaxAlong(nodes, n => n.Type == MapNodeType.Treasure ? 1 : 0),
                    runMax.Event + MaxAlong(nodes, n => n.Type == MapNodeType.Event ? 1 : 0),
                    runMax.Gold + MaxAlong(nodes, n => NodeGold(n, act)),
                    runMax.Fights + MaxAlong(nodes, n =>
                        n.Type is MapNodeType.Combat or MapNodeType.Elite or MapNodeType.Boss ? 1 : 0));
            }

            max = new PathMaxima(
                Math.Max(max.Combat, runMax.Combat), Math.Max(max.Elite, runMax.Elite),
                Math.Max(max.Rest, runMax.Rest), Math.Max(max.Shop, runMax.Shop),
                Math.Max(max.Treasure, runMax.Treasure), Math.Max(max.Event, runMax.Event),
                Math.Max(max.Gold, runMax.Gold), Math.Max(max.Fights, runMax.Fights));
        }

        return new RunPaths(seeds, sums.ToDictionary(kv => kv.Key, kv => kv.Value / seeds), max);
    }

    private static PathCounts Walk(IReadOnlyList<MapNode> nodes, ActDefinition act, Random rng)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var floorZero = nodes.Where(n => n.Floor == 0).ToList();
        var current = floorZero[rng.Next(floorZero.Count)];
        var counts = new PathCounts();

        while (true)
        {
            counts += Count(current, act);
            if (current.NextNodeIds.Count == 0) break;
            current = byId[current.NextNodeIds[rng.Next(current.NextNodeIds.Count)]];
        }

        return counts;
    }

    private static PathCounts Count(MapNode n, ActDefinition act) => new(
        n.Type == MapNodeType.Combat ? 1 : 0,
        n.Type == MapNodeType.Elite ? 1 : 0,
        n.Type == MapNodeType.Boss ? 1 : 0,
        n.Type == MapNodeType.Rest ? 1 : 0,
        n.Type == MapNodeType.Shop ? 1 : 0,
        n.Type == MapNodeType.Treasure ? 1 : 0,
        n.Type == MapNodeType.Event ? 1 : 0,
        NodeGold(n, act));

    // Mirrors MapScreen's three award sites. Boss gold is 0 in the final act
    // by design (nothing left to spend it on), which is data, not a bug.
    private static int NodeGold(MapNode n, ActDefinition act) => n.Type switch
    {
        MapNodeType.Combat => act.NormalGoldBase + n.EnemyIds.Count * act.GoldPerEnemy,
        MapNodeType.Elite => act.EliteGold,
        MapNodeType.Boss => act.BossGold,
        _ => 0,
    };

    // Best total of `value` over any floor-0-to-boss path. The graph is a
    // layered DAG, so processing floors back to front is all the ordering the
    // recurrence needs.
    private static int MaxAlong(IReadOnlyList<MapNode> nodes, Func<MapNode, int> value)
    {
        var best = new Dictionary<string, int>();
        foreach (var n in nodes.OrderByDescending(n => n.Floor))
        {
            int onward = n.NextNodeIds.Count == 0 ? 0 : n.NextNodeIds.Max(id => best[id]);
            best[n.Id] = value(n) + onward;
        }
        return nodes.Where(n => n.Floor == 0).Max(n => best[n.Id]);
    }

    // ---------------------------------------------------- score reachability

    // What a player routing *for one category* can reach, which is the only
    // question that decides whether a RunScore threshold is earnable at all.
    // Each field is maximised independently and over the whole seed sample -
    // a threshold no seed can reach is dead points, and RunScore's Mystery
    // Machine (5 event rooms against a mean of ~1.6) is how that failure looks
    // when nothing measures it.
    //
    // Deck size and relic count both take shop purchases into account, because
    // leaving them out does not make the answer conservative - it makes it
    // wrong. Cards come from fight rewards *and* from 50g a piece at a shop, so
    // a ceiling counting only rewards can report "unreachable" for a threshold
    // players clear routinely.
    //
    // Gold is the coupling: spending it on cards is spending it not-on-relics,
    // so each field maximises its own category and assumes the whole purse goes
    // there. That is the right question - a threshold is earnable if a player
    // *aiming at it* can reach it, not if one player can reach all of them.
    // One metric measured once per seed, keeping the whole sample rather than
    // just its maximum. The maximum alone is the wrong summary for a threshold
    // question: "some seed somewhere allows 9 event rooms" and "most maps do
    // not contain 5" are both true, and only the second explains why nobody
    // has ever scored Mystery Machine.
    public sealed record Metric(string Name, IReadOnlyList<int> PerSeed)
    {
        public int Best => PerSeed.Count == 0 ? 0 : PerSeed.Max();
        public int Median => PerSeed.Count == 0 ? 0 : PerSeed.OrderBy(v => v).ElementAt(PerSeed.Count / 2);

        // Share of seeds on which a player routing for this category can clear
        // the threshold. This is the number a threshold should be set against.
        public double FractionAtLeast(int threshold) =>
            PerSeed.Count == 0 ? 0 : PerSeed.Count(v => v >= threshold) / (double)PerSeed.Count;
    }

    public sealed record Reachability(
        int Seeds,
        Metric Gold,
        Metric Relics,
        Metric DeckSize,
        Metric DeckSizeNoPurchases,
        Metric EventRooms,
        Metric Fights,
        Metric Shops,
        Metric Elites);

    // Mirrors ShopScreen's prices and stock sizes.
    public const int ShopCardPrice = 50;
    public const int ShopCardsInStock = 4;
    public const int ShopRelicPrice = 150;
    public const int ShopRelicsInStock = 2;

    public static Reachability Reachable(int seeds = 500, int startingGold = 99,
        int startingRelics = 1, int startingDeckSize = 10)
    {
        var gold = new List<int>();
        var relics = new List<int>();
        var deck = new List<int>();
        var deckFree = new List<int>();
        var events = new List<int>();
        var fights = new List<int>();
        var shops = new List<int>();
        var elites = new List<int>();

        for (int s = 0; s < seeds; s++)
        {
            var rng = new Random(s);
            var cardRun = (Score: 0, Gold: startingGold);
            var relicRun = (Score: 0, Gold: startingGold);
            int seedGold = startingGold, seedEvents = 0, seedFights = 0;
            int seedShops = 0, seedElites = 0;

            foreach (var act in ActDatabase.All)
            {
                var nodes = MapGenerator.Generate(rng, act);

                cardRun = BestForward(nodes, act, cardRun, CardsAt);
                relicRun = BestForward(nodes, act, relicRun, RelicsAt);

                seedGold += MaxAlong(nodes, n => NodeGold(n, act));
                seedEvents += MaxAlong(nodes, n => n.Type == MapNodeType.Event ? 1 : 0);
                seedShops += MaxAlong(nodes, n => n.Type == MapNodeType.Shop ? 1 : 0);
                seedElites += MaxAlong(nodes, n => n.Type == MapNodeType.Elite ? 1 : 0);
                seedFights += MaxAlong(nodes, n =>
                    n.Type is MapNodeType.Combat or MapNodeType.Elite or MapNodeType.Boss ? 1 : 0);
            }

            gold.Add(seedGold);
            relics.Add(startingRelics + relicRun.Score);
            deck.Add(startingDeckSize + cardRun.Score);
            deckFree.Add(startingDeckSize + seedFights);
            events.Add(seedEvents);
            fights.Add(seedFights);
            shops.Add(seedShops);
            elites.Add(seedElites);
        }

        return new Reachability(seeds,
            new Metric("gold", gold),
            new Metric("relics", relics),
            new Metric("deck size", deck),
            new Metric("deck size (rewards only)", deckFree),
            new Metric("event rooms", events),
            new Metric("fights", fights),
            new Metric("shops", shops),
            new Metric("elites", elites));
    }

    // One card from each fight's three-card reward pick, plus whatever the
    // purse covers at a shop.
    private static (int Gained, int Spent) CardsAt(MapNode n, int gold)
    {
        if (n.Type is MapNodeType.Combat or MapNodeType.Elite or MapNodeType.Boss) return (1, 0);
        if (n.Type != MapNodeType.Shop) return (0, 0);
        int bought = Math.Min(ShopCardsInStock, gold / ShopCardPrice);
        return (bought, bought * ShopCardPrice);
    }

    // The guaranteed relic on every Elite and Boss reward, one per Treasure
    // node, plus shop stock.
    private static (int Gained, int Spent) RelicsAt(MapNode n, int gold)
    {
        if (n.Type is MapNodeType.Elite or MapNodeType.Boss or MapNodeType.Treasure) return (1, 0);
        if (n.Type != MapNodeType.Shop) return (0, 0);
        int bought = Math.Min(ShopRelicsInStock, gold / ShopRelicPrice);
        return (bought, bought * ShopRelicPrice);
    }

    // Forward pass over one act's DAG carrying (score, gold) from floor 0.
    // Forward rather than the backward MaxAlong above because a shop's payout
    // depends on the gold earned *before* reaching it, which a backward
    // recurrence cannot see.
    //
    // States are ranked lexicographically - more score wins, gold breaks ties -
    // instead of keeping a full Pareto frontier. So what this returns is a
    // witness: a path that actually achieves the number, possibly not the best
    // one. For "is this threshold earnable at all" a witness is the whole
    // answer, and a witness that clears the bar settles it either way.
    private static (int Score, int Gold) BestForward(
        IReadOnlyList<MapNode> nodes, ActDefinition act, (int Score, int Gold) start,
        Func<MapNode, int, (int Gained, int Spent)> reward)
    {
        var incoming = new Dictionary<string, (int Score, int Gold)>();
        foreach (var n in nodes.Where(n => n.Floor == 0)) incoming[n.Id] = start;

        var best = start;
        foreach (var n in nodes.OrderBy(n => n.Floor))
        {
            if (!incoming.TryGetValue(n.Id, out var state)) continue;

            int gold = state.Gold + NodeGold(n, act);
            var (gained, spent) = reward(n, gold);
            var here = (Score: state.Score + gained, Gold: gold - spent);

            if (Better(here, best)) best = here;
            foreach (var next in n.NextNodeIds)
            {
                if (!incoming.TryGetValue(next, out var current) || Better(here, current))
                {
                    incoming[next] = here;
                }
            }
        }

        return best;
    }

    private static bool Better((int Score, int Gold) a, (int Score, int Gold) b) =>
        a.Score != b.Score ? a.Score > b.Score : a.Gold > b.Gold;

    // RunScore's two tier tables are private, and reading them back out beats
    // both alternatives: a second copy of the numbers here drifts silently the
    // first time somebody retunes one, and widening RunScore's API for a debug
    // tool's benefit puts a seam in shipping code to serve a report.
    //
    // Tuple element names are compile-time only, so this has to cast to the
    // underlying ValueTuple - GetField("Size") comes back null and the caller
    // reads "no tiers", which looks like a passing check.
    public static IReadOnlyList<int> DeckSizeThresholds() => Tiers("DeckSizeTiers");
    public static IReadOnlyList<int> GoldThresholds() => Tiers("GoldTiers");

    private static IReadOnlyList<int> Tiers(string fieldName)
    {
        var field = typeof(RunScore).GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return field?.GetValue(null) is not ValueTuple<int, int, string>[] tiers
            ? Array.Empty<int>()
            : tiers.Select(t => t.Item1).ToList();
    }

    // -------------------------------------------------------------- utilities

    private static double Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }
}
