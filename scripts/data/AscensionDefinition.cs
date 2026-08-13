using System.Collections.Generic;

namespace Hollowdeck.Data;

// One rung of the ascension ladder: a label and the deltas that rung adds on
// top of everything below it. Authored in data/ascension/ascension.json in
// Level order, and folded into a cumulative AscensionModifiers by
// AscensionDatabase - so a row here is read as "what rung 7 adds", never as
// "what the run looks like at rung 7".
//
// The modifier vocabulary is deliberately closed, and every field below names a
// knob that already existed before the ladder did: enemy HP and damage, boss
// HP, starting max HP, the act-clear heal, shop prices, elite frequency, the
// potion drop rate, and the opening deck. A rung wanting anything outside that
// list is asking for new plumbing and should be recognised as such rather than
// smuggled in as a content row - twenty rungs against a vocabulary that is
// already full is the "vocabulary before content" rule the roadmap states,
// arriving one layer up.
//
// Percent fields are *deltas against 100*, not multipliers: 0 means "this rung
// does not touch that knob", 10 means "+10% on top of the rungs below". That is
// what makes the fold a sum rather than a product, and what makes a rung that
// forgot a key inert rather than catastrophic (a multiplier field defaulting to
// 0 would zero the whole ladder).
public class AscensionDefinition
{
    // 1-based, contiguous, and the array index is derived from it rather than
    // from position in the file - AscensionSmokeTest asserts the two agree, so
    // a row inserted in the wrong place fails a test instead of silently
    // renumbering every rung above it.
    public int Level { get; set; }

    // The one line RunSetupScreen prints for this rung. Player-facing prose:
    // what changed, not which field moved.
    public string Label { get; set; } = "";

    public int EnemyHpPercent { get; set; }
    public int EnemyDamagePercent { get; set; }

    // Applied on top of EnemyHpPercent for bosses only, so a rung can make a
    // boss harder without touching the normal fights whose mean is the
    // denominator every boss ratio in BalanceReport is measured against.
    public int BossHpPercent { get; set; }

    public int StartingMaxHpDelta { get; set; }

    // Subtracted from ActDefinition.ClearHealPercent, which is authored at 100
    // (a full heal on an act clear). ActDefinition's own comment already calls
    // this "the rung an ascension ladder would turn back down".
    public int ClearHealPercentDelta { get; set; }

    public int ShopPricePercent { get; set; }

    // Moved *from* Combat's weight into Elite's in MapGenerator's node table,
    // not added on top of it - see AscensionModifiers.EliteWeight.
    public int EliteWeightDelta { get; set; }

    // Subtracted from both of an act's drop rates. Negative-going, like the
    // heal above: the ladder only ever takes.
    public int PotionDropPercentDelta { get; set; }

    // Card ids added to the opening deck. Unplayable ones (Curse/Status) are
    // the point - this is the knob Phase 7's add_card primitive and derived
    // IsPlayable made authorable at all. AscensionSmokeTest asserts every id
    // here names a real card and that it is unplayable, since a rung handing
    // the player a *good* card is a rung that reads as a bug.
    public List<string> StartingCurseIds { get; set; } = new();
}
