using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// Every 3rd card played in a turn deals damage to the first alive enemy.
public class MomentumTokenRelic : RelicBehavior
{
    private int _cardsThisTurn;

    public MomentumTokenRelic(RelicDefinition definition) : base(definition) { }

    public override void OnTurnStart(RelicContext ctx) => _cardsThisTurn = 0;

    public override void OnCardPlayed(RelicContext ctx, CardInstance card)
    {
        _cardsThisTurn++;
        if (_cardsThisTurn % 3 != 0) return;

        var target = ctx.Combat.Enemies.FirstOrDefault(e => !e.IsDead);
        if (target is null) return;

        Apply(ctx, new EffectSpec { Action = "deal_damage", Amount = Param("amount", 4) },
            new List<Combatant> { target });
    }
}
