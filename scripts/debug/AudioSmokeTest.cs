using System;
using Godot;
using Hollowdeck.Audio;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for the procedural audio system: stream construction
// correctness (byte length/format/mix rate/loop points), signal sanity (not
// silent, no overflow), AudioManager bus setup and SFX pool playback, and
// Settings Music/SfxVolume round-trip. Godot's --headless flag forces the
// Dummy audio driver, so playback logic runs fully without a real device -
// this can prove the data is right, not that it sounds right (see the
// windowed manual-listening pass called out in the Phase 5 plan).
// Run via `godot --headless scenes/debug/AudioSmokeTest.tscn`.
public partial class AudioSmokeTest : Node
{
    private const string ScratchPath = "user://audio_settings_test.json";

    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        TestSfxCuesBuildCorrectly();
        TestMusicLoopsBuildCorrectly();
        TestStingersBuildCorrectly();
        TestAudioManagerBusesExist();
        TestSfxPoolPlaybackDoesNotThrow();
        TestUnknownCueDoesNotThrow();
        TestSettingsVolumeRoundTrip();
        TestVolumeChangesLeaveTheWindowAlone();

        GD.Print($"AudioSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void CheckStream(string label, AudioStreamWav stream)
    {
        Check($"{label}_format_16bit", stream.Format == AudioStreamWav.FormatEnum.Format16Bits,
            $"format={stream.Format}");
        Check($"{label}_not_stereo", !stream.Stereo, "expected mono");
        Check($"{label}_even_byte_length", stream.Data.Length % 2 == 0, $"length={stream.Data.Length}");
        Check($"{label}_not_empty", stream.Data.Length > 0, "expected non-empty PCM data");

        // Stride through the buffer rather than decoding every frame - plenty
        // for a "something audible was synthesized" check, and Mono (Godot's
        // C# runtime) is dramatically slower per-call on Span/BinaryPrimitives
        // than CoreCLR, so scanning a full ~500K-frame music loop sample-by-
        // sample here (not in production code, which never does this) turned
        // a 28ms synthesis into a 50-second test.
        int frameCount = stream.Data.Length / 2;
        int step = Math.Max(1, frameCount / 5000);
        int peakAbs = 0;
        for (int i = 0; i < frameCount; i += step)
        {
            int byteOffset = i * 2;
            short s = (short)(stream.Data[byteOffset] | (stream.Data[byteOffset + 1] << 8));
            peakAbs = Math.Max(peakAbs, Math.Abs((int)s));
        }
        // short can't literally overflow (BuildWav clamps before casting) -
        // the meaningful check is that something audible was synthesized.
        Check($"{label}_signal_has_amplitude", peakAbs > 1000, $"peak amplitude={peakAbs} (out of {short.MaxValue})");
    }

    private void TestSfxCuesBuildCorrectly()
    {
        CheckStream("ui_click", AudioCues.BuildUiClick());
        CheckStream("card_play", AudioCues.BuildCardPlay());
        CheckStream("card_draw", AudioCues.BuildCardDraw());
        CheckStream("damage_hit", AudioCues.BuildDamageHit());
        CheckStream("block_gain", AudioCues.BuildBlockGain());
        CheckStream("status_apply", AudioCues.BuildStatusApply());
        CheckStream("heal", AudioCues.BuildHeal());
        CheckStream("energy_gain", AudioCues.BuildEnergyGain());
        CheckStream("enemy_death", AudioCues.BuildEnemyDeath());
        CheckStream("reward_pickup", AudioCues.BuildRewardPickup());
    }

    private void TestMusicLoopsBuildCorrectly()
    {
        var ambient = AudioMusic.BuildAmbientLoop();
        CheckStream("ambient_loop", ambient);
        Check("ambient_loop_mode_forward", ambient.LoopMode == AudioStreamWav.LoopModeEnum.Forward,
            $"mode={ambient.LoopMode}");
        Check("ambient_loop_begin_zero", ambient.LoopBegin == 0, $"loopBegin={ambient.LoopBegin}");
        Check("ambient_loop_end_within_data", ambient.LoopEnd > 0 && ambient.LoopEnd <= ambient.Data.Length / 2,
            $"loopEnd={ambient.LoopEnd} frames, data={ambient.Data.Length / 2} frames");

        var combat = AudioMusic.BuildCombatLoop();
        CheckStream("combat_loop", combat);
        Check("combat_loop_mode_forward", combat.LoopMode == AudioStreamWav.LoopModeEnum.Forward,
            $"mode={combat.LoopMode}");
        Check("combat_loop_begin_zero", combat.LoopBegin == 0, $"loopBegin={combat.LoopBegin}");
        Check("combat_loop_end_within_data", combat.LoopEnd > 0 && combat.LoopEnd <= combat.Data.Length / 2,
            $"loopEnd={combat.LoopEnd} frames, data={combat.Data.Length / 2} frames");
    }

    private void TestStingersBuildCorrectly()
    {
        var victory = AudioMusic.BuildVictoryStinger();
        CheckStream("victory_stinger", victory);
        Check("victory_stinger_no_loop", victory.LoopMode == AudioStreamWav.LoopModeEnum.Disabled,
            $"mode={victory.LoopMode}");

        var defeat = AudioMusic.BuildDefeatStinger();
        CheckStream("defeat_stinger", defeat);
        Check("defeat_stinger_no_loop", defeat.LoopMode == AudioStreamWav.LoopModeEnum.Disabled,
            $"mode={defeat.LoopMode}");
    }

    private void TestAudioManagerBusesExist()
    {
        Check("audio_manager_instance_exists", AudioManager.Instance is not null, "AudioManager autoload not found");
        Check("music_bus_exists", AudioServer.GetBusIndex("Music") != -1, "Music bus missing");
        Check("sfx_bus_exists", AudioServer.GetBusIndex("SFX") != -1, "SFX bus missing");
    }

    private void TestSfxPoolPlaybackDoesNotThrow()
    {
        bool threw = false;
        try
        {
            for (int i = 0; i < 12; i++) AudioManager.Instance.PlaySfx("ui_click"); // > SfxPoolSize (8)
        }
        catch (System.Exception e)
        {
            threw = true;
            GD.PushWarning($"AudioSmokeTest: PlaySfx threw: {e.Message}");
        }
        Check("sfx_pool_playback_no_throw", !threw, "PlaySfx threw on repeated/overlapping calls");

        // This used to assert that a pooled voice still reported Playing, and
        // that is a race the test loses on a loaded machine. Play() registers
        // the playback synchronously, but the audio thread then mixes on
        // wall-clock time and drops it when the stream ends - and ui_click is
        // 60ms of audio (AudioCues.BuildUiClick: a 0.06s glide). So the
        // assertion had a 60ms budget between the loop above and the read
        // below, which this machine always wins and a shared CI runner
        // intermittently loses to a scheduler hiccup. It failed on Linux CI,
        // passed on a re-run of the identical commit, and never reproduced in
        // 15 consecutive local runs.
        //
        // What it was really guarding is that PlaySfx routes a cue onto a
        // pooled voice rather than silently doing nothing, and that does not
        // need the audio backend to still be mid-sound. The stream assignment
        // is synchronous and permanent, so compare the bytes: AudioManager
        // builds its ui_click from AudioCues.BuildUiClick, whose synthesis is
        // pure (a glide through an ADSR envelope, no RNG - unlike BuildCardPlay,
        // which mixes noise and would not reproduce), so rebuilding it here
        // yields the identical PCM. Matching those bytes on a pooled player
        // proves the routing *and* that it is this cue and not another one.
        var expected = AudioCues.BuildUiClick().Data;
        int voicesCarryingTheCue = 0;
        foreach (var child in AudioManager.Instance.GetChildren())
        {
            if (child is AudioStreamPlayer { Stream: AudioStreamWav wav }
                && wav.Data.AsSpan().SequenceEqual(expected))
            {
                voicesCarryingTheCue++;
            }
        }
        Check("sfx_pool_routes_the_cue_onto_a_voice", voicesCarryingTheCue > 0,
            $"{voicesCarryingTheCue} pooled players carry ui_click's PCM - PlaySfx assigned nothing");
    }

    private void TestUnknownCueDoesNotThrow()
    {
        bool threw = false;
        try { AudioManager.Instance.PlaySfx("not_a_real_cue"); }
        catch (System.Exception) { threw = true; }
        Check("unknown_cue_no_throw", !threw, "PlaySfx should warn and return, not throw, for an unknown cue");
    }

    private void TestSettingsVolumeRoundTrip()
    {
        if (FileAccess.FileExists(ScratchPath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ScratchPath));
        var settings = SettingsManager.Instance;
        settings.LoadFrom(ScratchPath);

        settings.SetMusicVolume(0.4f, ScratchPath);
        settings.SetSfxVolume(0.65f, ScratchPath);
        settings.LoadFrom(ScratchPath);
        Check("music_volume_round_trip", Mathf.IsEqualApprox(settings.MusicVolume, 0.4f), $"got {settings.MusicVolume}");
        Check("sfx_volume_round_trip", Mathf.IsEqualApprox(settings.SfxVolume, 0.65f), $"got {settings.SfxVolume}");

        using (var file = FileAccess.Open(ScratchPath, FileAccess.ModeFlags.Write))
        {
            file.StoreString("{ not valid json [[[");
        }
        settings.LoadFrom(ScratchPath);
        Check("corrupted_settings_falls_back_to_defaults",
            settings.MusicVolume == 1f && settings.SfxVolume == 1f,
            $"music={settings.MusicVolume} sfx={settings.SfxVolume}");

        // Restore real settings from disk so this test doesn't leave the
        // singleton pointed at scratch data for whatever runs after it.
        settings.LoadFrom("user://settings.json");
    }

    // The reported bug: dragging a volume slider dropped the game out of
    // fullscreen. Every setter called one Apply() that did the audio buses
    // *and* re-asserted the saved window mode, so a player who had gone
    // fullscreen through the OS - never touching the toggle, so the flag said
    // "windowed" - got thrown back into a window by changing the volume.
    //
    // Asserted by counting ApplyWindow calls rather than by reading the window
    // mode back: a headless DisplayServer reports Windowed no matter what it is
    // told, so comparing modes here would be a check that cannot fail.
    private void TestVolumeChangesLeaveTheWindowAlone()
    {
        var settings = SettingsManager.Instance;
        settings.LoadFrom("user://settings.json");

        // Whatever the window is already doing. SetFullscreen below is called
        // with it, so the check exercises ApplyWindow without actually moving
        // a developer's window if this suite is ever run windowed.
        bool current = settings.Fullscreen;

        int before = SettingsManager.WindowModeApplications;
        settings.SetMasterVolume(0.5f, ScratchPath);
        settings.SetMusicVolume(0.5f, ScratchPath);
        settings.SetSfxVolume(0.5f, ScratchPath);
        int afterVolumes = SettingsManager.WindowModeApplications;

        settings.SetFullscreen(current, ScratchPath);
        int afterFullscreen = SettingsManager.WindowModeApplications;

        Check("volume_changes_do_not_touch_the_window_mode", afterVolumes == before,
            $"{afterVolumes - before} window-mode apply(s) from three volume changes - Apply() is fused again");
        Check("the_fullscreen_setting_still_applies_the_window_mode", afterFullscreen == afterVolumes + 1,
            $"SetFullscreen produced {afterFullscreen - afterVolumes} apply(s), expected 1");

        settings.LoadFrom("user://settings.json");
    }
}
