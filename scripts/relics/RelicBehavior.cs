using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// The seven hooks a relic can fire on. Hooks default to no-ops so a subclass
// only overrides what it cares about.
//
// Unlike IEffect (stateless, shared singleton instances), each RelicInstance
// gets its OWN RelicBehavior object via RelicRegistry's factory, because a
// relic's firing limits are per-instance state ("once per combat" has to
// remember).
//
// Subclassing this is the escape hatch, not the normal path - every relic in
// the game is a data row driven by SimpleHookEffectRelic, and nothing needs a
// bespoke class today. It stays for the same reason IScriptedEffect does: to
// prove the seam exists for the mechanic that genuinely doesn't decompose.
public abstract class RelicBehavior
{
    protected RelicDefinition Definition { get; }

    protected RelicBehavior(RelicDefinition definition)
    {
        Definition = definition;
    }

    protected void Apply(RelicContext ctx, EffectSpec spec, List<Combatant> targets)
    {
        EffectRegistry.Execute(new EffectContext { Source = ctx.Player, Targets = targets, Combat = ctx.Combat }, spec);
    }

    public virtual void OnCombatStart(RelicContext ctx) { }
    public virtual void OnTurnStart(RelicContext ctx) { }
    public virtual void OnTurnEnd(RelicContext ctx) { }
    public virtual void OnCardPlayed(RelicContext ctx, CardInstance card) { }
    public virtual void OnDamageDealt(RelicContext ctx, Combatant target, int amount) { }
    public virtual void OnDamageTaken(RelicContext ctx, Combatant attacker, int amount) { }
    public virtual void OnCombatEnd(RelicContext ctx, CombatOutcome outcome) { }
}
