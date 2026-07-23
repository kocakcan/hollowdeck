using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class TreasureScreen : Control
{
    public override void _Ready()
    {
        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var available = RelicDatabase.All
            .Where(r => !ownedRelicIds.Contains(r.Id) && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();

        var label = GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");
        if (available.Count == 0)
        {
            label.Text = "The treasure chest is empty.";
        }
        else
        {
            var picked = available[RngStreams.Shop.Next(available.Count)];
            RunState.Relics.Add(new RelicInstance(picked));
            label.Text = $"You found: {picked.Name}\n{picked.Description}";
        }

        GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton").Pressed += OnContinuePressed;
    }

    private void OnContinuePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
