using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// Winning a fight heals a small amount of HP.
public class SecondWindRelic : RelicBehavior
{
    public SecondWindRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCombatEnd(RelicContext ctx, CombatOutcome outcome)
    {
        if (outcome != CombatOutcome.Win) return;
        Apply(ctx, new EffectSpec { Action = "heal", Amount = Param("amount", 6) },
            new List<Combatant> { ctx.Player });
    }
}
