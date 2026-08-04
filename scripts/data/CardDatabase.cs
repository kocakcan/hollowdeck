using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class CardDatabase
{
	private static readonly Dictionary<string, CardDefinition> ById = new();

	public static void LoadAll()
	{
		var defs = DataFile.LoadList<CardDefinition>("res://data/cards/cards.json");
		ById.Clear();
		foreach (var def in defs) ById[def.Id] = def;
	}

	public static CardDefinition Get(string id) => ById[id];

	public static IReadOnlyCollection<CardDefinition> All => ById.Values;
}
