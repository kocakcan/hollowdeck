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
    // How wide a branching floor gets. 5 is not a taste call and not the
    // genre's number - Slay the Spire is seven wide and scrolls. This map does
    // not scroll (Phase 4 made it fill one canvas deliberately), and MapScreen
    // derives the vertical pitch from the band it has left, so width is spent
    // directly out of the space between nodes: at 1152x648 a 6-wide floor puts
    // 58px of pitch under 64px nodes once the run is carrying three rows of
    // relics, and a 7-wide floor overlaps with no relics at all. 5 is the
    // widest the canvas holds.
    //
    // So this constant and MapScreen's layout are coupled, which nothing about
    // reading either file alone would suggest: raising it without re-deriving
    // the pitch there draws nodes on top of each other, and every suite stays
    // green while it happens. MapSmokeTest.TestFloorWidthsAreInBand restates
    // the band rather than importing it from here, so widening has to be
    // argued for in two places instead of inherited in one.
    private const int MinNodesPerFloor = 3;
    private const int MaxNodesPerFloor = 5;

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
        //
        // Floor 0 stays pinned at MinNodesPerFloor rather than rolling up to
        // MaxNodesPerFloor with everything else, and that is a design rule
        // rather than an oversight. Every node on it is a Combat by
        // construction (see the tuple below), so a wider opening floor is a
        // wider choice between rooms that are the same room - five identical
        // doors, which is the opposite of what the width is being bought for.
        // The fan-out starts on floor 1, where the types start differing.
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
    //
    // None of this changed when the floor width band went 3-4 to 3-5, but what
    // it *does* changed enough to be worth writing down, because the width
    // gap between adjacent floors can now be 2 rather than 1:
    //
    //   - Widening a floor does not widen the fan-in. Across a 3 -> 5
    //     transition the primaries land on columns 0, 2 and 4, so columns 1
    //     and 3 get no primary at all and are reachable only by a +-1
    //     secondary from one of their two neighbours - 55% * 50% = 27.5%
    //     each, so a bit over half of all 3 -> 5 transitions hand at least one
    //     interior node to the backfill below. 4 -> 5 is the same shape for
    //     column 2.
    //   - The backfill is not the long crossing edge that suggests. It inverts
    //     the same projection, so the orphaned to-column 1 wires back to
    //     from-column 0 and to-column 3 to from-column 2: half a row in screen
    //     space, crossing nothing that was not already crossed. What a wider
    //     floor actually costs visually is steepness, since a 5-wide floor is
    //     four pitches tall against a 3-wide floor's two.
    //   - Out-degree is deliberately untouched (one edge, plus 55% of a
    //     second), so a wider floor is a *thinner* graph per node: more nodes
    //     whose only parent is the backfill. That is the accepted cost of
    //     changing one thing at a time - width and connectivity have separate
    //     balance signatures, and moving both at once makes any drift in the
    //     report unattributable to either. Don't "fix" it by raising the 55%
    //     without measuring that on its own.
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

    // Math.Round(double) is banker's rounding - ties go to even - and at a 3-5
    // band that is now load-bearing in four transitions (3->5, 4->5, 5->4,
    // 5->3) rather than the two a 3-4 band could reach. It is what keeps
    // 5 -> 3 symmetric: columns 0,1,2,3,4 project to 0,0,1,2,2 only because
    // the 0.5 tie rounds down and the 1.5 tie rounds up. Passing
    // MidpointRounding.AwayFromZero here - the "obvious" fix if this ever
    // looks wrong - would bias every odd-to-odd transition toward the bottom
    // of the screen, and nothing in the suite is shaped to notice.
    private static int ProjectColumn(float column, int fromCount, int toCount)
    {
        if (toCount == 1) return 0;
        if (fromCount <= 1) return (int)Math.Round((toCount - 1) / 2.0);
        float ratio = column / (fromCount - 1);
        return Math.Clamp((int)Math.Round(ratio * (toCount - 1)), 0, toCount - 1);
    }
}
