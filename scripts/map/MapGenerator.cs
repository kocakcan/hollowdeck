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
        if (floor == floorCount - 1) return new List<MapNode> { MakeNode(floor, 0, MapNodeType.Boss, act, rng) };
        if (floor == floorCount - 2) return new List<MapNode> { MakeNode(floor, 0, MapNodeType.Rest, act, rng) };

        int count = floor == 0 ? MinNodesPerFloor : rng.Next(MinNodesPerFloor, MaxNodesPerFloor + 1);
        var nodes = new List<MapNode>();
        for (int c = 0; c < count; c++)
        {
            var type = floor == 0 ? MapNodeType.Combat : PickNodeType(floor, rng);
            nodes.Add(MakeNode(floor, c, type, act, rng));
        }
        return nodes;
    }

    private static MapNodeType PickNodeType(int floor, Random rng)
    {
        // Elites don't show up on the first branching floor - too early for
        // a tougher-than-normal fight before the player has any relics/cards.
        var weights = new List<(MapNodeType type, int weight)>
        {
            (MapNodeType.Combat, 50),
            (MapNodeType.Shop, 12),
            (MapNodeType.Treasure, 12),
            (MapNodeType.Rest, 12),
            (MapNodeType.Event, 10),
        };
        weights.Add(floor >= 2 ? (MapNodeType.Elite, 14) : (MapNodeType.Combat, 14));

        int total = weights.Sum(w => w.weight);
        int roll = rng.Next(total);
        foreach (var (type, weight) in weights)
        {
            if (roll < weight) return type;
            roll -= weight;
        }
        return MapNodeType.Combat;
    }

    private static MapNode MakeNode(int floor, int column, MapNodeType type, ActDefinition act, Random rng)
    {
        var node = new MapNode { Id = $"f{floor}_{column}", Floor = floor, Column = column, Type = type };
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
