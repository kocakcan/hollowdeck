using System.Collections.Generic;
using Godot;

namespace Hollowdeck.Data;

// Same shape as EnemyDatabase/CardDatabase, with one addition: All preserves
// the authored order, because for acts the order *is* the play order (act 1,
// then 2, then 3). RunState.ActIndex indexes into it.
public static class ActDatabase
{
    private static readonly List<ActDefinition> Acts = new();

    public static IReadOnlyList<ActDefinition> All => Acts;

    public static int Count => Acts.Count;

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<ActDefinition>("res://data/acts/acts.json");
        Acts.Clear();
        Acts.AddRange(defs);
    }

    // Clamped rather than throwing: an ActIndex from a save written by a build
    // with more acts than this one has must degrade to the last act, not kill
    // the run - same "stale save data stays inert" discipline as
    // RunSaveManager's id resolution.
    public static ActDefinition At(int index) => Acts[Mathf.Clamp(index, 0, Acts.Count - 1)];

    public static ActDefinition Get(string id) => Acts.Find(a => a.Id == id)!;
}
