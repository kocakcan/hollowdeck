using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GetNode<Button>("CenterContainer/VBoxContainer/StartButton").Pressed += OnStartPressed;
        GetNode<Button>("CenterContainer/VBoxContainer/UnlocksButton").Pressed += OnUnlocksPressed;
    }

    private void OnStartPressed() => RunManager.Instance.StartNewRun();

    private void OnUnlocksPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MetaProgression);
}
