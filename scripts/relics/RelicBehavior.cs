using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// Every relic implements this. Hooks default to no-ops so a relic only
// overrides what it actually cares about. Unlike IEffect (stateless, shared
// singleton instances), each RelicInstance gets its OWN RelicBehavior object
// via RelicRegistry's factory, since several relics below hold per-instance
// counters/flags.
public abstract class RelicBehavior
{
    protected RelicDefinition Definition { get; }

    protected RelicBehavior(RelicDefinition definition)
    {
        Definition = definition;
    }

    protected int Param(string key, int fallback = 0) => Definition.Params.GetValueOrDefault(key, fallback);

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
