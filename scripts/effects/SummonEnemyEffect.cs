using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

// Brings an enemy into a fight already in progress. The primitive behind the
// behaviour half of ROADMAP Phase 8: minions, escorts and splitting are all
// downstream of CombatManager.Enemies being able to grow at all, which it
// could not until this existed.
//
// Resolves through ctx.Combat.SummonEnemy rather than touching Enemies itself,
// the same way AddCardEffect goes through ctx.Combat.Player rather than casting
// ctx.Source. The manager owns the roster cap, the newcomer's opening intent
// and the CombatantsChanged the screen rebuilds from; an effect reaching past
// it would have to restate all three.
//
// Scope is meaningless here - a summon joins the enemy side regardless of who
// or what the move was aimed at - which is the shape gain_gold and add_card
// already have.
public class SummonEnemyEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (spec.EnemyId is not { Length: > 0 } id)
        {
            GD.PushError("SummonEnemyEffect: spec has no enemyId - the move authoring it "
                + "will silently do nothing.");
            return;
        }

        // No Math.Max(1, ...) clamp, for AddCardEffect's reason: an authored
        // amount of 0 summons nothing, and a clamp would turn an authoring bug
        // into a move that works by accident. Phase4ContentSmokeTest audits
        // every authored summon_enemy spec for a count instead.
        ctx.Combat.SummonEnemy(id, ctx.AmountFor(spec));
    }
}
