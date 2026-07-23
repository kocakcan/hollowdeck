using Hollowdeck.Data;

namespace Hollowdeck.Effects;

// Direct HP loss, bypassing Block/Vulnerable entirely - for high-risk/
// high-reward cards that cost HP instead of Energy (e.g. "pay 3 HP, draw 2
// cards"). Distinct from deal_damage, which is Block/Vulnerable-aware.
public class LoseHpEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        foreach (var target in ctx.Targets)
        {
            target.CurrentHp -= System.Math.Min(target.CurrentHp, spec.Amount);
        }
    }
}
