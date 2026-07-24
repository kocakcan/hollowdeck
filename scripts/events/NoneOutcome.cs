using Hollowdeck.Data;

namespace Hollowdeck.Events;

// No-op, for pure-flavor "walk away" choices.
public class NoneOutcome : IEventOutcome
{
    public string? Execute(EventChoice choice) => null;
}
