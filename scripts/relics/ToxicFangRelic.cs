using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// Whenever you play an Attack card, apply Poison to a random alive enemy.
public class ToxicFangRelic : RelicBehavior
{
    public ToxicFangRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCardPlayed(RelicContext ctx, CardInstance card)
    {
        if (card.Definition.Type != CardType.Attack) return;

        var alive = ctx.Combat.Enemies.Where(e => !e.IsDead).ToList();
        if (alive.Count == 0) return;

        var target = alive[RngStreams.Combat.Next(alive.Count)];
        Apply(ctx, new EffectSpec { Action = "apply_status", Status = "Poison", Amount = Param("amount", 1) },
            new List<Combatant> { target });
    }
}
