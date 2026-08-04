using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class RelicDatabase
{
    private static readonly Dictionary<string, RelicDefinition> ById = new();

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<RelicDefinition>("res://data/relics/relics.json");
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    public static RelicDefinition Get(string id) => ById[id];

    public static IReadOnlyCollection<RelicDefinition> All => ById.Values;
}
