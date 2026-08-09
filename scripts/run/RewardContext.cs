using System.Collections.Generic;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Which rewards a won fight is *offering*. Every field here is an offer rather
// than a record of something already handed over - RewardScreen's list is what
// grants them, one row at a time.
//
// That is a change from how this used to work, and it is the load-bearing part.
// Gold was added to RunState by CombatScreen before the screen ever loaded, and
// the relic was granted inside the same call that picked it, so two of the four
// rewards were already in the player's hands while the screen was still calling
// them a reward. That was invisible while the screen offered nothing but cards.
// It stops being invisible the moment the screen is a list you claim from: a
// row that is already yours is not a row.
public static class RewardContext
{
    public static List<CardDefinition> CardChoices = new();
    public static int GoldAwarded;

    // Set by CombatScreen when the fight just won was an Elite or Boss. Picked,
    // not granted - RunState.Relics gains it when the row is claimed.
    public static RelicDefinition? GuaranteedRelic;

    // The potion this fight dropped, if it dropped one. Null far more often
    // than not: the roll is per act (ActDefinition.PotionDropPercent) and a
    // boss never rolls at all.
    public static PotionDefinition? PotionDrop;

    // Set when the boss that just died ended an act (so: never on the final
    // act's boss, which routes to RunEndScreen instead). Null for every
    // ordinary fight - CombatScreen assigns it unconditionally so a stale one
    // can't survive into the next reward. Not a reward row: it is information
    // about a bonus already applied by RunState.AdvanceAct, and there is
    // nothing to claim.
    public static ActClear? ActCleared;

    // Which rows the player has already taken. A set rather than a bool per
    // kind, because the next two things the roadmap puts on this screen - the
    // boss relic as a choice of three, and a skip streak on card rewards - both
    // add rows rather than fields, and a set does not have to be widened for
    // either.
    //
    // Cleared by CombatScreen alongside the offers above. It is what survives a
    // rebuild of the screen, so "Taken" is a fact about the run rather than
    // about a Button that happens to still exist.
    public static readonly HashSet<RewardKind> Claimed = new();
}

// The row kinds the reward list can offer, in the order it lists them. Gold
// first because it is unconditional and reads as the fight's receipt; the card
// last because it is the only one that opens a second view.
public enum RewardKind
{
    Gold,
    Relic,
    Potion,
    Card,
}
