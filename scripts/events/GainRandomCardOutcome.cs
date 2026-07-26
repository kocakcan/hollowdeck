using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainRandomCardOutcome : IEventOutcome
{
    public string? Execute(EventChoice choice)
    {
        // Unlock-filtered, same as reward picks and shop stock - an event
        // must not be a side door onto content the player hasn't earned.
        var all = MetaProgressionManager.Instance.UnlockedCards().ToList();
        var picked = all[RngStreams.Shop.Next(all.Count)];
        RunState.Deck.Add(picked);
        return null;
    }
}
