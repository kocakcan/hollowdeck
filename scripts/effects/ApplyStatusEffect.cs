using System;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

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
            target.AddStatus(status, spec.Amount);
        }
    }
}
