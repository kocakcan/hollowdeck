using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class SettingsScreen : Control
{
    public override void _Ready()
    {
        ScreenBackground.Attach(this, "black_cobalt", new Color(0.7f, 0.7f, 0.75f));
        var volumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/VolumeSlider");
        volumeSlider.Value = SettingsManager.Instance.MasterVolume;
        volumeSlider.ValueChanged += v => SettingsManager.Instance.SetMasterVolume((float)v);

        var musicVolumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/MusicVolumeSlider");
        musicVolumeSlider.Value = SettingsManager.Instance.MusicVolume;
        musicVolumeSlider.ValueChanged += v => SettingsManager.Instance.SetMusicVolume((float)v);

        var sfxVolumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/SfxVolumeSlider");
        sfxVolumeSlider.Value = SettingsManager.Instance.SfxVolume;
        sfxVolumeSlider.ValueChanged += v => SettingsManager.Instance.SetSfxVolume((float)v);

        var fullscreenToggle = GetNode<CheckButton>("CenterContainer/VBoxContainer/FullscreenToggle");
        fullscreenToggle.ButtonPressed = SettingsManager.Instance.Fullscreen;
        fullscreenToggle.Toggled += pressed => SettingsManager.Instance.SetFullscreen(pressed);

        var reduceMotionToggle = GetNode<CheckButton>("CenterContainer/VBoxContainer/ReduceMotionToggle");
        reduceMotionToggle.ButtonPressed = SettingsManager.Instance.ReduceMotion;
        reduceMotionToggle.Toggled += pressed => SettingsManager.Instance.SetReduceMotion(pressed);

        var backButton = GetNode<Button>("CenterContainer/VBoxContainer/BackButton");
        backButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        backButton.Pressed += OnBackPressed;
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
}
