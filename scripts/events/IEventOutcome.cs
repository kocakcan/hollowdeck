using System.Collections.Generic;
using Hollowdeck.Data;

namespace Hollowdeck.Events;

public interface IEventOutcome
{
    // Returns an override message when the authored ResultText doesn't
    // apply (e.g. no relics left to grant); null means use
    // choice.ResultText as-is.
    string? Execute(EventOutcomeSpec spec);
}

// An outcome that has to ask the player *which card* before it can do
// anything - "remove a card", "upgrade a card".
//
// A second interface rather than making Execute async: only two outcomes need
// to ask a question, and the other thirteen would have paid for it. Execute is
// still implemented here and covers exactly one case - the deck has nothing
// selectable, so there is no question to ask and the outcome degrades to a
// message. That is the same shape LoseRelicOutcome already uses for an empty
// relic list.
public interface ICardPickerOutcome : IEventOutcome
{
    // Shown above the card grid. Each outcome phrases its own question:
    // "Choose a card to remove" and "Choose a card to upgrade" are not
    // interchangeable, and a shared generic prompt would make them look it.
    string Prompt { get; }

    // Indices into RunState.Deck the player may pick. Indices rather than
    // CardDefinitions because the deck holds N separate slots for "5x Strike"
    // and the choice is of a *slot*, not of a card type.
    IEnumerable<int> Selectable();

    // Applies the choice and returns the text to show for it.
    string Apply(int deckIndex);
}
