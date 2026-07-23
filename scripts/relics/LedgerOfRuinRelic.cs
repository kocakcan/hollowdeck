using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// The first Attack card you play each combat permanently grants Strength
// for the rest of the fight.
public class LedgerOfRuinRelic : RelicBehavior
{
    private bool _usedThisCombat;

    public LedgerOfRuinRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCombatStart(RelicContext ctx) => _usedThisCombat = false;

    public override void OnCardPlayed(RelicContext ctx, CardInstance card)
    {
        if (_usedThisCombat || card.Definition.Type != CardType.Attack) return;
        _usedThisCombat = true;
        Apply(ctx, new EffectSpec { Action = "apply_status", Status = "Strength", Amount = Param("amount", 1) },
            new List<Combatant> { ctx.Player });
    }
}
