using System;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Pins RunManager's screen changes to a hard cut for the duration of a test.
//
// Phase 5 put a fade in RunManager.ChangeScreen, gated on Reduce Motion. That
// gate reads the *developer's* user://settings.json, which means a suite that
// drives a button into ChangeScreen behaves differently depending on a file
// that has nothing to do with the test: with the fade on, ChangeSceneToFile is
// deferred into a tween callback and never fires before GetTree().Quit(); with
// it off, it fires synchronously. Two suites document an expected engine error
// that only appears on the synchronous path, so "expected output" would have
// become machine-dependent.
//
//     using (HardCutGuard.Protect())
//     {
//         // ...test that presses a button which navigates...
//     }
//
// Reduce Motion is the real seam rather than a test-only flag on ScreenFade:
// there is no behaviour here a player cannot also reach from the settings
// screen, so the tests exercise a shipping path.
//
// Writes to a scratch path, never user://settings.json, and restores the real
// file's values on Dispose - so running the suite can never change what the
// player sees next launch.
public readonly struct HardCutGuard : IDisposable
{
    private const string ScratchPath = "user://hardcut_settings_test.json";

    private readonly bool _wasReduceMotion;

    private HardCutGuard(bool wasReduceMotion) => _wasReduceMotion = wasReduceMotion;

    public static HardCutGuard Protect()
    {
        var settings = SettingsManager.Instance;
        bool previous = settings.ReduceMotion;
        settings.SetReduceMotion(true, ScratchPath);
        return new HardCutGuard(previous);
    }

    public void Dispose() => SettingsManager.Instance.SetReduceMotion(_wasReduceMotion, ScratchPath);
}
