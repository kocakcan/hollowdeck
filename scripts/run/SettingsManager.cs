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
    public bool Fullscreen => _data.Fullscreen;

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

    public void SetFullscreen(bool fullscreen, string? path = null)
    {
        _data.Fullscreen = fullscreen;
        Apply();
        SaveTo(path ?? SavePath);
    }

    private void Apply()
    {
        int masterBus = AudioServer.GetBusIndex("Master");
        bool muted = _data.MasterVolume <= 0.0001f;
        AudioServer.SetBusMute(masterBus, muted);
        if (!muted) AudioServer.SetBusVolumeDb(masterBus, Mathf.LinearToDb(_data.MasterVolume));

        DisplayServer.WindowSetMode(_data.Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
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
