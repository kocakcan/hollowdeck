using System;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class HealEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        foreach (var target in ctx.Targets)
        {
            target.CurrentHp = Math.Min(target.MaxHp, target.CurrentHp + spec.Amount);
        }
    }
}
