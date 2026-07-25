using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Hollowdeck.Run;

// Autoload singleton for OS/hardware-scoped settings (volume, window mode) -
// kept separate from MetaProgressionManager since it's not save-progress
// data, same reasoning as MetaProgressionManager being separate from
// RunState. Same FileAccess+System.Text.Json persistence idiom, same
// path-parameterized LoadFrom/SaveTo testability seam.
public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; } = null!;

    private const string SavePath = "user://settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private SettingsData _data = new();

    public float MasterVolume => _data.MasterVolume;
    public float MusicVolume => _data.MusicVolume;
    public float SfxVolume => _data.SfxVolume;
    public bool Fullscreen => _data.Fullscreen;
    public bool ReduceMotion => _data.ReduceMotion;

    public override void _Ready()
    {
        Instance = this;
        LoadFrom(SavePath);
        Apply();
    }

    public void SetMasterVolume(float linear, string? path = null)
    {
        _data.MasterVolume = Math.Clamp(linear, 0f, 1f);
        Apply();
        SaveTo(path ?? SavePath);
    }

    public void SetMusicVolume(float linear, string? path = null)
    {
        _data.MusicVolume = Math.Clamp(linear, 0f, 1f);
        Apply();
        SaveTo(path ?? SavePath);
    }

    public void SetSfxVolume(float linear, string? path = null)
    {
        _data.SfxVolume = Math.Clamp(linear, 0f, 1f);
        Apply();
        SaveTo(path ?? SavePath);
    }

    public void SetFullscreen(bool fullscreen, string? path = null)
    {
        _data.Fullscreen = fullscreen;
        Apply();
        SaveTo(path ?? SavePath);
    }

    public void SetReduceMotion(bool reduceMotion, string? path = null)
    {
        _data.ReduceMotion = reduceMotion;
        SaveTo(path ?? SavePath);
    }

    private void Apply()
    {
        ApplyBusVolume("Master", _data.MasterVolume);
        ApplyBusVolume("Music", _data.MusicVolume);
        ApplyBusVolume("SFX", _data.SfxVolume);

        DisplayServer.WindowSetMode(_data.Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }

    // Guarded on GetBusIndex != -1 since "Music"/"SFX" are created at
    // runtime by AudioManager, not authored in a bus layout resource -
    // this keeps Apply() safe even if autoload order is ever changed.
    private static void ApplyBusVolume(string busName, float linear)
    {
        int idx = AudioServer.GetBusIndex(busName);
        if (idx == -1) return;
        bool muted = linear <= 0.0001f;
        AudioServer.SetBusMute(idx, muted);
        if (!muted) AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(linear));
    }

    public void LoadFrom(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            _data = new SettingsData();
            return;
        }

        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            _data = JsonSerializer.Deserialize<SettingsData>(file.GetAsText(), Options) ?? new SettingsData();
        }
        catch (Exception e)
        {
            GD.PushWarning($"SettingsManager: '{path}' unreadable ({e.Message}); using defaults.");
            _data = new SettingsData();
        }
    }

    public void SaveTo(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(_data, Options));
    }
}
