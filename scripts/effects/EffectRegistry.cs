using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public static class EffectRegistry
{
    private static readonly Dictionary<string, IEffect> Effects = new()
    {
        ["deal_damage"] = new DealDamageEffect(),
        ["gain_block"] = new GainBlockEffect(),
        ["apply_status"] = new ApplyStatusEffect(),
        ["draw_cards"] = new DrawCardsEffect(),
        ["heal"] = new HealEffect(),
        ["gain_energy"] = new GainEnergyEffect(),
        ["lose_hp"] = new LoseHpEffect(),
    };

    public static void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (!Effects.TryGetValue(spec.Action, out var effect))
        {
            GD.PushError($"EffectRegistry: unknown action '{spec.Action}'");
            return;
        }

        effect.Execute(ctx, spec);
    }
}
