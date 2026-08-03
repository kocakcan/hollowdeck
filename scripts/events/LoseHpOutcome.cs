using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class LoseHpOutcome : IEventOutcome
{
    // Floored at 1, not 0 - an event choice shouldn't be able to kill the
    // player outright the way combat's LoseHpEffect can.
    public string? Execute(EventOutcomeSpec spec)
    {
        RunState.PlayerCurrentHp = Mathf.Max(1, RunState.PlayerCurrentHp - spec.Amount);
        return null;
    }
}
