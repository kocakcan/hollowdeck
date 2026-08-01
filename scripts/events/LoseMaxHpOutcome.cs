using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// The cost half of a bargain event. Two floors, both load-bearing: max HP
// never drops below 1 (a 0-max run is unplayable and unrecoverable), and
// current HP is pulled down with it so it can never sit above the new max -
// every HP bar in the game renders current/max and would show 34/30.
//
// Deliberately cannot kill: an event that ends the run from a menu, with no
// fight and no way to react, is the one outcome the genre never uses.
public class LoseMaxHpOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        RunState.PlayerMaxHp = Mathf.Max(1, RunState.PlayerMaxHp - spec.Amount);
        RunState.PlayerCurrentHp = Mathf.Clamp(RunState.PlayerCurrentHp, 1, RunState.PlayerMaxHp);
        return null;
    }
}
