using System.Collections.Generic;

namespace Hollowdeck.Data;

public enum CardType { Attack, Skill }
public enum CardTargetType { SingleEnemy, AllEnemies, Self, None }

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
    public List<EffectSpec> Effects { get; set; } = new();
}
