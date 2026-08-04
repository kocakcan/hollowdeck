using System.Collections.Generic;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

public static class RewardContext
{
    public static List<CardDefinition> CardChoices = new();
    public static int GoldAwarded;

    // Set by CombatScreen when the fight just won was an Elite or Boss -
    // a relic already picked and granted, shown here purely for display.
    public static RelicDefinition? GuaranteedRelic;

    // Set when the boss that just died ended an act (so: never on the final
    // act's boss, which routes to RunEndScreen instead). Null for every
    // ordinary fight - CombatScreen assigns it unconditionally so a stale one
    // can't survive into the next reward.
    public static ActClear? ActCleared;
}
