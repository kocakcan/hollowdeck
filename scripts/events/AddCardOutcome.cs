using System;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Puts one named card into the deck. The event-side twin of the add_card
// effect, and the reason it exists: before this, every downside an event could
// author was HP, gold, max HP or a relic, so an event was always a trade of one
// resource for another and never a risk. A Curse is a cost that follows the
// player for the rest of the run.
//
// Deliberately NOT routed through CardPool: this outcome names its card, which
// is the whole point. CardPool answers "what may the player be offered", and
// the answer for a Curse is never.
public class AddCardOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        if (CardDatabase.Find(spec.CardId) is not { } definition)
        {
            GD.PushError($"AddCardOutcome: unknown cardId '{spec.CardId}'");
            return null;
        }

        // Amount is a copy count, and an unauthored 0 would make the whole
        // outcome a silent no-op. EventSmokeTest audits the authored data for
        // a count rather than clamping it here.
        for (int i = 0; i < spec.Amount; i++) RunState.Deck.Add(definition);

        // Null: the choice's own resultText is the prose, the same as every
        // other non-picker outcome.
        return null;
    }
}
