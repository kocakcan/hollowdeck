using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class HealOutcome : IEventOutcome
{
    public string? Execute(EventChoice choice)
    {
        RunState.PlayerCurrentHp = Mathf.Min(RunState.PlayerMaxHp, RunState.PlayerCurrentHp + choice.Amount);
        return null;
    }
}
