using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// Data-only relic: fires Definition.Effect against the player whenever
// Definition.Hook matches the firing hook. Covers the relics that are just
// "apply this EffectSpec on this hook, unconditionally" - no per-instance
// state, no bespoke class needed.
public class SimpleHookEffectRelic : RelicBehavior
{
    public SimpleHookEffectRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCombatStart(RelicContext ctx) => FireIfHook(ctx, "OnCombatStart");
    public override void OnTurnStart(RelicContext ctx) => FireIfHook(ctx, "OnTurnStart");

    private void FireIfHook(RelicContext ctx, string hook)
    {
        if (Definition.Hook != hook || Definition.Effect is null) return;
        Apply(ctx, Definition.Effect, new List<Combatant> { ctx.Player });
    }
}
