using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Upgrades one un-upgraded card in the deck at random - the no-choice version
// of UpgradeChosenCardOutcome, for events where not getting to pick is the
// point of the trade.
public class UpgradeRandomCardOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        var candidates = Upgradable().ToList();
        if (candidates.Count == 0) return "Every card you carry is already as sharp as it will get.";

        // Replaces the one list slot, not the shared CardDefinition every
        // same-named copy points at - the same care RestScreen.OnCardUpgraded
        // documents. RunState.Deck holds N separate slots for "5x Strike".
        int index = candidates[RngStreams.Shop.Next(candidates.Count)];
        RunState.Deck[index] = CardUpgrade.Apply(RunState.Deck[index]);
        return null;
    }

    public static IEnumerable<int> Upgradable() =>
        Enumerable.Range(0, RunState.Deck.Count).Where(i => !CardUpgrade.IsUpgraded(RunState.Deck[i]));
}
