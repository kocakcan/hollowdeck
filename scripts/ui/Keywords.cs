using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Effects;

namespace Hollowdeck.UI;

// The single roster of every word the game will explain on hover, and the one
// place its explanation is written.
//
// This used to be two lists that only overlapped by accident: StatusRow.Describe
// held amount-parameterised prose for the eleven statuses (rendered as the icon
// tooltips in combat), and a file-local KeywordHighlights table inside CardView
// held generic prose for five of them (rendered as the card hover panel). Five
// against eleven is the interesting number - Dexterity, Frail, Metallicize,
// Ritual, Regen, Fervor and Foresight had no card-side explanation at all, and
// nothing anywhere failed when a twelfth status would have made that six
// against twelve. Same failure mode CardUpgrade.ShouldScale has (a new status
// silently missing an arm), so it gets the same treatment: one switch, and a
// smoke test that walks StatusType and fails on a missing entry.
//
// Blurb is the body sentence only, with no name prefix - the hover panel puts
// the name in its own coloured title line, and StatusTooltip re-attaches it for
// the icon tooltips, which is why that composition lives here too rather than
// being re-derived by each caller.
public static class Keywords
{
    // A word that can appear in generated card/intent text, plus how the hover
    // panel should colour and explain it.
    public readonly record struct Entry(string Keyword, Color Color, string Blurb);

    // Statuses come off the enum rather than being re-listed, so a twelfth
    // StatusType joins the highlight regex and the hover panel the moment it
    // exists - Blurb's default arm is what would then need writing, and
    // EffectSmokeTest.TestEveryStatusHasAKeywordBlurb is what says so.
    //
    // Six non-status words the generated text uses that a player still has to
    // be taught. None is a StatusType: Block is a field on Combatant, Exhaust
    // is a card destination, and the last four are card keywords - facts about
    // the card printed by CardDefinition.KeywordLine rather than anything held
    // by a combatant. They are hand-listed for exactly that reason: there is no
    // enum to walk, so a seventh needs a line here and the smoke test for the
    // *status* half cannot catch its absence.
    public static readonly IReadOnlyList<Entry> All =
        Enum.GetValues<StatusType>()
            .Select(s => new Entry(s.ToString(), Color(s), Blurb(s)))
            .Append(new Entry("Block", UiTheme.Palette.Block,
                "Reduces incoming damage this turn. Clears at the start of your turn."))
            .Append(new Entry("Exhaust", UiTheme.Palette.ExhaustAccent,
                "The card is removed from your deck for the rest of this combat."))
            .Append(new Entry("Retain", UiTheme.Palette.StatusBuff,
                "Stays in your hand when you end your turn instead of being discarded."))
            .Append(new Entry("Innate", UiTheme.Palette.StatusBuff,
                "Starts in your opening hand every combat."))
            // Ethereal is coloured as an exhaust rather than as a debuff,
            // because that is what it does and the word Exhaust is not in its
            // own blurb to say so.
            .Append(new Entry("Ethereal", UiTheme.Palette.ExhaustAccent,
                "If it is still in your hand when the turn ends, it is exhausted instead of discarded."))
            .Append(new Entry("Unplayable", UiTheme.Palette.StatusDebuff,
                "Cannot be played. It clogs your hand until the turn ends."))
            // Longest first so a keyword that is a prefix of another can never
            // shadow it in the alternation - regex alternation takes the first
            // branch that matches at a position, not the longest.
            .OrderByDescending(e => e.Keyword.Length)
            .ToList();

    // Single alternation over the whole roster, exposed rather than rebuilt per
    // caller. CardView folds it into a larger pattern (it tints modified damage
    // numbers in the same pass); EnemyView just runs Find over an intent's
    // generated sentence. Both mean the same thing by "keyword" because both
    // read this.
    //
    // One regex pass, never a string.Replace-per-keyword loop: that would
    // re-scan its own already-substituted output, so a later keyword could
    // match text sitting inside an earlier keyword's just-inserted markup
    // (Poison's blurb literally contains the word "Block"). Regex.Replace only
    // ever matches against the original input.
    public static readonly Regex Pattern = new(
        string.Join("|", All.Select(e => Regex.Escape(e.Keyword))));

    // Every keyword `text` mentions, in roster order, each appearing once.
    public static List<Entry> Find(string text) =>
        Pattern.Matches(text)
            .Select(m => m.Value)
            .Distinct()
            .Select(k => All.First(e => e.Keyword == k))
            .ToList();

    public static Color Color(StatusType status) =>
        StatusRow.IsDebuff(status) ? UiTheme.Palette.StatusDebuff : UiTheme.Palette.StatusBuff;

    // The icon tooltips in combat, where the player is looking at a specific
    // stack and wants that stack's numbers: "Weak 2: attacks deal 25% less
    // damage. Wears off by 1 each turn."
    public static string StatusTooltip(StatusType status, int amount) =>
        $"{status} {amount}: {Decapitalise(Blurb(status, amount))}";

    // Pass an amount for the icon tooltips (the numbers the player currently
    // has); pass nothing for the card hover panel, which explains what the
    // word means before any of it is on the board.
    //
    // Percentages are read off DamageMath/BlockMath in both forms for the same
    // no-drift reason they always were: they are facts about the resolution
    // code, not about how much of the status you are holding.
    public static string Blurb(StatusType status, int? amount = null) => status switch
    {
        StatusType.Strength => amount is { } str
            ? $"Attacks deal +{str} damage."
            : "Attacks deal more damage.",
        StatusType.Weak =>
            $"Attacks deal {(int)((1 - DamageMath.WeakMultiplier) * 100)}% less damage. Wears off by 1 each turn.",
        StatusType.Vulnerable =>
            $"Takes {(int)((DamageMath.VulnerableMultiplier - 1) * 100)}% more damage. Wears off by 1 each turn.",
        StatusType.Poison => amount is { } psn
            ? $"Loses {psn} HP each turn (ignores Block), then Poison drops by 1."
            : "Loses HP each turn (ignores Block), then Poison drops by 1.",

        // All five turn-start grants spell out "does not wear off" - every
        // other status in the game decays, so persistence is the surprising
        // half, and it is the reason a Power is worth a card that never comes
        // back.
        StatusType.Metallicize => amount is { } met
            ? $"Gains {met} Block at the start of each turn. Does not wear off."
            : "Gains Block at the start of each turn. Does not wear off.",
        StatusType.Ritual => amount is { } rit
            ? $"Gains {rit} Strength at the start of each turn. Does not wear off."
            : "Gains Strength at the start of each turn. Does not wear off.",
        StatusType.Regen => amount is { } rgn
            ? $"Heals {rgn} HP at the start of each turn. Does not wear off."
            : "Heals HP at the start of each turn. Does not wear off.",
        // Same phrasing and the same word for what they pay out, since the
        // player meets these as a number on an icon before ever reading a card.
        StatusType.Fervor => amount is { } frv
            ? $"Gains {frv} extra Energy at the start of each turn. Does not wear off."
            : "Gains extra Energy at the start of each turn. Does not wear off.",
        StatusType.Foresight => amount is { } frs
            ? $"Draws {frs} extra card(s) at the start of each turn. Does not wear off."
            : "Draws extra cards at the start of each turn. Does not wear off.",

        // Phrased to mirror Strength/Weak word for word - these are the same
        // two effects applied to Block, and the wording is what says so.
        StatusType.Dexterity => amount is { } dex
            ? $"Gains +{dex} Block."
            : "Gains more Block.",
        StatusType.Frail =>
            $"Gains {(int)((1 - BlockMath.FrailMultiplier) * 100)}% less Block. Wears off by 1 each turn.",

        // Reached only by a StatusType with no arm above. Deliberately useless
        // prose rather than a throw - a missing blurb should be caught by the
        // smoke test at build time, not crash a combat screen at runtime.
        _ => amount is { } n ? $"{status} {n}." : $"{status}.",
    };

    // StatusTooltip reads "<Name> <n>: <blurb>", so the blurb's leading capital
    // has to go. Only the first character, and only if it is one - the arms
    // above are written as standalone sentences because that is the form the
    // hover panel needs, and this is the one caller that needs the other.
    private static string Decapitalise(string sentence) =>
        sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]) + sentence[1..];
}
