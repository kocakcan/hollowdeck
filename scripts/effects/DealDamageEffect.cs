using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class DealDamageEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        int amount = spec.Amount + ctx.Source.GetStatus(StatusType.Strength);
        if (ctx.Source.GetStatus(StatusType.Weak) > 0)
        {
            amount = (int)(amount * 0.75f);
        }

        foreach (var target in ctx.Targets)
        {
            int targetAmount = amount;
            if (target.GetStatus(StatusType.Vulnerable) > 0)
            {
                targetAmount = (int)(targetAmount * 1.5f);
            }

            int absorbedByBlock = System.Math.Min(target.Block, targetAmount);
            target.Block -= absorbedByBlock;
            target.CurrentHp -= targetAmount - absorbedByBlock;
        }
    }
}
