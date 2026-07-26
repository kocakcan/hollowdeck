using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class CombatScreen : Control
{
    private const float CardWidth = 224f;
    // Fan layout: cards overlap by up to ~55% of their width (shrinking
    // further if the hand is too wide to fit), rotate up to MaxFanRotationDeg
    // at the outer edges, and arc so the outer cards sit higher than the
    // center one - see RefreshHand() for the actual formula.
    private const float MaxFanRotationDeg = 12f;
    private const float FanArcHeight = 36f;
    // HandArea's own rect only needs to be wide enough for the fan-width
    // math below; its top edge sits well below where cards actually rest -
    // this pulls the resting fan up so cards (308 tall) stay mostly inside
    // the 648-tall viewport instead of hanging off the bottom edge.
    private const float FanBaseY = -140f;

    private CombatManager _combat = null!;
    private HBoxContainer _enemyRow = null!;
    private Control _handArea = null!;
    private HBoxContainer _potionBelt = null!;
    private HBoxContainer _relicBar = null!;
    private Label _goldLabel = null!;
    private ProgressBar _playerHpBar = null!;
    private ProgressBar _playerGhostHpBar = null!;
    private Tween? _playerGhostHpTween;
    private Label _playerHpLabel = null!;
    private HBoxContainer _energyRow = null!;
    private Label _energyLabel = null!;
    private Label _pileCountsLabel = null!;
    private HBoxContainer _playerStatusRow = null!;
    private Label _targetHintLabel = null!;
    private Button _endTurnButton = null!;
    private Control _combatEndPanel = null!;
    private Label _outcomeLabel = null!;
    private Button _continueButton = null!;
    private Control _drawPileAnchor = null!;
    private Control _discardPileAnchor = null!;
    private Control _exhaustPileAnchor = null!;
    private TextureRect _playerSprite = null!;
    private Label _turnBannerLabel = null!;
    private PanelContainer _turnBannerPanel = null!;

    private PackedScene _cardViewScene = null!;
    private PackedScene _enemyViewScene = null!;
    private PackedScene _potionViewScene = null!;
    private PackedScene _floatingTextScene = null!;

    private Texture2D? _energyOrbTexture;
    private Texture2D? _sparkTexture;
    private Vector2 _playerSpriteRestPos;
    private Tween? _playerIdleTween;
    private Tween? _screenShakeTween;
    private Vector2 _turnBannerRestPos;
    private Tween? _turnBannerTween;
    private CombatState _lastKnownState = CombatState.Start;
    private bool _pulseEnergyPipsOnNextRefresh;
    private Tween? _endTurnPulseTween;
    private bool _endTurnPulsing;

    // Previous HP/Block per combatant, so Refresh() can diff and pop up
    // floating combat text - CombatManager only tells us "something
    // changed," not what.
    private readonly Dictionary<Combatant, (int Hp, int Block)> _lastStats = new();
    private Dictionary<StatusType, int>? _lastPlayerStatuses;

    // RefreshHand()/RefreshEnemies() used to destroy and reinstantiate every
    // CardView/EnemyView on every single Refresh() call (which fires on any
    // state/hand/combatant/potion event, not just ones relevant to that
    // node) - fatal to any continuous per-node animation (idle bob, hit-
    // shake, draw/discard motion), since the node holding that animation
    // state kept getting destroyed out from under it. These dictionaries
    // let both methods diff against what's already on screen and update
    // existing views in place instead of rebuilding, so a view's identity
    // (and any animation state on it) survives across refreshes.
    private readonly Dictionary<CardInstance, CardView> _cardViews = new();
    private readonly Dictionary<EnemyCombatant, EnemyView> _enemyViews = new();

    // Keyboard card-play (Left/Right/number keys to select, Space to play) -
    // a second input path feeding the exact same CombatManager.TryPlayCard
    // drag-and-drop already uses, so none of this touches CombatManager's
    // own state machine. Tracked by instance (not index) so a hand reorder
    // between key presses can't leave these pointing at the wrong slot.
    private CardInstance? _keyboardSelectedCard;
    private CardInstance? _armedCard;
    private EnemyCombatant? _keyboardTargetEnemy;

    public override void _Ready()
    {
        ScreenBackground.AttachCombat(this, "crypt", new Color(0.6f, 0.58f, 0.62f));
        DeckViewButtons.Attach(this, includeCombatPiles: true);
        _combat = GetNode<CombatManager>("CombatManager");
        _enemyRow = GetNode<HBoxContainer>("EnemyRow");
        _handArea = GetNode<Control>("HandArea");
        _potionBelt = GetNode<HBoxContainer>("PotionPanel/PotionBelt");
        _relicBar = GetNode<HBoxContainer>("RelicPanel/RelicBar");
        _goldLabel = GetNode<Label>("GoldPanel/GoldLabel");
        _playerHpBar = GetNode<ProgressBar>("PlayerHpFrame/HpBar");
        _playerGhostHpBar = GetNode<ProgressBar>("PlayerHpFrame/GhostHpBar");
        _playerHpLabel = GetNode<Label>("PlayerHpFrame/HpLabel");
        _energyRow = GetNode<HBoxContainer>("EnergyRow");
        _energyLabel = GetNode<Label>("EnergyLabel");
        _pileCountsLabel = GetNode<Label>("PileCountsLabel");
        _drawPileAnchor = GetNode<Control>("DrawPileAnchor");
        _discardPileAnchor = GetNode<Control>("DiscardPileAnchor");
        _exhaustPileAnchor = GetNode<Control>("ExhaustPileAnchor");
        _playerStatusRow = GetNode<HBoxContainer>("PlayerStatusRow");
        _targetHintLabel = GetNode<Label>("TargetHintLabel");
        _turnBannerLabel = GetNode<Label>("TurnBannerPanel/TurnBannerLabel");
        _turnBannerPanel = GetNode<PanelContainer>("TurnBannerPanel");
        _endTurnButton = GetNode<Button>("EndTurnButton");
        _combatEndPanel = GetNode<Control>("CombatEndPanel");
        _outcomeLabel = GetNode<Label>("CombatEndPanel/OutcomeLabel");
        _continueButton = GetNode<Button>("CombatEndPanel/ContinueButton");

        _cardViewScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        _enemyViewScene = GD.Load<PackedScene>("res://scenes/EnemyView.tscn");
        _potionViewScene = GD.Load<PackedScene>("res://scenes/PotionView.tscn");
        _floatingTextScene = GD.Load<PackedScene>("res://scenes/FloatingText.tscn");

        _playerSprite = GetNode<TextureRect>("PlayerSprite");
        _playerSprite.Texture = ArtAssets.PlayerSprite();
        GetNode<TextureRect>("PlayerSprite/Shadow").Texture = BuildShadowTexture();
        _playerSpriteRestPos = _playerSprite.Position;
        StartPlayerIdleBob();

        ChromeStyles.ApplyHpBarStyle(_playerHpBar, _playerGhostHpBar);
        GetNode<PanelContainer>("GoldPanel").AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        GetNode<PanelContainer>("RelicPanel").AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        GetNode<PanelContainer>("PotionPanel").AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        _playerHpLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _energyLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _turnBannerLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _turnBannerLabel.AddThemeFontSizeOverride("font_size", 32);
        _turnBannerPanel.AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        _turnBannerRestPos = _turnBannerPanel.Position;

        _endTurnButton.AddThemeStyleboxOverride("normal", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_normal.png"));
        _endTurnButton.AddThemeStyleboxOverride("hover", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_hover.png"));
        _endTurnButton.AddThemeStyleboxOverride("pressed", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_pressed.png"));
        _endTurnButton.AddThemeStyleboxOverride("disabled", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_normal.png"));

        // FocusModeEnum.None on both: Godot's automatic arrow-key focus
        // navigation (any Button is focusable by default) otherwise fights
        // the keyboard card/target cycling below - Left/Right would silently
        // shift focus onto End Turn/Continue instead of only doing what
        // OnKeyboardCycle expects.
        _endTurnButton.FocusMode = FocusModeEnum.None;
        _continueButton.FocusMode = FocusModeEnum.None;

        _endTurnButton.Pressed += OnEndTurnRequested;
        _continueButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _continueButton.Pressed += OnContinuePressed;

        _combat.StateChanged += OnCombatStateChanged;
        _combat.HandChanged += Refresh;
        _combat.CombatantsChanged += Refresh;
        _combat.PotionsChanged += Refresh;
        _combat.EnemyActing += OnEnemyActing;

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
            var tooltip = $"{relic.Definition.Name}\n{relic.Definition.Description}";
            if (ArtAssets.RelicIcon(relic.Definition.Id) is { } icon)
            {
                _relicBar.AddChild(new TextureRect
                {
                    Texture = icon,
                    CustomMinimumSize = new Vector2(34, 34),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TooltipText = tooltip,
                    MouseFilter = MouseFilterEnum.Stop,
                });
            }
            else
            {
                _relicBar.AddChild(new Label
                {
                    Text = relic.Definition.Name,
                    TooltipText = tooltip,
                });
            }
        }
    }

    // Continuous gentle bob - Position is safe here since PlayerSprite is a
    // direct child of the CombatScreen root, not Container-managed.
    private void StartPlayerIdleBob()
    {
        _playerIdleTween?.Kill();
        _playerSprite.Position = _playerSpriteRestPos;
        var tween = _playerSprite.CreateTween();
        _playerIdleTween = tween;
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(_playerSprite, "position", _playerSpriteRestPos + new Vector2(0, -6), 1.3);
        tween.TweenProperty(_playerSprite, "position", _playerSpriteRestPos, 1.3);
    }

    // Shared driver for any one-off PlayerSprite Position beat (hit-shake,
    // attack lunge) - always kills the idle bob first (both drive Position)
    // and restarts it once the beat settles back to rest.
    private void PlayPlayerPositionBeat(List<Vector2> waypoints, float stepDuration)
    {
        _playerIdleTween?.Kill();
        var tween = _playerSprite.CreateTween();
        foreach (var wp in waypoints)
        {
            tween.TweenProperty(_playerSprite, "position", wp, stepDuration);
        }
        tween.TweenProperty(_playerSprite, "position", _playerSpriteRestPos, stepDuration);
        tween.TweenCallback(Callable.From(StartPlayerIdleBob));
    }

    private void PlayPlayerHitShake()
    {
        var rng = new RandomNumberGenerator();
        var waypoints = new List<Vector2>();
        for (int i = 0; i < 4; i++)
        {
            waypoints.Add(_playerSpriteRestPos + new Vector2(rng.RandfRange(-8f, 8f), rng.RandfRange(-4f, 4f)));
        }
        PlayPlayerPositionBeat(waypoints, 0.035f);
    }

    private void PlayPlayerLungeToward(Vector2 targetGlobalPos)
    {
        var direction = (targetGlobalPos - (_playerSprite.GlobalPosition + _playerSprite.Size / 2f)).Normalized();
        PlayPlayerPositionBeat(new List<Vector2> { _playerSpriteRestPos + direction * 26f }, 0.09f);
    }

    // Fired right before an enemy's telegraphed move resolves (see
    // CombatManager.ResolveEnemyTurnAsync's PreActionDelaySec beat) - plays
    // a brief wind-up lean on that specific enemy during the pause.
    private void OnEnemyActing(EnemyCombatant enemy)
    {
        if (_enemyViews.TryGetValue(enemy, out var view))
        {
            view.PlayWindUp();
        }
    }

    // StateChanged only carries the new state - CombatManager.TransitionTo
    // (CombatManager.cs:108-113) doesn't pass the previous one, and
    // PlayerTurn is entered from three different places (combat start,
    // after a card resolves mid-turn, and after the enemy turn ends) but
    // only the first/last are an actual turn boundary worth a banner - a
    // card-resolve bounce-back to PlayerTurn should never re-trigger "Your
    // Turn". Tracking the last-seen state locally is what lets this
    // distinguish "just started a new turn" from "just finished resolving
    // one card mid-turn" without CombatManager needing a new event.
    private void OnCombatStateChanged(CombatState next)
    {
        bool enteringPlayerTurn = next == CombatState.PlayerTurn &&
            _lastKnownState is CombatState.Start or CombatState.ResolvingEnemyIntent;
        bool enteringEnemyTurn = next == CombatState.EnemyTurn && _lastKnownState == CombatState.PlayerTurn;

        if (enteringPlayerTurn)
        {
            PlayTurnBanner("Your Turn");
            _pulseEnergyPipsOnNextRefresh = true;
        }
        else if (enteringEnemyTurn)
        {
            PlayTurnBanner("Enemy Turn");
        }

        _lastKnownState = next;
        Refresh();
    }

    // Slides up + fades in, holds briefly, then fades out - a short beat
    // between turns rather than an instant label swap.
    private void PlayTurnBanner(string text)
    {
        _turnBannerTween?.Kill();
        _turnBannerLabel.Text = text;
        _turnBannerPanel.Modulate = new Color(1f, 1f, 1f, 0f);
        _turnBannerPanel.Position = _turnBannerRestPos + new Vector2(0, 16);

        var tween = CreateTween();
        _turnBannerTween = tween;
        tween.SetParallel(true);
        tween.TweenProperty(_turnBannerPanel, "modulate:a", 1f, 0.18).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(_turnBannerPanel, "position", _turnBannerRestPos, 0.22).SetTrans(Tween.TransitionType.Back);
        tween.Chain();
        tween.TweenInterval(0.5);
        tween.TweenProperty(_turnBannerPanel, "modulate:a", 0f, 0.25).SetTrans(Tween.TransitionType.Sine);
    }

    // Screen shake: jitters the CombatScreen root itself - safe since it's
    // the scene root, not Container-managed. Kills any shake already in
    // flight first so overlapping hits (e.g. an AOE card) don't leave the
    // screen stuck off-center from two competing tweens. Magnitude scales
    // with how much HP was actually lost (capped) instead of being a flat
    // constant regardless of a tickle vs. a near-lethal hit; skipped
    // entirely under Settings > Reduce Motion.
    private const float MaxShakeMagnitude = 14f;

    private void PlayScreenShake(int hpDeltaAbs)
    {
        if (SettingsManager.Instance.ReduceMotion) return;
        float magnitude = Mathf.Clamp(hpDeltaAbs * 0.6f, 3f, MaxShakeMagnitude);
        _screenShakeTween?.Kill();
        Position = Vector2.Zero;
        var tween = CreateTween();
        _screenShakeTween = tween;
        var rng = new RandomNumberGenerator();
        for (int i = 0; i < 5; i++)
        {
            var offset = new Vector2(rng.RandfRange(-magnitude, magnitude), rng.RandfRange(-magnitude, magnitude));
            tween.TweenProperty(this, "position", offset, 0.03);
        }
        tween.TweenProperty(this, "position", Vector2.Zero, 0.03);
    }

    // Control (unlike Node2D/Node3D) has no ToLocal() - invert its own
    // global transform manually to convert a global point into this node's
    // local coordinate space.
    private Vector2 ToLocalPoint(Vector2 globalPos) => GetGlobalTransform().AffineInverse() * globalPos;

    // One-shot particle burst (CPUParticles2D, simpler than GPU particles
    // for this small a burst) at a global position - works fine as a direct
    // child of a Control since Control is still a CanvasItem. Texture is a
    // procedural radial dot (same GradientTexture2D technique as the energy
    // orbs/vignette), so no external asset is needed.
    private void SpawnHitSpark(Vector2 globalPos, Color tint)
    {
        _sparkTexture ??= BuildSparkTexture();
        var particles = new CpuParticles2D
        {
            Position = ToLocalPoint(globalPos),
            Emitting = false,
            OneShot = true,
            Amount = SettingsManager.Instance.ReduceMotion ? 5 : 14,
            Lifetime = 0.35,
            Texture = _sparkTexture,
            Direction = Vector2.Up,
            Spread = 180f,
            InitialVelocityMin = 60f,
            InitialVelocityMax = 160f,
            ScaleAmountMin = 0.4f,
            ScaleAmountMax = 0.9f,
            Color = tint,
            Gravity = new Vector2(0, 200f),
        };
        AddChild(particles);
        particles.Emitting = true;
        var tween = particles.CreateTween();
        tween.TweenInterval(particles.Lifetime + 0.1);
        tween.TweenCallback(Callable.From(particles.QueueFree));
    }

    private static Texture2D BuildSparkTexture()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0f, 1f },
            Colors = new Color[] { Colors.White, new Color(1, 1, 1, 0) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 24,
            Height = 24,
        };
    }

    // Soft elliptical contact shadow beneath the player sprite - same
    // technique EnemyView.BuildShadowTexture uses for enemy sprites (a
    // non-square radial gradient reads as an ellipse, not a circle).
    private static Texture2D BuildShadowTexture()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0f, 1f },
            Colors = new Color[] { new(0f, 0f, 0f, 0.55f), new(0f, 0f, 0f, 0f) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 20,
        };
    }

    // Tapering stroke from attacker to target, fading fast - reads as a
    // directional slash better than a particle cloud would for a melee hit.
    private void PlaySlashTrail(Vector2 fromGlobal, Vector2 toGlobal)
    {
        var line = new Line2D
        {
            Width = 10f,
            DefaultColor = new Color(1f, 1f, 1f, 0.85f),
        };
        line.AddPoint(ToLocalPoint(fromGlobal));
        line.AddPoint(ToLocalPoint(toGlobal));
        AddChild(line);
        var tween = line.CreateTween();
        tween.TweenProperty(line, "modulate:a", 0f, 0.18).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(line.QueueFree));
    }

    // Blue tint pulse for block gain, mirroring FlashHit's red damage pulse.
    private static void FlashBlock(CanvasItem target)
    {
        var original = target.Modulate;
        var tween = target.CreateTween();
        tween.TweenProperty(target, "modulate", new Color(0.55f, 0.8f, 1f), 0.06);
        tween.TweenProperty(target, "modulate", original, 0.14);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _combat.CancelTargeting();
            ClearArmedTarget();
        }
        else if (@event is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    _combat.CancelTargeting();
                    ClearArmedTarget();
                    break;
                case Key.Left:
                    OnKeyboardCycle(-1);
                    break;
                case Key.Right:
                    OnKeyboardCycle(1);
                    break;
                case Key.Space:
                    OnKeyboardConfirm();
                    break;
                case Key.Enter:
                case Key.KpEnter:
                    OnEndTurnRequested();
                    break;
                case Key.Key1: OnKeyboardSelectByNumber(0); break;
                case Key.Key2: OnKeyboardSelectByNumber(1); break;
                case Key.Key3: OnKeyboardSelectByNumber(2); break;
                case Key.Key4: OnKeyboardSelectByNumber(3); break;
                case Key.Key5: OnKeyboardSelectByNumber(4); break;
                case Key.Key6: OnKeyboardSelectByNumber(5); break;
                case Key.Key7: OnKeyboardSelectByNumber(6); break;
                case Key.Key8: OnKeyboardSelectByNumber(7); break;
                case Key.Key9: OnKeyboardSelectByNumber(8); break;
                case Key.Key0: OnKeyboardSelectByNumber(9); break;
            }
        }
    }

    // Shared by CycleCardSelection and OnKeyboardSelectByNumber - swaps the
    // CardView highlight (CardView.SetHighlighted) from whatever was
    // selected before onto the new card, or clears it entirely for null.
    private void SelectCard(CardInstance? card)
    {
        if (_keyboardSelectedCard is { } previous && _cardViews.TryGetValue(previous, out var previousView))
        {
            previousView.SetHighlighted(false);
        }
        _keyboardSelectedCard = card;
        if (card is not null && _cardViews.TryGetValue(card, out var view))
        {
            view.SetHighlighted(true);
        }
    }

    private void CycleCardSelection(int direction)
    {
        var hand = _combat.Player.Piles.Hand;
        if (hand.Count == 0) return;

        int currentIndex = _keyboardSelectedCard is null ? -1 : hand.IndexOf(_keyboardSelectedCard);
        int nextIndex = currentIndex < 0 ? 0 : Mathf.PosMod(currentIndex + direction, hand.Count);
        SelectCard(hand[nextIndex]);
    }

    private void CycleEnemyTarget(int direction)
    {
        var enemies = _combat.Enemies;
        if (enemies.Count == 0) return;

        int currentIndex = _keyboardTargetEnemy is null ? -1 : enemies.IndexOf(_keyboardTargetEnemy);
        int nextIndex = currentIndex < 0 ? 0 : Mathf.PosMod(currentIndex + direction, enemies.Count);

        if (currentIndex >= 0 && _enemyViews.TryGetValue(enemies[currentIndex], out var previousView))
        {
            previousView.SetTargetLocked(false);
        }
        _keyboardTargetEnemy = enemies[nextIndex];
        if (_enemyViews.TryGetValue(_keyboardTargetEnemy, out var view)) view.SetTargetLocked(true);
    }

    private void OnKeyboardCycle(int direction)
    {
        if (_combat.State != CombatState.PlayerTurn) return;
        if (_armedCard is not null) CycleEnemyTarget(direction);
        else CycleCardSelection(direction);
    }

    private void OnKeyboardSelectByNumber(int handIndex)
    {
        if (_combat.State != CombatState.PlayerTurn) return;
        ClearArmedTarget();

        var hand = _combat.Player.Piles.Hand;
        if (handIndex < hand.Count) SelectCard(hand[handIndex]);
    }

    private void OnKeyboardConfirm()
    {
        if (_combat.State != CombatState.PlayerTurn) return;

        if (_armedCard is { } armed)
        {
            if (_keyboardTargetEnemy is { } target && _combat.TryPlayCard(armed, target))
            {
                AudioManager.Instance?.PlaySfx("card_play");
            }
            ClearArmedTarget();
            SelectCard(null);
            return;
        }

        if (_keyboardSelectedCard is not { } card) return;

        if (card.Definition.Target == CardTargetType.SingleEnemy)
        {
            if (_combat.Enemies.Count == 0) return;
            _armedCard = card;
            _keyboardTargetEnemy = _combat.Enemies[0];
            if (_enemyViews.TryGetValue(_keyboardTargetEnemy, out var view)) view.SetTargetLocked(true);
            return;
        }

        if (_combat.TryPlayCard(card))
        {
            AudioManager.Instance?.PlaySfx("card_play");
        }
        SelectCard(null);
    }

    // Shared by the End Turn button's Pressed signal and the Enter/Kp Enter
    // keybinding, so ending a keyboard-played turn doesn't require reaching
    // for the mouse. The button itself is already Disabled outside
    // PlayerTurn (RefreshStateUi), but the keybinding bypasses that, so this
    // re-checks state itself.
    private void OnEndTurnRequested()
    {
        if (_combat.State != CombatState.PlayerTurn) return;
        AudioManager.Instance?.PlaySfx("ui_click");
        _combat.TryEndTurn();
    }

    // Un-arms a pending single-enemy target (Escape/right-click, or a
    // second Space confirming it) without touching _keyboardSelectedCard -
    // canceling a target keeps the card itself selected.
    private void ClearArmedTarget()
    {
        if (_armedCard is null) return;
        if (_keyboardTargetEnemy is { } enemy && _enemyViews.TryGetValue(enemy, out var view))
        {
            view.SetTargetLocked(false);
        }
        _armedCard = null;
        _keyboardTargetEnemy = null;
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
        _goldLabel.Text = $"Gold: {RunState.Gold}";
        PopupDelta(player, this, _playerHpBar.GlobalPosition);
        _playerHpBar.MaxValue = player.MaxHp;
        _playerGhostHpBar.MaxValue = player.MaxHp;
        ChromeStyles.TweenHpBar(_playerHpBar, _playerGhostHpBar, ref _playerGhostHpTween, player.CurrentHp);
        _playerHpLabel.Text = $"{player.CurrentHp}/{player.MaxHp}" +
                               (player.Block > 0 ? $"  🛡{player.Block}" : "");
        _energyLabel.Text = $"{player.CurrentEnergy}/{player.MaxEnergy}";
        RefreshEnergyPips(player.CurrentEnergy, player.MaxEnergy);
        _pileCountsLabel.Text =
            $"Draw {player.Piles.DrawPile.Count} · Discard {player.Piles.Discard.Count} · Exhaust {player.Piles.Exhaust.Count}";
        StatusRow.Populate(_playerStatusRow, player, 20, _lastPlayerStatuses);
        _lastPlayerStatuses = new Dictionary<StatusType, int>(player.Statuses);
    }

    // Rebuilt fully each refresh, same as the (non-animated) relic/potion
    // bars - unlike hand/enemy views, these pips have no animation state
    // worth preserving across refreshes. _pulseEnergyPipsOnNextRefresh (set
    // by OnCombatStateChanged only on an actual turn start, not every
    // refresh) gives filled pips a staggered pop-in for that one call
    // instead of the usual instant snap.
    private void RefreshEnergyPips(int current, int max)
    {
        foreach (var child in _energyRow.GetChildren())
        {
            _energyRow.RemoveChild(child);
            child.QueueFree();
        }

        bool pulse = _pulseEnergyPipsOnNextRefresh;
        _pulseEnergyPipsOnNextRefresh = false;

        _energyOrbTexture ??= BuildEnergyOrbTexture();
        for (int i = 0; i < max; i++)
        {
            bool filled = i < current;
            var pip = new TextureRect
            {
                Texture = _energyOrbTexture,
                CustomMinimumSize = new Vector2(24, 24),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = filled ? Colors.White : new Color(1, 1, 1, 0.25f),
                MouseFilter = MouseFilterEnum.Ignore,
                PivotOffset = new Vector2(12, 12),
            };
            _energyRow.AddChild(pip);

            if (pulse && filled)
            {
                pip.Scale = Vector2.Zero;
                var tween = pip.CreateTween();
                tween.TweenInterval(i * 0.05);
                tween.TweenProperty(pip, "scale", Vector2.One, 0.2).SetTrans(Tween.TransitionType.Back);
            }
        }
    }

    // Procedural radial glow (no external asset needed) standing in for the
    // "glowing blue crystal" energy pips until Phase 8's chrome pass.
    private static Texture2D BuildEnergyOrbTexture()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0f, 0.6f, 1f },
            Colors = new Color[]
            {
                new(0.75f, 0.9f, 1f, 1f),
                new(0.25f, 0.55f, 0.95f, 1f),
                new(0.1f, 0.25f, 0.6f, 0f),
            },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 48,
            Height = 48,
        };
    }

    private void RefreshHand()
    {
        var hand = _combat.Player.Piles.Hand;
        var handSet = new HashSet<CardInstance>(hand);

        // A keyboard-selected/armed card removed from hand by something
        // other than OnKeyboardConfirm (e.g. a relic/effect discard) would
        // otherwise leave _keyboardSelectedCard/_armedCard pointing at a
        // CardInstance no CardView tracks anymore.
        if (_keyboardSelectedCard is not null && !handSet.Contains(_keyboardSelectedCard))
        {
            ClearArmedTarget();
            _keyboardSelectedCard = null;
        }

        // Drop tracking for any card no longer in hand. Two distinct cases:
        // - Still parented under _handArea: nobody's handled its exit yet
        //   (e.g. a bulk end-of-turn discard) - fly it to the matching pile
        //   anchor and fade it out instead of just vanishing.
        // - Already reparented elsewhere: this is the "played" case -
        //   CardView.OnReleased already reparents itself to the screen root
        //   *before* TryPlayCard mutates the hand, specifically so its own
        //   PlayResolveTween survives this Refresh() - just drop the dict
        //   entry and leave the node alone.
        foreach (var (card, view) in _cardViews.ToList())
        {
            if (handSet.Contains(card)) continue;
            _cardViews.Remove(card);
            if (!IsInstanceValid(view)) continue;
            if (view.GetParent() == _handArea)
            {
                bool isExhaust = _combat.Player.Piles.Exhaust.Contains(card);
                var anchor = isExhaust ? _exhaustPileAnchor : _discardPileAnchor;
                view.PlayExitTween(anchor.GlobalPosition, isExhaust);
            }
        }

        // Add newly-drawn cards, update everyone's slot/live description.
        int n = hand.Count;
        // Target a total fan width that stays clear of the player HP/energy
        // column on the left and the End Turn button on the right, while
        // keeping per-card spacing within a readable-but-still-overlapping
        // range and never exceeding what actually fits in the hand area.
        // 760 (the original value here) undershoots this for the common
        // 4-5 card hand: outer cards rotate up to MaxFanRotationDeg, and a
        // rotated 224x308 card's bounding box is measurably wider than its
        // unrotated rect (~283 vs 224 at 12deg), so the leftmost card's
        // visual edge lands well left of its unrotated x - with 760 that
        // edge crept into PlayerHpFrame's column (offset_right=220 in the
        // .tscn). 620 keeps the worst-case (5-card) rotated bounding box
        // clear of it with margin to spare.
        const float FanSafeWidth = 620f;
        float spacing = HandFanLayout.ComputeSpacing(n, _handArea.Size.X, CardWidth, FanSafeWidth);
        float totalWidth = CardWidth + (n - 1) * spacing;
        float startX = (_handArea.Size.X - totalWidth) / 2f;

        for (int i = 0; i < n; i++)
        {
            var card = hand[i];
            bool isNew = false;
            if (!_cardViews.TryGetValue(card, out var cardView))
            {
                cardView = _cardViewScene.Instantiate<CardView>();
                _handArea.AddChild(cardView);
                _cardViews[card] = cardView;
                isNew = true;
            }
            // Always re-set (not just on creation): shown damage numbers
            // depend on live player Strength/Weak, which can change between
            // refreshes without this specific card being re-drawn.
            cardView.SetCardInstance(card);
            cardView.SetHotkeyNumber(i < 10 ? (i + 1) % 10 : null);

            // Fan: cards rotate outward from center and the outer cards sit
            // higher than the center one, like cards spread from a grip
            // point below the screen - center stays at baseline, edges lift.
            float t = n <= 1 ? 0.5f : (float)i / (n - 1);
            float centered = t - 0.5f;
            float rotationDeg = centered * 2f * MaxFanRotationDeg;
            float yOffset = FanArcHeight * (1f - Mathf.Cos(centered * Mathf.Pi));
            var pos = new Vector2(startX + i * spacing, FanBaseY + yOffset);
            cardView.SetHomeTransform(pos, rotationDeg, i);
            if (isNew)
            {
                cardView.PlayDrawTween(_drawPileAnchor.GlobalPosition, i * 0.04f);
            }
        }
    }

    private void RefreshEnemies()
    {
        var currentSet = new HashSet<EnemyCombatant>(_combat.Enemies);

        // Defensive: an armed keyboard target that died between key presses
        // (shouldn't normally happen mid-input, but a relic/effect could
        // kill something off-turn) shouldn't leave _armedCard/
        // _keyboardTargetEnemy pointing at a gone EnemyCombatant.
        if (_keyboardTargetEnemy is not null && !currentSet.Contains(_keyboardTargetEnemy))
        {
            _armedCard = null;
            _keyboardTargetEnemy = null;
        }

        // Death case: still tracked but no longer in Enemies (already
        // stripped by Enemies.RemoveAll(e => e.IsDead) before
        // CombatantsChanged fires) - play a death tween before removing.
        foreach (var (enemyCombatant, view) in _enemyViews.ToList())
        {
            if (currentSet.Contains(enemyCombatant)) continue;
            _enemyViews.Remove(enemyCombatant);
            if (!IsInstanceValid(view)) continue;
            view.PlayDeathTween(() =>
            {
                if (!IsInstanceValid(view)) return;
                if (view.GetParent() == _enemyRow) _enemyRow.RemoveChild(view);
                view.QueueFree();
            });
        }

        foreach (var enemy in _combat.Enemies)
        {
            if (!_enemyViews.TryGetValue(enemy, out var enemyView))
            {
                enemyView = _enemyViewScene.Instantiate<EnemyView>();
                enemyView.Combatant = enemy;
                _enemyRow.AddChild(enemyView);
                _enemyViews[enemy] = enemyView;
            }
            else
            {
                // Update in place - never destroy/recreate an existing
                // enemy's view, so idle/hit animations on it stay continuous.
                enemyView.Refresh();
            }
            PopupDelta(enemy, enemyView, new Vector2(30, 4));
        }
    }

    // Diffs a combatant's HP/Block against the last time Refresh() saw it,
    // spawning floating +/- text (and a hit flash on damage) for whatever
    // changed. popupParent/localSpawnPos let this work for both the player
    // (spawned under CombatScreen itself, at the HP label's position) and
    // enemies (spawned as a child of their freshly-rebuilt EnemyView).
    // hpDelta and blockDelta are correlated (not just branched on
    // independently) so a hit that Block fully absorbed (blockDelta<0,
    // hpDelta==0) reads as a distinct "blocked" beat instead of showing
    // nothing at all, and a hit that broke through remaining Block
    // (blockDelta<0 and hpDelta<0 together) still just plays the normal hit
    // reaction - the correlation only changes what visual plays, it never
    // reads engine state beyond the two fields already being diffed.
    private void PopupDelta(Combatant c, Node popupParent, Vector2 localSpawnPos)
    {
        if (_lastStats.TryGetValue(c, out var prev))
        {
            int hpDelta = c.CurrentHp - prev.Hp;
            int blockDelta = c.Block - prev.Block;

            if (hpDelta < 0)
            {
                bool bigHit = -hpDelta >= Mathf.Max(10, c.MaxHp * 0.2f);
                PlayHitReaction(popupParent, localSpawnPos, -hpDelta, bigHit);
            }
            else if (hpDelta > 0)
            {
                SpawnFloatingText(popupParent, localSpawnPos, $"+{hpDelta}", new Color(0.4f, 1f, 0.4f));
            }

            if (blockDelta > 0)
            {
                SpawnFloatingText(popupParent, localSpawnPos + new Vector2(0, 16),
                    $"+{blockDelta} Block", new Color(0.6f, 0.8f, 1f));
                if (popupParent is CanvasItem blockTarget) FlashBlock(blockTarget);
            }
            else if (blockDelta < 0 && hpDelta == 0)
            {
                PlayBlockAbsorbVfx(popupParent, localSpawnPos);
            }
        }
        _lastStats[c] = (c.CurrentHp, c.Block);
    }

    // On a big hit, hold every reaction (damage number, flash, shake,
    // lunge/slash/spark) behind a short real-time delay instead of firing
    // them all the instant the HP diff is seen - a lightweight stand-in for
    // "hit-stop" that reads as impact anticipation without touching
    // Engine.TimeScale (which would also affect CombatManager's enemy-turn
    // pacing delays and every other in-flight tween - too broad a hammer for
    // this). Small hits stay instant so combat doesn't feel sluggish.
    private const float HitStopSeconds = 0.06f;

    private void PlayHitReaction(Node popupParent, Vector2 localSpawnPos, int hpLost, bool bigHit)
    {
        void Fire()
        {
            if (popupParent is Node n && !IsInstanceValid(n)) return;
            SpawnFloatingText(popupParent, localSpawnPos, $"-{hpLost}", new Color(1f, 0.35f, 0.35f), bigHit);
            if (popupParent is CanvasItem hitTarget) FlashHit(hitTarget);
            PlayHitVfx(popupParent, hpLost);
        }

        if (bigHit) GetTree().CreateTimer(HitStopSeconds).Timeout += Fire;
        else Fire();
    }

    // Damage-direction VFX: player-attacks-enemy gets a lunge + slash trail
    // in addition to the target's recoil; enemy-attacks-player gets a
    // camera-shake-style hit on the player sprite instead. Both get a spark
    // burst and a screen shake scaled to how much HP was actually lost.
    private void PlayHitVfx(Node popupParent, int hpLost)
    {
        if (popupParent is EnemyView enemyView)
        {
            var center = enemyView.GlobalPosition + enemyView.Size / 2f;
            enemyView.PlayHitRecoil();
            PlayPlayerLungeToward(center);
            PlaySlashTrail(_playerSprite.GlobalPosition + _playerSprite.Size / 2f, center);
            SpawnHitSpark(center, new Color(1f, 0.5f, 0.35f));
            PlayScreenShake(hpLost);
        }
        else if (popupParent == this)
        {
            var center = _playerSprite.GlobalPosition + _playerSprite.Size / 2f;
            PlayPlayerHitShake();
            SpawnHitSpark(center, new Color(1f, 0.5f, 0.35f));
            PlayScreenShake(hpLost);
        }
    }

    // Block absorbed the hit entirely (no HP lost) - previously silent; a
    // "Blocked" text plus a metallic-blue flash/spark gives it the same
    // "something happened" feedback a landed hit already gets.
    private void PlayBlockAbsorbVfx(Node popupParent, Vector2 localSpawnPos)
    {
        SpawnFloatingText(popupParent, localSpawnPos, "Blocked!", new Color(0.6f, 0.8f, 1f));
        if (popupParent is CanvasItem target) FlashBlock(target);

        Vector2? center = popupParent switch
        {
            EnemyView enemyView => enemyView.GlobalPosition + enemyView.Size / 2f,
            CombatScreen => _playerSprite.GlobalPosition + _playerSprite.Size / 2f,
            _ => null,
        };
        if (center is { } c) SpawnHitSpark(c, new Color(0.5f, 0.75f, 1f));
    }

    private void SpawnFloatingText(Node parent, Vector2 localPos, string text, Color color, bool bigHit = false)
    {
        var floatingText = _floatingTextScene.Instantiate<FloatingText>();
        parent.AddChild(floatingText);
        floatingText.Play(text, color, localPos, bigHit);
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
        bool isPlayerTurn = _combat.State == CombatState.PlayerTurn;
        _endTurnButton.Disabled = !isPlayerTurn;

        // Don't let a keyboard selection/armed target carry a leftover
        // highlight into the enemy turn (or any other non-PlayerTurn state).
        if (!isPlayerTurn)
        {
            ClearArmedTarget();
            SelectCard(null);
        }

        bool nothingPlayable = isPlayerTurn &&
            _combat.Player.Piles.Hand.All(c => c.Definition.Cost > _combat.Player.CurrentEnergy);
        SetEndTurnPulsing(nothingPlayable);

        if (_combat.State == CombatState.CombatEnd)
        {
            _combatEndPanel.Visible = true;
            // Hand cards carry a per-card ZIndex (CardView.SetHomeTransform)
            // that's global to the canvas layer, not just tree order - past
            // the first card that's higher than this panel's default 0, so
            // without this it paints over (and eats clicks meant for) the
            // Continue button below. Ignore is permanent for the rest of
            // this scene's life since CombatEnd never returns to PlayerTurn.
            _combatEndPanel.ZIndex = 1000;
            foreach (var view in _cardViews.Values)
            {
                if (IsInstanceValid(view)) view.MouseFilter = MouseFilterEnum.Ignore;
            }
            _outcomeLabel.Text = _combat.Outcome == CombatOutcome.Win ? "Victory!" : "Defeated...";
        }
        else
        {
            _combatEndPanel.Visible = false;
        }
    }

    // Only starts/stops on an actual true<->false transition (tracked via
    // _endTurnPulsing) rather than every RefreshStateUi() call, so the
    // breathing loop doesn't visibly reset its phase on every single card
    // played while the condition stays true.
    private void SetEndTurnPulsing(bool shouldPulse)
    {
        if (shouldPulse == _endTurnPulsing) return;
        _endTurnPulsing = shouldPulse;
        _endTurnPulseTween?.Kill();

        if (!shouldPulse)
        {
            _endTurnButton.Modulate = Colors.White;
            return;
        }

        var tween = _endTurnButton.CreateTween();
        _endTurnPulseTween = tween;
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(_endTurnButton, "modulate", new Color(1f, 0.85f, 0.5f), 0.6);
        tween.TweenProperty(_endTurnButton, "modulate", Colors.White, 0.6);
    }

    // Folds this fight's tallies into the run-long RunStats that RunScore
    // reads at the end of the run. Counted on a loss too - the enemies the
    // player killed in the fight that finally got them still happened.
    // _statsFolded guards against a double Continue press double-counting.
    private bool _statsFolded;

    private void FoldCombatStatsIntoRun()
    {
        if (_statsFolded) return;
        _statsFolded = true;

        var stats = RunState.Stats;
        stats.EnemiesSlain += _combat.EnemiesKilled;
        if (_combat.LargestSingleHit >= RunScore.OverkillDamage) stats.OverkillEarned = true;
        if (_combat.MostCardsInOneTurn >= RunScore.ComboCards) stats.ComboEarned = true;

        if (_combat.Outcome != CombatOutcome.Win) return;

        if (CombatContext.IsBoss)
        {
            stats.BossesSlain++;
            if (!_combat.TookDamage) stats.PerfectBosses++;
        }
        else if (CombatContext.IsElite)
        {
            stats.ElitesSlain++;
            if (!_combat.TookDamage) stats.PerfectElites++;
        }
    }

    private void OnContinuePressed()
    {
        FoldCombatStatsIntoRun();

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
        // Same unlock-filtered pool ShopScreen and the random-card event
        // outcome draw from (MetaProgressionManager.UnlockTrack).
        var pool = MetaProgressionManager.Instance.UnlockedCards().ToList();
        var rng = RngStreams.Shop;
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(count).ToList();
    }
}
