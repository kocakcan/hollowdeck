using Hollowdeck.Combat;

namespace Hollowdeck.Relics;

public class RelicContext
{
    public required CombatManager Combat { get; init; }
    public required PlayerCombatant Player { get; init; }
}
