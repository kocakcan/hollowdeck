using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class LoseGoldOutcome : IEventOutcome
{
    public string? Execute(EventChoice choice)
    {
        RunState.Gold = Mathf.Max(0, RunState.Gold - choice.Amount);
        return null;
    }
}
