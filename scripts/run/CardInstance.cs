using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Wraps a CardDefinition by id rather than embedding it, so a future save
// format can reference instance ids without breaking when balance data
// changes (hollowdeck.md risk #3).
public class CardInstance
{
    public string DefinitionId;
    public CardDefinition Definition;

    public CardInstance(CardDefinition definition)
    {
        Definition = definition;
        DefinitionId = definition.Id;
    }
}
