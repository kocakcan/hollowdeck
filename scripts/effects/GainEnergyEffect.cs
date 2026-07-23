using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class GainEnergyEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (ctx.Source is not PlayerCombatant player)
        {
            GD.PushWarning("GainEnergyEffect: source is not a PlayerCombatant, ignoring.");
            return;
        }

        player.CurrentEnergy += spec.Amount;
    }
}
