using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Map;

// Builds a small layered DAG (Slay-the-Spire-style branching path), seeded
// from RngStreams.Map so map shape is reproducible per run seed like combat
// shuffles/enemy AI already are (CLAUDE.md risk #2).
//
// Everything act-specific - how many floors, which encounters, which boss -
// comes from the ActDefinition passed in, so a new act is a row in
// data/acts/acts.json rather than more code here (the same content-is-data
// split cards/relics/enemies already use). The boss is drawn from the act's
// pool with the same rng as the map shape, so which boss ends an act is part
// of the run seed too.
public static class MapGenerator
{
    private const int MinNodesPerFloor = 3;
    private const int MaxNodesPerFloor = 4;

    public static List<MapNode> Generate(Random rng, ActDefinition act)
    {
        int floorCount = act.FloorCount;
        var floors = new List<List<MapNode>>();

        for (int f = 0; f < floorCount; f++)
        {
            floors.Add(BuildFloor(f, floorCount, act, rng));
        }

        for (int f = 0; f < floorCount - 1; f++)
        {
            ConnectFloors(floors[f], floors[f + 1], rng);
        }

        return floors.SelectMany(floor => floor).ToList();
    }

    private static List<MapNode> BuildFloor(int floor, int floorCount, ActDefinition act, Random rng)
    {
        // Floor 0 is always a soft-open Combat floor; the floor right before
        // the boss is always a forced single Rest node (guaranteed reachable
        // from every path since it's the sole node on its floor - see
        // ConnectFloors); the boss floor is always a single Boss node.
        if (floor == floorCount - 1) return new List<MapNode> { MakeNode(floor, 0, MapNodeType.Boss, false, act, rng) };
        if (floor == floorCount - 2) return new List<MapNode> { MakeNode(floor, 0, MapNodeType.Rest, false, act, rng) };

        int count = floor == 0 ? MinNodesPerFloor : rng.Next(MinNodesPerFloor, MaxNodesPerFloor + 1);
        var nodes = new List<MapNode>();
        for (int c = 0; c < count; c++)
        {
            var (type, concealed) = floor == 0
                ? (MapNodeType.Combat, false)
                : PickNodeType(floor, rng);
            nodes.Add(MakeNode(floor, c, type, concealed, act, rng));
        }
        return nodes;
    }

    // The three forced floors above never reach here, which is what keeps the
    // boss, the pre-boss Rest and the opening Combat floor permanently legible
    // for free - a "?" hiding the one landmark the whole act routes toward
    // would be fog rather than a gamble. MapSmokeTest asserts it rather than
    // leaving it to this comment.
    private static (MapNodeType Type, bool Concealed) PickNodeType(int floor, Random rng)
    {
        // Elites don't show up on the first branching floor - too early for
        // a tougher-than-normal fight before the player has any relics/cards.
        // The four utility weights are down from a flat 12/12/12/10 to pay for
        // the "?" slot below. A "?" comes back as a fight only one time in
        // five, so carving its weight out of the whole table taxes fights:
        // measured, that cost 1.1 reward picks and 51 gold a run and pushed
        // RunScore's Encyclopedian from reachable on 23% of seeds to 15%.
        // Paying for the fog out of the rooms it mostly turns into keeps the
        // run the same length.
        //
        // Elite is 15 rather than 14 for the denominator, not for itself. An
        // unchanged weight is not an unchanged share once the table grows
        // 110 -> 119, and Elite is the one type the "?" table cannot hand back
        // (see PickConcealedType), so Combat breaks even on its 20% share
        // while Elite would quietly lose 7% of its frequency - measured, 1.9
        // elites a run down to 1.8 and the best path 10 down to 8. Nothing
        // would have caught it: BalanceSmokeTest bands elite *cost ratios*,
        // never how often an elite is offered.
        var weights = new List<(MapNodeType type, int weight)>
        {
            (MapNodeType.Combat, 50),
            (MapNodeType.Shop, 10),
            (MapNodeType.Treasure, 10),
            (MapNodeType.Rest, 11),
            (MapNodeType.Event, 5),
        };
        weights.Add(floor >= 2 ? (MapNodeType.Elite, 15) : (MapNodeType.Combat, 15));

        // A slot in this table rather than a coin flip layered on top of it, so
        // "how much of the map is unknown" is authored against the same
        // denominator every other node type is and moving it trades against
        // them visibly rather than diluting all six at once.
        int total = weights.Sum(w => w.weight) + ConcealedWeight;
        int roll = rng.Next(total);
        foreach (var (type, weight) in weights)
        {
            if (roll < weight) return (type, false);
            roll -= weight;
        }
        return (PickConcealedType(rng), true);
    }

    private const int ConcealedWeight = 18;

    // What is actually behind a "?", drawn from its own table rather than from
    // the one above. Two exclusions carry the design:
    //
    //   Elite - an unadvertised elite is a fight the player committed to
    //           without the one piece of information that decides whether to
    //           take it. A "?" should be a gamble, not an ambush.
    //   Boss  - structural; there is exactly one and it owns its own floor.
    //
    // Event-heavy because that is what makes the slot worth having: events are
    // the rarest room in the game (a mean of 1.6 per run before this) and the
    // only one whose *content* is also a surprise, so hiding one behind a "?"
    // compounds rather than merely delays.
    private static MapNodeType PickConcealedType(Random rng)
    {
        var weights = new List<(MapNodeType type, int weight)>
        {
            (MapNodeType.Event, 50),
            (MapNodeType.Combat, 20),
            (MapNodeType.Shop, 12),
            (MapNodeType.Treasure, 12),
            (MapNodeType.Rest, 6),
        };

        int roll = rng.Next(weights.Sum(w => w.weight));
        foreach (var (type, weight) in weights)
        {
            if (roll < weight) return type;
            roll -= weight;
        }
        return MapNodeType.Event;
    }

    private static MapNode MakeNode(
        int floor, int column, MapNodeType type, bool concealed, ActDefinition act, Random rng)
    {
        var node = new MapNode
        {
            Id = $"f{floor}_{column}", Floor = floor, Column = column, Type = type, Concealed = concealed,
        };
        if (type is MapNodeType.Combat or MapNodeType.Elite or MapNodeType.Boss)
        {
            node.EnemyIds = type switch
            {
                MapNodeType.Boss => new List<string> { act.BossIds[rng.Next(act.BossIds.Count)] },
                MapNodeType.Elite => new List<string>(act.EliteEncounters[rng.Next(act.EliteEncounters.Count)]),
                _ => new List<string>(act.NormalEncounters[rng.Next(act.NormalEncounters.Count)]),
            };
        }
        return node;
    }

    // Projects each node's column onto the next floor's column range and
    // connects it to its nearest 1-2 neighbours there, then backfills any
    // next-floor node that ended up with no incoming edge (so nothing is
    // ever unreachable) by wiring it to its nearest current-floor node.
    private static void ConnectFloors(List<MapNode> from, List<MapNode> to, Random rng)
    {
        foreach (var node in from)
        {
            int primary = ProjectColumn(node.Column, from.Count, to.Count);
            node.NextNodeIds.Add(to[primary].Id);

            if (to.Count > 1 && rng.Next(100) < 55)
            {
                int offset = rng.Next(2) == 0 ? -1 : 1;
                int secondary = Math.Clamp(primary + offset, 0, to.Count - 1);
                if (!node.NextNodeIds.Contains(to[secondary].Id)) node.NextNodeIds.Add(to[secondary].Id);
            }
        }

        var reached = new HashSet<string>(from.SelectMany(n => n.NextNodeIds));
        foreach (var target in to)
        {
            if (reached.Contains(target.Id)) continue;
            int nearestFrom = ProjectColumn(target.Column, to.Count, from.Count);
            from[nearestFrom].NextNodeIds.Add(target.Id);
        }
    }

    private static int ProjectColumn(float column, int fromCount, int toCount)
    {
        if (toCount == 1) return 0;
        if (fromCount <= 1) return (int)Math.Round((toCount - 1) / 2.0);
        float ratio = column / (fromCount - 1);
        return Math.Clamp((int)Math.Round(ratio * (toCount - 1)), 0, toCount - 1);
    }
}
