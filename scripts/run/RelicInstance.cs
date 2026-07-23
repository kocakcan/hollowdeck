using Hollowdeck.Data;
using Hollowdeck.Relics;

namespace Hollowdeck.Run;

public class RelicInstance
{
    public RelicDefinition Definition { get; }
    public RelicBehavior Behavior { get; }

    public RelicInstance(RelicDefinition definition)
    {
        Definition = definition;
        Behavior = RelicRegistry.Create(definition);
    }
}
