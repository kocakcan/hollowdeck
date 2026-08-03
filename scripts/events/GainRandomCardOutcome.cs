using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainRandomCardOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        // Unlock-filtered, same as reward picks and shop stock - an event
        // must not be a side door onto content the player hasn't earned, and
        // (since CardPool landed) must not be a side door onto Rares at
        // twenty times their reward-screen odds either.
        var picked = CardPool.SampleOne(MetaProgressionManager.Instance.UnlockedCards(), RngStreams.Shop);
        if (picked is not null) RunState.Deck.Add(picked);
        return null;
    }
}
