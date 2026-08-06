using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class GainBlockEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        foreach (var target in ctx.Targets)
        {
            // Dexterity/Frail are read off the combatant *receiving* the
            // Block, not ctx.Source. For every card in the data those are the
            // same combatant (gain_block is always self-scoped), but an enemy
            // move that shields a different enemy would otherwise apply the
            // caster's Dexterity to someone else's Block.
            target.Block += BlockMath.ComputeOutgoing(ctx.AmountFor(spec), target);
        }
    }
}
