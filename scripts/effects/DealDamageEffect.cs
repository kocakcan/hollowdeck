using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class DealDamageEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        int amount = DamageMath.ComputeOutgoing(spec.Amount, ctx.Source);

        foreach (var target in ctx.Targets)
        {
            int targetAmount = DamageMath.ApplyVulnerable(amount, target);

            int absorbedByBlock = System.Math.Min(target.Block, targetAmount);
            target.Block -= absorbedByBlock;
            target.CurrentHp -= targetAmount - absorbedByBlock;
        }
    }
}
