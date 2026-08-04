using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class EventDatabase
{
    private static readonly Dictionary<string, EventDefinition> ById = new();

    public static void LoadAll()
    {
        var defs = DataFile.LoadList<EventDefinition>("res://data/events/events.json");
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    public static EventDefinition Get(string id) => ById[id];

    public static IReadOnlyCollection<EventDefinition> All => ById.Values;
}
