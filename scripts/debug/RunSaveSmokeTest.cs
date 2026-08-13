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
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestSaveThenLoadRoundTrip();
        TestActIndexToleratesOldAndOutOfRangeSaves();
        TestConcealedFlagToleratesOldSaves();
        TestSkipStreakToleratesOldAndOutOfRangeSaves();
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
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("bash"), CardUpgrade.Apply(CardDatabase.Get("defend")),
            // A Curse, because a run save is the only thing standing between a
            // player and quitting out of one. It reaches the deck through
            // add_card rather than a reward, so nothing else in the save path
            // has ever seen a card the pools cannot produce.
            CardDatabase.Get("pain"),
        };
        RunState.Relics = new List<RelicInstance> { new(RelicDatabase.Get(RelicDatabase.All.First().Id)) };
        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get(PotionDatabase.All.First().Id)) };
        RunState.MapNodes = new List<MapNode>
        {
            new() { Id = "n0", Floor = 0, Column = 1.5f, Type = MapNodeType.Combat, NextNodeIds = { "n1" }, EnemyIds = { "cultist" } },
            // Concealed, because the "?" node's whole safety argument is that
            // the fog persists while the type underneath it never re-rolls. A
            // flag that quietly reset on resume would hand the player a fresh
            // gamble on a room they had already committed to.
            new() { Id = "n1", Floor = 1, Column = 0.5f, Type = MapNodeType.Rest, Concealed = true },
        };
        RunState.CurrentNodeId = "n0";
        RunState.VisitedNodeIds = new HashSet<string> { "n0" };
        RunState.ActIndex = 1;
        // Mid-ladder rather than at the cap, so a load that clamped to the
        // wrong end - or ignored the value and re-derived it - lands on a
        // different number than the one written.
        RunState.CardSkipStreak = 2;
        // Likewise mid-ladder rather than 0 or the cap.
        RunState.AscensionLevel = 7;

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
        RunState.ActIndex = 0;
        RunState.CardSkipStreak = 0;
        RunState.AscensionLevel = 0;

        var seed = RunSaveManager.TryLoad(ScratchPath);
        Check("round_trip_seed", seed == 12345, $"seed={seed}");
        Check("round_trip_card_skip_streak", RunState.CardSkipStreak == 2,
            $"streak={RunState.CardSkipStreak}");
        Check("round_trip_ascension_level", RunState.AscensionLevel == 7,
            $"ascension={RunState.AscensionLevel}");
        Check("round_trip_gold", RunState.Gold == 42, $"gold={RunState.Gold}");
        Check("round_trip_max_hp", RunState.PlayerMaxHp == 60, $"maxHp={RunState.PlayerMaxHp}");
        Check("round_trip_current_hp", RunState.PlayerCurrentHp == 35, $"currentHp={RunState.PlayerCurrentHp}");
        Check("round_trip_deck", RunState.Deck.Count == 4 && RunState.Deck.Any(c => c.Id == "strike") && RunState.Deck.Any(c => c.Id == "bash"),
            $"deck=[{string.Join(",", RunState.Deck.Select(c => c.Id))}]");
        // Both halves: the id resolves, and the reconstructed definition is
        // still unplayable. A Curse that reloads as a playable card would be a
        // free deck-thin on every quit-and-resume.
        var reloadedCurse = RunState.Deck.FirstOrDefault(c => c.Id == "pain");
        Check("round_trip_curse_survives_and_stays_unplayable",
            reloadedCurse is { IsPlayable: false },
            $"deck=[{string.Join(",", RunState.Deck.Select(c => c.Id))}]");
        // An upgraded card round-trips as "<baseId>+" (CardUpgrade's naming),
        // not its own CardDatabase entry - RunSaveManager has to resolve the
        // base id and re-derive the upgrade, not look it up directly.
        var reloadedUpgraded = RunState.Deck.FirstOrDefault(c => c.Id == "defend+");
        Check("round_trip_upgraded_card_survives", reloadedUpgraded is not null,
            $"deck=[{string.Join(",", RunState.Deck.Select(c => c.Id))}]");
        Check("round_trip_upgraded_card_keeps_boosted_effect",
            reloadedUpgraded is not null && reloadedUpgraded.Effects[0].Amount > CardDatabase.Get("defend").Effects[0].Amount,
            $"amount={reloadedUpgraded?.Effects[0].Amount}");
        Check("round_trip_relics", RunState.Relics.Count == 1, $"relics={RunState.Relics.Count}");
        Check("round_trip_potions", RunState.Potions.Count == 1, $"potions={RunState.Potions.Count}");
        Check("round_trip_map_nodes", RunState.MapNodes.Count == 2 && RunState.MapNodes.Any(n => n.Id == "n0" && n.Type == MapNodeType.Combat),
            $"mapNodes={RunState.MapNodes.Count}");
        Check("round_trip_map_node_enemy_ids", RunState.MapNodes.First(n => n.Id == "n0").EnemyIds.Contains("cultist"),
            "expected n0's EnemyIds to survive the round trip (MapNode uses fields, needs IncludeFields)");
        // Both directions, because `false` is the default a lost field also
        // produces: asserting only that n1 stayed concealed would pass just as
        // well against a save that dropped the flag and one that never set it.
        Check("round_trip_map_node_concealed",
            RunState.MapNodes.First(n => n.Id == "n1").Concealed
            && !RunState.MapNodes.First(n => n.Id == "n0").Concealed,
            $"n1={RunState.MapNodes.First(n => n.Id == "n1").Concealed}, " +
            $"n0={RunState.MapNodes.First(n => n.Id == "n0").Concealed}");
        Check("round_trip_current_node", RunState.CurrentNodeId == "n0", $"currentNodeId={RunState.CurrentNodeId}");
        Check("round_trip_visited_nodes", RunState.VisitedNodeIds.Contains("n0"), $"visited={string.Join(",", RunState.VisitedNodeIds)}");
        Check("round_trip_act_index", RunState.ActIndex == 1, $"actIndex={RunState.ActIndex}");
    }

    // Save v2 predates acts. Its ActIndex is absent, which must deserialize as
    // act 0 - every such save is a single-act run and its MapNodes are already
    // act 1's graph, so there is nothing to migrate. An index past the end (a
    // save from a build with more acts) has to clamp instead of throwing when
    // RunState.CurrentAct is read.
    private void TestActIndexToleratesOldAndOutOfRangeSaves()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 2, "runSeed": 7, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "deckCardIds": ["strike"], "relicIds": [], "potions": [],
              "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);
        RunState.ActIndex = 2;
        var seed = RunSaveManager.TryLoad(ScratchPath);
        Check("v2_save_loads", seed == 7, $"seed={seed}");
        Check("v2_save_without_act_index_is_act_one", RunState.ActIndex == 0, $"actIndex={RunState.ActIndex}");

        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 3, "runSeed": 8, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "actIndex": 99, "deckCardIds": ["strike"], "relicIds": [], "potions": [],
              "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);
        RunSaveManager.TryLoad(ScratchPath);
        Check("out_of_range_act_index_clamps_to_last_act",
            RunState.ActIndex == ActDatabase.Count - 1 && RunState.CurrentAct is not null,
            $"actIndex={RunState.ActIndex}, acts={ActDatabase.Count}");

        RunState.ActIndex = 0;
    }

    // Save v3 predates the "?" node, so its map nodes carry no `concealed` key.
    // Deserializing that as false is the whole migration: every map drawn
    // before this feature was fully visible, and a resumed run should show what
    // it always showed. The failure this guards against is the opposite one -
    // a default of true would fog a v3 map the player had already read.
    private void TestConcealedFlagToleratesOldSaves()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 3, "runSeed": 9, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "actIndex": 0, "deckCardIds": ["strike"], "relicIds": [], "potions": [],
              "mapNodes": [{ "Id": "n0", "Floor": 0, "Column": 0, "Type": "Combat",
                             "NextNodeIds": [], "EnemyIds": ["cultist"] }],
              "currentNodeId": "n0", "visitedNodeIds": ["n0"] }
            """);

        // Poisoned first, so a load that silently kept the old list rather than
        // replacing it cannot pass this by leaving a `false` of its own behind.
        RunState.MapNodes = new List<MapNode> { new() { Id = "poison", Concealed = true } };

        var seed = RunSaveManager.TryLoad(ScratchPath);
        Check("v3_save_loads", seed == 9, $"seed={seed}");
        Check("v3_map_nodes_without_concealed_load_as_visible",
            RunState.MapNodes.Count == 1 && RunState.MapNodes[0].Id == "n0"
            && !RunState.MapNodes[0].Concealed,
            $"nodes=[{string.Join(",", RunState.MapNodes.Select(n => $"{n.Id}:{n.Concealed}"))}]");

        RunState.MapNodes = new List<MapNode>();
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();
    }

    // Save v4 predates the skip streak. Its cardSkipStreak is absent, which has
    // to load as 0 - a run saved before the feature existed has declined
    // nothing, and its next reward belongs on the flat table it was always
    // going to be drawn on. A value past the cap has to clamp for the reason
    // RunSaveManager states: CardPool clamps the *draw* on its own, so what an
    // unclamped load would produce is not a biased offer but a reward row
    // reading "9 skipped" beside rung 3's odds.
    private void TestSkipStreakToleratesOldAndOutOfRangeSaves()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 4, "runSeed": 11, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "actIndex": 0, "deckCardIds": ["strike"], "relicIds": [], "potions": [],
              "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);

        // Poisoned first. Without this, a load that never assigned the field at
        // all would pass against whatever 0 happened to already be there - the
        // same trap the concealed-flag test above documents.
        RunState.CardSkipStreak = 3;

        var seed = RunSaveManager.TryLoad(ScratchPath);
        Check("v4_save_loads", seed == 11, $"seed={seed}");
        Check("v4_save_without_skip_streak_loads_as_zero",
            RunState.CardSkipStreak == 0, $"streak={RunState.CardSkipStreak}");

        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 5, "runSeed": 12, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "actIndex": 0, "cardSkipStreak": 99, "deckCardIds": ["strike"], "relicIds": [],
              "potions": [], "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);

        RunSaveManager.TryLoad(ScratchPath);
        Check("out_of_range_skip_streak_clamps_to_the_cap",
            RunState.CardSkipStreak == CardPool.MaxSkipStreak, $"streak={RunState.CardSkipStreak}");

        // v5 -> v6, the ascension level. Poisoned first for the same reason the
        // skip streak above is: absent-is-0 and never-assigned-is-0 are the
        // same value, and only one of them is the field working.
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 5, "runSeed": 13, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "actIndex": 0, "deckCardIds": ["strike"], "relicIds": [], "potions": [],
              "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);

        RunState.AscensionLevel = 9;
        Check("v5_save_loads", RunSaveManager.TryLoad(ScratchPath) == 13, "v5 save did not load");
        Check("v5_save_without_an_ascension_level_loads_as_zero",
            RunState.AscensionLevel == 0, $"ascension={RunState.AscensionLevel}");

        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 6, "runSeed": 14, "gold": 10, "playerMaxHp": 50, "playerCurrentHp": 50,
              "actIndex": 0, "ascensionLevel": 999, "deckCardIds": ["strike"], "relicIds": [],
              "potions": [], "mapNodes": [], "currentNodeId": "", "visitedNodeIds": [] }
            """);

        RunSaveManager.TryLoad(ScratchPath);
        Check("out_of_range_ascension_level_clamps_to_the_ladder",
            RunState.AscensionLevel == AscensionDatabase.MaxLevel,
            $"ascension={RunState.AscensionLevel}");

        RunState.CardSkipStreak = 0;
        RunState.AscensionLevel = 0;
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
            // RunSetup is the one exclusion that is not obvious from the name,
            // because BeginRun has already built a complete RunState by the
            // time that screen opens - a save taken here would be perfectly
            // valid. It is excluded because Continue jumps straight to Map: a
            // player who opened the setup screen and quit would come back to a
            // run whose blessing was never offered, with nothing to say so.
            RunManager.ScreenState.RunSetup,
        };

        var field = typeof(RunManager).GetField("AutoSaveScreens",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var autoSaveScreens = (HashSet<RunManager.ScreenState>)field!.GetValue(null)!;

        Check("autosave_includes_expected_screens", expectedIncluded.All(autoSaveScreens.Contains),
            $"missing: {string.Join(",", expectedIncluded.Where(s => !autoSaveScreens.Contains(s)))}");
        Check("autosave_excludes_combat_and_terminal_screens", expectedExcluded.All(s => !autoSaveScreens.Contains(s)),
            $"unexpectedly included: {string.Join(",", expectedExcluded.Where(autoSaveScreens.Contains))}");

        // Every member of the enum now has a scene behind it, which was not
        // true until RunSetup was wired - the enum used to document the
        // *intended* flow and ScenePaths only the built part of it. Now that
        // they agree, keeping them agreeing is worth an assertion: ChangeScreen
        // to an unregistered state pushes an error and returns, which changes
        // no screen and leaves the run wherever it was. That is a soft-lock
        // rather than a crash, the same shape MapScreen.EnterNode's missing
        // default arm had.
        var pathField = typeof(RunManager).GetField("ScenePaths",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var scenePaths = (Dictionary<RunManager.ScreenState, string>)pathField!.GetValue(null)!;
        var unregistered = System.Enum.GetValues<RunManager.ScreenState>()
            .Where(s => !scenePaths.ContainsKey(s)).ToList();
        Check("every_screen_state_has_a_scene", unregistered.Count == 0,
            $"unregistered: {string.Join(",", unregistered)}");
    }
}
