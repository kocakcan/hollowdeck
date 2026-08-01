using Hollowdeck.Combat;

namespace Hollowdeck.Effects;

// Single source of truth for Dexterity/Frail Block math, shared by
// GainBlockEffect (actual resolution) and EffectDescriptionFormatter (the
// number printed on the card) - the same split, for the same reason, as
// DamageMath: the Block a card promises and the Block it grants must never
// drift.
//
// Deliberately a separate file rather than two more methods on DamageMath.
// The two are read by different callers and the pairs don't mix - nothing
// applies Strength to Block or Frail to damage - so keeping them apart is
// what stops a future edit from reaching for the wrong multiplier.
public static class BlockMath
{
    // Frail mirrors Weak's 25% cut, and intentionally uses the same
    // proportion: two debuffs that read as "your numbers get smaller" should
    // shrink them by the same amount, or the player has two figures to learn
    // instead of one.
    public const float FrailMultiplier = 0.75f;

    // `source` is whoever *gains* the Block, not whoever played the card -
    // for a self-scoped gain_block those are the same combatant, but the
    // caller resolves that, not this.
    public static int ComputeOutgoing(int baseAmount, Combatant source)
    {
        int amount = baseAmount + source.GetStatus(StatusType.Dexterity);
        if (source.GetStatus(StatusType.Frail) > 0)
        {
            amount = (int)(amount * FrailMultiplier);
        }
        // Dexterity can't push Block negative, but Frail on a 1-Block card
        // floors to 0 rather than going negative if the numbers ever change.
        return amount < 0 ? 0 : amount;
    }
}
