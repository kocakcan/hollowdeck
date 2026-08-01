using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Removes one card the player picks. Deck thinning has no other home in the
// game - the shop sells cards and the rest site upgrades them - so this is
// the only way a starting Strike ever leaves a deck.
public class RemoveChosenCardOutcome : ICardPickerOutcome
{
    public string Prompt => "Choose a card to give up.";

    // Every card is removable, including starters: a deck that cannot shed
    // its opening Strikes cannot be built into anything, and that is the
    // whole reason the outcome exists. The floor is one card - an empty deck
    // draws nothing and the next fight is unwinnable and unquittable.
    public IEnumerable<int> Selectable() =>
        RunState.Deck.Count <= 1 ? Enumerable.Empty<int>() : Enumerable.Range(0, RunState.Deck.Count);

    public string Apply(int deckIndex)
    {
        var removed = RunState.Deck[deckIndex];
        RunState.Deck.RemoveAt(deckIndex);
        return $"{removed.Name} is gone from your deck.";
    }

    public string? Execute(EventOutcomeSpec spec) => "You have nothing you can afford to give up.";
}
