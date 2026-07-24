using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for RunSaveManager's save/load logic. Always operates
// against a scratch file (ScratchPath), NEVER the real save path, so a test
// run can never clobber a developer's/player's actual mid-run save. Run via
// `godot --headless scenes/debug/RunSaveSmokeTest.tscn`.
public partial class RunSaveSmokeTest : Node
{
    private const string ScratchPath = "user://run_save_test.json";

    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestSaveThenLoadRoundTrip();
        TestCorruptedFileFallsBackToNull();
        TestStaleCardRelicPotionIdsAreDropped();
        TestDeleteRemovesFile();
        TestAutoSaveScreensSetIsCorrect();

        GD.Print($"RunSaveSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void ResetScratch()
    {
        if (FileAccess.FileExists(ScratchPath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ScratchPath));
    }

    private void WriteScratchRaw(string json)
    {
        using var file = FileAccess.Open(ScratchPath, FileAccess.ModeFlags.Write);
        file.StoreString(json);
    }

    private void TestSaveThenLoadRoundTrip()
    {
        ResetScratch();

        RunState.Gold = 42;
        RunState.PlayerMaxHp = 60;
        RunState.PlayerCurrentHp = 35;
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("bash") };
        RunState.Relics = new List<RelicInstance> { new(RelicDatabase.Get(RelicDatabase.All.First().Id)) };
        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get(PotionDatabase.All.First().Id)) };
        RunState.MapNodes = new List<MapNode>
        {
            new() { Id = "n0", Floor = 0, Column = 1.5f, Type = MapNodeType.Combat, NextNodeIds = { "n1" }, EnemyIds = { "cultist" } },
            new() { Id = "n1", Floor = 1, Column = 0.5f, Type = MapNodeType.Rest },
        };
        RunState.CurrentNodeId = "n0";
        RunState.VisitedNodeIds = new HashSet<string> { "n0" };

        RunSaveManager.Save(runSeed: 12345, path: ScratchPath);

        // Wipe in-memory state so the load assertions can't accidentally
        // pass against leftover values instead of what was actually loaded.
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 0;
        RunState.PlayerCurrentHp = 0;
        RunState.Deck = new List<CardDefinition>();
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RunState.MapNodes = new List<MapNode>();
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();

        var seed = RunSaveManager.TryLoad(ScratchPath);
        Check("round_trip_seed", seed == 12345, $"seed={seed}");
        Check("round_trip_gold", RunState.Gold == 42, $"gold={RunState.Gold}");
        Check("round_trip_max_hp", RunState.PlayerMaxHp == 60, $"maxHp={RunState.PlayerMaxHp}");
        Check("round_trip_current_hp", RunState.PlayerCurrentHp == 35, $"currentHp={RunState.PlayerCurrentHp}");
        Check("round_trip_deck", RunState.Deck.Count == 2 && RunState.Deck.Any(c => c.Id == "strike") && RunState.Deck.Any(c => c.Id == "bash"),
            $"deck=[{string.Join(",", RunState.Deck.Select(c => c.Id))}]");
        Check("round_trip_relics", RunState.Relics.Count == 1, $"relics={RunState.Relics.Count}");
        Check("round_trip_potions", RunState.Potions.Count == 1, $"potions={RunState.Potions.Count}");
        Check("round_trip_map_nodes", RunState.MapNodes.Count == 2 && RunState.MapNodes.Any(n => n.Id == "n0" && n.Type == MapNodeType.Combat),
            $"mapNodes={RunState.MapNodes.Count}");
        Check("round_trip_map_node_enemy_ids", RunState.MapNodes.First(n => n.Id == "n0").EnemyIds.Contains("cultist"),
            "expected n0's EnemyIds to survive the round trip (MapNode uses fields, needs IncludeFields)");
        Check("round_trip_current_node", RunState.CurrentNodeId == "n0", $"currentNodeId={RunState.CurrentNodeId}");
        Check("round_trip_visited_nodes", RunState.VisitedNodeIds.Contains("n0"), $"visited={string.Join(",", RunState.VisitedNodeIds)}");
    }

    private void TestCorruptedFileFallsBackToNull()
    {
        ResetScratch();
        WriteScratchRaw("{ not valid json [[[");

        var seed = RunSaveManager.TryLoad(ScratchPath);
        Check("corrupted_file_returns_null", seed is null, $"seed={seed}");
    }

    private void TestStaleCardRelicPotionIdsAreDropped()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 1, "runSeed": 1, "gold": 0, "playerMaxHp": 50, "playerCurrentHp": 50,
              "deckCardIds": ["this_card_does_not_exist", "strike"],
              "relicIds": ["this_relic_does_not_exist"],
              "potions": [{ "definitionId": "this_potion_does_not_exist" }],
              "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);

        int? seed = null;
        bool threw = false;
        try { seed = RunSaveManager.TryLoad(ScratchPath); }
        catch { threw = true; }

        Check("stale_ids_do_not_throw", !threw, "TryLoad threw on stale ids");
        Check("stale_ids_dropped_valid_ids_kept", seed is not null && RunState.Deck.Count == 1 && RunState.Deck[0].Id == "strike",
            $"deck=[{string.Join(",", RunState.Deck.Select(c => c.Id))}]");
        Check("stale_relic_id_dropped", RunState.Relics.Count == 0, $"relics={RunState.Relics.Count}");
        Check("stale_potion_id_dropped", RunState.Potions.Count == 0, $"potions={RunState.Potions.Count}");
    }

    private void TestDeleteRemovesFile()
    {
        ResetScratch();
        RunState.Deck = new List<CardDefinition>();
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RunState.MapNodes = new List<MapNode>();
        RunState.VisitedNodeIds = new HashSet<string>();

        RunSaveManager.Save(runSeed: 1, path: ScratchPath);
        Check("save_creates_file", RunSaveManager.SaveExists(ScratchPath), "expected file to exist after Save");

        RunSaveManager.Delete(ScratchPath);
        Check("delete_removes_file", !RunSaveManager.SaveExists(ScratchPath), "expected file to be gone after Delete");
    }

    private void TestAutoSaveScreensSetIsCorrect()
    {
        var expectedIncluded = new[]
        {
            RunManager.ScreenState.Map, RunManager.ScreenState.Rest, RunManager.ScreenState.Shop,
            RunManager.ScreenState.Treasure, RunManager.ScreenState.Reward,
        };
        var expectedExcluded = new[]
        {
            RunManager.ScreenState.Combat, RunManager.ScreenState.MainMenu, RunManager.ScreenState.Settings,
            RunManager.ScreenState.MetaProgression, RunManager.ScreenState.Victory, RunManager.ScreenState.Defeat,
        };

        var field = typeof(RunManager).GetField("AutoSaveScreens",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var autoSaveScreens = (HashSet<RunManager.ScreenState>)field!.GetValue(null)!;

        Check("autosave_includes_expected_screens", expectedIncluded.All(autoSaveScreens.Contains),
            $"missing: {string.Join(",", expectedIncluded.Where(s => !autoSaveScreens.Contains(s)))}");
        Check("autosave_excludes_combat_and_terminal_screens", expectedExcluded.All(s => !autoSaveScreens.Contains(s)),
            $"unexpectedly included: {string.Join(",", expectedExcluded.Where(autoSaveScreens.Contains))}");
    }
}
