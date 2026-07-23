using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

// Generates mechanically-accurate description text for a card or potion from
// its raw EffectSpec list, instead of relying on hand-authored prose that
// can silently drift out of sync with the numbers Strength/Weak/Vulnerable
// actually produce. Pass a live `source` (the player, mid-combat) to get
// Strength/Weak-adjusted numbers; pass null (e.g. Reward/Shop screens,
// outside combat) to show base numbers only.
public static class EffectDescriptionFormatter
{
    public static string Describe(List<EffectSpec> effects, Combatant? source = null)
    {
        var parts = new List<string>();
        foreach (var effect in effects)
        {
            var text = DescribeEffect(effect, source);
            if (text.Length > 0) parts.Add(text);
        }
        return string.Join(" ", parts);
    }

    private static string DescribeEffect(EffectSpec effect, Combatant? source)
    {
        switch (effect.Action)
        {
            case "deal_damage":
            {
                int amount = source is null ? effect.Amount : DamageMath.ComputeOutgoing(effect.Amount, source);
                int vsVulnerable = DamageMath.PreviewVsVulnerable(amount);
                return $"Deal {amount} damage. (~{vsVulnerable} vs Vulnerable)";
            }
            case "gain_block":
                return $"Gain {effect.Amount} Block.";
            case "apply_status":
                return effect.Scope == EffectScope.Self && effect.Status == "Strength"
                    ? $"Gain {effect.Amount} Strength."
                    : $"Apply {effect.Amount} {effect.Status}.";
            case "draw_cards":
                return $"Draw {effect.Amount} card{(effect.Amount == 1 ? "" : "s")}.";
            case "heal":
                return $"Heal {effect.Amount} HP.";
            case "gain_energy":
                return $"Gain {effect.Amount} Energy.";
            default:
                return "";
        }
    }
}
