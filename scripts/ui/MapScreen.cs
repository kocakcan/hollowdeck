using System.Collections.Generic;
using System.Linq;
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

    private const int TreasureAtEncounterIndex = 1;

    private Label _progressLabel = null!;
    private Button _treasureButton = null!;
    private Label _relicsLabel = null!;

    public override void _Ready()
    {
        _progressLabel = GetNode<Label>("ProgressLabel");
        _treasureButton = GetNode<Button>("TreasureButton");
        _relicsLabel = GetNode<Label>("RelicsLabel");

        GetNode<Button>("BackButton").Pressed += OnBackPressed;
        GetNode<Button>("NextFightButton").Pressed += OnNextFightPressed;
        GetNode<Button>("ShopButton").Pressed += OnShopPressed;
        _treasureButton.Pressed += OnTreasurePressed;

        int index = RunManager.Instance.CurrentEncounterIndex;
        _progressLabel.Text = $"Fight {index + 1} of {Encounters.Count}";
        _treasureButton.Visible = !RunState.TreasureClaimed && index == TreasureAtEncounterIndex;

        _relicsLabel.Text = RunState.Relics.Count == 0
            ? "Relics: none yet"
            : $"Relics: {string.Join(", ", RunState.Relics.Select(r => r.Definition.Name))}";
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);

    private void OnShopPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Shop);

    private void OnTreasurePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Treasure);

    private void OnNextFightPressed()
    {
        int index = RunManager.Instance.CurrentEncounterIndex;
        var encounter = Encounters[index];
        CombatContext.EnemyDefinitionIds = encounter;
        CombatContext.IsFinalEncounter = index == Encounters.Count - 1;
        CombatContext.GoldReward = 20 + encounter.Count * 5;
        RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
    }
}
