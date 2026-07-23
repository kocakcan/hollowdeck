using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// Winning a fight with more than half max HP remaining grants bonus gold.
public class ScavengersCharmRelic : RelicBehavior
{
    public ScavengersCharmRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCombatEnd(RelicContext ctx, CombatOutcome outcome)
    {
        if (outcome != CombatOutcome.Win) return;
        if (ctx.Player.CurrentHp * 2 <= ctx.Player.MaxHp) return;
        RunState.Gold += Param("amount", 5);
    }
}
