using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Hollowdeck.Data;

public static class EnemyDatabase
{
    private static readonly Dictionary<string, EnemyDefinition> ById = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void LoadAll()
    {
        using var file = FileAccess.Open("res://data/enemies/enemies.json", FileAccess.ModeFlags.Read);
        var json = file.GetAsText();
        var defs = JsonSerializer.Deserialize<List<EnemyDefinition>>(json, Options)!;
        ById.Clear();
        foreach (var def in defs) ById[def.Id] = def;
    }

    public static EnemyDefinition Get(string id) => ById[id];
}
