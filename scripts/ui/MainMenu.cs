using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        ScreenBackground.Attach(this, "etched", new Color(0.9f, 0.9f, 0.95f));
        var continueButton = GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");
        continueButton.Visible = RunSaveManager.SaveExists();
        continueButton.Pressed += OnContinuePressed;
        var startButton = GetNode<Button>("CenterContainer/VBoxContainer/StartButton");
        startButton.Pressed += OnStartPressed;
        var unlocksButton = GetNode<Button>("CenterContainer/VBoxContainer/UnlocksButton");
        unlocksButton.Pressed += OnUnlocksPressed;
        var settingsButton = GetNode<Button>("CenterContainer/VBoxContainer/SettingsButton");
        settingsButton.Pressed += OnSettingsPressed;

        foreach (var button in new[] { continueButton, startButton, unlocksButton, settingsButton })
        {
            ApplyButtonChrome(button);
        }
    }

    // Same sourced Fantasy UI Box nine-patch the End Turn button already
    // uses (CREDITS.md) - reused as-is rather than sourcing new menu-button
    // art, matching the "no new external art this session" constraint the
    // rest of the visual overhaul has followed.
    private static void ApplyButtonChrome(Button button)
    {
        button.CustomMinimumSize = new Vector2(220, 44);
        button.AddThemeStyleboxOverride("normal", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_normal.png"));
        button.AddThemeStyleboxOverride("hover", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_hover.png"));
        button.AddThemeStyleboxOverride("pressed", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_pressed.png"));
        button.AddThemeStyleboxOverride("disabled", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_normal.png"));
    }

    private void OnContinuePressed() => RunManager.Instance.TryContinueRun();

    private void OnStartPressed() => RunManager.Instance.StartNewRun();

    private void OnUnlocksPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MetaProgression);

    private void OnSettingsPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Settings);
}
