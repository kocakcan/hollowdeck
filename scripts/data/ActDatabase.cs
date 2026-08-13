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

    // Every id named in any act's BossIds. Derived rather than authored,
    // because BossIds is already the one place the game decides what a boss is
    // - MapGenerator draws from it and nothing else marks a boss. A bool on
    // EnemyDefinition would make "is this a boss" answerable two ways, and the
    // two would disagree the first time a boss was repurposed as an elite.
    private static readonly HashSet<string> BossIds = new();

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<ActDefinition>("res://data/acts/acts.json");
        Acts.Clear();
        Acts.AddRange(defs);

        BossIds.Clear();
        foreach (var act in Acts)
        {
            foreach (var id in act.BossIds) BossIds.Add(id);
        }
    }

    // Read by the ascension ladder's boss-HP knob, which needs to raise a boss
    // without touching the normal fights whose mean is the denominator every
    // boss ratio in BalanceReport is divided by.
    public static bool IsBoss(string enemyId) => BossIds.Contains(enemyId);

    // Clamped rather than throwing: an ActIndex from a save written by a build
    // with more acts than this one has must degrade to the last act, not kill
    // the run - same "stale save data stays inert" discipline as
    // RunSaveManager's id resolution.
    public static ActDefinition At(int index) => Acts[Mathf.Clamp(index, 0, Acts.Count - 1)];

    public static ActDefinition Get(string id) => Acts.Find(a => a.Id == id)!;
}
