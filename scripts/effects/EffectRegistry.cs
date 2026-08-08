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
        ["discard_cards"] = new DiscardCardsEffect(),
        ["exhaust_hand"] = new ExhaustHandEffect(),
        ["gain_gold"] = new GainGoldEffect(),
        ["add_card"] = new AddCardEffect(),
        // The two that change who is in the fight rather than a number on
        // someone in it. Enemy-move-only in practice, and EffectSmokeTest
        // refuses a card, potion or relic authoring either.
        ["summon_enemy"] = new SummonEnemyEffect(),
        ["escape"] = new EscapeEffect(),
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
        // Both move cards between piles, which is what card_draw already
        // sounds like - a bespoke cue per pile transition would be three
        // near-identical shuffles.
        ["discard_cards"] = "card_draw",
        ["exhaust_hand"] = "card_draw",
        // Same argument: add_card moves a card into a pile, which is what
        // card_draw already sounds like.
        ["add_card"] = "card_draw",
        // summon_enemy and escape are deliberately absent. AudioCues has ten
        // cues, one per category, and none of them reads as a creature
        // arriving or fleeing - borrowing an unrelated one would land as a bug
        // rather than as atmosphere. Same call GainGoldEffect already makes.
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
