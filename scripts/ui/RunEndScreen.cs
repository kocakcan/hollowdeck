using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RunEndScreen : Control
{
    private const int RunCompletionShards = 10;
    private const int RunWinBonusShards = 15;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "demonic", new Color(0.75f, 0.7f, 0.7f));
        bool won = RunEndContext.Outcome == RunEndOutcome.Win;

        int shards = RunCompletionShards + (won ? RunWinBonusShards : 0);
        MetaProgressionManager.Instance.GrantShards(shards);
        MetaProgressionManager.Instance.LogSeed(RunManager.Instance.RunSeed, won ? "Win" : "Lose");
        RunSaveManager.Delete();

        var outcomeLabel = GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");
        outcomeLabel.Text = (won ? "Victory! You cleared the run." : "Defeated...") +
                             $"\nSeed: {RunManager.Instance.RunSeed}  (+{shards} Shards)";

        GetNode<Button>("CenterContainer/VBoxContainer/RestartButton").Pressed += OnRestartPressed;
        GetNode<Button>("CenterContainer/VBoxContainer/ViewUnlocksButton").Pressed += OnViewUnlocksPressed;
    }

    private void OnRestartPressed() => RunManager.Instance.StartNewRun();

    private void OnViewUnlocksPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MetaProgression);
}
