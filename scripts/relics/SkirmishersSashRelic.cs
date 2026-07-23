using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// Whenever you play a Skill card, gain Block.
public class SkirmishersSashRelic : RelicBehavior
{
    public SkirmishersSashRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCardPlayed(RelicContext ctx, CardInstance card)
    {
        if (card.Definition.Type != CardType.Skill) return;
        Apply(ctx, new EffectSpec { Action = "gain_block", Amount = Param("amount", 1) },
            new List<Combatant> { ctx.Player });
    }
}
