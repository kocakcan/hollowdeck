using System.Collections.Generic;

namespace Hollowdeck.Data;

// Power is the genre's third card class: played once, then it leaves the fight
// entirely rather than cycling back through the discard pile. See
// PileManager.Powers for where it goes and why that is a pile of its own
// rather than Exhaust.
public enum CardType { Attack, Skill, Power }
public enum CardTargetType { SingleEnemy, AllEnemies, Self, None }

// Common is the zero value, so a cards.json entry that omits "rarity" still
// deserializes - the same tolerant-deserialization pattern used for save data.
// Every card in the pool now declares one explicitly, and
// EffectSmokeTest asserts the distribution stays sane, so the default is a
// safety net rather than something content relies on.
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
