using Hollowdeck.Data;

namespace Hollowdeck.Run;

public class PotionInstance
{
    public string DefinitionId;
    public PotionDefinition Definition;

    public PotionInstance(PotionDefinition definition)
    {
        Definition = definition;
        DefinitionId = definition.Id;
    }
}
