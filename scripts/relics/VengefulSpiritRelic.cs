using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// Whenever a hit you deal kills its target, heal a little. OnDamageDealt
// fires after the damage is already applied (see CombatManager.ExecuteEffect),
// so target.IsDead is already accurate here.
public class VengefulSpiritRelic : RelicBehavior
{
    public VengefulSpiritRelic(RelicDefinition definition) : base(definition) { }

    public override void OnDamageDealt(RelicContext ctx, Combatant target, int amount)
    {
        if (!target.IsDead) return;
        Apply(ctx, new EffectSpec { Action = "heal", Amount = Param("amount", 3) },
            new List<Combatant> { ctx.Player });
    }
}
