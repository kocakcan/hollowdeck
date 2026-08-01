using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Max HP goes up and the player is healed for the same amount, so the gain is
// felt now rather than only after the next rest site. This mirrors what
// RunState.AdvanceAct already does with an act's max-HP bonus.
public class GainMaxHpOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        RunState.PlayerMaxHp += spec.Amount;
        RunState.PlayerCurrentHp += spec.Amount;
        return null;
    }
}
