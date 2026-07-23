using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class SettingsScreen : Control
{
    public override void _Ready()
    {
        var volumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/VolumeSlider");
        volumeSlider.Value = SettingsManager.Instance.MasterVolume;
        volumeSlider.ValueChanged += v => SettingsManager.Instance.SetMasterVolume((float)v);

        var fullscreenToggle = GetNode<CheckButton>("CenterContainer/VBoxContainer/FullscreenToggle");
        fullscreenToggle.ButtonPressed = SettingsManager.Instance.Fullscreen;
        fullscreenToggle.Toggled += pressed => SettingsManager.Instance.SetFullscreen(pressed);

        GetNode<Button>("CenterContainer/VBoxContainer/BackButton").Pressed += OnBackPressed;
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
}
