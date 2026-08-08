using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

// Takes an enemy out of the fight *alive*. The distinction is the whole point:
// a corpse pays out - a kill in RunScore's tally, and the fight is closer to
// won - and a runaway pays out nothing. That is what lets a fight be lost by
// playing correctly but slowly, which is a shape the game could not express
// while the only exit from Enemies was death.
//
// Marks rather than removes. The strip happens one pass later in
// CombatManager.ResolveDeathsAndSettle, so an enemy whose escape move also
// steals gold cannot vanish part-way through its own remaining specs - and so
// the removal keeps living in the one place that also decides Lose vs Win.
//
// Self-scoped by nature: an enemy can only remove itself. Reading ctx.Source
// rather than ctx.Targets is what makes a mis-authored `scope: Target` a
// refusal here instead of an enemy deleting the player.
public class EscapeEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (ctx.Source is not EnemyCombatant enemy)
        {
            GD.PushError("EscapeEffect: only an enemy can escape a fight - the player "
                + "leaving is what losing is. Check the card/potion authoring this.");
            return;
        }

        // Death wins over escape, and the rule is stated here rather than left
        // to the order of the two RemoveAll lines in ResolveDeaths. An escape
        // move that also deals damage - the obvious hit-and-run thief, which
        // nothing authors yet - can be killed by the player's Thorns *inside*
        // ExecuteEffect for its own damage spec, and this spec then runs on a
        // corpse. Without the guard, the HasEscaped sweep claims the body
        // before the IsDead sweep does: EnemiesKilled never counts a kill the
        // player earned, RunScore loses the points, and CombatScreen plays the
        // runaway tween over a death. Ordering alone would fix it today and
        // silently un-fix it the next time those two lines are reordered.
        if (enemy.IsDead) return;

        enemy.HasEscaped = true;
    }
}
