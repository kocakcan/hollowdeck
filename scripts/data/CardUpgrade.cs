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

    // Everything here is a benefit, so scaling it can only improve the card -
    // which is the rule that keeps lose_hp, discard_cards and exhaust_hand out
    // (they are costs, and a bigger cost is a worse upgrade).
    //
    // gain_gold sat outside this list while it was relic-only, and relics do
    // not upgrade, so nothing noticed. The moment a *card* used it, Tithe+
    // became a card that reads and plays exactly like Tithe - the silent
    // failure ShouldScale's comment describes, arriving through the action
    // list instead of the status list.
    private static readonly HashSet<string> AlwaysScaledActions = new()
    {
        "deal_damage", "gain_block", "gain_energy", "heal", "draw_cards", "gain_gold",
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
        //
        // Every status has to be named here explicitly, and the failure mode
        // of forgetting one is silent: the card upgrades to a "+" that reads
        // identically and plays identically. Metallicize/Ritual/Regen are the
        // per-turn grants, so scaling them is the strongest upgrade in the
        // game - which is correct, they are all Powers.
        return effect.Scope switch
        {
            EffectScope.Target => effect.Status is "Vulnerable" or "Weak" or "Poison" or "Frail",
            // Fervor and Foresight scale for the same reason and are the far
            // end of it: +1 becomes +2 Energy or +2 cards every turn, which is
            // the single largest upgrade delta in the pool. That is deliberate
            // - both sit on cost-3 Rares that spend a whole turn to land - but
            // it is the first thing the balance pass should look at.
            EffectScope.Self => effect.Status is "Strength" or "Dexterity"
                or "Metallicize" or "Ritual" or "Regen" or "Fervor" or "Foresight",
            _ => false,
        };
    }
}
