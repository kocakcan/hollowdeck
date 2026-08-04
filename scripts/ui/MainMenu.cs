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
            button.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        }

        ShowVersion();

        // Resume if there is a run to resume, otherwise start one - the same
        // button the mouse would go for first. No cancel action: this is the
        // root screen, there is nothing behind it.
        ScreenKeyboardNav.Attach(this, () => continueButton.Visible ? continueButton : startButton);
    }

    // Read back out of ProjectSettings rather than restated as a C# const, for
    // the same no-drift reason ScreenKeyboardNav.KeyHint reads the InputMap
    // instead of naming a key: application/config/version is what Godot stamps
    // into the Windows .exe version fields and the macOS Info.plist, so the
    // number on screen and the number in the build metadata cannot disagree.
    // A Label is not focusable, so this does not touch keyboard navigation.
    private void ShowVersion()
    {
        var label = GetNode<Label>("VersionLabel");
        var version = ProjectSettings.GetSetting("application/config/version").AsString();
        label.Text = string.IsNullOrEmpty(version) ? "" : $"v{version}";
        label.AddThemeColorOverride("font_color", PixelSpec.Ramp.N5);
        // 16, not 14: ART_SPEC section 7 puts every rendered size on the 8px
        // grid so bitmap type lands on whole pixels, and PixelSpecSmokeTest
        // fails anything off it.
        label.AddThemeFontSizeOverride("font_size", 16);
    }

    // Same sourced Fantasy UI Box nine-patch the End Turn button already
    // uses (CREDITS.md) - reused as-is rather than sourcing new menu-button
    // art, matching the "no new external art this session" constraint the
    // rest of the visual overhaul has followed.
    private static void ApplyButtonChrome(Button button)
    {
        button.CustomMinimumSize = new Vector2(220, 44);
        ChromeStyles.ApplyEmphasisButtonStyle(button);
    }

    private void OnContinuePressed() => RunManager.Instance.TryContinueRun();

    private void OnStartPressed() => RunManager.Instance.StartNewRun();

    private void OnUnlocksPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MetaProgression);

    private void OnSettingsPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Settings);
}
