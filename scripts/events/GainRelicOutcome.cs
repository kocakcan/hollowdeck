using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainRelicOutcome : IEventOutcome
{
    // Draws the ordinary ladder plus the Event tier, which is the only place
    // that tier is reachable from - the unowned + unlocked filter every grant
    // site used to spell out for itself lives in RelicPool now. The empty-pool
    // override mirrors TreasureScreen's own fallback.
    public string? Execute(EventOutcomeSpec spec)
    {
        var picked = RelicPool.SampleOne(RelicSite.Event, RngStreams.Shop);
        if (picked is null) return "There were no relics to be found.";

        RunState.Relics.Add(new RelicInstance(picked));
        return null;
    }
}
