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

        // Thunderclap read "Deal 4 damage to ALL enemies. Apply 1 Vulnerable
        // to ALL enemies." - the same four words twice, in a 152x88 box that
        // then had nowhere to put them. When more than one effect would carry
        // the suffix, hoist it into a single prefix. A card with only one
        // keeps the inline phrasing, which reads better as a sentence ("Deal
        // 8 damage to ALL enemies." beats "ALL enemies: Deal 8 damage.") -
        // Cleave, Whirlwind and Toxic Cloud are all in that group.
        bool hoisted = effects.Count(e => TargetsAllEnemies(e, ctx)) > 1;
        if (hoisted) parts.Add("ALL enemies:");

        for (int i = 0; i < effects.Count;)
        {
            // A run of identical effects is how the data expresses a multi-hit
            // (Twin Strike is authored as two separate 4-damage specs), but it
            // is not how a card should read - that rendered as "Deal 4 damage.
            // Deal 4 damage." Collapse the run into one sentence instead.
            //
            // Consecutive and byte-identical only: Crippling Blow repeats the
            // apply_status *action* with different arguments, and must keep
            // its two distinct sentences.
            int repeats = 1;
            while (i + repeats < effects.Count && SameEffect(effects[i], effects[i + repeats])) repeats++;

            var text = DescribeEffect(effects[i], ctx, buffed, weakened, hoisted);
            if (text.Length > 0) parts.Add(Repeated(text, repeats));
            i += repeats;
        }

        // The vs-Vulnerable hint is appended once for the whole card rather
        // than once per deal_damage effect - Twin Strike (two 4-damage hits)
        // otherwise printed the parenthetical twice, doubling the longest
        // line in a description box that's only 152x88. Skipped entirely
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

    // EffectSpec is a plain serialization class, so it has reference equality
    // and cannot be compared directly. Deliberately not converted to a record:
    // it is the shape the content JSON deserializes into, and value semantics
    // are not what the rest of the effect pipeline wants from it.
    // Public because EnemyView counts a move's hits with the same rule: a
    // multi-hit is a run of identical specs on both sides of the game, and
    // "Deal 4 damage twice" and a "4 x2" telegraph must never disagree about
    // what counts as one.
    public static bool SameEffect(EffectSpec a, EffectSpec b) =>
        a.Action == b.Action && a.Amount == b.Amount && a.Status == b.Status && a.Scope == b.Scope;

    // DescribeEffect hands back a finished sentence, so the repetition has to
    // go inside it, before the full stop.
    private static string Repeated(string sentence, int times) =>
        times switch
        {
            1 => sentence,
            2 => $"{sentence.TrimEnd('.')} twice.",
            _ => $"{sentence.TrimEnd('.')} {times} times.",
        };

    private static bool TargetsAllEnemies(EffectSpec effect, DescribeContext ctx) =>
        effect.Scope == EffectScope.Target && ctx.TargetType == CardTargetType.AllEnemies;

    private static string DescribeEffect(EffectSpec effect, DescribeContext ctx, List<int> buffed, List<int> weakened, bool hoistedAllEnemies)
    {
        switch (effect.Action)
        {
            case "deal_damage":
            {
                int outgoing = Outgoing(effect.Amount, ctx.Source);
                Record(effect.Amount, outgoing, buffed, weakened);
                return $"Deal {DamageAmount(outgoing, effect, ctx, buffed)} damage{Suffix(effect, ctx, hoistedAllEnemies)}.";
            }
            case "gain_block":
            {
                // Block goes through BlockMath for the same reason damage goes
                // through DamageMath: once Dexterity and Frail exist, the
                // authored amount stops being the amount the player gets, and
                // this arm was the one that printed the raw number. Recorded
                // so CardView tints an adjusted figure the way it already
                // tints Strength/Weak-adjusted damage.
                int block = ctx.Source is null
                    ? effect.Amount
                    : BlockMath.ComputeOutgoing(effect.Amount, ctx.Source);
                Record(effect.Amount, block, buffed, weakened);
                return $"Gain {block} Block.";
            }
            // "Gain" for anything you put on yourself, "Apply" for anything
            // you put on someone else. This used to special-case Strength by
            // name, which read correctly right up until the first other
            // self-status shipped - Metallicize and Ritual would have said
            // "Apply 3 Metallicize" on a card that targets nobody but you.
            case "apply_status":
                return effect.Scope == EffectScope.Self
                    ? $"Gain {effect.Amount} {effect.Status}."
                    : $"Apply {effect.Amount} {effect.Status}{Suffix(effect, ctx, hoistedAllEnemies)}.";
            case "draw_cards":
                return $"Draw {effect.Amount} card{(effect.Amount == 1 ? "" : "s")}.";
            case "heal":
                return $"Heal {effect.Amount} HP.";
            case "gain_energy":
                return $"Gain {effect.Amount} Energy.";
            case "lose_hp":
                return $"Lose {effect.Amount} HP.";
            case "discard_cards":
                return $"Discard {effect.Amount} card{(effect.Amount == 1 ? "" : "s")} at random.";
            case "exhaust_hand":
                return "Exhaust your hand.";
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
    // already phrased as "Gain"/"Heal"/"Lose". Suppressed when DescribeDetailed
    // has already said it once as a prefix.
    private static string Suffix(EffectSpec effect, DescribeContext ctx, bool hoistedAllEnemies) =>
        !hoistedAllEnemies && TargetsAllEnemies(effect, ctx) ? " to ALL enemies" : "";

    private static void Record(int baseAmount, int shown, List<int> buffed, List<int> weakened)
    {
        if (shown > baseAmount && !buffed.Contains(shown)) buffed.Add(shown);
        else if (shown < baseAmount && !weakened.Contains(shown)) weakened.Add(shown);
    }
}
