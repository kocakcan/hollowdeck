using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// At the end of your turn, if you have unspent energy, gain Block.
public class FrugalSatchelRelic : RelicBehavior
{
    public FrugalSatchelRelic(RelicDefinition definition) : base(definition) { }

    public override void OnTurnEnd(RelicContext ctx)
    {
        if (ctx.Player.CurrentEnergy < 2) return;
        Apply(ctx, new EffectSpec { Action = "gain_block", Amount = Param("amount", 2) },
            new List<Combatant> { ctx.Player });
    }
}
