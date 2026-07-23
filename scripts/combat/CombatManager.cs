using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
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

    public CombatState State { get; private set; } = CombatState.Start;
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.None;

    public PlayerCombatant Player { get; private set; } = null!;
    public List<EnemyCombatant> Enemies { get; private set; } = new();

    private CardInstance? _pendingCard;
    private int _enemyTurnIndex;

    public override void _Ready()
    {
        Instance = this;
    }

    public void StartCombat(PlayerCombatant player, List<EnemyCombatant> enemies)
    {
        Player = player;
        Enemies = enemies;
        State = CombatState.Start;
        Outcome = CombatOutcome.None;

        Player.Piles.Shuffle(Player.Piles.DrawPile);
        // OnCombatStart relic-trigger seam - no relics yet (Phase 2).

        foreach (var enemy in Enemies)
        {
            AdvanceEnemyIntent(enemy);
        }
        CombatantsChanged?.Invoke();

        BeginPlayerTurn();
    }

    private void BeginPlayerTurn()
    {
        Player.CurrentEnergy = Player.MaxEnergy;
        Player.Piles.DrawHand(5);
        HandChanged?.Invoke();
        TransitionTo(CombatState.PlayerTurn);
    }

    private void TransitionTo(CombatState next)
    {
        GD.Print($"[Combat] {State} -> {next}");
        State = next;
        StateChanged?.Invoke(next);
    }

    // Returns true only if the card resolved synchronously in this call (i.e.
    // it actually left the hand) - callers (CardView) use this to decide
    // whether to snap the dragged card back to its hand position. A card
    // entering AwaitingTarget hasn't left the hand yet (ResolveCard runs
    // later, from TryTargetEnemy), so it also snaps back for now; nothing
    // else needs to track "the visual" separately from _pendingCard.
    public bool TryPlayCard(CardInstance card)
    {
        if (State != CombatState.PlayerTurn) return false;
        if (card.Definition.Cost > Player.CurrentEnergy) return false;

        if (card.Definition.Target == CardTargetType.SingleEnemy)
        {
            _pendingCard = card;
            TransitionTo(CombatState.AwaitingTarget);
            return false;
        }

        ResolveCard(card, ResolveTargets(card.Definition.Target, null));
        return true;
    }

    public void CancelTargeting()
    {
        if (State != CombatState.AwaitingTarget) return;
        _pendingCard = null;
        TransitionTo(CombatState.PlayerTurn);
    }

    public void TryTargetEnemy(EnemyCombatant enemy)
    {
        if (State != CombatState.AwaitingTarget || _pendingCard is null) return;
        var card = _pendingCard;
        _pendingCard = null;
        ResolveCard(card, ResolveTargets(card.Definition.Target, enemy));
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
            EffectRegistry.Execute(new EffectContext { Source = Player, Targets = scopedTargets, Combat = this }, effect);
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

        Player.Piles.DiscardHand();
        Player.DecayStatus(StatusType.Vulnerable);
        Player.DecayStatus(StatusType.Weak);

        _enemyTurnIndex = 0;
        TransitionTo(CombatState.EnemyTurn);
        ResolveNextEnemyTurn();
    }

    private void ResolveNextEnemyTurn()
    {
        if (_enemyTurnIndex >= Enemies.Count)
        {
            BeginPlayerTurn();
            return;
        }

        var enemy = Enemies[_enemyTurnIndex];
        TransitionTo(CombatState.ResolvingEnemyIntent);

        var move = enemy.CurrentMove!;
        var playerTargets = new List<Combatant> { Player };
        foreach (var effect in move.Effects)
        {
            var scopedTargets = effect.Scope == EffectScope.Self
                ? new List<Combatant> { enemy }
                : playerTargets;
            EffectRegistry.Execute(new EffectContext { Source = enemy, Targets = scopedTargets, Combat = this }, effect);
        }

        if (Player.IsDead)
        {
            EndCombat(CombatOutcome.Lose);
            return;
        }

        enemy.DecayStatus(StatusType.Vulnerable);
        enemy.DecayStatus(StatusType.Weak);
        AdvanceEnemyIntent(enemy);
        CombatantsChanged?.Invoke();

        _enemyTurnIndex++;
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
        TransitionTo(CombatState.CombatEnd);
    }
}
