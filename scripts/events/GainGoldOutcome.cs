using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainGoldOutcome : IEventOutcome
{
    public string? Execute(EventChoice choice)
    {
        RunState.Gold += choice.Amount;
        return null;
    }
}
