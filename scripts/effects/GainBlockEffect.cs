using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class GainBlockEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        foreach (var target in ctx.Targets)
        {
            target.Block += spec.Amount;
        }
    }
}
