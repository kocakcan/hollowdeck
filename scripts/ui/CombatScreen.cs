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

    public override void _Ready()
    {
        _combat = GetNode<CombatManager>("CombatManager");
        _enemyRow = GetNode<HBoxContainer>("EnemyRow");
        _handArea = GetNode<Control>("HandArea");
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

        _endTurnButton.Pressed += () => _combat.TryEndTurn();
        _continueButton.Pressed += OnContinuePressed;

        _combat.StateChanged += _ => Refresh();
        _combat.HandChanged += Refresh;
        _combat.CombatantsChanged += Refresh;

        var player = new PlayerCombatant
        {
            Name = "Player",
            MaxHp = 50,
            CurrentHp = 50,
            Piles = new PileManager(CardDatabase.All),
        };

        var enemies = CombatContext.EnemyDefinitionIds
            .Select(id => EnemyFactory.Create(EnemyDatabase.Get(id)))
            .ToList();

        _combat.StartCombat(player, enemies);
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
        RefreshStateUi();
    }

    private void RefreshPlayerInfo()
    {
        var player = _combat.Player;
        _hpLabel.Text = $"HP {player.CurrentHp}/{player.MaxHp}";
        _blockLabel.Text = player.Block > 0 ? $"Block {player.Block}" : "";
        _energyLabel.Text = $"Energy {player.CurrentEnergy}/{player.MaxEnergy}";
        _pileCountsLabel.Text =
            $"Draw {player.Piles.DrawPile.Count} · Discard {player.Piles.Discard.Count} · Exhaust {player.Piles.Exhaust.Count}";
    }

    private void RefreshHand()
    {
        foreach (var child in _handArea.GetChildren())
        {
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
            child.QueueFree();
        }

        foreach (var enemy in _combat.Enemies)
        {
            var enemyView = _enemyViewScene.Instantiate<EnemyView>();
            enemyView.Combatant = enemy;
            _enemyRow.AddChild(enemyView);
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
            if (CombatContext.IsFinalEncounter)
            {
                RunEndContext.Outcome = RunEndOutcome.Win;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Victory);
            }
            else
            {
                RunManager.Instance.AdvanceEncounter();
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
            }
        }
        else
        {
            RunEndContext.Outcome = RunEndOutcome.Lose;
            RunManager.Instance.ChangeScreen(RunManager.ScreenState.Defeat);
        }
    }
}
