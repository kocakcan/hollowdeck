using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Audio;

namespace Hollowdeck.Run;

// Autoload singleton (same static-Instance pattern as RunManager/
// SettingsManager) owning all sound. SFX play through a small round-robin
// pool of AudioStreamPlayers; music crossfades between two players so a
// screen transition never has an audible cut. All sounds are procedurally
// synthesized (AudioCues/AudioMusic) - built once here and cached.
//
// Must be registered before MetaProgressionManager/SettingsManager in
// project.godot's [autoload] list: SettingsManager.Apply() looks up the
// Music/SFX bus indices this node creates in _Ready().
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    private const int SfxPoolSize = 8;
    private readonly AudioStreamPlayer[] _sfxPool = new AudioStreamPlayer[SfxPoolSize];
    private int _nextSfxSlot;

    private AudioStreamPlayer _musicA = null!;
    private AudioStreamPlayer _musicB = null!;
    private AudioStreamPlayer _activeMusicPlayer = null!;
    private AudioStreamPlayer _idleMusicPlayer => _activeMusicPlayer == _musicA ? _musicB : _musicA;
    private string? _currentTrackKey;

    private readonly Dictionary<string, AudioStreamWav> _sfx = new();
    private readonly Dictionary<string, AudioStreamWav> _music = new();

    public override void _Ready()
    {
        Instance = this;
        SetupBuses();
        SetupSfxPool();
        SetupMusicPlayers();
        BuildCuesAndTracks();
    }

    private void SetupBuses()
    {
        AddBusIfMissing("Music");
        AddBusIfMissing("SFX");
    }

    private static void AddBusIfMissing(string name)
    {
        if (AudioServer.GetBusIndex(name) != -1) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, "Master");
    }

    private void SetupSfxPool()
    {
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var player = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(player);
            _sfxPool[i] = player;
        }
    }

    private void SetupMusicPlayers()
    {
        _musicA = new AudioStreamPlayer { Bus = "Music" };
        _musicB = new AudioStreamPlayer { Bus = "Music" };
        AddChild(_musicA);
        AddChild(_musicB);
        _activeMusicPlayer = _musicA;
    }

    private void BuildCuesAndTracks()
    {
        _sfx["ui_click"] = AudioCues.BuildUiClick();
        _sfx["card_play"] = AudioCues.BuildCardPlay();
        _sfx["card_draw"] = AudioCues.BuildCardDraw();
        _sfx["damage_hit"] = AudioCues.BuildDamageHit();
        _sfx["block_gain"] = AudioCues.BuildBlockGain();
        _sfx["status_apply"] = AudioCues.BuildStatusApply();
        _sfx["heal"] = AudioCues.BuildHeal();
        _sfx["energy_gain"] = AudioCues.BuildEnergyGain();
        _sfx["enemy_death"] = AudioCues.BuildEnemyDeath();
        _sfx["reward_pickup"] = AudioCues.BuildRewardPickup();

        _music["ambient"] = AudioMusic.BuildAmbientLoop();
        _music["combat"] = AudioMusic.BuildCombatLoop();
        _music["victory"] = AudioMusic.BuildVictoryStinger();
        _music["defeat"] = AudioMusic.BuildDefeatStinger();
    }

    public void PlaySfx(string cue)
    {
        if (!_sfx.TryGetValue(cue, out var stream))
        {
            GD.PushWarning($"AudioManager: unknown SFX cue '{cue}'");
            return;
        }
        var player = PickSfxPlayer();
        player.Stream = stream;
        player.Play();
    }

    private AudioStreamPlayer PickSfxPlayer()
    {
        foreach (var p in _sfxPool)
        {
            if (!p.Playing) return p;
        }
        var picked = _sfxPool[_nextSfxSlot]; // pool exhausted - round-robin steal
        _nextSfxSlot = (_nextSfxSlot + 1) % SfxPoolSize;
        return picked;
    }

    public void PlayMusicForState(RunManager.ScreenState state)
    {
        if (state == RunManager.ScreenState.Victory) { _ = PlayStingerAsync("victory"); return; }
        if (state == RunManager.ScreenState.Defeat) { _ = PlayStingerAsync("defeat"); return; }

        string key = state == RunManager.ScreenState.Combat ? "combat" : "ambient";
        if (key == _currentTrackKey) return; // e.g. Map -> Shop -> Map: no restart/re-crossfade
        CrossfadeTo(key);
    }

    private void CrossfadeTo(string trackKey)
    {
        var incoming = _idleMusicPlayer;
        var outgoing = _activeMusicPlayer;

        incoming.Stream = _music[trackKey];
        incoming.VolumeDb = -80f;
        incoming.Play();

        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(incoming, "volume_db", 0f, 1.0);
        if (outgoing.Playing) tween.TweenProperty(outgoing, "volume_db", -80f, 1.0);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (outgoing != incoming) outgoing.Stop();
        }));

        _activeMusicPlayer = incoming;
        _currentTrackKey = trackKey;
    }

    private async Task PlayStingerAsync(string key)
    {
        var stingerPlayer = _idleMusicPlayer;
        stingerPlayer.Stream = _music[key];
        stingerPlayer.VolumeDb = 0f;

        GetTree().CreateTween().TweenProperty(_activeMusicPlayer, "volume_db", -80f, 0.3);
        stingerPlayer.Play();

        await ToSignal(stingerPlayer, AudioStreamPlayer.SignalName.Finished);
        if (IsInstanceValid(this))
        {
            GetTree().CreateTween().TweenProperty(_activeMusicPlayer, "volume_db", 0f, 0.5);
        }
    }
}
