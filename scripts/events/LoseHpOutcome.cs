using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class LoseHpOutcome : IEventOutcome
{
    // Floored at 1, not 0 - an event choice shouldn't be able to kill the
    // player outright the way combat's LoseHpEffect can.
    public string? Execute(EventChoice choice)
    {
        RunState.PlayerCurrentHp = Mathf.Max(1, RunState.PlayerCurrentHp - choice.Amount);
        return null;
    }
}
