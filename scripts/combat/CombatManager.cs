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
        // After the shuffle and before the opening draw in BeginPlayerTurn -
        // the only window where "drawn first" means anything. See
        // PileManager.PromoteInnate for why it appends rather than prepends.
        Player.Piles.PromoteInnate();

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
        // The fourth gate, first in the list because it is the only one that
        // is a property of the card rather than of the situation: a Curse or a
        // Status card is never playable, at any energy, against any target.
        if (!card.Definition.IsPlayable) return false;

        // An X-cost card spends everything left, so it is always affordable -
        // including at zero energy, where it resolves for nothing. Reading the
        // cost through this local rather than off the definition is what keeps
        // the -1 sentinel from leaking into the comparison below and into
        // ResolveCard's subtraction, where it would *grant* energy.
        int cost = card.Definition.IsXCost ? Player.CurrentEnergy : card.Definition.Cost;
        // An X card at zero energy resolves for nothing and is gone - a pure
        // trap, and one the player would spring by misreading an always-bright
        // frame. Refusing it costs no expressiveness, and it is why CardView
        // treats an X card as unaffordable at zero rather than always bright.
        if (card.Definition.IsXCost && cost < 1) return false;
        if (cost > Player.CurrentEnergy) return false;
        if (card.Definition.Target == CardTargetType.SingleEnemy && explicitTarget is null) return false;

        ResolveCard(card, ResolveTargets(card.Definition.Target, explicitTarget), cost);
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

    // Who a single EffectSpec actually hits, given the card- or move-level
    // targets already resolved by ResolveTargets. This used to be a three-line
    // ternary written out twice - once for the player in ResolveCard, once for
    // the enemy turn - which is exactly how the two would have drifted the
    // moment a third scope existed.
    //
    // AllEnemies and RandomEnemy are resolved against the source's opposition
    // rather than against the player's, so an enemy authoring one is coherent
    // (its "all enemies" is the player) instead of crashing. Nothing authors
    // them, and Phase4ContentSmokeTest refuses any enemy move that does: an
    // intent's telegraph is derived from these specs, and a scope EnemyView
    // does not account for is a route to a telegraph that lies.
    private List<Combatant> ScopedTargets(EffectSpec effect, Combatant source, List<Combatant> declared)
    {
        switch (effect.Scope)
        {
            case EffectScope.Self:
                return new List<Combatant> { source };
            case EffectScope.AllEnemies:
                return Opposition(source);
            case EffectScope.RandomEnemy:
            {
                var pool = Opposition(source);
                // Combat, not a stream of its own: a card resolving is combat,
                // and risk 2 asks for a new stream per new *system*.
                return pool.Count == 0
                    ? pool
                    : new List<Combatant> { pool[RngStreams.Combat.Next(pool.Count)] };
            }
            default:
                return declared;
        }
    }

    private List<Combatant> Opposition(Combatant source) =>
        source == Player
            ? Enemies.Cast<Combatant>().ToList()
            : new List<Combatant> { Player };

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
    // x is how much energy an X-cost card spent, or null for everything else -
    // see EffectContext.AmountFor, which is the only thing that reads it.
    private void ExecuteEffect(EffectSpec spec, Combatant source, List<Combatant> targets, int? x = null)
    {
        // Snapshotted around EVERY effect, not just deal_damage, so the
        // no-damage-taken scoring bonus (RunScore's Champion/Perfect) also
        // counts self-inflicted lose_hp (Reckless Charge, Last Stand) and
        // anything a future effect does to the player's HP.
        int playerHpBefore = Player.CurrentHp;

        var ctx = new EffectContext { Source = source, Targets = targets, Combat = this, XAmount = x };

        if (spec.Action != "deal_damage")
        {
            EffectRegistry.Execute(ctx, spec);
            if (Player.CurrentHp < playerHpBefore) TookDamage = true;
            return;
        }

        var before = targets.ToDictionary(t => t, t => t.CurrentHp);
        EffectRegistry.Execute(ctx, spec);

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

    // cost is what TryPlayCard already resolved: the authored cost, or all
    // remaining energy for an X-cost card. Never card.Definition.Cost, which
    // for an X card is the -1 sentinel and would *grant* two energy here.
    private void ResolveCard(CardInstance card, List<Combatant> targets, int cost)
    {
        TransitionTo(CombatState.ResolvingCard);

        // Read before the spend, because the spend zeroes it.
        int? x = card.Definition.IsXCost ? cost : null;

        Player.CurrentEnergy -= cost;
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
            ExecuteEffect(effect, Player, ScopedTargets(effect, Player, targets), x);
        }

        // add_card can put a card straight into the hand, and drawing already
        // could - neither shows until the hand is rebuilt. CombatantsChanged
        // below drives the same Refresh, so this is covered; it is called out
        // because the HandChanged above fires *before* the effects run and
        // reads as though it were the one doing it.
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
            ExecuteEffect(effect, Player, ScopedTargets(effect, Player, targets));
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
        DecayTurnEndStatuses(Player);

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
                ExecuteEffect(effect, enemy, ScopedTargets(effect, enemy, playerTargets));
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
                DecayTurnEndStatuses(enemy);
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

    // The statuses that wear off by 1 when their holder's turn ends, in one
    // list rather than spelled out at the two sites that decay them (the
    // player's in TryEndTurn, the enemy's mid-loop in ResolveEnemyTurnAsync).
    //
    // It was two hand-written lists until Intangible arrived, and that is
    // exactly the shape of bug worth spending a field on: a status added to one
    // site and not the other decays for the player and not the enemy, both
    // sites keep compiling, and the asymmetry only shows up as a fight that
    // feels wrong. Poison is deliberately absent - it decays as it ticks, at
    // the *start* of its holder's turn, which is a different trigger point and
    // lives in ApplyPoisonTick.
    private static readonly StatusType[] DecayAtTurnEnd =
    {
        StatusType.Vulnerable, StatusType.Weak, StatusType.Frail, StatusType.Intangible,
    };

    private static void DecayTurnEndStatuses(Combatant c)
    {
        foreach (var status in DecayAtTurnEnd) c.DecayStatus(status);
    }

    // What a Power buys: statuses that pay out every turn instead of once.
    // None of the four decays - Metallicize, Ritual, Regen and Plating persist
    // for the fight, unlike Vulnerable/Weak/Frail/Intangible (end-of-turn
    // decay) or Poison (decays as it ticks). Plating is the near miss there: it
    // is spent by taking unblocked damage in DealDamageEffect, never by a
    // clock, so it belongs with the grants rather than in DecayAtTurnEnd.
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

        // Plating pays out exactly like Metallicize and sits beside it for that
        // reason, inheriting the Block-clear ordering above rather than
        // restating it. What separates the two is the cost: Plating loses a
        // stack to every hit that gets past the Block it just granted, so it is
        // a shrinking wall against a big deck and a permanent one against a
        // small one. That erosion lives in DealDamageEffect, where the hit is.
        int plating = c.GetStatus(StatusType.Plating);
        if (plating > 0) c.Block += plating;

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
