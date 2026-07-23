using Godot;
using System.Collections.Generic;

namespace Hollowdeck.Run;

// Autoload singleton. Owns top-level run/screen state and drives scene
// transitions. ScreenState documents the full intended flow from
// hollowdeck.md; ScenePaths only maps states that have a real scene so far.
public partial class RunManager : Node
{
    public static RunManager Instance { get; private set; }

    public enum ScreenState
    {
        MainMenu, RunSetup, Map, Combat, Elite, Event, Rest, Shop,
        Treasure, Reward, Victory, Defeat, MetaProgression,
    }

    private static readonly Dictionary<ScreenState, string> ScenePaths = new()
    {
        { ScreenState.MainMenu, "res://scenes/MainMenu.tscn" },
        { ScreenState.Map, "res://scenes/MapScreen.tscn" },
        // TODO(Phase 1+): Combat, Elite, Event, Rest, Shop, Treasure,
        // Reward, Victory, Defeat, MetaProgression.
    };

    public ScreenState CurrentScreen { get; private set; } = ScreenState.MainMenu;

    public override void _Ready() => Instance = this;

    public void ChangeScreen(ScreenState next)
    {
        if (!ScenePaths.TryGetValue(next, out var path))
        {
            GD.PushError($"RunManager: no scene registered for {next} yet (Phase 1+ TODO).");
            return;
        }
        CurrentScreen = next;
        GetTree().ChangeSceneToFile(path);
    }
}
