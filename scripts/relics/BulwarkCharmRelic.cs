using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// The first time you take unblocked damage each turn, gain Block.
public class BulwarkCharmRelic : RelicBehavior
{
    private bool _triggeredThisTurn;

    public BulwarkCharmRelic(RelicDefinition definition) : base(definition) { }

    public override void OnTurnStart(RelicContext ctx) => _triggeredThisTurn = false;

    public override void OnDamageTaken(RelicContext ctx, Combatant attacker, int amount)
    {
        if (_triggeredThisTurn) return;
        _triggeredThisTurn = true;
        Apply(ctx, new EffectSpec { Action = "gain_block", Amount = Param("amount", 4) },
            new List<Combatant> { ctx.Player });
    }
}
