using System.Collections.Generic;

namespace Hollowdeck.Data;

public class EventChoice
{
    public string Label { get; set; } = "";
    // Keys into EventOutcomeRegistry - same string-key-into-a-code-side-
    // registry idiom as EffectSpec.Action, deliberately not a C# enum so
    // new outcomes stay data-addressable.
    public string Outcome { get; set; } = "";
    public int Amount { get; set; }
    public string ResultText { get; set; } = "";
}

public class EventDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<EventChoice> Choices { get; set; } = new();
}
