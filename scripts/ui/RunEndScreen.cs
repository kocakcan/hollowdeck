using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RunEndScreen : Control
{
    public override void _Ready()
    {
        var outcomeLabel = GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");
        outcomeLabel.Text = RunEndContext.Outcome == RunEndOutcome.Win
            ? "Victory! You cleared the run."
            : "Defeated...";

        GetNode<Button>("CenterContainer/VBoxContainer/RestartButton").Pressed += OnRestartPressed;
    }

    private void OnRestartPressed() => RunManager.Instance.StartNewRun();
}
