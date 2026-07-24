using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hollowdeck.Data;

// Generic, formula-driven upgrade rather than hand-authored "+" data rows
// for every one of the ~30 cards - scales each effect that's good for the
// player (damage, block, enemy debuffs, self-buffs, draw/energy) by the same
// modest multiplier, and leaves anything that's bad for the player
// (lose_hp) untouched, so upgrading a card can never make it worse. The
// upgraded CardDefinition only ever lives in RunState.Deck / a save file
// (RunSaveManager.cs reconstructs it via Apply() from the "<id>+" it wrote
// out) - it's never added to CardDatabase, so reward/shop pools can't roll
// it as a "new" card.
public static class CardUpgrade
{
    private const float ScaleFactor = 1.4f;

    private static readonly HashSet<string> AlwaysScaledActions = new()
    {
        "deal_damage", "gain_block", "gain_energy", "heal", "draw_cards",
    };

    public static bool IsUpgraded(CardDefinition card) => card.Id.EndsWith("+");

    public static CardDefinition Apply(CardDefinition original)
    {
        if (IsUpgraded(original)) return original;

        return new CardDefinition
        {
            Id = original.Id + "+",
            Name = original.Name + "+",
            Cost = original.Cost,
            Type = original.Type,
            Target = original.Target,
            Exhaust = original.Exhaust,
            Rarity = original.Rarity,
            Effects = original.Effects.Select(ScaleEffect).ToList(),
        };
    }

    private static EffectSpec ScaleEffect(EffectSpec effect)
    {
        if (!ShouldScale(effect)) return effect;
        return new EffectSpec
        {
            Action = effect.Action,
            Status = effect.Status,
            Scope = effect.Scope,
            Amount = Mathf.Max(effect.Amount + 1, Mathf.RoundToInt(effect.Amount * ScaleFactor)),
        };
    }

    private static bool ShouldScale(EffectSpec effect)
    {
        if (AlwaysScaledActions.Contains(effect.Action)) return true;
        if (effect.Action != "apply_status") return false;

        // Debuffing the enemy harder, or buffing yourself harder, are both
        // upgrades; a self-targeted status here would be a self-debuff
        // (none exist in the current data, but this stays correct if one
        // ever gets added) and must never be scaled up.
        return effect.Scope switch
        {
            EffectScope.Target => effect.Status is "Vulnerable" or "Weak" or "Poison",
            EffectScope.Self => effect.Status == "Strength",
            _ => false,
        };
    }
}
