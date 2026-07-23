using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// Whenever you deal damage, heal a small amount of HP.
public class VampireFangRelic : RelicBehavior
{
    public VampireFangRelic(RelicDefinition definition) : base(definition) { }

    public override void OnDamageDealt(RelicContext ctx, Combatant target, int amount)
    {
        Apply(ctx, new EffectSpec { Action = "heal", Amount = Param("amount", 1) },
            new List<Combatant> { ctx.Player });
    }
}
