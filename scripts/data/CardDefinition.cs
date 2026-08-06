using System.Collections.Generic;

namespace Hollowdeck.Data;

// Power is the genre's third card class: played once, then it leaves the fight
// entirely rather than cycling back through the discard pile. See
// PileManager.Powers for where it goes and why that is a pile of its own
// rather than Exhaust.
//
// Status and Curse are the two that cannot be played at all - deck pollution
// rather than deck building. They exist because add_card exists: until an
// effect could put a card into a pile at runtime there was no way to give the
// player one, and so every event downside in the game had to be HP or gold.
public enum CardType { Attack, Skill, Power, Status, Curse }
public enum CardTargetType { SingleEnemy, AllEnemies, Self, None }

// Common is the zero value, so a cards.json entry that omits "rarity" still
// deserializes - the same tolerant-deserialization pattern used for save data.
// Every card in the pool now declares one explicitly, and
// EffectSmokeTest asserts the distribution stays sane, so the default is a
// safety net rather than something content relies on.
public enum Rarity { Common, Uncommon, Rare }

// No Description field - display text is generated from Effects by
// EffectDescriptionFormatter so it can never drift from what the card
// actually does (e.g. Strength/Vulnerable-adjusted damage numbers).
public class CardDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // -1 is the X-cost sentinel: the card spends all remaining energy and
    // hands the amount down to its PerX effects. See IsXCost below - never
    // compare against -1 at a call site.
    public int Cost { get; set; }
    public CardType Type { get; set; }
    public CardTargetType Target { get; set; }
    public bool Exhaust { get; set; }
    public Rarity Rarity { get; set; } = Rarity.Common;

    // The three pile keywords, all enforced in PileManager rather than at the
    // call sites that move cards around - one place to look, and one place for
    // a fourth keyword to join.
    //
    // Retain: DiscardHand leaves it in hand. It does NOT reduce next turn's
    // draw, so a retained card is a six-card hand; that is the genre's
    // behaviour and the reason the keyword is worth a card slot.
    // Innate: the opening draw pulls it first (PileManager.PromoteInnate).
    // Ethereal: DiscardHand exhausts it instead of discarding it.
    public bool Retain { get; set; }
    public bool Innate { get; set; }
    public bool Ethereal { get; set; }

    // Derived from Type rather than authored as its own bool, so the two can
    // never disagree about whether a Curse is playable. Every gate reads this:
    // CombatManager.TryPlayCard, CardPool.Sample, CardUpgrade.Apply, CardView.
    public bool IsPlayable => Type is not (CardType.Status or CardType.Curse);

    public bool IsXCost => Cost < 0;

    public List<EffectSpec> Effects { get; set; } = new();

    // The keyword sentence CardView prefixes onto the generated description.
    // Lives here rather than in the formatter because it is a fact about the
    // card, not about its effects - and it is what stops an unplayable card
    // with no effects rendering an empty description box.
    public string KeywordLine()
    {
        var words = new List<string>();
        if (!IsPlayable) words.Add("Unplayable");
        if (Innate) words.Add("Innate");
        if (Retain) words.Add("Retain");
        if (Ethereal) words.Add("Ethereal");
        return words.Count == 0 ? "" : string.Join(". ", words) + ".";
    }
}
