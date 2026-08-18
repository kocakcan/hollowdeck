using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class SettingsScreen : Control
{
    public override void _Ready()
    {
        ScreenBackground.AttachMenu(this);
        var volumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/VolumeSlider");
        volumeSlider.Value = SettingsManager.Instance.MasterVolume;
        volumeSlider.ValueChanged += v => SettingsManager.Instance.SetMasterVolume((float)v);

        var musicVolumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/MusicVolumeSlider");
        musicVolumeSlider.Value = SettingsManager.Instance.MusicVolume;
        musicVolumeSlider.ValueChanged += v => SettingsManager.Instance.SetMusicVolume((float)v);

        var sfxVolumeSlider = GetNode<HSlider>("CenterContainer/VBoxContainer/SfxVolumeSlider");
        sfxVolumeSlider.Value = SettingsManager.Instance.SfxVolume;
        sfxVolumeSlider.ValueChanged += v => SettingsManager.Instance.SetSfxVolume((float)v);

        // Seeded from the live window mode rather than the saved flag: the OS
        // can put the game fullscreen without this setting ever being touched
        // (macOS green button), and a checkbox reading "off" on a fullscreen
        // game needs two clicks to turn it off. Assigned before Toggled is
        // subscribed, so seeding fires nothing.
        var fullscreenToggle = GetNode<CheckButton>("CenterContainer/VBoxContainer/FullscreenToggle");
        fullscreenToggle.ButtonPressed = SettingsManager.Instance.WindowIsFullscreen;
        fullscreenToggle.Toggled += pressed => SettingsManager.Instance.SetFullscreen(pressed);

        var reduceMotionToggle = GetNode<CheckButton>("CenterContainer/VBoxContainer/ReduceMotionToggle");
        reduceMotionToggle.ButtonPressed = SettingsManager.Instance.ReduceMotion;
        reduceMotionToggle.Toggled += pressed => SettingsManager.Instance.SetReduceMotion(pressed);

        foreach (var slider in new[] { volumeSlider, musicVolumeSlider, sfxVolumeSlider })
        {
            ChromeStyles.ApplyFocusableSliderStyle(slider);
        }

        var backButton = GetNode<Button>("CenterContainer/VBoxContainer/BackButton");
        backButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        backButton.Pressed += OnBackPressed;

        // Focus starts on the first slider, and Escape leaves - this screen
        // had no keyboard exit at all. Note HSlider eats Left/Right to change
        // its value, so moving *between* rows here is Up/Down (or Tab), which
        // is Godot's default behaviour and the reason nothing here overrides
        // the arrow keys.
        ScreenKeyboardNav.Attach(this, () => volumeSlider, OnBackPressed);
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
}
