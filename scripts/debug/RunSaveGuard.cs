using System;
using System.Collections.Generic;
using Godot;

namespace Hollowdeck.Debug;

// Protects the developer's/player's real save files from tests that can't help
// writing them.
//
// Most persistence tests pass a `user://*_test.json` scratch path to
// RunSaveManager and never come near the real files. The exception is any test
// that drives a screen far enough to change scenes: a combat-end Continue click
// routes through RunManager.ChangeScreen(Reward), and Reward is in
// RunManager.AutoSaveScreens, so simply reaching it overwrites
// user://run_save.json with the test's fixture state. RunEndScreen goes further
// and banks a run result into the meta save.
//
// ScreenShot.cs already solves exactly this for screenshots by copying both
// files aside and restoring them in a finally; this is the same idea scoped to
// a single test:
//
//     using (RunSaveGuard.Protect())
//     {
//         // ...test that ends up triggering a save...
//     }
//
// Restore is unconditional (Dispose runs even if the test throws), and a file
// that didn't exist beforehand is deleted again rather than left behind.
public readonly struct RunSaveGuard : IDisposable
{
    private static readonly string[] ProtectedPaths =
    {
        "user://run_save.json",
        "user://meta_progression.json",
    };

    // null value = the file did not exist before the test and must not after it.
    private readonly Dictionary<string, string?> _contents;

    private RunSaveGuard(Dictionary<string, string?> contents) => _contents = contents;

    public static RunSaveGuard Protect()
    {
        var contents = new Dictionary<string, string?>();
        foreach (var path in ProtectedPaths)
        {
            contents[path] = Read(path);
        }
        return new RunSaveGuard(contents);
    }

    public void Dispose()
    {
        if (_contents is null) return;
        foreach (var (path, original) in _contents)
        {
            Write(path, original);
        }
    }

    private static string? Read(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    private static void Write(string path, string? contents)
    {
        if (contents is null)
        {
            if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
            return;
        }
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file?.StoreString(contents);
    }
}
