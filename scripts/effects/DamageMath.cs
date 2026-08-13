using Hollowdeck.Combat;
using Hollowdeck.Run;

namespace Hollowdeck.Effects;

// Single source of truth for Strength/Weak/Vulnerable/Intangible damage math,
// shared by DealDamageEffect (actual resolution) and EffectDescriptionFormatter
// (live preview text on cards/potions) so the number shown to the player can
// never drift from the number that actually lands.
//
// Two methods, split by *whose* statuses they read: ComputeOutgoing takes the
// source, ApplyIncoming takes the target. Every target-side modifier belongs in
// the second one rather than in a third method, which is what makes a new one
// show up in the live damage preview and on the enemy telegraph for free -
// EffectDescriptionFormatter and EnemyView.LiveAttackAmount both call it.
public static class DamageMath
{
    public const float WeakMultiplier = 0.75f;
    public const float VulnerableMultiplier = 1.5f;

    // What an attack is floored to against an Intangible target. A constant
    // rather than a literal 1 so Keywords.Blurb can interpolate it instead of
    // restating it in prose - the same no-drift rule the two multipliers above
    // are read under.
    public const int IntangibleDamage = 1;

    // The ascension ladder's enemy-damage rung is applied here, and the place
    // is the whole of why it is honest. This method is the one thing both
    // DealDamageEffect (what actually lands) and EnemyView.LiveAttackAmount
    // (what the telegraph advertises) call, so scaling it moves both together
    // and they cannot disagree. Applying the rung at resolution alone would
    // make every enemy in the game telegraph a lie, which is this genre's
    // canonical bad bug - the player has already committed a turn against it.
    //
    // Two details are deliberate. It scales the *base* amount, before Strength,
    // so the rung raises what the move is authored at rather than compounding
    // with the fight's accumulated buffs - Strength is worth the same to an
    // enemy at every rung. And it is gated on the source being an enemy: the
    // player's cards go through this method too, and a ladder that scaled the
    // player's damage would hand back what every other knob is taking.
    //
    // Thorns is not scaled and does not come through here - DealDamageEffect
    // subtracts it directly, deliberately, so it cannot re-enter the effect
    // system. A flat retaliation is what that status is.
    public static int ComputeOutgoing(int baseAmount, Combatant source)
    {
        if (source is EnemyCombatant) baseAmount = RunState.Ascension.EnemyDamage(baseAmount);

        int amount = baseAmount + source.GetStatus(StatusType.Strength);
        if (source.GetStatus(StatusType.Weak) > 0)
        {
            amount = (int)(amount * WeakMultiplier);
        }
        return amount;
    }

    // Vulnerable first, Intangible last, and the order is the rule rather than
    // an accident: flooring before amplifying would let Vulnerable multiply the
    // floor back up to 1.5x, so an Intangible target would take more from a
    // Vulnerable-stacking deck than from a plain one. Applying the floor last
    // means Intangible wins outright, which is what the status is for.
    public static int ApplyIncoming(int amount, Combatant target)
    {
        if (target.GetStatus(StatusType.Vulnerable) > 0)
        {
            amount = (int)(amount * VulnerableMultiplier);
        }

        if (target.GetStatus(StatusType.Intangible) > 0 && amount > IntangibleDamage)
        {
            amount = IntangibleDamage;
        }

        return amount;
    }
}
