using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainGoldOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        RunState.Gold += spec.Amount;
        return null;
    }
}
