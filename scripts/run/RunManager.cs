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
        MainMenu, RunSetup, Map, Combat, Event, Rest, Shop,
        Treasure, Reward, Victory, Defeat, MetaProgression, Settings,
    }

    private static readonly Dictionary<ScreenState, string> ScenePaths = new()
    {
        { ScreenState.MainMenu, "res://scenes/MainMenu.tscn" },
        { ScreenState.Map, "res://scenes/MapScreen.tscn" },
        { ScreenState.Combat, "res://scenes/CombatScreen.tscn" },
        { ScreenState.Rest, "res://scenes/RestScreen.tscn" },
        { ScreenState.Shop, "res://scenes/ShopScreen.tscn" },
        { ScreenState.Treasure, "res://scenes/TreasureScreen.tscn" },
        { ScreenState.Reward, "res://scenes/RewardScreen.tscn" },
        { ScreenState.Victory, "res://scenes/RunEndScreen.tscn" },
        { ScreenState.Defeat, "res://scenes/RunEndScreen.tscn" },
        { ScreenState.MetaProgression, "res://scenes/MetaProgressionScreen.tscn" },
        { ScreenState.Settings, "res://scenes/SettingsScreen.tscn" },
        // TODO(Phase 4+): Event. Elite fights reuse the Combat scene (see
        // MapScreen) rather than needing their own ScreenState/scene.
        // RunSetup deliberately stays unregistered too - no pre-run choices
        // exist yet (all content unlocked, no character/class selection) to
        // justify a pause screen between MainMenu and Map.
    };

    public ScreenState CurrentScreen { get; private set; } = ScreenState.MainMenu;
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
        RunSeed = new Random().Next();
        RngStreams.Init(RunSeed);
        RunState.InitNewRun();
        GD.Print($"Run seed: {RunSeed}");
        ChangeScreen(ScreenState.Map);
    }
}
