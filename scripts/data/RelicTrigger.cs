namespace Hollowdeck.Data;

// The vocabulary that lets a relic be a data row instead of a C# class:
// which combatants its effect lands on, when it is allowed to fire, and how
// often. Every member below is used by a relic in data/relics/relics.json -
// nothing here is speculative, same rule the statuses and effect actions
// follow.

// Who the relic's effect applies to. Note this is NOT EffectSpec.Scope: that
// says "the card's targets" vs "the player", and a relic has no card targets
// to inherit - it picks its own.
public enum RelicTarget
{
    Self,        // the player - the default, and what most relics want
    Attacker,    // OnDamageTaken only: whoever just hit the player
    FirstEnemy,  // first alive enemy in row order
    RandomEnemy, // drawn from RngStreams.Combat, never a fresh Random (risk 2)
    AllEnemies,  // every alive enemy
}

// Gates whether the hook fires at all. Each field is checked only when set,
// and only makes sense on some hooks - an outcome filter on OnTurnStart is
// authoring nonsense, not a runtime error.
public class RelicCondition
{
    // OnCardPlayed: only fire for this card type.
    public CardType? CardType { get; set; }

    // OnCombatEnd: "Win" or "Lose". A string rather than the CombatOutcome
    // enum because that enum lives in Hollowdeck.Combat and this namespace
    // does not reference it - the same reason RelicDefinition.Hook is a
    // string.
    public string? Outcome { get; set; }

    // Player must have at least this much unspent Energy.
    public int? MinEnergy { get; set; }

    // Player's current HP must be strictly above this percentage of max.
    public int? MinHpPercent { get; set; }

    // OnDamageDealt: only fire when the hit killed its target.
    public bool TargetKilled { get; set; }
}

// Caps how often a relic pays out. All three counters reset in OnTurnStart,
// which fires only on the PLAYER's turn (CombatManager.BeginPlayerTurn) - so
// damage taken during the enemy turn shares a bucket with the player turn
// that follows it. That is deliberate and is what the bespoke Bulwark Charm
// and Momentum Token classes did before they became data.
public class RelicLimit
{
    public bool OncePerTurn { get; set; }
    public bool OncePerCombat { get; set; }

    // Fire on every Nth firing within a turn (3 = the 3rd, 6th, ...).
    // 0 means no interval.
    public int EveryNth { get; set; }
}
