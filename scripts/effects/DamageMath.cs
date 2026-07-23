using Hollowdeck.Combat;

namespace Hollowdeck.Effects;

// Single source of truth for Strength/Weak/Vulnerable damage math, shared by
// DealDamageEffect (actual resolution) and EffectDescriptionFormatter (live
// preview text on cards/potions) so the number shown to the player can never
// drift from the number that actually lands.
public static class DamageMath
{
    public const float WeakMultiplier = 0.75f;
    public const float VulnerableMultiplier = 1.5f;

    public static int ComputeOutgoing(int baseAmount, Combatant source)
    {
        int amount = baseAmount + source.GetStatus(StatusType.Strength);
        if (source.GetStatus(StatusType.Weak) > 0)
        {
            amount = (int)(amount * WeakMultiplier);
        }
        return amount;
    }

    public static int ApplyVulnerable(int amount, Combatant target)
    {
        return target.GetStatus(StatusType.Vulnerable) > 0 ? (int)(amount * VulnerableMultiplier) : amount;
    }

    public static int PreviewVsVulnerable(int amount) => (int)(amount * VulnerableMultiplier);
}
