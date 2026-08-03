using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    // Nullable, and cleared in _ExitTree: unlike RunManager/AudioManager/
    // MetaProgressionManager, CombatManager is NOT an autoload - it's a child
    // node of CombatScreen.tscn, so it dies with every fight. Leaving the
    // static pointing at a finished fight is what made Shop/Reward render
    // cards dimmed (stale CurrentEnergy), print damage numbers inflated by
    // the last fight's Strength, and made the Deck button show the previous
    // fight's piles. "null Instance" is every call site's existing shorthand
    // for "not in combat", so clearing it is all those checks needed.
    public static CombatManager? Instance { get; private set; }

    // Plain C# events, not Godot [Signal]s - CombatManager is only ever
    // consumed from other C# scripts in-process, so there's no need to pay
    // Godot's Variant marshalling for enum payloads.
    public event Action<CombatState>? StateChanged;
    public event Action? HandChanged;
    public event Action? CombatantsChanged;
    public event Action? PotionsChanged;

    // Fired right before an enemy's telegraphed move actually resolves, so
    // the UI can play a wind-up animation on that specific enemy during the
    // PreActionDelaySec beat below. Purely additive - nothing here reads it.
    public event Action<EnemyCombatant>? EnemyActing;

    // Beats between enemy actions in ResolveEnemyTurnAsync, so multi-enemy
    // turns read as a sequence of distinct hits instead of one instant
    // simultaneous burst - this is what makes per-hit cinematic effects
    // (screen shake, hit-pause, sequential impact) legible.
    // Named because Foresight adds to it: a literal 5 at the draw site reads
    // as "draw five", and "5 + Foresight" reads as arithmetic on nothing.
    public const int BaseHandSize = 5;

    private const float PreActionDelaySec = 0.2f;
    private const float PostActionDelaySec = 0.15f;

    public CombatState State { get; private set; } = CombatState.Start;
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.None;

    public PlayerCombatant Player { get; private set; } = null!;
    public List<EnemyCombatant> Enemies { get; private set; } = new();
    public List<RelicInstance> Relics { get; private set; } = new();

    // Per-fight tallies read once by CombatScreen when the fight ends and
    // folded into RunState.Stats for end-of-run scoring (see RunScore) -
    // kept here rather than in RunState because only this class sees the
    // individual hits, kills and per-turn card counts they're derived from.
    public int EnemiesKilled { get; private set; }
    public bool TookDamage { get; private set; }
    public int LargestSingleHit { get; private set; }
    public int MostCardsInOneTurn { get; private set; }

    private int _cardsThisTurn;
    private PotionInstance? _pendingPotion;
    private List<EnemyCombatant> _enemyTurnOrder = new();

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void StartCombat(PlayerCombatant player, List<EnemyCombatant> enemies, List<RelicInstance> relics)
    {
        Player = player;
        Enemies = enemies;
        Relics = relics;
        State = CombatState.Start;
        Outcome = CombatOutcome.None;
        EnemiesKilled = 0;
        TookDamage = false;
        LargestSingleHit = 0;
        MostCardsInOneTurn = 0;
        _cardsThisTurn = 0;

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
        ApplyPoisonTick(Player);
        if (Player.IsDead)
        {
            EndCombat(CombatOutcome.Lose);
            return;
        }

        // After the poison tick and its death check, and after EndEnemyTurn
        // has cleared Block - see ApplyTurnStartGrants.
        ApplyTurnStartGrants(Player);

        // The other two turn-start grants, and the reason they are not in
        // ApplyTurnStartGrants with Metallicize/Ritual/Regen: energy and hand
        // size are *assigned* here rather than accumulated, so a grant applied
        // in the pass above would be overwritten a line later - the same
        // ordering trap Block has, running the other way. Folding them into the
        // assignments themselves is what makes that unable to happen at all.
        // Both are also player-only: an enemy has no energy pool and no piles.
        Player.CurrentEnergy = Player.MaxEnergy + Player.GetStatus(StatusType.Fervor);
        Player.Piles.DrawHand(BaseHandSize + Player.GetStatus(StatusType.Foresight));
        _cardsThisTurn = 0;

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
        // Snapshotted around EVERY effect, not just deal_damage, so the
        // no-damage-taken scoring bonus (RunScore's Champion/Perfect) also
        // counts self-inflicted lose_hp (Reckless Charge, Last Stand) and
        // anything a future effect does to the player's HP.
        int playerHpBefore = Player.CurrentHp;

        if (spec.Action != "deal_damage")
        {
            EffectRegistry.Execute(new EffectContext { Source = source, Targets = targets, Combat = this }, spec);
            if (Player.CurrentHp < playerHpBefore) TookDamage = true;
            return;
        }

        var before = targets.ToDictionary(t => t, t => t.CurrentHp);
        EffectRegistry.Execute(new EffectContext { Source = source, Targets = targets, Combat = this }, spec);

        var relicCtx = MakeRelicContext();
        foreach (var target in targets)
        {
            int dealt = before[target] - target.CurrentHp;
            if (dealt <= 0) continue;
            if (source == Player)
            {
                LargestSingleHit = Math.Max(LargestSingleHit, dealt);
                foreach (var relic in Relics) relic.Behavior.OnDamageDealt(relicCtx, target, dealt);
            }
            if (target == Player) foreach (var relic in Relics) relic.Behavior.OnDamageTaken(relicCtx, source, dealt);
        }

        if (Player.CurrentHp < playerHpBefore) TookDamage = true;
    }

    private void ResolveCard(CardInstance card, List<Combatant> targets)
    {
        TransitionTo(CombatState.ResolvingCard);

        Player.CurrentEnergy -= card.Definition.Cost;
        _cardsThisTurn++;
        MostCardsInOneTurn = Math.Max(MostCardsInOneTurn, _cardsThisTurn);
        Player.Piles.Hand.Remove(card);
        // Exhaust is checked first so a Power that also declares exhaust: true
        // still reads as exhausted - the two would otherwise disagree about
        // which pile wins, and Exhaust is the one with player-visible cost.
        if (card.Definition.Exhaust) Player.Piles.Exhaust.Add(card);
        else if (card.Definition.Type == CardType.Power) Player.Piles.Powers.Add(card);
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

        RemoveDeadEnemies();
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

        RemoveDeadEnemies();
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
        Player.DecayStatus(StatusType.Frail);

        _enemyTurnOrder = new List<EnemyCombatant>(Enemies);
        TransitionTo(CombatState.EnemyTurn);
        _ = ResolveEnemyTurnAsync();
    }

    // Iterates the enemy turn order one enemy at a time with a wind-up beat
    // before each action and an impact/hit-pause beat after, instead of
    // resolving the whole turn synchronously in one frame - same order and
    // outcomes as before, just paced so per-hit VFX (shake, sparks, floating
    // damage) has time to read, especially in multi-enemy fights.
    private async Task ResolveEnemyTurnAsync()
    {
        foreach (var enemy in _enemyTurnOrder)
        {
            if (enemy.IsDead) continue; // Died earlier this round (e.g. a relic retaliation kill).

            ApplyPoisonTick(enemy);
            if (enemy.IsDead)
            {
                RemoveDeadEnemies();
                CombatantsChanged?.Invoke();
                if (Enemies.Count == 0)
                {
                    EndCombat(CombatOutcome.Win);
                    return;
                }
                continue;
            }

            EnemyActing?.Invoke(enemy);
            TransitionTo(CombatState.ResolvingEnemyIntent);
            await Delay(PreActionDelaySec);

            enemy.Block = 0;
            // Must follow the Block clear above, not sit with the poison tick
            // at the top of the loop, or Metallicize is wiped the instant it
            // is granted - see ApplyTurnStartGrants.
            ApplyTurnStartGrants(enemy);

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

            RemoveDeadEnemies();
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
                enemy.DecayStatus(StatusType.Frail);
                AdvanceEnemyIntent(enemy);
                CombatantsChanged?.Invoke();
            }

            await Delay(PostActionDelaySec);
        }

        // Block clears here, not in BeginPlayerTurn itself - it must persist
        // through the enemy's turn (so it can absorb their attacks) and must
        // NOT be wiped by the very first call to BeginPlayerTurn from
        // StartCombat, which runs right after OnCombatStart relics (e.g.
        // Anchor Stone) grant their bonus.
        Player.Block = 0;
        BeginPlayerTurn();
    }

    private async Task Delay(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), Timer.SignalName.Timeout);
    }

    // Poison deals direct HP loss (bypasses Block, per genre convention) at
    // the start of the afflicted's own turn, then decays by 1 - a different
    // trigger point than Vulnerable/Weak's end-of-turn decay above, which is
    // an intentional difference in when each status resolves.
    private void ApplyPoisonTick(Combatant c)
    {
        int poison = c.GetStatus(StatusType.Poison);
        if (poison <= 0) return;
        c.CurrentHp -= Math.Min(c.CurrentHp, poison);
        c.DecayStatus(StatusType.Poison);
        if (c == Player) TookDamage = true;
    }

    // What a Power buys: statuses that pay out every turn instead of once.
    // None of the three decays - Metallicize, Ritual and Regen persist for the
    // fight, unlike Vulnerable/Weak/Frail (end-of-turn decay) or Poison
    // (decays as it ticks).
    //
    // Kept separate from ApplyPoisonTick and called at a different point on
    // purpose. Both combatants clear Block on their own turn, and Metallicize
    // has to land *after* that clear or it is granted and immediately wiped -
    // for the player EndEnemyTurn clears it just before BeginPlayerTurn, but
    // for an enemy the clear happens mid-loop, after its poison tick. Poison
    // also has to stay where it is, because it gates a death check.
    private void ApplyTurnStartGrants(Combatant c)
    {
        int metallicize = c.GetStatus(StatusType.Metallicize);
        if (metallicize > 0) c.Block += metallicize;

        // Compounding on purpose: Ritual grants Strength *every* turn, so the
        // Strength total climbs each round. That is the whole reason a Power
        // is worth a card slot it never returns from.
        int ritual = c.GetStatus(StatusType.Ritual);
        if (ritual > 0) c.AddStatus(StatusType.Strength, ritual);

        // Regen is the only grant here that isn't affected by the Block-clear
        // ordering the comment above is about - it heals - but it lives with
        // the other two because it is the same *kind* of thing (a Power that
        // pays out each turn) and a player reading the roster should find all
        // three in one place. Capped at MaxHp, like every other heal.
        int regen = c.GetStatus(StatusType.Regen);
        if (regen > 0) c.CurrentHp = Math.Min(c.MaxHp, c.CurrentHp + regen);
    }

    // Replaces the four bare Enemies.RemoveAll(e => e.IsDead) calls this
    // class used to make, so every path that clears corpses - card kill,
    // potion kill, poison tick, enemy-turn retaliation - feeds the same
    // kill tally without each call site having to remember to.
    private void RemoveDeadEnemies()
    {
        EnemiesKilled += Enemies.RemoveAll(e => e.IsDead);
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
