using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class LoseGoldOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        RunState.Gold = Mathf.Max(0, RunState.Gold - spec.Amount);
        return null;
    }
}
