using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Relics;

// The data-only relic path, and since the relic-hook pass the ONLY one: every
// relic in data/relics/relics.json is a row here rather than a C# class.
//
// A relic is "fires Definition.Effect on Definition.Hook", narrowed by three
// optional pieces of vocabulary declared in RelicTrigger.cs - a Target
// selector, a Condition gate, and a Limit on how often it pays out. Together
// those cover all seven hooks; the eleven bespoke subclasses this replaced
// were each some combination of them.
//
// Note Apply() goes through EffectRegistry.Execute directly, NOT through
// CombatManager.ExecuteEffect. That is what stops a relic that deals damage
// (Thorned Carapace) from re-entering OnDamageDealt/OnDamageTaken and
// retaliating against its own retaliation. Routing it through ExecuteEffect
// to "make relic damage count" would build exactly that loop.
public class SimpleHookEffectRelic : RelicBehavior
{
    private bool _firedThisTurn;
    private bool _firedThisCombat;
    private int _firingsThisTurn;

    public SimpleHookEffectRelic(RelicDefinition definition) : base(definition) { }

    public override void OnCombatStart(RelicContext ctx)
    {
        _firedThisCombat = false;
        ResetTurnLimits();
        Fire(ctx, "OnCombatStart");
    }

    // The only reset point for the per-turn limits, and it fires on the
    // PLAYER's turn only (CombatManager.BeginPlayerTurn) - so a hit taken
    // during the enemy turn counts against the player turn that follows it.
    // Bulwark Charm and Momentum Token both behaved this way as classes.
    public override void OnTurnStart(RelicContext ctx)
    {
        ResetTurnLimits();
        Fire(ctx, "OnTurnStart");
    }

    public override void OnTurnEnd(RelicContext ctx) => Fire(ctx, "OnTurnEnd");

    public override void OnCardPlayed(RelicContext ctx, CardInstance card) =>
        Fire(ctx, "OnCardPlayed", card: card);

    public override void OnDamageDealt(RelicContext ctx, Combatant target, int amount) =>
        Fire(ctx, "OnDamageDealt", damaged: target);

    public override void OnDamageTaken(RelicContext ctx, Combatant attacker, int amount) =>
        Fire(ctx, "OnDamageTaken", attacker: attacker);

    public override void OnCombatEnd(RelicContext ctx, CombatOutcome outcome) =>
        Fire(ctx, "OnCombatEnd", outcome: outcome.ToString());

    private void ResetTurnLimits()
    {
        _firedThisTurn = false;
        _firingsThisTurn = 0;
    }

    private void Fire(RelicContext ctx, string hook, CardInstance? card = null,
        Combatant? attacker = null, Combatant? damaged = null, string? outcome = null)
    {
        if (Definition.Hook != hook || Definition.Effect is null) return;
        if (!ConditionMet(ctx, card, damaged, outcome)) return;
        if (!LimitAllows()) return;

        var targets = ResolveTargets(ctx, attacker);
        if (targets.Count == 0) return;
        Apply(ctx, Definition.Effect, targets);
    }

    private bool ConditionMet(RelicContext ctx, CardInstance? card, Combatant? damaged, string? outcome)
    {
        var condition = Definition.Condition;
        if (condition is null) return true;

        if (condition.CardType is { } cardType && card?.Definition.Type != cardType) return false;
        if (condition.Outcome is { } wanted && outcome != wanted) return false;
        if (condition.MinEnergy is { } energy && ctx.Player.CurrentEnergy < energy) return false;
        // Strictly above the threshold, so minHpPercent 50 means "more than
        // half", which is how Scavenger's Charm has always been worded.
        if (condition.MinHpPercent is { } percent && ctx.Player.CurrentHp * 100 <= ctx.Player.MaxHp * percent) return false;
        if (condition.TargetKilled && damaged?.IsDead != true) return false;

        return true;
    }

    // Checked after the condition, so a limited relic only spends its
    // allowance on firings that would actually have paid out.
    private bool LimitAllows()
    {
        var limit = Definition.Limit;
        if (limit is null) return true;

        if (limit.OncePerCombat)
        {
            if (_firedThisCombat) return false;
            _firedThisCombat = true;
        }

        if (limit.OncePerTurn)
        {
            if (_firedThisTurn) return false;
            _firedThisTurn = true;
        }

        if (limit.EveryNth > 0)
        {
            _firingsThisTurn++;
            if (_firingsThisTurn % limit.EveryNth != 0) return false;
        }

        return true;
    }

    private List<Combatant> ResolveTargets(RelicContext ctx, Combatant? attacker)
    {
        switch (Definition.Target)
        {
            case RelicTarget.Attacker:
                return attacker is null ? new List<Combatant>() : new List<Combatant> { attacker };
            case RelicTarget.FirstEnemy:
                var first = ctx.Combat.Enemies.FirstOrDefault(e => !e.IsDead);
                return first is null ? new List<Combatant>() : new List<Combatant> { first };
            case RelicTarget.RandomEnemy:
                var alive = ctx.Combat.Enemies.Where(e => !e.IsDead).ToList();
                // RngStreams.Combat, never a fresh Random - risk 2, a relic
                // that borrowed its own stream would desync seeded runs.
                return alive.Count == 0
                    ? new List<Combatant>()
                    : new List<Combatant> { alive[RngStreams.Combat.Next(alive.Count)] };
            case RelicTarget.AllEnemies:
                return ctx.Combat.Enemies.Where(e => !e.IsDead).Cast<Combatant>().ToList();
            default:
                return new List<Combatant> { ctx.Player };
        }
    }
}
