using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Hollowdeck.Data;

// The one place a content JSON is read off res://.
//
// The six databases each carried their own copy of these eight lines, which was
// harmless while the only reader was the editor: there res:// *is* the source
// tree, so FileAccess.Open cannot miss. Under export it can, and the way it
// fails is the reason for the guard below.
//
// Open returns null rather than throwing. GetAsText() on that null is a bare
// NullReferenceException naming neither the file nor the cause - and Godot's
// .NET layer logs an unhandled exception through GD.PushError rather than
// aborting, so the process keeps running and **still exits 0**. Verified by
// forcing it: a build with data/ excluded from the .pck boots to a menu, has no
// cards, no enemies and no acts, and reports success. That is what
// tools/build-export.sh's boot check greps the log for instead of trusting an
// exit code.
//
// The null check is why this file exists at all: six copies of a guard is six
// places to forget it, and the seventh database gets written by copy-pasting
// whichever of the six was open.
//
// This deliberately does not turn the JSONs into Godot Resources. The
// data-vs-code split (CLAUDE.md, risk 1) is load-bearing; packing is a build
// concern and gets a build fix.
public static class DataFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<T> LoadList<T>(string resPath)
    {
        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            // Throwing rather than returning empty: a deckbuilder with no cards
            // is not a degraded game, it is a broken one, and limping on would
            // bury this line under a cascade of downstream NullReferences. This
            // is the first thing printed and the only line naming the cause.
            throw new InvalidOperationException(
                $"DataFile: could not open '{resPath}' ({FileAccess.GetOpenError()}). " +
                "In the editor that means the file is missing or unreadable. In an exported " +
                "build it means the file did not make it into the .pck - check the " +
                "include_filter/exclude_filter of the preset in export_presets.cfg that built " +
                "it. Nothing downstream will work: the game will run on with no content.");
        }

        return JsonSerializer.Deserialize<List<T>>(file.GetAsText(), Options)!;
    }
}
