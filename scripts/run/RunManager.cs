using Godot;
using System;
using System.Collections.Generic;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Autoload singleton. Owns top-level run/screen state and drives scene
// transitions. ScreenState documents the full intended flow from
// hollowdeck.md; ScenePaths only maps states that have a real scene so far.
public partial class RunManager : Node
{
    public static RunManager Instance { get; private set; } = null!;

    public enum ScreenState
    {
        MainMenu, RunSetup, Map, Combat, Elite, Event, Rest, Shop,
        Treasure, Reward, Victory, Defeat, MetaProgression,
    }

    private static readonly Dictionary<ScreenState, string> ScenePaths = new()
    {
        { ScreenState.MainMenu, "res://scenes/MainMenu.tscn" },
        { ScreenState.Map, "res://scenes/MapScreen.tscn" },
        { ScreenState.Combat, "res://scenes/CombatScreen.tscn" },
        { ScreenState.Shop, "res://scenes/ShopScreen.tscn" },
        { ScreenState.Treasure, "res://scenes/TreasureScreen.tscn" },
        { ScreenState.Reward, "res://scenes/RewardScreen.tscn" },
        { ScreenState.Victory, "res://scenes/RunEndScreen.tscn" },
        { ScreenState.Defeat, "res://scenes/RunEndScreen.tscn" },
        { ScreenState.MetaProgression, "res://scenes/MetaProgressionScreen.tscn" },
        // TODO(Phase 4+): Elite, Event, Rest.
        // RunSetup deliberately stays unregistered too - no pre-run choices
        // exist yet (all content unlocked, no character/class selection) to
        // justify a pause screen between MainMenu and Map.
    };

    public ScreenState CurrentScreen { get; private set; } = ScreenState.MainMenu;
    public int CurrentEncounterIndex { get; private set; }
    public int RunSeed { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
    }

    public void ChangeScreen(ScreenState next)
    {
        if (!ScenePaths.TryGetValue(next, out var path))
        {
            GD.PushError($"RunManager: no scene registered for {next} yet (Phase 2+ TODO).");
            return;
        }
        CurrentScreen = next;
        GetTree().ChangeSceneToFile(path);
    }

    public void StartNewRun()
    {
        CurrentEncounterIndex = 0;
        RunSeed = new Random().Next();
        RngStreams.Init(RunSeed);
        RunState.InitNewRun();
        GD.Print($"Run seed: {RunSeed}");
        ChangeScreen(ScreenState.Map);
    }

    public void AdvanceEncounter()
    {
        CurrentEncounterIndex++;
    }
}
