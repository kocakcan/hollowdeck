using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MapScreen : Control
{
    private static readonly List<List<string>> Encounters = new()
    {
        new() { "cultist" },
        new() { "slime", "slime" },
        new() { "cultist", "slime" },
    };

    private Label _progressLabel = null!;

    public override void _Ready()
    {
        _progressLabel = GetNode<Label>("ProgressLabel");
        GetNode<Button>("BackButton").Pressed += OnBackPressed;
        GetNode<Button>("NextFightButton").Pressed += OnNextFightPressed;

        int index = RunManager.Instance.CurrentEncounterIndex;
        _progressLabel.Text = $"Fight {index + 1} of {Encounters.Count}";
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);

    private void OnNextFightPressed()
    {
        int index = RunManager.Instance.CurrentEncounterIndex;
        CombatContext.EnemyDefinitionIds = Encounters[index];
        CombatContext.IsFinalEncounter = index == Encounters.Count - 1;
        RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
    }
}
