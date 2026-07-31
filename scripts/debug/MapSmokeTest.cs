using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;
using Hollowdeck.UI;

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

        ActDatabase.LoadAll();

        TestSingleSeedShape();
        TestManySeedsStayConnected();
        TestBossPickIsSeedDeterministic();
        TestEncounterPoolsResolve();
        TestMapScreenRendersGraph();
        TestLongestActFitsOnScreen();

        GD.Print($"MapSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    // Every act, not just the first: floor counts and encounter pools now come
    // from data, so an act authored with a too-short floorCount or a boss pool
    // that doesn't match its own ids has to fail here rather than mid-run.
    private void TestSingleSeedShape()
    {
        foreach (var act in ActDatabase.All)
        {
            var nodes = MapGenerator.Generate(new Random(42), act);
            AssertShape(nodes, $"{act.Id}_seed42_");

            Check($"{act.Id}_honours_authored_floor_count",
                nodes.Max(n => n.Floor) + 1 == act.FloorCount,
                $"generated={nodes.Max(n => n.Floor) + 1}, authored={act.FloorCount}");

            var boss = nodes.Single(n => n.Type == MapNodeType.Boss);
            Check($"{act.Id}_boss_comes_from_its_own_pool",
                boss.EnemyIds.Count == 1 && act.BossIds.Contains(boss.EnemyIds[0]),
                $"boss=[{string.Join(",", boss.EnemyIds)}], pool=[{string.Join(",", act.BossIds)}]");
        }
    }

    private void TestManySeedsStayConnected()
    {
        bool allOk = true;
        string detail = "";
        foreach (var act in ActDatabase.All)
        {
            for (int seed = 0; seed < 25 && allOk; seed++)
            {
                var nodes = MapGenerator.Generate(new Random(seed), act);
                var (ok, why) = ValidateConnectivity(nodes);
                if (!ok) { allOk = false; detail = $"{act.Id} seed {seed}: {why}"; }
            }
        }
        Check("many_seeds_stay_fully_connected", allOk, detail);
    }

    // The boss pick has to be part of the seed like the map shape is, or a
    // "same seed, same run" promise breaks the moment an act has two bosses.
    private void TestBossPickIsSeedDeterministic()
    {
        bool stable = true;
        string detail = "";
        foreach (var act in ActDatabase.All)
        {
            for (int seed = 0; seed < 10 && stable; seed++)
            {
                var first = MapGenerator.Generate(new Random(seed), act).Single(n => n.Type == MapNodeType.Boss);
                var second = MapGenerator.Generate(new Random(seed), act).Single(n => n.Type == MapNodeType.Boss);
                if (first.EnemyIds[0] != second.EnemyIds[0])
                {
                    stable = false;
                    detail = $"{act.Id} seed {seed}: {first.EnemyIds[0]} then {second.EnemyIds[0]}";
                }
            }
        }
        Check("boss_pick_is_reproducible_for_a_seed", stable, detail);

        // ...and not pinned to one boss either, or the pool is decorative.
        var pooled = ActDatabase.All.Where(a => a.BossIds.Count > 1).ToList();
        bool anyVariety = pooled.All(act => Enumerable.Range(0, 40)
            .Select(seed => MapGenerator.Generate(new Random(seed), act).Single(n => n.Type == MapNodeType.Boss).EnemyIds[0])
            .Distinct().Count() > 1);
        Check("multi_boss_acts_actually_roll_different_bosses", pooled.Count > 0 && anyVariety,
            $"acts with a pool: {pooled.Count}");
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
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 33;
        RunState.Relics = new List<RelicInstance>();
        RunState.ActIndex = 0;
        RunState.MapNodes = MapGenerator.Generate(new Random(7), ActDatabase.At(0));
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();

        var packed = GD.Load<PackedScene>("res://scenes/MapScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        // HP moved into the run-status block ScreenChrome builds; the map's
        // own hand-placed Gold/HP labels and relic strip are gone.
        var hpLabel = instance.GetNode<Label>(ScreenChrome.HpLabelPath);
        Check("map_screen_shows_current_hp", hpLabel.Text.Contains("33") && hpLabel.Text.Contains("50"),
            $"text='{hpLabel.Text}'");

        var nodeButtons = instance.GetNode<Control>("NodeButtons");
        int floor0Count = RunState.MapNodes.Count(n => n.Floor == 0);
        int enabledCount = nodeButtons.GetChildren().Cast<Button>().Count(b => !b.Disabled);

        Check("map_screen_renders_one_button_per_node",
            nodeButtons.GetChildCount() == RunState.MapNodes.Count,
            $"buttons={nodeButtons.GetChildCount()}, nodes={RunState.MapNodes.Count}");
        Check("map_screen_enables_only_floor0_nodes_initially", enabledCount == floor0Count,
            $"enabled={enabledCount}, floor0={floor0Count}");

        var actLabel = instance.GetChildren().OfType<Label>()
            .FirstOrDefault(l => l.Text.StartsWith("Act "));
        Check("map_screen_names_the_current_act",
            actLabel is not null && actLabel.Text.Contains(ActDatabase.At(0).Name),
            $"label='{actLabel?.Text}'");

        instance.QueueFree();
    }

    // Node ids are only ever resolved through EnemyDatabase at fight start, so
    // a typo in an act's encounter pool would otherwise surface as a crash on
    // entering that node, several minutes into a run.
    private void TestEncounterPoolsResolve()
    {
        var known = EnemyDatabase.All.Select(e => e.Id).ToHashSet();
        var unknown = new List<string>();
        foreach (var act in ActDatabase.All)
        {
            var referenced = act.NormalEncounters.Concat(act.EliteEncounters).SelectMany(g => g)
                .Concat(act.BossIds);
            unknown.AddRange(referenced.Where(id => !known.Contains(id)).Select(id => $"{act.Id}:{id}"));
        }
        Check("every_act_encounter_id_exists", unknown.Count == 0, string.Join(", ", unknown));

        foreach (var act in ActDatabase.All)
        {
            Check($"{act.Id}_has_pools_for_every_node_type",
                act.NormalEncounters.Count > 0 && act.EliteEncounters.Count > 0 && act.BossIds.Count > 0,
                $"normals={act.NormalEncounters.Count}, elites={act.EliteEncounters.Count}, bosses={act.BossIds.Count}");
        }
    }

    // Floor spacing is derived from the act's length (MapScreen.RightMargin):
    // the longest act's 10 floors at the old fixed 130px would have put the
    // boss node ~150px past the right edge of the 1152px design width.
    private void TestLongestActFitsOnScreen()
    {
        var longest = ActDatabase.All.OrderByDescending(a => a.FloorCount).First();
        RunState.ActIndex = ActDatabase.All.ToList().IndexOf(longest);
        RunState.MapNodes = MapGenerator.Generate(new Random(11), longest);
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();

        var instance = GD.Load<PackedScene>("res://scenes/MapScreen.tscn").Instantiate();
        AddChild(instance);

        const float designWidth = 1152f;
        const float designHeight = 648f;
        var buttons = instance.GetNode<Control>("NodeButtons").GetChildren().OfType<Button>().ToList();
        var overflowing = buttons
            .Where(b => b.Position.X < 0f || b.Position.X + b.Size.X > designWidth)
            .Select(b => $"x={b.Position.X}+{b.Size.X}")
            .ToList();
        Check($"{longest.Id}_all_nodes_within_design_width", overflowing.Count == 0,
            string.Join(", ", overflowing));

        // Column spacing is derived from the height the same way (ROADMAP
        // Phase 4): the graph now fills the band between the run-status block
        // and the footer instead of stacking down from a fixed y=60, so it can
        // run off the *bottom* if the derivation is wrong - which the width
        // check would never have caught.
        var tooTall = buttons
            .Where(b => b.Position.Y < 0f || b.Position.Y + b.Size.Y > designHeight)
            .Select(b => $"y={b.Position.Y}+{b.Size.Y}")
            .ToList();
        Check($"{longest.Id}_all_nodes_within_design_height", tooTall.Count == 0,
            string.Join(", ", tooTall));

        // And it should actually use that band - the defect being fixed was a
        // map whose bottom 45% was empty, which no "fits on screen" assertion
        // can see. 60% of the design height is comfortably below what a
        // centred multi-row act produces and well above the old layout.
        float top = buttons.Min(b => b.Position.Y);
        float bottom = buttons.Max(b => b.Position.Y + b.Size.Y);
        Check($"{longest.Id}_graph_fills_the_vertical_band", bottom - top > designHeight * 0.6f,
            $"spans {bottom - top:F0}px of {designHeight:F0}");

        RunState.ActIndex = 0;
        instance.QueueFree();
    }
}
