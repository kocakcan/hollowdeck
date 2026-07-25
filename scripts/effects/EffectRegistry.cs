using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

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

    // Single dispatch point for gameplay SFX - every card/potion/enemy-move
    // effect routes through Execute, so this one map covers all of them by
    // action category rather than hooking each IEffect implementation.
    private static readonly Dictionary<string, string> SfxCues = new()
    {
        ["deal_damage"] = "damage_hit",
        ["lose_hp"] = "damage_hit",
        ["gain_block"] = "block_gain",
        ["apply_status"] = "status_apply",
        ["draw_cards"] = "card_draw",
        ["heal"] = "heal",
        ["gain_energy"] = "energy_gain",
    };

    public static void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (!Effects.TryGetValue(spec.Action, out var effect))
        {
            GD.PushError($"EffectRegistry: unknown action '{spec.Action}'");
            return;
        }

        effect.Execute(ctx, spec);
        if (SfxCues.TryGetValue(spec.Action, out var cue)) AudioManager.Instance?.PlaySfx(cue);
    }
}
