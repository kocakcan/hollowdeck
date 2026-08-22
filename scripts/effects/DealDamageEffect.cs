using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class DealDamageEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        int amount = DamageMath.ComputeOutgoing(ctx.AmountFor(spec), ctx.Source);

        foreach (var target in ctx.Targets)
        {
            int targetAmount = DamageMath.ApplyIncoming(amount, target);

            int absorbedByBlock = System.Math.Min(target.Block, targetAmount);
            target.Block -= absorbedByBlock;
            int unblocked = targetAmount - absorbedByBlock;
            target.CurrentHp -= unblocked;

            // The one place Block eats anything, which is why the counter the
            // view layer diffs is incremented here rather than inferred there.
            // See Combatant.HitsAbsorbed: falling Block on its own cannot tell
            // an absorbed hit from an expired one.
            //
            // Gated on `absorbedByBlock` rather than on Block having eaten the
            // hit *whole*, which is the tempting version and puts one
            // distinction in two places. A partly absorbed hit moves this pair
            // and the one below, and it is the reader that separates them:
            // CombatScreen.IsAbsorbedHit demands hpDelta == 0, so a partial
            // absorb takes the ordinary hit-reaction arm and draws exactly one
            // blade rather than two.
            if (absorbedByBlock > 0)
            {
                target.HitsAbsorbed++;
                target.LastAbsorbedAttacker = ctx.Source;
            }

            // And the other half of that argument: the one place a hit reaches
            // HP, so the view layer can tell an attack from every other way HP
            // falls. See Combatant.HitsTaken - a blade is drawn from the
            // attacker to the target, and a Poison tick has no attacker to draw
            // it from.
            //
            // Gated on `unblocked` rather than on targetAmount for the same
            // reason Plating is below, and the reason is now only about *which*
            // beat rather than about whether there is one: a hit Block ate
            // whole is an absorption, so it moves the pair above and plays the
            // ward burst. Both pairs carry a blade; what separates them is that
            // this one also cost HP.
            if (unblocked > 0)
            {
                target.HitsTaken++;
                target.LastAttacker = ctx.Source;
            }

            // Thorns bills the attacker for the hit, and it deliberately fires
            // on the *attempt* rather than on damage getting through - a hit
            // fully eaten by Block still gets pricked, which is what makes
            // Thorns an answer to a multi-hit deck rather than a worse Block.
            //
            // It subtracts HP directly and must never route back through
            // EffectRegistry. That is the same discipline SimpleHookEffectRelic
            // already documents for the thorned_carapace relic: a retaliation
            // that resolves as a real deal_damage would re-enter this method and
            // retaliate against its own retaliation. Direct subtraction cannot,
            // whatever statuses either side is holding.
            //
            // Consequence worth knowing: CombatManager.ExecuteEffect raises
            // damage popups by diffing CurrentHp over `targets` only, so Thorns
            // damage to the source shows on the HP bar without a floating
            // number. Cosmetic, and it belongs with Phase 11's feel work.
            if (targetAmount > 0)
            {
                int thorns = target.GetStatus(StatusType.Thorns);
                if (thorns > 0) ctx.Source.CurrentHp -= thorns;
            }

            // Plating spends a stack per hit that actually lands. Gated on
            // unblocked rather than on targetAmount so the Block it granted at
            // turn start does not also pay for its own erosion - otherwise a
            // Plating holder loses a stack to every hit its own Block ate, and
            // the status would be strictly worse than the Metallicize it sits
            // beside in ApplyTurnStartGrants.
            if (unblocked > 0) target.DecayStatus(StatusType.Plating);
        }
    }
}
