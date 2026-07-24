using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

public class GainRelicOutcome : IEventOutcome
{
    // Same unowned + unlocked filter TreasureScreen/ShopScreen already
    // apply; empty-pool override mirrors TreasureScreen's own fallback.
    public string? Execute(EventChoice choice)
    {
        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var available = RelicDatabase.All
            .Where(r => !ownedRelicIds.Contains(r.Id) && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();

        if (available.Count == 0) return "There were no relics to be found.";

        var picked = available[RngStreams.Shop.Next(available.Count)];
        RunState.Relics.Add(new RelicInstance(picked));
        return null;
    }
}
