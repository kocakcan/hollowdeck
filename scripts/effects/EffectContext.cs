using System.Collections.Generic;
using Hollowdeck.Combat;

namespace Hollowdeck.Effects;

public class EffectContext
{
    public required Combatant Source { get; init; }
    public required IReadOnlyList<Combatant> Targets { get; init; }
    public required CombatManager Combat { get; init; }
}
