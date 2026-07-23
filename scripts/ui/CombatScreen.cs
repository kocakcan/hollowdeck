using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class CombatScreen : Control
{
    private const float CardWidth = 160f;
    private const float CardGap = 16f;

    private CombatManager _combat = null!;
    private HBoxContainer _enemyRow = null!;
    private Control _handArea = null!;
    private HBoxContainer _potionBelt = null!;
    private HBoxContainer _relicBar = null!;
    private Label _hpLabel = null!;
    private Label _blockLabel = null!;
    private Label _energyLabel = null!;
    private Label _pileCountsLabel = null!;
    private Label _targetHintLabel = null!;
    private Button _endTurnButton = null!;
    private Control _combatEndPanel = null!;
    private Label _outcomeLabel = null!;
    private Button _continueButton = null!;

    private PackedScene _cardViewScene = null!;
    private PackedScene _enemyViewScene = null!;
    private PackedScene _potionViewScene = null!;
    private PackedScene _floatingTextScene = null!;

    // Previous HP/Block per combatant, so Refresh() can diff and pop up
    // floating combat text - CombatManager only tells us "something
    // changed," not what, and EnemyView gets torn down/rebuilt on every
    // refresh (see RefreshEnemies), so this has to live here, not there.
    private readonly Dictionary<Combatant, (int Hp, int Block)> _lastStats = new();

    public override void _Ready()
    {
        _combat = GetNode<CombatManager>("CombatManager");
        _enemyRow = GetNode<HBoxContainer>("EnemyRow");
        _handArea = GetNode<Control>("HandArea");
        _potionBelt = GetNode<HBoxContainer>("PotionBelt");
        _relicBar = GetNode<HBoxContainer>("RelicBar");
        _hpLabel = GetNode<Label>("PlayerInfoPanel/HpLabel");
        _blockLabel = GetNode<Label>("PlayerInfoPanel/BlockLabel");
        _energyLabel = GetNode<Label>("PlayerInfoPanel/EnergyLabel");
        _pileCountsLabel = GetNode<Label>("PileCountsLabel");
        _targetHintLabel = GetNode<Label>("TargetHintLabel");
        _endTurnButton = GetNode<Button>("EndTurnButton");
        _combatEndPanel = GetNode<Control>("CombatEndPanel");
        _outcomeLabel = GetNode<Label>("CombatEndPanel/OutcomeLabel");
        _continueButton = GetNode<Button>("CombatEndPanel/ContinueButton");

        _cardViewScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        _enemyViewScene = GD.Load<PackedScene>("res://scenes/EnemyView.tscn");
        _potionViewScene = GD.Load<PackedScene>("res://scenes/PotionView.tscn");
        _floatingTextScene = GD.Load<PackedScene>("res://scenes/FloatingText.tscn");

        _endTurnButton.Pressed += () => _combat.TryEndTurn();
        _continueButton.Pressed += OnContinuePressed;

        _combat.StateChanged += _ => Refresh();
        _combat.HandChanged += Refresh;
        _combat.CombatantsChanged += Refresh;
        _combat.PotionsChanged += Refresh;

        var player = new PlayerCombatant
        {
            Name = "Player",
            MaxHp = RunState.PlayerMaxHp,
            CurrentHp = RunState.PlayerCurrentHp,
            Piles = new PileManager(RunState.Deck),
        };

        var enemies = CombatContext.EnemyDefinitionIds
            .Select(id => EnemyFactory.Create(EnemyDatabase.Get(id)))
            .ToList();

        _combat.StartCombat(player, enemies, RunState.Relics);
        RefreshRelics();
    }

    private void RefreshRelics()
    {
        foreach (var child in _relicBar.GetChildren())
        {
            _relicBar.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var relic in RunState.Relics)
        {
            _relicBar.AddChild(new Label
            {
                Text = relic.Definition.Name,
                TooltipText = relic.Definition.Description,
            });
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _combat.CancelTargeting();
        }
        else if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            _combat.CancelTargeting();
        }
    }

    private void Refresh()
    {
        RefreshPlayerInfo();
        RefreshHand();
        RefreshEnemies();
        RefreshPotions();
        RefreshStateUi();
    }

    private void RefreshPlayerInfo()
    {
        var player = _combat.Player;
        PopupDelta(player, this, _hpLabel.GlobalPosition);
        _hpLabel.Text = $"HP {player.CurrentHp}/{player.MaxHp}";
        _blockLabel.Text = player.Block > 0 ? $"Block {player.Block}" : "";
        _energyLabel.Text = $"Energy {player.CurrentEnergy}/{player.MaxEnergy}";
        _pileCountsLabel.Text =
            $"Draw {player.Piles.DrawPile.Count} · Discard {player.Piles.Discard.Count} · Exhaust {player.Piles.Exhaust.Count}";
    }

    private void RefreshHand()
    {
        // RemoveChild (not just QueueFree) so the removal is immediate -
        // Refresh() can run multiple times in the same frame (state/hand/
        // combatant events all fire synchronously), and QueueFree alone
        // defers removal until frame end, letting rebuilds stack duplicates.
        foreach (var child in _handArea.GetChildren())
        {
            _handArea.RemoveChild(child);
            child.QueueFree();
        }

        var hand = _combat.Player.Piles.Hand;
        for (int i = 0; i < hand.Count; i++)
        {
            var cardView = _cardViewScene.Instantiate<CardView>();
            _handArea.AddChild(cardView);
            cardView.SetCardInstance(hand[i]);
            cardView.SetHomePosition(new Vector2(i * (CardWidth + CardGap), 0));
        }
    }

    private void RefreshEnemies()
    {
        foreach (var child in _enemyRow.GetChildren())
        {
            _enemyRow.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var enemy in _combat.Enemies)
        {
            var enemyView = _enemyViewScene.Instantiate<EnemyView>();
            enemyView.Combatant = enemy;
            _enemyRow.AddChild(enemyView);
            PopupDelta(enemy, enemyView, new Vector2(30, 4));
        }
    }

    // Diffs a combatant's HP/Block against the last time Refresh() saw it,
    // spawning floating +/- text (and a hit flash on damage) for whatever
    // changed. popupParent/localSpawnPos let this work for both the player
    // (spawned under CombatScreen itself, at the HP label's position) and
    // enemies (spawned as a child of their freshly-rebuilt EnemyView).
    private void PopupDelta(Combatant c, Node popupParent, Vector2 localSpawnPos)
    {
        if (_lastStats.TryGetValue(c, out var prev))
        {
            int hpDelta = c.CurrentHp - prev.Hp;
            if (hpDelta < 0)
            {
                SpawnFloatingText(popupParent, localSpawnPos, $"-{-hpDelta}", new Color(1f, 0.35f, 0.35f));
                if (popupParent is CanvasItem hitTarget) FlashHit(hitTarget);
            }
            else if (hpDelta > 0)
            {
                SpawnFloatingText(popupParent, localSpawnPos, $"+{hpDelta}", new Color(0.4f, 1f, 0.4f));
            }

            int blockDelta = c.Block - prev.Block;
            if (blockDelta > 0)
            {
                SpawnFloatingText(popupParent, localSpawnPos + new Vector2(0, 16),
                    $"+{blockDelta} Block", new Color(0.6f, 0.8f, 1f));
            }
        }
        _lastStats[c] = (c.CurrentHp, c.Block);
    }

    private void SpawnFloatingText(Node parent, Vector2 localPos, string text, Color color)
    {
        var floatingText = _floatingTextScene.Instantiate<FloatingText>();
        parent.AddChild(floatingText);
        floatingText.Play(text, color, localPos);
    }

    // A brief red tint pulse rather than a positional shake - EnemyView
    // lives inside an HBoxContainer, which would fight (and win against)
    // any manual Position tween on it every layout pass.
    private static void FlashHit(CanvasItem target)
    {
        var original = target.Modulate;
        var tween = target.GetTree().CreateTween();
        tween.TweenProperty(target, "modulate", new Color(1f, 0.4f, 0.4f), 0.06);
        tween.TweenProperty(target, "modulate", original, 0.12);
    }

    private void RefreshPotions()
    {
        foreach (var child in _potionBelt.GetChildren())
        {
            _potionBelt.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var potion in RunState.Potions)
        {
            var potionView = _potionViewScene.Instantiate<PotionView>();
            _potionBelt.AddChild(potionView);
            potionView.SetPotionInstance(potion);
        }
    }

    private void RefreshStateUi()
    {
        _targetHintLabel.Visible = _combat.State == CombatState.AwaitingTarget;
        _endTurnButton.Disabled = _combat.State != CombatState.PlayerTurn;

        if (_combat.State == CombatState.CombatEnd)
        {
            _combatEndPanel.Visible = true;
            _outcomeLabel.Text = _combat.Outcome == CombatOutcome.Win ? "Victory!" : "Defeated...";
        }
        else
        {
            _combatEndPanel.Visible = false;
        }
    }

    private void OnContinuePressed()
    {
        if (_combat.Outcome == CombatOutcome.Win)
        {
            RunState.PlayerCurrentHp = _combat.Player.CurrentHp;
            RunState.PlayerMaxHp = _combat.Player.MaxHp;
            RunState.Gold += CombatContext.GoldReward;

            if (CombatContext.IsBoss)
            {
                RunEndContext.Outcome = RunEndOutcome.Win;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Victory);
            }
            else
            {
                RewardContext.CardChoices = SampleCardChoices(3);
                RewardContext.GoldAwarded = CombatContext.GoldReward;
                RewardContext.GuaranteedRelic = CombatContext.IsElite ? GrantEliteRelic() : null;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Reward);
            }
        }
        else
        {
            RunEndContext.Outcome = RunEndOutcome.Lose;
            RunManager.Instance.ChangeScreen(RunManager.ScreenState.Defeat);
        }
    }

    // Elite fights guarantee a relic on top of the usual card/gold reward -
    // same unowned+unlock-filtered pool ShopScreen/TreasureScreen already
    // draw from, sampled from the dedicated Shop RNG stream.
    private static RelicDefinition? GrantEliteRelic()
    {
        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var available = RelicDatabase.All
            .Where(r => !ownedRelicIds.Contains(r.Id) && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();
        if (available.Count == 0) return null;

        var picked = available[RngStreams.Shop.Next(available.Count)];
        RunState.Relics.Add(new RelicInstance(picked));
        return picked;
    }

    private static List<CardDefinition> SampleCardChoices(int count)
    {
        // No unlock filter - all cards are available from the start (see
        // MetaProgressionManager.LockedRelicIds; only relics are lockable).
        var pool = CardDatabase.All.ToList();
        var rng = RngStreams.Shop;
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(count).ToList();
    }
}
