using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

// What the caller knows about the situation a card/potion would be played
// in. Everything is optional: outside combat (Reward/Shop/Rest/Meta screens)
// all three are absent and the text falls back to base numbers, which is
// exactly what those screens want to show.
//
// TargetType is the *card's* declared target (CardDefinition.Target), needed
// because an AllEnemies card's effects are indistinguishable from a
// SingleEnemy card's by their EffectSpecs alone - both are
// EffectScope.Target - which is why Cleave used to read identically to
// Strike.
//
// Targets is who would actually be hit right now: the one enemy a card is
// being dragged onto, or every living enemy for an AllEnemies card. Supplying
// it turns the hypothetical "(~9 vs Vulnerable)" hint into the real number.
public readonly record struct DescribeContext(
    Combatant? Source = null,
    CardTargetType TargetType = CardTargetType.None,
    IReadOnlyList<Combatant>? Targets = null);

// The numbers a description ended up printing that differ from the authored
// base amount, so a presentation layer can call them out (CardView tints
// them) without re-deriving the Strength/Weak/Vulnerable math itself. Text is
// the single source of truth every screen renders; these are a decoration
// hint alongside it, never a substitute.
public readonly record struct DescribedEffects(string Text, List<int> Buffed, List<int> Weakened);

// Generates mechanically-accurate description text for a card or potion from
// its raw EffectSpec list, instead of relying on hand-authored prose that
// can silently drift out of sync with the numbers Strength/Weak/Vulnerable
// actually produce. Pass a live `source` (the player, mid-combat) to get
// Strength/Weak-adjusted numbers; pass nothing (outside combat) to show base
// numbers only.
public static class EffectDescriptionFormatter
{
    public static string Describe(List<EffectSpec> effects, Combatant? source = null) =>
        Describe(effects, new DescribeContext(source));

    public static string Describe(List<EffectSpec> effects, DescribeContext ctx) =>
        DescribeDetailed(effects, ctx).Text;

    public static DescribedEffects DescribeDetailed(List<EffectSpec> effects, DescribeContext ctx)
    {
        var parts = new List<string>();
        var buffed = new List<int>();
        var weakened = new List<int>();

        foreach (var effect in effects)
        {
            var text = DescribeEffect(effect, ctx, buffed, weakened);
            if (text.Length > 0) parts.Add(text);
        }

        // The vs-Vulnerable hint is appended once for the whole card rather
        // than once per deal_damage effect - Twin Strike (two 4-damage hits)
        // otherwise printed the parenthetical twice, doubling the longest
        // line in a description box that's only 200x160. Skipped entirely
        // when Targets is known, since then the printed numbers are already
        // the real post-Vulnerable ones and the hint would contradict them.
        int hint = VulnerablePreviewTotal(effects, ctx);
        if (hint > 0) parts.Add($"(~{hint} vs Vulnerable)");

        return new DescribedEffects(string.Join(" ", parts), buffed, weakened);
    }

    private static int VulnerablePreviewTotal(List<EffectSpec> effects, DescribeContext ctx)
    {
        if (ctx.Targets is { Count: > 0 }) return 0;
        return effects
            .Where(e => e.Action == "deal_damage" && e.Scope == EffectScope.Target)
            .Sum(e => DamageMath.PreviewVsVulnerable(Outgoing(e.Amount, ctx.Source)));
    }

    private static int Outgoing(int baseAmount, Combatant? source) =>
        source is null ? baseAmount : DamageMath.ComputeOutgoing(baseAmount, source);

    private static string DescribeEffect(EffectSpec effect, DescribeContext ctx, List<int> buffed, List<int> weakened)
    {
        switch (effect.Action)
        {
            case "deal_damage":
            {
                int outgoing = Outgoing(effect.Amount, ctx.Source);
                Record(effect.Amount, outgoing, buffed, weakened);
                return $"Deal {DamageAmount(outgoing, effect, ctx, buffed)} damage{Suffix(effect, ctx)}.";
            }
            case "gain_block":
                return $"Gain {effect.Amount} Block.";
            case "apply_status":
                return effect.Scope == EffectScope.Self && effect.Status == "Strength"
                    ? $"Gain {effect.Amount} Strength."
                    : $"Apply {effect.Amount} {effect.Status}{Suffix(effect, ctx)}.";
            case "draw_cards":
                return $"Draw {effect.Amount} card{(effect.Amount == 1 ? "" : "s")}.";
            case "heal":
                return $"Heal {effect.Amount} HP.";
            case "gain_energy":
                return $"Gain {effect.Amount} Energy.";
            case "lose_hp":
                return $"Lose {effect.Amount} HP.";
            default:
                return "";
        }
    }

    // With no known targets this is just the outgoing amount. With targets,
    // each one's Vulnerable is resolved through the same DamageMath the real
    // resolution uses; a spread (some Vulnerable, some not, on an AllEnemies
    // card) prints as a range rather than picking one enemy's number and
    // being wrong about the rest.
    private static string DamageAmount(int outgoing, EffectSpec effect, DescribeContext ctx, List<int> buffed)
    {
        if (effect.Scope != EffectScope.Target || ctx.Targets is not { Count: > 0 } targets)
        {
            return outgoing.ToString();
        }

        var landed = targets.Select(t => DamageMath.ApplyVulnerable(outgoing, t)).ToList();
        int low = landed.Min();
        int high = landed.Max();

        // Vulnerable only ever raises damage, so anything above the authored
        // base is worth calling out as buffed even when Strength is 0.
        foreach (int amount in landed.Where(a => a > effect.Amount).Distinct())
        {
            if (!buffed.Contains(amount)) buffed.Add(amount);
        }

        return low == high ? low.ToString() : $"{low}-{high}";
    }

    // "to ALL enemies" is the whole point of this suffix: an AllEnemies card
    // has no other tell in its description. SingleEnemy stays bare (dragging
    // the card onto one enemy already says who it hits) and Self effects are
    // already phrased as "Gain"/"Heal"/"Lose".
    private static string Suffix(EffectSpec effect, DescribeContext ctx) =>
        effect.Scope == EffectScope.Target && ctx.TargetType == CardTargetType.AllEnemies
            ? " to ALL enemies"
            : "";

    private static void Record(int baseAmount, int shown, List<int> buffed, List<int> weakened)
    {
        if (shown > baseAmount && !buffed.Contains(shown)) buffed.Add(shown);
        else if (shown < baseAmount && !weakened.Contains(shown)) weakened.Add(shown);
    }
}
