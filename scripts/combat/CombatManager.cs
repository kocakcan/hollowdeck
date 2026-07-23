using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Relics;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public enum CombatState
{
    Start,
    PlayerTurn,
    AwaitingTarget,
    ResolvingCard,
    EnemyTurn,
    ResolvingEnemyIntent,
    CombatEnd,
}

public enum CombatOutcome { None, Win, Lose }

public partial class CombatManager : Node
{
    public static CombatManager Instance { get; private set; } = null!;

    // Plain C# events, not Godot [Signal]s - CombatManager is only ever
    // consumed from other C# scripts in-process, so there's no need to pay
    // Godot's Variant marshalling for enum payloads.
    public event Action<CombatState>? StateChanged;
    public event Action? HandChanged;
    public event Action? CombatantsChanged;
    public event Action? PotionsChanged;

    public CombatState State { get; private set; } = CombatState.Start;
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.None;

    public PlayerCombatant Player { get; private set; } = null!;
    public List<EnemyCombatant> Enemies { get; private set; } = new();
    public List<RelicInstance> Relics { get; private set; } = new();

    private PotionInstance? _pendingPotion;
    private int _enemyTurnIndex;
    private List<EnemyCombatant> _enemyTurnOrder = new();

    public override void _Ready()
    {
        Instance = this;
    }

    public void StartCombat(PlayerCombatant player, List<EnemyCombatant> enemies, List<RelicInstance> relics)
    {
        Player = player;
        Enemies = enemies;
        Relics = relics;
        State = CombatState.Start;
        Outcome = CombatOutcome.None;

        Player.Piles.Shuffle(Player.Piles.DrawPile);

        var ctx = MakeRelicContext();
        foreach (var relic in Relics) relic.Behavior.OnCombatStart(ctx);

        foreach (var enemy in Enemies)
        {
            AdvanceEnemyIntent(enemy);
        }
        CombatantsChanged?.Invoke();

        BeginPlayerTurn();
    }

    private RelicContext MakeRelicContext() => new() { Combat = this, Player = Player };

    private void BeginPlayerTurn()
    {
        Player.CurrentEnergy = Player.MaxEnergy;
        Player.Piles.DrawHand(5);

        var ctx = MakeRelicContext();
        foreach (var relic in Relics) relic.Behavior.OnTurnStart(ctx);

        HandChanged?.Invoke();
        TransitionTo(CombatState.PlayerTurn);
    }

    private void TransitionTo(CombatState next)
    {
        GD.Print($"[Combat] {State} -> {next}");
        State = next;
        StateChanged?.Invoke(next);
    }

    // Cards are targeted by dragging directly onto an enemy (CardView hit-
    // tests EnemyView.Instances on release and passes the result here), not
    // via the click-based AwaitingTarget flow potions still use below - a
    // SingleEnemy card dropped with no enemy under it is simply rejected so
    // the caller snaps it back to hand. Returns true only if the card
    // resolved synchronously in this call (i.e. it actually left the hand).
    public bool TryPlayCard(CardInstance card, EnemyCombatant? explicitTarget = null)
    {
        if (State != CombatState.PlayerTurn) return false;
        if (card.Definition.Cost > Player.CurrentEnergy) return false;
        if (card.Definition.Target == CardTargetType.SingleEnemy && explicitTarget is null) return false;

        ResolveCard(card, ResolveTargets(card.Definition.Target, explicitTarget));
        return true;
    }

    public bool TryUsePotion(PotionInstance potion)
    {
        if (State != CombatState.PlayerTurn) return false;

        if (potion.Definition.Target == CardTargetType.SingleEnemy)
        {
            _pendingPotion = potion;
            TransitionTo(CombatState.AwaitingTarget);
            return false;
        }

        ResolvePotion(potion, ResolveTargets(potion.Definition.Target, null));
        return true;
    }

    public void CancelTargeting()
    {
        if (State != CombatState.AwaitingTarget) return;
        _pendingPotion = null;
        TransitionTo(CombatState.PlayerTurn);
    }

    // Only potions use click-to-target now - cards resolve directly through
    // TryPlayCard's explicitTarget parameter via drag-and-drop.
    public void TryTargetEnemy(EnemyCombatant enemy)
    {
        if (State != CombatState.AwaitingTarget || _pendingPotion is null) return;
        var potion = _pendingPotion;
        _pendingPotion = null;
        ResolvePotion(potion, ResolveTargets(potion.Definition.Target, enemy));
    }

    private List<Combatant> ResolveTargets(CardTargetType targetType, EnemyCombatant? explicitTarget)
    {
        return targetType switch
        {
            CardTargetType.SingleEnemy => new List<Combatant> { explicitTarget! },
            CardTargetType.AllEnemies => Enemies.Cast<Combatant>().ToList(),
            CardTargetType.Self => new List<Combatant> { Player },
            CardTargetType.None => new List<Combatant>(),
            _ => new List<Combatant>(),
        };
    }

    // Wraps EffectRegistry.Execute so deal_damage effects also fire the
    // OnDamageDealt/OnDamageTaken relic hooks, computed from actual HP lost
    // (post-block), and always attributed to the Player since relics in
    // this game are always player-owned.
    private void ExecuteEffect(EffectSpec spec, Combatant source, List<Combatant> targets)
    {
        if (spec.Action != "deal_damage")
        {
            EffectRegistry.Execute(new EffectContext { Source = source, Targets = targets, Combat = this }, spec);
            return;
        }

        var before = targets.ToDictionary(t => t, t => t.CurrentHp);
        EffectRegistry.Execute(new EffectContext { Source = source, Targets = targets, Combat = this }, spec);

        var relicCtx = MakeRelicContext();
        foreach (var target in targets)
        {
            int dealt = before[target] - target.CurrentHp;
            if (dealt <= 0) continue;
            if (source == Player) foreach (var relic in Relics) relic.Behavior.OnDamageDealt(relicCtx, target, dealt);
            if (target == Player) foreach (var relic in Relics) relic.Behavior.OnDamageTaken(relicCtx, source, dealt);
        }
    }

    private void ResolveCard(CardInstance card, List<Combatant> targets)
    {
        TransitionTo(CombatState.ResolvingCard);

        Player.CurrentEnergy -= card.Definition.Cost;
        Player.Piles.Hand.Remove(card);
        if (card.Definition.Exhaust) Player.Piles.Exhaust.Add(card);
        else Player.Piles.Discard.Add(card);
        HandChanged?.Invoke();

        foreach (var effect in card.Definition.Effects)
        {
            var scopedTargets = effect.Scope == EffectScope.Self
                ? new List<Combatant> { Player }
                : targets;
            ExecuteEffect(effect, Player, scopedTargets);
        }

        var relicCtx = MakeRelicContext();
        foreach (var relic in Relics) relic.Behavior.OnCardPlayed(relicCtx, card);

        Enemies.RemoveAll(e => e.IsDead);
        CombatantsChanged?.Invoke();

        if (Enemies.Count == 0)
        {
            EndCombat(CombatOutcome.Win);
            return;
        }

        TransitionTo(CombatState.PlayerTurn);
    }

    private void ResolvePotion(PotionInstance potion, List<Combatant> targets)
    {
        TransitionTo(CombatState.ResolvingCard);

        RunState.Potions.Remove(potion);
        PotionsChanged?.Invoke();

        foreach (var effect in potion.Definition.Effects)
        {
            var scopedTargets = effect.Scope == EffectScope.Self
                ? new List<Combatant> { Player }
                : targets;
            ExecuteEffect(effect, Player, scopedTargets);
        }

        Enemies.RemoveAll(e => e.IsDead);
        CombatantsChanged?.Invoke();

        if (Enemies.Count == 0)
        {
            EndCombat(CombatOutcome.Win);
            return;
        }

        TransitionTo(CombatState.PlayerTurn);
    }

    public void TryEndTurn()
    {
        if (State != CombatState.PlayerTurn) return;

        var relicCtx = MakeRelicContext();
        foreach (var relic in Relics) relic.Behavior.OnTurnEnd(relicCtx);

        Player.Piles.DiscardHand();
        Player.DecayStatus(StatusType.Vulnerable);
        Player.DecayStatus(StatusType.Weak);

        _enemyTurnOrder = new List<EnemyCombatant>(Enemies);
        _enemyTurnIndex = 0;
        TransitionTo(CombatState.EnemyTurn);
        ResolveNextEnemyTurn();
    }

    private void ResolveNextEnemyTurn()
    {
        if (_enemyTurnIndex >= _enemyTurnOrder.Count)
        {
            // Block clears here, not in BeginPlayerTurn itself - it must
            // persist through the enemy's turn (so it can absorb their
            // attacks) and must NOT be wiped by the very first call to
            // BeginPlayerTurn from StartCombat, which runs right after
            // OnCombatStart relics (e.g. Anchor Stone) grant their bonus.
            Player.Block = 0;
            BeginPlayerTurn();
            return;
        }

        var enemy = _enemyTurnOrder[_enemyTurnIndex];
        _enemyTurnIndex++;

        if (enemy.IsDead)
        {
            // Died earlier this round (e.g. a relic retaliation kill) -
            // nothing left to resolve for it.
            ResolveNextEnemyTurn();
            return;
        }

        TransitionTo(CombatState.ResolvingEnemyIntent);

        enemy.Block = 0;
        var move = enemy.CurrentMove!;
        var playerTargets = new List<Combatant> { Player };
        foreach (var effect in move.Effects)
        {
            var scopedTargets = effect.Scope == EffectScope.Self
                ? new List<Combatant> { enemy }
                : playerTargets;
            ExecuteEffect(effect, enemy, scopedTargets);
        }

        if (Player.IsDead)
        {
            EndCombat(CombatOutcome.Lose);
            return;
        }

        Enemies.RemoveAll(e => e.IsDead);
        CombatantsChanged?.Invoke();

        if (Enemies.Count == 0)
        {
            EndCombat(CombatOutcome.Win);
            return;
        }

        if (!enemy.IsDead)
        {
            enemy.DecayStatus(StatusType.Vulnerable);
            enemy.DecayStatus(StatusType.Weak);
            AdvanceEnemyIntent(enemy);
            CombatantsChanged?.Invoke();
        }

        ResolveNextEnemyTurn();
    }

    private void AdvanceEnemyIntent(EnemyCombatant enemy)
    {
        enemy.LastMove = enemy.CurrentMove;
        enemy.CurrentMove = enemy.IntentPicker.PickNext(enemy);
    }

    private void EndCombat(CombatOutcome outcome)
    {
        Outcome = outcome;
        var relicCtx = MakeRelicContext();
        foreach (var relic in Relics) relic.Behavior.OnCombatEnd(relicCtx, outcome);
        TransitionTo(CombatState.CombatEnd);
    }
}
