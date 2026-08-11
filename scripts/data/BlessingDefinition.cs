using System.Collections.Generic;

namespace Hollowdeck.Data;

// One of the three offers on the start-of-run screen.
//
// Deliberately not a new mechanical vocabulary: Outcomes is a list of the same
// EventOutcomeSpec an event choice carries, resolved through the same
// EventOutcomeRegistry.Begin. That registry exists precisely because it is the
// non-combat one - EffectContext requires a live CombatManager/Combatant, and
// there is no fight at run start any more than there is on an event screen -
// so a blessing is an event choice offered before the map rather than a
// parallel system that would have to grow its own copy of every outcome.
//
// What it does NOT carry is EventChoice's single-outcome shorthand
// (Outcome/Amount at the top level). That field pair exists only because every
// event authored before compound choices had to keep loading unchanged;
// blessings are new, so there is exactly one authoring form and a reader never
// has to ask which one a given row used.
public class BlessingDefinition
{
    public string Id { get; set; } = "";

    // The tile's heading - what the player is choosing.
    public string Label { get; set; } = "";

    // The tile's body: what it does, in plain terms. This is the only place a
    // blessing's effect is stated, so it has to be accurate rather than
    // atmospheric - the tile is the whole decision.
    public string Description { get; set; } = "";

    public List<EventOutcomeSpec> Outcomes { get; set; } = new();

    // Shown after the choice resolves. Overridden by any outcome that could
    // not do what its label promised, exactly as an event's is.
    public string ResultText { get; set; } = "";
}
