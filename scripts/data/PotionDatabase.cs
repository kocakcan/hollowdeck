using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class PotionDatabase
{
    private static readonly Dictionary<string, PotionDefinition> ById = new();

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<PotionDefinition>("res://data/potions/potions.json");
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    public static PotionDefinition Get(string id) => ById[id];

    public static IReadOnlyCollection<PotionDefinition> All => ById.Values;
}
