using System;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.UI;

namespace Hollowdeck.Effects;

public class ApplyStatusEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (spec.Status is null || !Enum.TryParse<StatusType>(spec.Status, true, out var status))
        {
            GD.PushError($"ApplyStatusEffect: unknown status '{spec.Status}'");
            return;
        }

        foreach (var target in ctx.Targets)
        {
            // Artifact refuses one *application*, not one stack of the debuff:
            // a spec applying Vulnerable 3 costs a single Artifact and lands
            // nothing. Per target, so an AllEnemies debuff into a room where one
            // enemy holds Artifact still lands on the others - it is that
            // enemy's ward, not a property of the card.
            //
            // Gated on StatusRow.IsDebuff rather than on a list local to this
            // file, so there is exactly one answer to "is this a debuff" and the
            // icon tint, the EnemyView badge and this gate cannot disagree. The
            // cost of that is real and worth stating: a new debuff added to
            // StatusType but not to IsDebuff walks straight past Artifact, and
            // nothing here would throw. EffectSmokeTest drives this over the
            // whole enum for that reason.
            if (StatusRow.IsDebuff(status) && target.GetStatus(StatusType.Artifact) > 0)
            {
                target.DecayStatus(StatusType.Artifact);
                continue;
            }

            target.AddStatus(status, ctx.AmountFor(spec));
        }
    }
}
