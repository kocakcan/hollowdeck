using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// Whenever you take damage, retaliate against the attacker.
public class ThornedCarapaceRelic : RelicBehavior
{
    public ThornedCarapaceRelic(RelicDefinition definition) : base(definition) { }

    public override void OnDamageTaken(RelicContext ctx, Combatant attacker, int amount)
    {
        Apply(ctx, new EffectSpec { Action = "deal_damage", Amount = Param("amount", 2) },
            new List<Combatant> { attacker });
    }
}
