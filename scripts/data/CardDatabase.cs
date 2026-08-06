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

	// The non-throwing form, for the two callers that read an id out of
	// content data rather than out of code: AddCardEffect and the add_card
	// event outcome. A typo in cards.json must name itself in the log, not
	// throw a KeyNotFoundException out of the middle of combat resolution.
	public static CardDefinition? Find(string id) =>
		ById.TryGetValue(id, out var def) ? def : null;

	public static IReadOnlyCollection<CardDefinition> All => ById.Values;
}
