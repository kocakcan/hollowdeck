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
}
