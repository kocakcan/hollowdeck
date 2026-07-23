using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        ScreenBackground.Attach(this, "etched", new Color(0.9f, 0.9f, 0.95f));
        GetNode<Button>("CenterContainer/VBoxContainer/StartButton").Pressed += OnStartPressed;
        GetNode<Button>("CenterContainer/VBoxContainer/UnlocksButton").Pressed += OnUnlocksPressed;
        GetNode<Button>("CenterContainer/VBoxContainer/SettingsButton").Pressed += OnSettingsPressed;
    }

    private void OnStartPressed() => RunManager.Instance.StartNewRun();

    private void OnUnlocksPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MetaProgression);

    private void OnSettingsPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Settings);
}
