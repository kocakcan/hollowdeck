using System.Collections.Generic;

namespace Hollowdeck.Data;

// One thing an event choice does. Keys into EventOutcomeRegistry - the same
// string-key-into-a-code-side-registry idiom as EffectSpec.Action, and
// deliberately not a C# enum so new outcomes stay data-addressable.
public class EventOutcomeSpec
{
    public string Outcome { get; set; } = "";
    public int Amount { get; set; }

    // Consumed only by the "gamble" outcome, which picks one of these at
    // random and resolves it. Empty for every other outcome.
    public List<EventOutcomeSpec> Alternatives { get; set; } = new();

    // Consumed only by "add_card". The spec is a flat union on purpose, the
    // same way Alternatives is - one shape that every outcome deserializes
    // into keeps new outcomes data-addressable without a schema per key.
    public string CardId { get; set; } = "";
}

public class EventChoice
{
    public string Label { get; set; } = "";

    // The single-outcome shorthand. Every event authored before compound
    // choices existed uses this form and still loads unchanged, which is why
    // it stayed rather than being migrated - deserialization is tolerant of
    // missing fields, so both shapes come out of the same schema.
    public string Outcome { get; set; } = "";
    public int Amount { get; set; }
    public List<EventOutcomeSpec> Alternatives { get; set; } = new();

    // The compound form: an ordered list, resolved front to back. This is
    // what lets one choice be "gain a relic, and lose 10 max HP" - a cost
    // attached to a reward, which is most of what makes an event a decision
    // rather than a free pick.
    public List<EventOutcomeSpec> Outcomes { get; set; } = new();

    public string ResultText { get; set; } = "";

    // The one list every consumer reads, so nothing downstream has to know
    // which of the two authoring forms a given choice used.
    public List<EventOutcomeSpec> Specs =>
        Outcomes.Count > 0
            ? Outcomes
            : new List<EventOutcomeSpec>
            {
                new() { Outcome = Outcome, Amount = Amount, Alternatives = Alternatives },
            };
}

public class EventDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<EventChoice> Choices { get; set; } = new();
}
