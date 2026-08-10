namespace Hollowdeck.Data;

// Which pool a relic is drawn from, and - for the first three - how often.
//
// Deliberately NOT CardDefinition's Rarity, which PotionDefinition does reuse.
// A potion tier and a card tier mean the same thing to a player, so sharing the
// enum there is honest. Here only the first three members are a power ladder;
// Boss, Shop and Event name a *source*, and the whole point of the feature is
// that a boss relic is one a shop can never stock. Folding six members into
// Rarity would also silently under-cover the two hardcoded three-element Rarity
// sweeps in EffectSmokeTest, which would keep passing while ignoring half the
// enum.
//
// Common is the zero value for the same reason it is on Rarity - an omitted key
// still deserializes. Nothing relies on that: EffectSmokeTest reads relics.json
// as *text* and counts "tier" keys, because a forgotten key and an authored
// Common are indistinguishable once DataFile has run.
public enum RelicTier { Common, Uncommon, Rare, Boss, Shop, Event }

public class RelicDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BehaviorId { get; set; } = "";
    public RelicTier Tier { get; set; } = RelicTier.Common;

    // Which RelicBehavior hook fires the effect below. See RelicTrigger.cs
    // for the target/condition/limit vocabulary that turns "fires an effect
    // on a hook" into something every relic in the game can be authored as.
    public string? Hook { get; set; }
    public EffectSpec? Effect { get; set; }
    public RelicTarget Target { get; set; } = RelicTarget.Self;
    public RelicCondition? Condition { get; set; }
    public RelicLimit? Limit { get; set; }
}
