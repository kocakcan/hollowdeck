using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class HealOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        RunState.PlayerCurrentHp = Mathf.Min(RunState.PlayerMaxHp, RunState.PlayerCurrentHp + spec.Amount);
        return null;
    }
}
