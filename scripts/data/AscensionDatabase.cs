using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hollowdeck.Data;

// The ascension ladder, loaded from data/ascension/ascension.json. Same shape
// as ActDatabase - authored order is play order, and the index into it is the
// rung - with one addition: Effective(level) hands back the *cumulative*
// modifiers for that rung rather than the row.
//
// The fold is done once in LoadAll and cached into an array indexed by level,
// because Effective is read from the combat loop (every enemy's HP and every
// point of enemy damage) and from BalanceModel's inner walk. Summing twenty
// rows per damage calculation would be the kind of cost nobody notices until a
// fight with four enemies stutters.
public static class AscensionDatabase
{
    private static readonly List<AscensionDefinition> Rungs = new();

    // Index i holds the modifiers for rung i; index 0 is AscensionModifiers.None.
    private static AscensionModifiers[] _resolved = { AscensionModifiers.None };

    public static IReadOnlyList<AscensionDefinition> All => Rungs;

    // The highest rung that exists. Not a constant: the ladder's length is a
    // property of the content file, and MetaProgressionManager clamps the
    // player's unlocked limit against this so a save from a build with a longer
    // ladder degrades to the top of this one rather than indexing off the end.
    public static int MaxLevel => Rungs.Count;

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<AscensionDefinition>("res://data/ascension/ascension.json");
        Rungs.Clear();
        Rungs.AddRange(defs.OrderBy(r => r.Level));
        Resolve();
    }

    // The row a rung adds, for the screens that name what a level did. Clamped
    // like ActDatabase.At, and for the same reason.
    public static AscensionDefinition At(int level) =>
        Rungs[Mathf.Clamp(level - 1, 0, Rungs.Count - 1)];

    // Every rung from 1 to level, folded. Out-of-range clamps rather than
    // throwing: this number reaches here from a save file and from a meta save
    // written by a different build, which is the same argument
    // CardPool.WeightOf's clamp makes about the skip streak.
    public static AscensionModifiers Effective(int level) =>
        _resolved[Mathf.Clamp(level, 0, _resolved.Length - 1)];

    private static void Resolve()
    {
        _resolved = new AscensionModifiers[Rungs.Count + 1];
        _resolved[0] = AscensionModifiers.None;

        for (int level = 1; level <= Rungs.Count; level++)
        {
            var below = _resolved[level - 1];
            var rung = Rungs[level - 1];

            var curses = new List<string>(below.StartingCurseIds);
            curses.AddRange(rung.StartingCurseIds);

            _resolved[level] = new AscensionModifiers(
                Level: level,
                EnemyHpPercent: below.EnemyHpPercent + rung.EnemyHpPercent,
                EnemyDamagePercent: below.EnemyDamagePercent + rung.EnemyDamagePercent,
                BossHpBonusPercent: below.BossHpBonusPercent + rung.BossHpPercent,
                StartingMaxHpDelta: below.StartingMaxHpDelta + rung.StartingMaxHpDelta,
                ClearHealPercentDelta: below.ClearHealPercentDelta + rung.ClearHealPercentDelta,
                ShopPricePercent: below.ShopPricePercent + rung.ShopPricePercent,
                EliteWeightDelta: below.EliteWeightDelta + rung.EliteWeightDelta,
                PotionDropPercentDelta: below.PotionDropPercentDelta + rung.PotionDropPercentDelta,
                StartingCurseIds: curses);
        }
    }
}
