using System.Collections.Generic;

namespace Hollowdeck.Data;

// No Description field - same reasoning as CardDefinition: generated from
// Effects by EffectDescriptionFormatter, never hand-authored/stale.
public class PotionDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public CardTargetType Target { get; set; }
    public List<EffectSpec> Effects { get; set; } = new();

    // Shares CardDefinition's Rarity enum rather than declaring a parallel one:
    // a potion tier and a card tier mean the same thing to a player and are
    // rendered in the same three colours (ChromeStyles.RarityColor). What is
    // *not* shared is the weight table - PotionPool is 65/25/10 against
    // CardPool's 60/37/3, because a consumable should not be as hard to see as
    // a deck slot.
    //
    // The Common default is a deserialization fallback, not a licence to omit
    // the key: EffectSmokeTest reads potions.json as text and counts "rarity"
    // keys, because the enum has no null and a forgotten tier is otherwise
    // indistinguishable from an authored Common.
    public Rarity Rarity { get; set; } = Rarity.Common;
}
