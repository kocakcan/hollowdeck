using Hollowdeck.Data;

namespace Hollowdeck.Events;

public interface IEventOutcome
{
    // Returns an override message when the authored ResultText doesn't
    // apply (e.g. no relics left to grant); null means use
    // choice.ResultText as-is.
    string? Execute(EventChoice choice);
}
