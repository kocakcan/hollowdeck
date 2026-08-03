using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Grants one random potion, respecting RunState.MaxPotionSlots - the same cap
// ShopScreen enforces before it will sell one. Without the check the belt
// silently overflows and the extra potion is unreachable, since the combat
// HUD only ever draws MaxPotionSlots slots.
public class GainPotionOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        if (RunState.Potions.Count >= RunState.MaxPotionSlots)
        {
            return "Your potion belt is full; you leave it where it lies.";
        }

        var pool = PotionDatabase.All.ToList();
        if (pool.Count == 0) return "There was nothing left worth drinking.";

        var picked = pool[RngStreams.Shop.Next(pool.Count)];
        RunState.Potions.Add(new PotionInstance(picked));
        return null;
    }
}
