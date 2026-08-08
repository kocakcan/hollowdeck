namespace Hollowdeck.Data;

// Target/Self were the whole vocabulary for six phases, and AoE existed only
// one level up at CardTargetType - so "deal 6 to your target, Weak to
// everything" was unauthorable and a random multi-hit was impossible.
//
// AllEnemies and RandomEnemy are resolved relative to the *source*, not to the
// player: see CombatManager.Opposition. An enemy authoring either would be
// coherent but is refused by Phase4ContentSmokeTest, because EnemyView derives
// a telegraph from these specs and a scope it doesn't account for is a route to
// a telegraph that lies.
public enum EffectScope { Target, Self, AllEnemies, RandomEnemy }

// Where add_card puts what it makes. Named CardPile rather than Pile so it can
// never be misread as one of PileManager's fields.
//
// Exhaust is deliberately absent: a card added straight to the exhaust pile
// does nothing observable, so offering it as a destination is offering a
// silent no-op.
public enum CardPile { Hand, Draw, Discard }

public class EffectSpec
{
    public string Action { get; set; } = "";
    public int Amount { get; set; }
    public string? Status { get; set; }
    public EffectScope Scope { get; set; } = EffectScope.Target;

    // add_card only. Amount is the number of copies.
    public string? CardId { get; set; }
    public CardPile Pile { get; set; } = CardPile.Discard;

    // summon_enemy only. Amount is the number of copies - the same reading
    // add_card gives it, deliberately, so "how many of these does this make"
    // is one question with one answer across both primitives.
    public string? EnemyId { get; set; }

    // X-cost opt-in. Amount is then read as "per point of X" - so a card that
    // deals X damage is authored amount 1, and one that deals 3 per X is
    // authored amount 3. Per-spec rather than a blanket override on the
    // context, because a mixed card ("Deal X damage. Gain 3 Block.") must be
    // able to scale one and not the other.
    public bool PerX { get; set; }
}
