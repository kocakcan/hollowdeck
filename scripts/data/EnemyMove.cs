using System.Collections.Generic;

namespace Hollowdeck.Data;

// Debuff is the intent for a move that only worsens the player's position -
// no damage, no block. Without it such a move has to be authored as an Attack
// telegraphing 0, which is the one thing the intent system exists to prevent.
//
// Summon and Escape are the two moves that change the roster rather than a
// number on it. Both need a type of their own rather than borrowing an
// existing one: Phase4ContentSmokeTest resolves a Buff to a Self-scoped
// apply_status/heal and an Attack to a deal_damage, and neither move has
// either - so authored as a Buff a summon fails that sweep, correctly, because
// the label it produced would be a lie.
public enum IntentType { Attack, Defend, Buff, Debuff, Summon, Escape }

public class EnemyIntent
{
    public IntentType Type { get; set; }

    // The authored number the telegraph shows: damage per hit for Attack, the
    // status amount for Buff/Debuff. Everything *else* about the label - how
    // many hits, which status it is - is derived from the move's own Effects
    // by EnemyView, so only this one value is redundant with the effects, and
    // Phase4ContentSmokeTest asserts it matches across every enemy.
    public int DisplayAmount { get; set; }
}

public class EnemyMove
{
    public string MoveId { get; set; } = "";
    public EnemyIntent Intent { get; set; } = new();
    public List<EffectSpec> Effects { get; set; } = new();
    public int Weight { get; set; } = 1;
}
