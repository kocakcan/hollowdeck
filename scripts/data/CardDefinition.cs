using System.Collections.Generic;

namespace Hollowdeck.Data;

public enum CardType { Attack, Skill }
public enum CardTargetType { SingleEnemy, AllEnemies, Self, None }

// Common is the zero value so cards.json entries that omit "rarity" (all 30
// do, as of this field's introduction) deserialize to Common for free via
// the same tolerant-deserialization pattern already used for save data -
// rarity assignment across the existing card pool is a content/balance
// decision for later, not a default this field should invent.
public enum Rarity { Common, Uncommon, Rare }

// No Description field - display text is generated from Effects by
// EffectDescriptionFormatter so it can never drift from what the card
// actually does (e.g. Strength/Vulnerable-adjusted damage numbers).
public class CardDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Cost { get; set; }
    public CardType Type { get; set; }
    public CardTargetType Target { get; set; }
    public bool Exhaust { get; set; }
    public Rarity Rarity { get; set; } = Rarity.Common;
    public List<EffectSpec> Effects { get; set; } = new();
}
