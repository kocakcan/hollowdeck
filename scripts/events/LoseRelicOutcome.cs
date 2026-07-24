using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class LoseRelicOutcome : IEventOutcome
{
    // Safe: RelicInstance/RelicBehavior are pull-based (CombatManager calls
    // relic.Behavior.<Hook>(ctx) by iterating the list it's handed at
    // combat start), not subscribed to any persistent event bus - removing
    // one from RunState.Relics needs no unwinding step.
    public string? Execute(EventChoice choice)
    {
        if (RunState.Relics.Count == 0) return "You have nothing to lose.";

        int index = RngStreams.Shop.Next(RunState.Relics.Count);
        RunState.Relics.RemoveAt(index);
        return null;
    }
}
