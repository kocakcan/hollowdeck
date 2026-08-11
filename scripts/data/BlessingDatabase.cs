using System;
using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class BlessingDatabase
{
    private static readonly List<BlessingDefinition> Blessings = new();

    public static void LoadAll()
    {
        Blessings.Clear();
        Blessings.AddRange(DataFile.LoadList<BlessingDefinition>("res://data/blessings/blessings.json"));
    }

    public static IReadOnlyList<BlessingDefinition> All => Blessings;

    public static BlessingDefinition? Find(string id) => Blessings.Find(b => b.Id == id);

    /// The three offers on the start-of-run screen: distinct rows, uniform.
    ///
    /// The single place "which blessings may be offered" is answered, the same
    /// argument that keeps that question in CardPool and RelicPool rather than
    /// at each grant site - here mostly so a suite can drive the draw without
    /// standing up the screen.
    ///
    /// Uniform rather than tier-weighted, and there is no Rarity on the row.
    /// TierPool exists for pools large enough that a player will not see most
    /// of them; this one is small and every row is meant to be seen, so a tier
    /// would only make some of the authoring invisible.
    ///
    /// Unlike RelicPool there is no exhaustion story and no top-up: nothing is
    /// owned yet, so the pool is the whole pool on every draw. The only way to
    /// come back with fewer than `count` is to author fewer than `count`, which
    /// BlessingSmokeTest refuses rather than this silently papering over.
    public static IReadOnlyList<BlessingDefinition> Offer(int count, Random rng)
    {
        var pool = new List<BlessingDefinition>(Blessings);
        var picked = new List<BlessingDefinition>();
        while (picked.Count < count && pool.Count > 0)
        {
            int index = rng.Next(pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return picked;
    }
}
