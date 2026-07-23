using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for MapGenerator's branching-DAG output: exactly one Boss
// node on the last floor, a forced single Rest node right before it, no
// orphaned (unreachable) nodes, and that MapScreen.tscn actually renders the
// generated graph. Run via `godot --headless scenes/debug/MapSmokeTest.tscn`.
public partial class MapSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestSingleSeedShape();
        TestManySeedsStayConnected();
        TestMapScreenRendersGraph();

        GD.Print($"MapSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void TestSingleSeedShape()
    {
        var nodes = MapGenerator.Generate(new Random(42));
        AssertShape(nodes, "seed42_");
    }

    private void TestManySeedsStayConnected()
    {
        bool allOk = true;
        string detail = "";
        for (int seed = 0; seed < 25 && allOk; seed++)
        {
            var nodes = MapGenerator.Generate(new Random(seed));
            var (ok, why) = ValidateConnectivity(nodes);
            if (!ok) { allOk = false; detail = $"seed {seed}: {why}"; }
        }
        Check("many_seeds_stay_fully_connected", allOk, detail);
    }

    private void AssertShape(List<MapNode> nodes, string prefix)
    {
        int lastFloor = nodes.Max(n => n.Floor);
        var bossNodes = nodes.Where(n => n.Type == MapNodeType.Boss).ToList();
        Check($"{prefix}exactly_one_boss", bossNodes.Count == 1, $"count={bossNodes.Count}");
        Check($"{prefix}boss_on_last_floor", bossNodes.Count == 1 && bossNodes[0].Floor == lastFloor,
            bossNodes.Count == 1 ? $"floor={bossNodes[0].Floor}, last={lastFloor}" : "no boss node");

        var preBossFloor = nodes.Where(n => n.Floor == lastFloor - 1).ToList();
        Check($"{prefix}pre_boss_floor_is_single_rest_node",
            preBossFloor.Count == 1 && preBossFloor[0].Type == MapNodeType.Rest,
            $"count={preBossFloor.Count}, types=[{string.Join(",", preBossFloor.Select(n => n.Type))}]");

        var (ok, why) = ValidateConnectivity(nodes);
        Check($"{prefix}fully_connected", ok, why);
    }

    // Every node except the last floor's must have >=1 outgoing edge, and
    // every node except floor 0's (implicitly reachable from the run start)
    // must have >=1 incoming edge from some earlier-floor node.
    private (bool ok, string why) ValidateConnectivity(List<MapNode> nodes)
    {
        int lastFloor = nodes.Max(n => n.Floor);
        var incoming = new HashSet<string>(nodes.SelectMany(n => n.NextNodeIds));

        foreach (var node in nodes)
        {
            if (node.Floor != lastFloor && node.NextNodeIds.Count == 0)
                return (false, $"node {node.Id} (floor {node.Floor}) has no outgoing edges");
            if (node.Floor != 0 && !incoming.Contains(node.Id))
                return (false, $"node {node.Id} (floor {node.Floor}) is unreachable");
        }
        return (true, "");
    }

    private void TestMapScreenRendersGraph()
    {
        RunState.Gold = 0;
        RunState.Relics = new List<RelicInstance>();
        RunState.MapNodes = MapGenerator.Generate(new Random(7));
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();

        var packed = GD.Load<PackedScene>("res://scenes/MapScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var nodeButtons = instance.GetNode<Control>("NodeButtons");
        int floor0Count = RunState.MapNodes.Count(n => n.Floor == 0);
        int enabledCount = nodeButtons.GetChildren().Cast<Button>().Count(b => !b.Disabled);

        Check("map_screen_renders_one_button_per_node",
            nodeButtons.GetChildCount() == RunState.MapNodes.Count,
            $"buttons={nodeButtons.GetChildCount()}, nodes={RunState.MapNodes.Count}");
        Check("map_screen_enables_only_floor0_nodes_initially", enabledCount == floor0Count,
            $"enabled={enabledCount}, floor0={floor0Count}");

        instance.QueueFree();
    }
}
