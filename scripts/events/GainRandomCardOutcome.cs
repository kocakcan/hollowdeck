using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainRandomCardOutcome : IEventOutcome
{
    public string? Execute(EventChoice choice)
    {
        var all = CardDatabase.All.ToList();
        var picked = all[RngStreams.Shop.Next(all.Count)];
        RunState.Deck.Add(picked);
        return null;
    }
}
