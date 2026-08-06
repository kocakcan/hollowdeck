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
// it is what turns a card's base "Deal 6 damage" into the 9 that will really
// land on a Vulnerable enemy; without it the text stays on base numbers.
public readonly record struct DescribeContext(
    Combatant? Source = null,
    CardTargetType TargetType = CardTargetType.None,
    IReadOnlyList<Combatant>? Targets = null,
    DescribeVoice Voice = DescribeVoice.Player);

// Who the sentence is about. Everything here was written in the imperative,
// because until enemy intents needed explaining, every caller was describing a
// card the player was about to play: "Deal 6 damage. Apply 2 Weak."
//
// An enemy's telegraph is the same effects seen from the other side, and it has
// to read as a statement about the enemy rather than an instruction to the
// player - "Deals 6 damage. Applies 2 Weak to you." Doing that as a voice on
// the one formatter, rather than a second formatter or a regex over finished
// prose, is what stops the two descriptions of the same EffectSpec drifting
// apart. Player is the default, so no existing call site changes.
public enum DescribeVoice { Player, Enemy }

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

    /// The card-aware form, for the one caller that has a whole
    /// CardDefinition: it prefixes the card's keyword sentence ("Retain.",
    /// "Unplayable.") onto the generated effect text.
    ///
    /// An overload rather than a change to the signature above, because
    /// potions and enemy moves have effects but no CardDefinition. It is also
    /// what stops an unplayable card with no effects rendering an empty
    /// description box, and it feeds Keywords.Find for free - the hover
    /// tooltips for the three new keywords come out of the same text scan that
    /// already explains Block and Exhaust.
    public static DescribedEffects DescribeCard(CardDefinition card, DescribeContext ctx)
    {
        var described = DescribeDetailed(card.Effects, ctx);
        string keywords = card.KeywordLine();
        if (keywords.Length == 0) return described;

        string text = described.Text.Length == 0 ? keywords : $"{keywords} {described.Text}";
        return described with { Text = text };
    }

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

        // An un-aimed card ends here, on its base numbers. It used to append a
        // hypothetical "(~N vs Vulnerable)" as well, which was the longest line
        // in a 152x88 description box and told the player nothing they could
        // act on: it showed up on cards with no damage worth the space (Sweeping
        // Blow's block line ran under it), on screens with no combat behind them
        // at all, and it went away the instant the card was actually aimed -
        // because from then on the printed numbers *are* the post-Vulnerable
        // ones. Live targeting is the honest version of the same information,
        // and it is the one that survived.
        return new DescribedEffects(string.Join(" ", parts), buffed, weakened);
    }

    private static int Outgoing(int baseAmount, Combatant? source) =>
        source is null ? baseAmount : DamageMath.ComputeOutgoing(baseAmount, source);

    // How much a spec is for, as text. An X-cost spec has no number until the
    // card is played - it is worth whatever energy is left at that moment - so
    // it prints the letter instead: "X" for the ordinary amount-1 case, "3X"
    // for a spec that pays 3 per point.
    //
    // Deliberately not a live preview against current energy. That would be a
    // real number, it would be correct, and it would change under the player
    // every time they spent a card - so the description would flicker between
    // two truths. "X" is not a lie; it is the card's actual rule.
    private static string Amount(EffectSpec effect) =>
        effect.PerX
            ? (effect.Amount == 1 ? "X" : $"{effect.Amount}X")
            : effect.Amount.ToString();

    // EffectSpec is a plain serialization class, so it has reference equality
    // and cannot be compared directly. Deliberately not converted to a record:
    // it is the shape the content JSON deserializes into, and value semantics
    // are not what the rest of the effect pipeline wants from it.
    // Public because EnemyView counts a move's hits with the same rule: a
    // multi-hit is a run of identical specs on both sides of the game, and
    // "Deal 4 damage twice" and a "4 x2" telegraph must never disagree about
    // what counts as one.
    // Every field, not just the four that existed when this was written: two
    // add_card specs naming *different* cards would otherwise collapse into
    // "Add 1 Wound to your discard pile. twice.", which is both wrong and the
    // kind of wrong a telegraph inherits.
    public static bool SameEffect(EffectSpec a, EffectSpec b) =>
        a.Action == b.Action && a.Amount == b.Amount && a.Status == b.Status && a.Scope == b.Scope
        && a.CardId == b.CardId && a.Pile == b.Pile && a.PerX == b.PerX;

    // DescribeEffect hands back a finished sentence, so the repetition has to
    // go inside it, before the full stop.
    private static string Repeated(string sentence, int times) =>
        times switch
        {
            1 => sentence,
            2 => $"{sentence.TrimEnd('.')} twice.",
            _ => $"{sentence.TrimEnd('.')} {times} times.",
        };

    // Two ways to mean "everything": the card-level CardTargetType.AllEnemies
    // that has existed since Cleave, and the per-effect EffectScope.AllEnemies
    // added in Phase 7 so one card can hit its target and debuff the room.
    // Both read the same to a player, so both produce the same suffix.
    private static bool TargetsAllEnemies(EffectSpec effect, DescribeContext ctx) =>
        effect.Scope == EffectScope.AllEnemies
        || (effect.Scope == EffectScope.Target && ctx.TargetType == CardTargetType.AllEnemies);

    // Aimed at somebody other than the caster - which is every scope except
    // Self. The three that qualify all take the Vulnerable-adjusted damage
    // treatment and the enemy voice's "to you" suffix; writing it as one
    // predicate is what stops a fifth scope being added to three of the four
    // sites that need it.
    private static bool IsOutward(EffectScope scope) => scope != EffectScope.Self;

    private static string DescribeEffect(EffectSpec effect, DescribeContext ctx, List<int> buffed, List<int> weakened, bool hoistedAllEnemies)
    {
        switch (effect.Action)
        {
            case "deal_damage":
            {
                // An X spec has no number to run Strength/Weak/Vulnerable
                // through, so it takes none of the tint bookkeeping below
                // either - there is nothing to compare an adjusted figure to.
                if (effect.PerX)
                {
                    return $"{Verb(ctx, "Deal", "Deals")} {Amount(effect)} damage{Suffix(effect, ctx, hoistedAllEnemies)}.";
                }
                int outgoing = Outgoing(effect.Amount, ctx.Source);
                Record(effect.Amount, outgoing, buffed, weakened);
                return $"{Verb(ctx, "Deal", "Deals")} {DamageAmount(outgoing, effect, ctx, buffed)} damage{Suffix(effect, ctx, hoistedAllEnemies)}.";
            }
            case "gain_block":
            {
                if (effect.PerX) return $"{Verb(ctx, "Gain", "Gains")} {Amount(effect)} Block.";
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
                return $"{Verb(ctx, "Gain", "Gains")} {block} Block.";
            }
            // "Gain" for anything you put on yourself, "Apply" for anything
            // you put on someone else. This used to special-case Strength by
            // name, which read correctly right up until the first other
            // self-status shipped - Metallicize and Ritual would have said
            // "Apply 3 Metallicize" on a card that targets nobody but you.
            case "apply_status":
                return effect.Scope == EffectScope.Self
                    ? $"{Verb(ctx, "Gain", "Gains")} {Amount(effect)} {effect.Status}."
                    : $"{Verb(ctx, "Apply", "Applies")} {Amount(effect)} {effect.Status}{Suffix(effect, ctx, hoistedAllEnemies)}.";
            case "draw_cards":
                return $"{Verb(ctx, "Draw", "Draws")} {Amount(effect)} card{(effect.Amount == 1 && !effect.PerX ? "" : "s")}.";
            case "heal":
                return $"{Verb(ctx, "Heal", "Heals")} {Amount(effect)} HP.";
            case "gain_energy":
                return $"{Verb(ctx, "Gain", "Gains")} {Amount(effect)} Energy.";
            case "lose_hp":
                return $"{Verb(ctx, "Lose", "Loses")} {Amount(effect)} HP.";
            case "discard_cards":
                return $"{Verb(ctx, "Discard", "Discards")} {Amount(effect)} card{(effect.Amount == 1 && !effect.PerX ? "" : "s")} at random.";
            case "exhaust_hand":
                return ctx.Voice == DescribeVoice.Enemy ? "Exhausts its hand." : "Exhaust your hand.";
            // Was missing until a card used it: gain_gold shipped for a relic,
            // and relics describe themselves from relics.json rather than from
            // here, so the gap only showed up as a card with no rules text at
            // all - which is exactly the silent failure the default arm below
            // produces for an unknown action.
            case "gain_gold":
                return $"{Verb(ctx, "Gain", "Gains")} {Amount(effect)} Gold.";
            // The first arm that has to resolve an id against a database. Find
            // rather than Get, and an empty string rather than a throw, for
            // the same reason AddCardEffect uses it: this runs on a shop tile
            // and in a card picker, where a typo in cards.json must not take
            // the screen down. The audit that catches it is in EffectSmokeTest.
            case "add_card":
            {
                var added = effect.CardId is { Length: > 0 } id ? CardDatabase.Find(id) : null;
                if (added is null) return "";
                string count = Amount(effect);
                // "Shuffle" for the draw pile because that is what the pile
                // insert actually does (a random index, not the top) - the
                // word is doing real work, not flavour.
                return effect.Pile switch
                {
                    CardPile.Draw => $"{Verb(ctx, "Shuffle", "Shuffles")} {count} {added.Name} into your draw pile.",
                    CardPile.Hand => $"{Verb(ctx, "Add", "Adds")} {count} {added.Name} to your hand.",
                    _ => $"{Verb(ctx, "Add", "Adds")} {count} {added.Name} to your discard pile.",
                };
            }
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
        if (!IsOutward(effect.Scope) || ctx.Targets is not { Count: > 0 } targets)
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

    // The imperative form for a card the player is about to play, the
    // third-person form for an enemy's telegraph. Only the leading verb ever
    // differs, which is why this is a two-argument helper at each arm rather
    // than a parallel set of sentence templates.
    private static string Verb(DescribeContext ctx, string imperative, string thirdPerson) =>
        ctx.Voice == DescribeVoice.Enemy ? thirdPerson : imperative;

    // "to ALL enemies" is the whole point of this suffix: an AllEnemies card
    // has no other tell in its description. SingleEnemy stays bare (dragging
    // the card onto one enemy already says who it hits) and Self effects are
    // already phrased as "Gain"/"Heal"/"Lose". Suppressed when DescribeDetailed
    // has already said it once as a prefix.
    //
    // In the enemy's voice the same Target scope means the player, and an
    // intent has no drag gesture to say so the way a card's targeting does, so
    // it names the recipient outright.
    private static string Suffix(EffectSpec effect, DescribeContext ctx, bool hoistedAllEnemies)
    {
        if (!hoistedAllEnemies && TargetsAllEnemies(effect, ctx)) return " to ALL enemies";
        // A random target has no drag gesture and no card-level tell either,
        // so the sentence is the only place it can be said.
        if (effect.Scope == EffectScope.RandomEnemy) return " to a random enemy";
        if (ctx.Voice == DescribeVoice.Enemy && IsOutward(effect.Scope)) return " to you";
        return "";
    }

    private static void Record(int baseAmount, int shown, List<int> buffed, List<int> weakened)
    {
        if (shown > baseAmount && !buffed.Contains(shown)) buffed.Add(shown);
        else if (shown < baseAmount && !weakened.Contains(shown)) weakened.Add(shown);
    }
}
