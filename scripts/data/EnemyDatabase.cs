using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class EnemyDatabase
{
    private static readonly Dictionary<string, EnemyDefinition> ById = new();

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<EnemyDefinition>("res://data/enemies/enemies.json");
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    // Card/Relic/PotionDatabase have always exposed All; EnemyDatabase was the
    // odd one out because nothing enumerated enemies until acts needed their
    // encounter pools validated (MapSmokeTest checks every id an act references
    // actually resolves).
    public static IReadOnlyCollection<EnemyDefinition> All => ById.Values;

    public static EnemyDefinition Get(string id) => ById[id];

    // The tolerant half, for the one caller that reads an id out of authored
    // content at *runtime* rather than at load: a typo in a summon_enemy spec
    // must name itself in the log rather than throw a KeyNotFoundException out
    // of the middle of an enemy turn. Same split CardDatabase.Find exists for.
    public static EnemyDefinition? Find(string id) => ById.GetValueOrDefault(id);
}
