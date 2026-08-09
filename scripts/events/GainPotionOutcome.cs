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

        // Rarity-weighted like the shop's stock and the combat drop, rather
        // than the uniform pick this used to do - PotionPool is the single
        // place "which potion is offered" is answered, so all four grant sites
        // agree on how rare a Rare is. Stays on RngStreams.Shop, which is the
        // stream every non-combat grant already draws from; RngStreams.Drops
        // is for the combat roll alone.
        var picked = PotionPool.SampleOne(PotionDatabase.All, RngStreams.Shop);
        if (picked is null) return "There was nothing left worth drinking.";

        RunState.Potions.Add(new PotionInstance(picked));
        return null;
    }
}
