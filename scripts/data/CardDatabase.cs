using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Hollowdeck.Data;

public static class CardDatabase
{
    private static readonly Dictionary<string, CardDefinition> ById = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void LoadAll()
    {
        using var file = FileAccess.Open("res://data/cards/cards.json", FileAccess.ModeFlags.Read);
        var json = file.GetAsText();
        var defs = JsonSerializer.Deserialize<List<CardDefinition>>(json, Options)!;
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    public static CardDefinition Get(string id) => ById[id];

    public static IReadOnlyCollection<CardDefinition> All => ById.Values;
}
