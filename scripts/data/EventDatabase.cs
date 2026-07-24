using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Hollowdeck.Data;

public static class EventDatabase
{
    private static readonly Dictionary<string, EventDefinition> ById = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void LoadAll()
    {
        using var file = FileAccess.Open("res://data/events/events.json", FileAccess.ModeFlags.Read);
        var json = file.GetAsText();
        var defs = JsonSerializer.Deserialize<List<EventDefinition>>(json, Options)!;
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    public static EventDefinition Get(string id) => ById[id];

    public static IReadOnlyCollection<EventDefinition> All => ById.Values;
}
