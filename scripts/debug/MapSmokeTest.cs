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

        AscensionDatabase.LoadAll();

        TestSingleSeedShape();
        TestFloorWidthsAreInBand();
        TestManySeedsStayConnected();
        TestBossPickIsSeedDeterministic();
        TestConcealedNodesAreLegal();
        TestEncounterPoolsResolve();
        TestMapScreenRendersGraph();
        TestMapScreenNodeStates();
        TestConcealedNodesHideTheirTypeUntilEntered();
        TestLongestActFitsOnScreen();
        TestNodesClearTheRunStatusBlock();
        TestNodesStayApartAtEveryRelicCount();

        GD.Print($"MapSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // Returns the condition so a caller can bail before dereferencing whatever
    // the failed check was about. A test that throws inside _Ready never
    // reaches GetTree().Quit(), so Godot sits in an idle main loop and the
    // sweep reports TIMEOUT - which per CLAUDE.md means a restructured .tscn,
    // not a red assertion. A failing test must fail, not hang.
    private bool Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
        return condition;
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

    // The four node states MapScreen.BuildButtons derives, all live at once.
    // Seeded exactly like ScreenShot.SeedMap so the assertions here and the
    // screenshot a human looks at describe the same picture: one visited node
    // (which is also the current one), its successors reachable, every other
    // node untraversed, and the boss unreachable for the whole act.
    //
    // The focus assertion is the one with a bug behind it. Disabled is not
    // enough on its own - a mouse press on a disabled Control still grabs key
    // focus, and Godot paints the focus stylebox over the disabled one, so
    // clicking an unreachable node used to leave the 4px FocusRing box on a
    // move the player could not make.
    private void TestMapScreenNodeStates()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 40;
        RunState.Relics = new List<RelicInstance>();
        RunState.ActIndex = 0;
        RunState.MapNodes = MapGenerator.Generate(new Random(7), ActDatabase.At(0));
        var start = RunState.MapNodes.First(n => n.Floor == 0);
        RunState.CurrentNodeId = start.Id;
        RunState.VisitedNodeIds = new HashSet<string> { start.Id };

        var packed = GD.Load<PackedScene>("res://scenes/MapScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        // OfType, not Cast: with CurrentNodeId set, BuildCurrentNodeRing adds a
        // Panel to this container (at child index 0, via MoveChild). The buttons
        // that remain are still in RunState.MapNodes order, which is what lets
        // them be zipped back onto their nodes below.
        var buttons = instance.GetNode<Control>("NodeButtons")
            .GetChildren().OfType<Button>().ToList();
        var reachable = RunState.GetMapNode(start.Id).NextNodeIds.ToHashSet();
        var paired = RunState.MapNodes.Zip(buttons, (node, button) => (node, button)).ToList();

        // Everything below indexes into `paired`, so a broken pairing has to
        // stop the test rather than let First() throw out of _Ready.
        if (!Check("map_screen_pairs_every_node_with_a_button",
                buttons.Count == RunState.MapNodes.Count,
                $"buttons={buttons.Count}, nodes={RunState.MapNodes.Count}"))
        {
            instance.QueueFree();
            return;
        }

        var untraversed = paired.Where(p =>
            p.node.Type != MapNodeType.Boss
            && !reachable.Contains(p.node.Id)
            && !RunState.VisitedNodeIds.Contains(p.node.Id)).ToList();
        Check("map_screen_dims_untraversed_nodes",
            untraversed.Count > 0 && untraversed.All(p => p.button.Modulate.R < 0.6f),
            $"untraversed={untraversed.Count}, " +
            $"bright={untraversed.Count(p => p.button.Modulate.R >= 0.6f)}");

        var reachableButtons = paired.Where(p => reachable.Contains(p.node.Id)).ToList();
        Check("map_screen_keeps_reachable_nodes_bright",
            reachableButtons.Count > 0 && reachableButtons.All(p => p.button.Modulate.R > 0.9f),
            $"reachable={reachableButtons.Count}, " +
            $"dim={reachableButtons.Count(p => p.button.Modulate.R <= 0.9f)}");

        // Both halves: the visited node stops being the dimmed one *and* picks
        // up the trail border. Asserting only the modulate would let the
        // stylebox override silently go missing, and the two are what separate
        // "walked through" from "cannot reach" once both are at full brightness.
        var visited = paired.First(p => p.node.Id == start.Id).button;
        var visitedBorder = (visited.GetThemeStylebox("disabled") as StyleBoxFlat)?.BorderColor;
        Check("map_screen_marks_visited_nodes_as_a_trail",
            visited.Modulate.R > 0.9f && visitedBorder == PixelSpec.Ramp.G0,
            $"modulate={visited.Modulate}, border={visitedBorder}");

        var boss = paired.First(p => p.node.Type == MapNodeType.Boss).button;
        Check("map_screen_keeps_the_boss_bright_while_unreachable",
            boss.Disabled && boss.Modulate.R > 0.9f,
            $"disabled={boss.Disabled}, modulate={boss.Modulate}");

        var focusable = paired.Where(p => p.button.FocusMode != Control.FocusModeEnum.None).ToList();
        Check("map_screen_denies_focus_to_unreachable_nodes",
            focusable.Count == reachableButtons.Count
            && focusable.All(p => reachable.Contains(p.node.Id)),
            $"focusable={focusable.Count}, reachable={reachableButtons.Count}, " +
            $"illegal=[{string.Join(",", focusable.Where(p => !reachable.Contains(p.node.Id)).Select(p => p.node.Id))}]");

        instance.QueueFree();
    }

    // Every type a "?" is allowed to turn out to be. Held here rather than
    // read back off MapGenerator on purpose: the exclusions are the design
    // (an unadvertised Elite is an ambush, not a gamble), so widening the
    // generator's table has to fail here and be argued for, not inherited.
    private static readonly HashSet<MapNodeType> LegalConcealedTypes = new()
    {
        MapNodeType.Event, MapNodeType.Combat, MapNodeType.Shop,
        MapNodeType.Treasure, MapNodeType.Rest,
    };

    // A "?" node hides its type from the player, not from the rest of the
    // codebase: MapNode.Type is rolled at generation like everything else, so
    // BalanceModel keeps counting the node correctly and quitting mid-fight
    // cannot re-roll it. What that buys has to be paid for with these
    // invariants, none of which the generator's shape enforces on its own.
    private void TestConcealedNodesAreLegal()
    {
        var seen = new HashSet<MapNodeType>();
        int concealedCount = 0;
        int rollableCount = 0;
        var illegalType = new List<string>();
        var illegalFloor = new List<string>();
        var missingEnemies = new List<string>();

        foreach (var act in ActDatabase.All)
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var nodes = MapGenerator.Generate(new Random(seed), act);
                int lastFloor = nodes.Max(n => n.Floor);

                // Only the branching floors are eligible, so this is the
                // denominator the "?" weight actually competes in - counting
                // against every node would fold the three forced floors into
                // the rate and make it drift with an act's floor count.
                rollableCount += nodes.Count(n => n.Floor > 0 && n.Floor < lastFloor - 1);

                foreach (var node in nodes.Where(n => n.Concealed))
                {
                    concealedCount++;
                    seen.Add(node.Type);

                    if (!LegalConcealedTypes.Contains(node.Type))
                        illegalType.Add($"{act.Id} seed {seed}: {node.Id} is {node.Type}");

                    // Floor 0, the forced pre-boss Rest and the boss itself all
                    // bypass PickNodeType, so this is asserting the bypass rather
                    // than a filter - and it is the assertion that notices if
                    // BuildFloor's forced floors are ever routed through it.
                    if (node.Floor == 0 || node.Floor >= lastFloor - 1)
                        illegalFloor.Add($"{act.Id} seed {seed}: {node.Id} on floor {node.Floor}");

                    // The seam the whole design exists to avoid. EnemyIds are
                    // drawn in MakeNode off the act's pools; a concealed Combat
                    // that skipped that draw would open as an empty fight, and
                    // nothing between here and CombatManager would say so.
                    if (node.Type == MapNodeType.Combat && node.EnemyIds.Count == 0)
                        missingEnemies.Add($"{act.Id} seed {seed}: {node.Id}");
                }
            }
        }

        // Banded, not just non-zero. "At least one over 120 maps" passes with
        // the weight set to 1, where a "?" turns up in about one run in seven
        // and the mechanic is effectively not in the game - and it passes just
        // as well with the weight set high enough to fog half the map. Neither
        // end is something the rest of the suite would notice, because every
        // other assertion here is about the nodes that *are* concealed.
        double rate = rollableCount == 0 ? 0 : concealedCount / (double)rollableCount;
        Check("concealed_nodes_are_a_meaningful_share_of_the_branching_floors",
            rate is > 0.08 and < 0.25,
            $"{concealedCount}/{rollableCount} = {rate:P1} of eligible nodes over " +
            $"40 seeds x {ActDatabase.Count} acts");
        Check("concealed_nodes_are_never_an_elite_or_the_boss", illegalType.Count == 0,
            string.Join("; ", illegalType.Take(5)));
        Check("concealed_nodes_never_land_on_a_forced_floor", illegalFloor.Count == 0,
            string.Join("; ", illegalFloor.Take(5)));
        Check("concealed_combat_nodes_still_carry_enemies", missingEnemies.Count == 0,
            string.Join("; ", missingEnemies.Take(5)));

        // ...and the second table is a table, not one type with decoration.
        Check("concealed_nodes_roll_more_than_one_type", seen.Count > 1,
            $"types=[{string.Join(",", seen)}]");
    }

    // The two halves of the mechanic that live outside the generator: the
    // screen must not paint the answer, and walking in must reveal it.
    private void TestConcealedNodesHideTheirTypeUntilEntered()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 40;
        RunState.Relics = new List<RelicInstance>();
        RunState.ActIndex = 0;
        RunState.Stats = new RunStats();

        // Searched rather than pinned to one lucky seed, so retuning the "?"
        // weight cannot silently turn this test into a no-op against a map with
        // nothing concealed in it.
        var act = ActDatabase.At(0);
        List<MapNode>? nodes = null;
        for (int seed = 0; seed < 40 && nodes is null; seed++)
        {
            var candidate = MapGenerator.Generate(new Random(seed), act);
            if (candidate.Any(n => n.Concealed)) nodes = candidate;
        }
        if (!Check("a_seeded_act_one_map_contains_a_concealed_node", nodes is not null,
                "no concealed node in 40 seeds of act 1"))
            return;

        RunState.MapNodes = nodes!;
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();

        var packed = GD.Load<PackedScene>("res://scenes/MapScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var buttons = instance.GetNode<Control>("NodeButtons").GetChildren().OfType<Button>().ToList();
        if (!Check("concealed_map_pairs_every_node_with_a_button",
                buttons.Count == RunState.MapNodes.Count,
                $"buttons={buttons.Count}, nodes={RunState.MapNodes.Count}"))
        {
            instance.QueueFree();
            return;
        }

        var paired = RunState.MapNodes.Zip(buttons, (node, button) => (node, button)).ToList();
        var hidden = paired.Where(p => p.node.Concealed).ToList();

        // The tooltip is the easy leak: node.Type holds the truth all along, so
        // the unguarded NodeLabel(node.Type) this replaced would have named the
        // room while the icon still said "?".
        Check("concealed_nodes_do_not_name_their_type_in_the_tooltip",
            hidden.All(p => p.button.TooltipText == "?"),
            $"leaked=[{string.Join(",", hidden.Where(p => p.button.TooltipText != "?").Select(p => $"{p.node.Id}='{p.button.TooltipText}'"))}]");

        var hiddenIcons = hidden
            .Select(p => (p.node, Rect: p.button.GetChildren().OfType<TextureRect>().FirstOrDefault()))
            .ToList();
        Check("concealed_nodes_draw_the_question_mark_icon",
            hiddenIcons.All(h => h.Rect?.Texture?.ResourcePath == "res://assets/icons/map/unknown.png"),
            $"wrong=[{string.Join(",", hiddenIcons.Where(h => h.Rect?.Texture?.ResourcePath != "res://assets/icons/map/unknown.png").Select(h => $"{h.node.Id}='{h.Rect?.Texture?.ResourcePath}'"))}]");

        // ...while an unconcealed node next to it still shows its own, or the
        // check above would pass just as well with every icon replaced.
        var shown = paired.Where(p => !p.node.Concealed).ToList();
        Check("unconcealed_nodes_still_draw_their_own_icon",
            shown.Count > 0 && shown.All(p =>
                p.button.GetChildren().OfType<TextureRect>().FirstOrDefault()?.Texture?.ResourcePath
                    == ArtAssets.MapIcon(p.node.Type)?.ResourcePath),
            $"visible={shown.Count}");

        instance.QueueFree();

        // Entering is what reveals it, and EnterNode is also the type -> screen
        // router - the arm a new MapNodeType silently falls out of. Driving the
        // real method covers both, which is why it was split out of the version
        // that ended in RunManager.ChangeScreen and could not be called here.
        var target = hidden[0].node;
        var screen = MapScreen.EnterNode(target);
        Check("entering_a_concealed_node_reveals_it", !target.Concealed, $"node={target.Id}");
        Check("entering_a_concealed_node_routes_by_its_real_type",
            screen == ExpectedScreen(target.Type),
            $"{target.Type} routed to {screen}, expected {ExpectedScreen(target.Type)}");

        // Every type, not just whichever one this seed happened to conceal: the
        // router is a switch and one sampled arm says nothing about the rest.
        var misrouted = System.Enum.GetValues<MapNodeType>()
            .Where(t => MapScreen.EnterNode(new MapNode { Id = "probe", Type = t }) != ExpectedScreen(t))
            .ToList();
        Check("every_node_type_routes_to_a_screen", misrouted.Count == 0,
            $"unrouted=[{string.Join(",", misrouted)}]");

        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();
        RunState.Stats = new RunStats();
    }

    private static RunManager.ScreenState ExpectedScreen(MapNodeType type) => type switch
    {
        MapNodeType.Combat or MapNodeType.Elite or MapNodeType.Boss => RunManager.ScreenState.Combat,
        MapNodeType.Rest => RunManager.ScreenState.Rest,
        MapNodeType.Shop => RunManager.ScreenState.Shop,
        MapNodeType.Treasure => RunManager.ScreenState.Treasure,
        MapNodeType.Event => RunManager.ScreenState.Event,
        // Deliberately not ScreenState.Map, which is EnterNode's error arm: a
        // new MapNodeType has to be added here as well, and until it is this
        // check fails rather than agreeing with the fallback.
        _ => RunManager.ScreenState.MainMenu,
    };

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

        // Explicit rather than inherited. RunState.Relics is process-global and
        // this test only ever passed its band check because whichever test ran
        // before it happened to leave the list empty - carrying relics shrinks
        // the band, and at four rows the span check below would read 51% and
        // go red for a reason that has nothing to do with what it measures.
        var relicsBefore = RunState.Relics;
        RunState.Relics = new List<RelicInstance>();

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

        RunState.Relics = relicsBefore;
        RunState.ActIndex = 0;
        instance.QueueFree();
    }

    // The run-status block grows downward out of the top-left corner as the run
    // collects relics (ScreenChrome wraps them 6 to a row), and the node band
    // used to start at a fixed y=116 regardless. That is one row of relics'
    // worth of clearance, so from the seventh relic on the grid was drawn over
    // the top-left nodes - and the relics are drawn second, so the node
    // underneath was the one that vanished.
    //
    // Thirteen relics is three rows, chosen so this fails on a band top nudged
    // down by a row rather than actually derived. The longest act for the same
    // reason the shot is act 3: more floors means more of them under the block.
    private void TestNodesClearTheRunStatusBlock()
    {
        var longest = ActDatabase.All.OrderByDescending(a => a.FloorCount).First();
        RunState.ActIndex = ActDatabase.All.ToList().IndexOf(longest);
        RunState.MapNodes = MapGenerator.Generate(new Random(11), longest);
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();
        var relicsBefore = RunState.Relics;
        RunState.Relics = RelicDatabase.All.Take(13).Select(r => new RelicInstance(r)).ToList();

        var instance = GD.Load<PackedScene>("res://scenes/MapScreen.tscn").Instantiate();
        AddChild(instance);

        // GetCombinedMinimumSize rather than Size for the same reason MapScreen
        // itself uses it: nothing has sorted this container yet, so Size is
        // still zero. The node buttons are not container-managed - BuildButtons
        // assigns Position and Size outright - so their rects are already real.
        var block = instance.GetNode<Control>("RunStatusBar");
        var blockRect = new Rect2(block.Position, block.GetCombinedMinimumSize());

        var buried = instance.GetNode<Control>("NodeButtons").GetChildren().OfType<Button>()
            .Where(b => blockRect.Intersects(new Rect2(b.Position, b.Size)))
            .Select(b => $"node at {b.Position}")
            .ToList();
        Check($"{longest.Id}_no_node_under_the_relic_grid", buried.Count == 0,
            $"{buried.Count} of them, block is {blockRect}: {string.Join(", ", buried)}");

        RunState.Relics = relicsBefore;
        RunState.ActIndex = 0;
        instance.QueueFree();
    }

    // How wide a branching floor is allowed to be. Restated here rather than
    // read off MapGenerator, for the same reason LegalConcealedTypes is: the
    // band is a design decision paid for out of MapScreen's vertical pitch,
    // so widening the generator has to fail here and be argued for rather than
    // inherited. Floor 0 is pinned to the minimum separately - it is all
    // Combat, so a wider one is a wider choice between identical rooms.
    private const int MinFloorWidth = 3;
    private const int MaxFloorWidth = 5;

    private void TestFloorWidthsAreInBand()
    {
        var widths = new List<int>();
        var illegal = new List<string>();
        bool openingAlwaysMinimal = true;

        foreach (var act in ActDatabase.All)
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var nodes = MapGenerator.Generate(new Random(seed), act);
                int lastFloor = nodes.Max(n => n.Floor);
                if (nodes.Count(n => n.Floor == 0) != MinFloorWidth) openingAlwaysMinimal = false;

                // The two forced floors at the end are single nodes by
                // construction and are covered by AssertShape, not here.
                foreach (var floor in nodes.Where(n => n.Floor > 0 && n.Floor < lastFloor - 1)
                             .GroupBy(n => n.Floor))
                {
                    int width = floor.Count();
                    widths.Add(width);
                    if (width < MinFloorWidth || width > MaxFloorWidth)
                    {
                        illegal.Add($"{act.Id} seed {seed} floor {floor.Key}: {width}");
                    }
                }
            }
        }

        Check("branching_floors_stay_in_the_width_band", illegal.Count == 0,
            $"{widths.Count} floors sampled, {illegal.Count} illegal: {string.Join(", ", illegal.Take(5))}");
        Check("floor_0_is_pinned_to_the_minimum_width", openingAlwaysMinimal,
            $"expected every opening floor to be {MinFloorWidth} nodes");

        // A band whose ends never occur is a band in name only - this is what
        // would catch MaxNodesPerFloor being raised and MinNodesPerFloor being
        // dragged up with it, which reads as "still 3-5" in the diff.
        Check("both_ends_of_the_width_band_actually_occur",
            widths.Contains(MinFloorWidth) && widths.Contains(MaxFloorWidth),
            $"widths seen: [{string.Join(",", widths.Distinct().OrderBy(w => w))}]");
    }

    // The clear air two node buttons must keep, and the relic rows the map is
    // willing to give the status block. MapScreen.MinNodeGap and
    // MapScreen.MaxRelicRows, restated for the reason above.
    private const float MinNodeGap = 4f;
    private const int MaxRelicRowsOnTheMap = 3;

    // Nothing in this repo has ever asserted that two map nodes do not overlap
    // each other, and at 3-4 wide nothing needed to: the pitch was 97px under
    // 64px nodes even at three rows of relics. Five wide spends that margin,
    // and the failure is silent in a specific way - TestLongestActFitsOnScreen
    // keeps passing through it, because nodes drawn on top of one another are
    // still inside the design rect and still span 60% of the band.
    //
    // Two things this drives that no other layout test here does. It sweeps
    // *relic counts*, because the band top is a function of them and the
    // crowded end is the one that breaks (20 is BalanceModel.Reachable's best
    // routed relic count, i.e. the most the game can actually hand out). And
    // it sets CurrentNodeId, because BuildCurrentNodeRing returns immediately
    // without one - so the ring, which is wider than the node and was a flat
    // constant until this change, has been invisible to every test in the file.
    private void TestNodesStayApartAtEveryRelicCount()
    {
        var longest = ActDatabase.All.OrderByDescending(a => a.FloorCount).First();
        var relicsBefore = RunState.Relics;
        RunState.ActIndex = ActDatabase.All.ToList().IndexOf(longest);

        // The seed is *searched for*, not written down. Floor width is rolled
        // per floor, so a hardcoded seed tests whatever width it happens to
        // produce - and a fixture that quietly stopped containing a widest
        // floor would leave every assertion below green while measuring a
        // narrower map than the one that can actually be generated.
        //
        // FirstOrDefault(-1) rather than First(), and the check reads the
        // *result* rather than re-running the predicate. First() would throw
        // inside _Ready, which never reaches Quit() and so surfaces as a
        // watchdog TIMEOUT rather than a red line; and an assertion that
        // recomputes its own selection criterion is one that cannot fail,
        // which is the shape this suite exists to refuse.
        int seed = Enumerable.Range(0, 200).FirstOrDefault(
            s => MapGenerator.Generate(new Random(s), longest)
                .GroupBy(n => n.Floor).Max(g => g.Count()) == MaxFloorWidth,
            -1);
        if (!Check("a_seed_producing_the_widest_floor_exists", seed >= 0,
                $"no seed under 200 gives {longest.Id} a {MaxFloorWidth}-wide floor"))
        {
            return;
        }

        // 21 is BalanceModel.Reachable's best routed relic count *after* the
        // widening (it was 20 before - more map to route through is more
        // relics to collect), and 24 is headroom over it. RelicColumnsForBand
        // keeps the grid to MaxRelicRows at any count, so what these actually
        // pin is that it still does at the counts the game can produce.
        foreach (int relicCount in new[] { 0, 6, 7, 13, 18, 21, 24 })
        {
            RunState.MapNodes = MapGenerator.Generate(new Random(seed), longest);
            RunState.VisitedNodeIds = new HashSet<string>();
            RunState.Relics = RelicDatabase.All.Take(relicCount).Select(r => new RelicInstance(r)).ToList();

            // The middle of the widest floor, so the ring is measured with a
            // neighbour above and below it rather than at the end of a column.
            var widest = RunState.MapNodes.GroupBy(n => n.Floor)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First()
                .OrderBy(n => n.Column).ToList();
            RunState.CurrentNodeId = widest[widest.Count / 2].Id;

            var instance = GD.Load<PackedScene>("res://scenes/MapScreen.tscn").Instantiate();
            AddChild(instance);
            var nodeButtons = instance.GetNode<Control>("NodeButtons");
            var buttons = nodeButtons.GetChildren().OfType<Button>().ToList();

            float closest = float.MaxValue;
            string closestPair = "none";
            for (int i = 0; i < buttons.Count; i++)
            {
                for (int j = i + 1; j < buttons.Count; j++)
                {
                    var a = new Rect2(buttons[i].Position, buttons[i].Size);
                    var b = new Rect2(buttons[j].Position, buttons[j].Size);
                    // Gap along whichever axis they are actually separated on;
                    // a negative on both axes is an overlap.
                    float gapX = Mathf.Max(a.Position.X - b.End.X, b.Position.X - a.End.X);
                    float gapY = Mathf.Max(a.Position.Y - b.End.Y, b.Position.Y - a.End.Y);
                    float gap = Mathf.Max(gapX, gapY);
                    if (gap < closest)
                    {
                        closest = gap;
                        closestPair = $"{buttons[i].Position} vs {buttons[j].Position}";
                    }
                }
            }
            var block = instance.GetNode<Control>("RunStatusBar");
            Check($"nodes_stay_apart_with_{relicCount}_relics", closest >= MinNodeGap,
                $"closest gap {closest:F1}px (need {MinNodeGap}), {closestPair}, " +
                $"block is {block.GetCombinedMinimumSize().Y:F0}px tall");

            // The pitch the layout settled on, read back off the rendered
            // buttons rather than recomputed - a test that redoes the
            // arithmetic agrees with itself rather than with the screen.
            var column = buttons.GroupBy(b => Mathf.RoundToInt(b.Position.X))
                .OrderByDescending(g => g.Count()).First()
                .OrderBy(b => b.Position.Y).ToList();
            float pitch = column.Count < 2
                ? float.MaxValue
                : column.Skip(1).Zip(column, (lower, upper) => lower.Position.Y - upper.Position.Y).Min();

            // The relic grid is what the band is competing with, so cap it
            // directly rather than only measuring the consequence. Without
            // this the mechanism is invisible: at the widths in play either
            // half of the fix alone squeaks past the gap check, so a dropped
            // RelicColumnsForBand would leave every other assertion green.
            int relicRows = relicCount == 0 ? 0 : Mathf.CeilToInt(
                relicCount / (float)instance.GetNode<GridContainer>(
                    "RunStatusBar/RelicRow").Columns);
            Check($"relic_grid_stays_within_its_row_budget_with_{relicCount}_relics",
                relicRows <= MaxRelicRowsOnTheMap,
                $"{relicRows} rows (budget {MaxRelicRowsOnTheMap}), pitch {pitch:F1}px");

            // The ring is added first and moved to index 0 so it draws under
            // the buttons; it is the only Panel in there.
            var ring = nodeButtons.GetChildren().OfType<Panel>().FirstOrDefault();

            // A ring wider than the pitch bleeds into the cell of the node
            // above and below even when it misses their button rects, which
            // muddies the one thing it exists to say. This is the assertion
            // that makes the ring's derivation observable - a flat
            // NodeSize + 20f passes every other check in this file.
            Check($"current_node_ring_is_no_wider_than_the_pitch_with_{relicCount}_relics",
                ring is not null && ring.Size.Y <= pitch,
                ring is null ? "no ring" : $"ring {ring.Size.Y:F1}px against a {pitch:F1}px pitch");
            var currentButton = buttons.FirstOrDefault(b =>
                b.GetRect().HasPoint(ring?.GetRect().GetCenter() ?? new Vector2(-1, -1)));
            var ringCollisions = ring is null
                ? new List<string>()
                : buttons.Where(b => b != currentButton && ring.GetRect().Intersects(b.GetRect()))
                    .Select(b => $"node at {b.Position}").ToList();
            Check($"current_node_ring_clears_its_neighbours_with_{relicCount}_relics",
                ring is not null && ringCollisions.Count == 0,
                ring is null
                    ? "no ring was built - CurrentNodeId did not take"
                    : $"ring {ring.GetRect()} hits {ringCollisions.Count}: {string.Join(", ", ringCollisions)}");

            instance.QueueFree();
        }

        RunState.Relics = relicsBefore;
        RunState.CurrentNodeId = "";
        RunState.ActIndex = 0;
    }
}
