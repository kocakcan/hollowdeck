using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Rolls one of spec.Alternatives and resolves it. The one outcome whose
// result the player cannot read off the label, which is the whole point of
// the events that use it.
//
// Card pickers are not allowed among the alternatives, and EventSmokeTest
// enforces it: a gamble that stops to open a grid and ask a question is not a
// gamble, and Begin() has already returned by the time this runs, so a picker
// rolled here would have no way to reach the screen anyway.
public class GambleOutcome : IEventOutcome
{
    public string? Execute(EventOutcomeSpec spec)
    {
        if (spec.Alternatives.Count == 0)
        {
            GD.PushError("GambleOutcome: no alternatives authored; nothing to roll.");
            return null;
        }

        // RngStreams.Shop, the stream every other non-combat grant already
        // draws from, so a gamble can't shift combat shuffles (genre risk 2).
        var rolled = spec.Alternatives[RngStreams.Shop.Next(spec.Alternatives.Count)];

        // The rolled alternative's own message always wins, even when it is
        // null-and-therefore-default: the choice's ResultText was written for
        // the act of gambling, not for the face that came up, so a gamble is
        // the one outcome that must say what actually happened.
        return EventOutcomeRegistry.Resolve(rolled) ?? Describe(rolled);
    }

    // A plain readback of the branch that landed. Deliberately generated
    // rather than authored per alternative: the alternatives are a list of
    // specs, and giving each one its own prose field would make the gamble
    // the only outcome shape carrying result text of its own.
    private static string Describe(EventOutcomeSpec spec) => spec.Outcome switch
    {
        "gain_gold" => $"The bones favour you: {spec.Amount} gold.",
        "lose_gold" => $"The bones take their due: {spec.Amount} gold gone.",
        "heal" => $"Warmth spreads through you. {spec.Amount} HP restored.",
        "lose_hp" => $"Something bites back. You lose {spec.Amount} HP.",
        "gain_relic" => "Something old and heavy settles into your pack.",
        "gain_max_hp" => $"You come away hardier. {spec.Amount} max HP.",
        "lose_max_hp" => $"Something of you does not come back. {spec.Amount} max HP.",
        _ => "The bones settle, and it is done.",
    };
}
