using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class CombatScreen : Control
{
    private const float CardWidth = 176f;
    private const float CardHeight = 240f;
    // Fan layout: cards overlap by up to ~55% of their width (shrinking
    // further if the hand is too wide to fit), rotate up to MaxFanRotationDeg
    // at the outer edges, and arc so the outer cards sit *lower* than the
    // center one - see RefreshHand() for the actual formula.
    private const float MaxFanRotationDeg = 12f;
    private const float FanArcHeight = 16f;
    // HandArea's own rect only needs to be wide enough for the fan-width
    // math below; its top edge sits well below where cards actually rest -
    // this pulls the resting fan up so cards stay inside the 648-tall
    // viewport instead of hanging off the bottom edge.
    //
    // Two constraints squeeze these two numbers from opposite ends, and each
    // one has already been violated once.
    //
    // Top: HandArea's top is y=460, so the center card - the highest, since the
    // arc pushes outward-and-down - rests at 460+FanBaseY=372 and clears
    // EnemyRow's bottom (y=330). At the old 224x308 card size and FanBaseY=-140
    // the card top landed at 320, *above* the enemy row, which is why a
    // five-card hand occluded nearly all of the act-3 boss.
    //
    // Bottom: a card is 240 tall and rotated about its own center, so the
    // outer card's bottom-left corner - where the hotkey badge lives - sits
    // 88*sin12 + 120*cos12 = 136px below that center, i.e. 256px below its top
    // edge. FanBaseY=-72 with a 36px arc put the outer card top at 424 and that
    // corner at y=680, 32px past the canvas: the leftmost card was sliced by
    // the bottom of the screen and its "1" badge never rendered at all. Lifting
    // to -88 and flattening the arc to 16 puts the outer top at 388 and the
    // corner at 644. HandLayoutSmokeTest asserts both ends.
    private const float FanBaseY = -88f;
    // The highest any card's top edge may reach, asserted by
    // HandLayoutSmokeTest so this cannot silently regress. The arc *adds* to
    // pos.Y (down the screen), so this is the center card with no arc term -
    // it used to subtract FanArcHeight, describing a fan curving the other way,
    // and only passed because guessing too high is the safe direction here.
    public const float EnemyRowBottomY = 330f;
    public static float HighestCardTopY => 460f + FanBaseY;
    // The same edge for a card the player is *looking at*, which is the one
    // that matters to anything sharing the band above the fan. A hovered or
    // arrow-key-selected card scales about its own centre and jumps to
    // ZIndex 100, so it reaches half the growth above its resting top - 18px -
    // and paints over whatever is there. TargetHintLabel sits in that band and
    // was measured against the resting top, which is 18px of clearance that
    // does not exist.
    public static float HighestHoveredCardTopY =>
        HighestCardTopY - CardHeight * (CardView.HoverScaleFactor - 1f) / 2f;
    // The lowest a card's rotated corner may reach: the design canvas floor.
    // Paired with the above so one test can bracket the fan from both sides.
    public const float CanvasBottomY = 648f;

    private CombatManager _combat = null!;
    private HBoxContainer _enemyRow = null!;
    private Control _handArea = null!;
    private HBoxContainer _potionBelt = null!;
    private GridContainer _relicBar = null!;
    private Label _goldLabel = null!;
    private ProgressBar _playerHpBar = null!;
    private ProgressBar _playerGhostHpBar = null!;
    private Tween? _playerGhostHpTween;
    private Label _playerHpLabel = null!;
    private HBoxContainer _energyRow = null!;
    private Label _energyLabel = null!;
    private HBoxContainer _playerStatusRow = null!;
    private Label _targetHintLabel = null!;
    private Button _endTurnButton = null!;
    private Control _combatEndPanel = null!;
    private Label _outcomeLabel = null!;
    private Button _continueButton = null!;
    // The top-right pile strip. Held (rather than fire-and-forget like the
    // other screens' DeckViewButtons.Attach calls) because drawn and
    // discarded cards fly to and from the matching counter - the three
    // *PileAnchor Control nodes that used to encode those positions a second
    // time in the .tscn are gone.
    private PileCounterBar _pileCounters = null!;
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
    private SpriteAnimator? _playerAnimator;
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

    // The widest an enemy's cell is allowed to get, and what one gets whenever
    // the row can afford it. Named here rather than read back off the scene
    // because FitEnemiesToTheRow overwrites that very property, so after the
    // first call the scene's value is no longer there to read.
    //
    // Derived from the longest name in the content, not picked: 20 characters
    // ("The Emberforge Smith", "The Sable Inquisitor") at Silkscreen-Bold 24 is
    // ~370px plus the display face's 4px outline. This was EnemyView.tscn's
    // authored 220 until a playtest screenshotted a lone boss reading
    // "CROWN REA" - 220 fits about eleven glyphs, so every boss in the game was
    // being trimmed while 580px of the row sat empty beside it.
    private const float EnemyViewMaxWidth = 400f;

    // How many relics a boss offers. Three, matching the card reward this screen
    // already pays and RewardScreen's picker already lays out - see
    // PickRewardRelics for why the boss is the only site that gets a choice.
    private const int BossRelicChoices = 3;

    // How many cards a won fight offers. Named rather than left as the literal
    // it was because BalanceModel has to read it: the skip streak's payoff is
    // "the chance of a Rare among the cards you are shown", which is not
    // answerable without this number. A copy of it in the analyser is the
    // ShopRelicPrice = 150 hazard, and this is the cheaper end of it.
    public const int RewardCardChoices = 3;

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
        // Per-act backdrop, so a fight in the Ember Reach doesn't look like the
        // same room as a fight in the Sunken Ward.
        var act = RunState.CurrentAct;
        ScreenBackground.AttachCombat(this, act.CombatBackground, Color.FromString(act.CombatTint, Colors.White));
        _pileCounters = DeckViewButtons.Attach(this, includeCombatPiles: true);
        _combat = GetNode<CombatManager>("CombatManager");
        _enemyRow = GetNode<HBoxContainer>("EnemyRow");
        _handArea = GetNode<Control>("HandArea");
        _potionBelt = GetNode<HBoxContainer>("PotionBelt");
        // Gold, relics and player statuses now live in one shrink-to-fit
        // column (TopLeftColumn) rather than as three fixed 280px-wide
        // panels. Each row was a wide bordered box holding one or two small
        // items, which read as an empty HUD; the relic bar's framing moved
        // down a level, to one slot per relic (see RefreshRelics).
        //
        // TopLeftColumn is capped at x=168 in the .tscn, 8px clear of
        // EnemyRow's x=176, and the relic bar is a 3-wide GridContainer so it
        // grows *downward* into that strip instead of rightward across the
        // fight. It used to be a single HBoxContainer inside a 460px-wide
        // column, and TopLeftColumn is declared after EnemyRow, so it painted
        // on top: slots are 40px on a 44px pitch, so the bar reached x=20+44n,
        // and from 5 relics on it covered the leftmost enemy's left bezel in a
        // 3-enemy fight (that enemy starts at x=242) - including the
        // target-lock glow, which is that Button's own background and so had
        // nothing left to show. Three columns is what fits: 3*40 + 2*4 = 128
        // of the 148px available. CombatTargetingSmokeTest asserts the
        // no-overlap property directly, at 3 enemies and 8 relics.
        _relicBar = GetNode<GridContainer>("TopLeftColumn/RelicBar");
        _goldLabel = GetNode<Label>("TopLeftColumn/GoldPanel/GoldLabel");
        _playerHpBar = GetNode<ProgressBar>("PlayerHpFrame/HpBar");
        _playerGhostHpBar = GetNode<ProgressBar>("PlayerHpFrame/GhostHpBar");
        _playerHpLabel = GetNode<Label>("PlayerHpFrame/HpLabel");
        _energyRow = GetNode<HBoxContainer>("EnergyRow");
        _energyLabel = GetNode<Label>("EnergyLabel");
        _playerStatusRow = GetNode<HBoxContainer>("TopLeftColumn/PlayerStatusRow");
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
        // No death or escape clip: a player death is RunEndScreen, and the
        // player never flees a fight.
        _playerAnimator = SpriteAnimator.Attach(_playerSprite, "player", "idle", "windup", "hit");
        StartPlayerIdleBob();

        ChromeStyles.ApplyHpBarStyle(_playerHpBar, _playerGhostHpBar);
        GetNode<PanelContainer>("TopLeftColumn/GoldPanel").AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        _playerHpLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _energyLabel.ThemeTypeVariation = "CombatDisplayLabel";
        // The energy number sits *on* the orb now rather than under it, and
        // CombatDisplayLabel's cream-on-dark default is low contrast against
        // the gem's lit B4/B5 facets. Flip it: near-black glyphs with a pale
        // outline, which is the highest-contrast pair available on a light
        // blue field.
        _energyLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N0);
        _energyLabel.AddThemeColorOverride("font_outline_color", PixelSpec.Ramp.B5);
        _turnBannerLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _turnBannerLabel.AddThemeFontSizeOverride("font_size", 32);
        _turnBannerPanel.AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        _turnBannerRestPos = _turnBannerPanel.Position;

        ChromeStyles.ApplyEmphasisButtonStyle(_endTurnButton);

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

    // One bordered slot per relic, rather than every relic loose inside one
    // fixed-width panel. The panel was 280px wide and a run typically carries
    // one to four 34px relics in it, so it read as an empty box with something
    // small in the corner; per-relic framing makes the row exactly as wide as
    // the relics you actually own and gives each one a readable edge against
    // the backdrop.
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
            var slot = new PanelContainer { TooltipText = tooltip, MouseFilter = MouseFilterEnum.Stop };
            slot.AddThemeStyleboxOverride("panel", ChromeStyles.SlotStyle(filled: true));
            _relicBar.AddChild(slot);

            if (ArtAssets.RelicIcon(relic.Definition.Id) is { } icon)
            {
                // 34x34 before, which is off-grid: an icon authored on the
                // 32px grid drawn at 34px is a 1.0625x scale, the fractional
                // scaling docs/ART_SPEC.md section 2 exists to forbid.
                var rect = new TextureRect { Texture = icon, MouseFilter = MouseFilterEnum.Ignore };
                PixelSpec.ApplyPixelRect(rect, PixelSpec.IconGrid, PixelSpec.HudIconScale);
                slot.AddChild(rect);
            }
            else
            {
                slot.AddChild(new Label
                {
                    Text = relic.Definition.Name,
                    MouseFilter = MouseFilterEnum.Ignore,
                });
            }
        }
    }

    // The player's breathing is a frame clip now (SpriteAnimator), not a
    // Position tween.
    //
    // The old one slid the sprite 6px and back on a Sine curve, and Position
    // *was* safe here in the layout sense - PlayerSprite is a direct child of
    // the CombatScreen root, not Container-managed, which is why this screen
    // and EnemyView ended up animating the same medium two different ways. It
    // was not safe in the pixel sense: the sprite renders at PixelSpec.
    // SpriteScale, so 6 canvas px is 1.2 source pixels and every intermediate
    // value of that tween put the art on a fractional offset. A pixel sprite
    // that drifts off its own grid shimmers - saint11's "Subpixel" tutorial is
    // this exact failure.
    private void StartPlayerIdleBob()
    {
        _playerSprite.Position = _playerSpriteRestPos;
        _playerAnimator?.PlayIdle();
    }

    // Shared driver for any one-off PlayerSprite Position beat (hit-shake,
    // attack lunge) and the single funnel every such beat goes through, so the
    // snap below cannot be bypassed by a new call site.
    //
    // A lunge genuinely crosses the screen toward a target, so unlike the idle
    // bob it cannot become a frame clip - the travel is the point. What it can
    // do is land only on offsets that keep the art pixel-aligned, which is what
    // SnapToPixelGrid buys.
    private void PlayPlayerPositionBeat(List<Vector2> waypoints, float stepDuration)
    {
        var tween = _playerSprite.CreateTween();
        foreach (var wp in waypoints)
        {
            tween.TweenProperty(_playerSprite, "position", SnapToPixelGrid(wp), stepDuration);
        }
        tween.TweenProperty(_playerSprite, "position", _playerSpriteRestPos, stepDuration);
        tween.TweenCallback(Callable.From(StartPlayerIdleBob));
    }

    // Round a destination to a whole source pixel, measured from the sprite's
    // rest position rather than from canvas zero: the offset is what the eye
    // reads against the backdrop, and PlayerSprite's rest position is not
    // itself a multiple of SpriteScale.
    //
    // This only pins the tween's *endpoints*. Godot still interpolates between
    // them, so a lunge passes through fractional offsets while it travels -
    // which is the accepted cost of having travel at all, and is invisible at
    // speed in a way a 2.6-second idle loop sitting on one is not.
    private Vector2 SnapToPixelGrid(Vector2 position)
    {
        var offset = position - _playerSpriteRestPos;
        return _playerSpriteRestPos + new Vector2(
            Mathf.Round(offset.X / PixelSpec.SpriteScale) * PixelSpec.SpriteScale,
            Mathf.Round(offset.Y / PixelSpec.SpriteScale) * PixelSpec.SpriteScale);
    }

    // The shake carries the displacement, the clip carries the flash and the
    // recoil pose. Both, because a shake alone reads as the camera moving and
    // a flash alone reads as a tint - the two together are what saint11's
    // "Impact" card puts side by side.
    private void PlayPlayerHitShake()
    {
        _playerAnimator?.Play("hit");
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
        _playerAnimator?.Play("windup");
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

    // One StringName per slot, built once. IsActionPressed takes a StringName,
    // so composing "hd_card_" + i inside _UnhandledInput would allocate on
    // every key press - and this runs for mouse motion too.
    private static readonly StringName[] CardSlotActions =
        Enumerable.Range(1, 10).Select(i => new StringName($"hd_card_{i}")).ToArray();

    private static readonly StringName[] PotionSlotActions =
        Enumerable.Range(1, RunState.MaxPotionSlots).Select(i => new StringName($"hd_potion_{i}")).ToArray();

    // Named actions from project.godot's [input] rather than raw keycodes, so
    // every binding lives in one place. InputEvent.IsActionPressed defaults
    // allow_echo to false, which is what keeps key repeat from auto-firing
    // card cycling - the reason DeckViewKeybindListener already filtered Echo.
    // Every branch that consumes an event marks it handled, which the old
    // keycode switch never did.
    public override void _UnhandledInput(InputEvent @event)
    {
        // hd_cancel carries right-click as well as Escape, so the two paths
        // that used to be separate branches are one action now.
        if (@event.IsActionPressed("hd_cancel"))
        {
            _combat.CancelTargeting();
            ClearArmedTarget();
            GetViewport().SetInputAsHandled();
            return;
        }

        // At CombatEnd the hand is inert and the only thing left to do is
        // leave, so confirm *and* end-turn both press Continue - otherwise a
        // fight played start to finish on the keyboard ends by reaching for
        // the mouse for exactly one button. Handled here rather than by
        // focusing the button, so _Ready's FocusModeEnum.None reasoning (and
        // the arrow-key cycling it protects) is untouched. Returns without
        // consuming anything else, which leaves the pile keys (a separate
        // node, _UnhandledKeyInput) working for a post-fight deck look.
        if (_combat.State == CombatState.CombatEnd)
        {
            if (@event.IsActionPressed("hd_confirm") || @event.IsActionPressed("hd_end_turn"))
            {
                // Marked handled BEFORE acting, unlike every other branch
                // here: OnContinuePressed leaves through RunManager
                // .ChangeScreen, and this node cannot be assumed to still be
                // addressable on the other side of a scene change - GetViewport
                // came back null and threw.
                GetViewport().SetInputAsHandled();
                AudioManager.Instance?.PlaySfx("ui_click");
                OnContinuePressed();
            }
            return;
        }

        if (@event.IsActionPressed("hd_nav_left")) OnKeyboardCycle(-1);
        else if (@event.IsActionPressed("hd_nav_right")) OnKeyboardCycle(1);
        else if (@event.IsActionPressed("hd_confirm")) OnKeyboardConfirm();
        else if (@event.IsActionPressed("hd_end_turn")) OnEndTurnRequested();
        else if (!TryHandleSlotAction(@event)) return;

        GetViewport().SetInputAsHandled();
    }

    // The card and potion belts are both "press the key matching the slot
    // number", so they share one loop rather than a twenty-arm switch.
    private bool TryHandleSlotAction(InputEvent @event)
    {
        for (int i = 0; i < CardSlotActions.Length; i++)
        {
            if (!@event.IsActionPressed(CardSlotActions[i])) continue;
            OnKeyboardSelectByNumber(i);
            return true;
        }

        for (int i = 0; i < PotionSlotActions.Length; i++)
        {
            if (!@event.IsActionPressed(PotionSlotActions[i])) continue;
            OnPotionHotkey(i);
            return true;
        }

        return false;
    }

    // Mirrors PotionView.OnPressed exactly, so the belt's mouse path and the
    // Z/X/C keys converge on the same TryUsePotion call. A SingleEnemy potion
    // returns false there and transitions to AwaitingTarget instead, which
    // OnKeyboardCycle/OnKeyboardConfirm now aim the same way a card is aimed.
    private void OnPotionHotkey(int slotIndex)
    {
        if (_combat.State != CombatState.PlayerTurn) return;
        if (slotIndex >= RunState.Potions.Count) return;

        AudioManager.Instance?.PlaySfx("ui_click");
        _combat.TryUsePotion(RunState.Potions[slotIndex]);
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
        SetKeyboardTarget(enemies[nextIndex]);
    }

    // The single place _keyboardTargetEnemy changes: moves the target-lock
    // glow and re-renders the armed card's description for the newly aimed-at
    // enemy, so arrow-key targeting reads exactly like a mouse drag (which
    // does both in CardView.UpdateTargetHighlight). Keeping it in one method
    // is what stops the two input paths from drifting apart, same reasoning
    // as SetHighlighted covering hover and keyboard selection with one call.
    private void SetKeyboardTarget(EnemyCombatant? enemy)
    {
        if (_keyboardTargetEnemy is { } previous && _enemyViews.TryGetValue(previous, out var previousView))
        {
            previousView.SetTargetLocked(false);
        }
        _keyboardTargetEnemy = enemy;
        if (enemy is not null && _enemyViews.TryGetValue(enemy, out var view)) view.SetTargetLocked(true);

        if (_armedCard is { } armed && _cardViews.TryGetValue(armed, out var cardView) && IsInstanceValid(cardView))
        {
            cardView.RefreshDescriptionForTarget(enemy);
        }
    }

    private void OnKeyboardCycle(int direction)
    {
        // AwaitingTarget is the potion aiming sub-state (CombatManager
        // .TryUsePotion): the potion is already committed, so there is no
        // card to cycle - only the enemy it is about to hit. Without this
        // arm, a keyboard player who drank a single-target potion could only
        // ever cancel out of it.
        if (_combat.State == CombatState.AwaitingTarget)
        {
            CycleEnemyTarget(direction);
            return;
        }

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
        // The keyboard half of EnemyView's click-to-target, which is the only
        // way a potion's AwaitingTarget ever resolved before. TryTargetEnemy
        // drops back to PlayerTurn on its own; RefreshStateUi clears the glow
        // from there.
        if (_combat.State == CombatState.AwaitingTarget)
        {
            if (_keyboardTargetEnemy is { } potionTarget) _combat.TryTargetEnemy(potionTarget);
            return;
        }

        if (_combat.State != CombatState.PlayerTurn) return;

        if (_armedCard is { } armed)
        {
            if (_keyboardTargetEnemy is { } target) PlaySelectedCard(armed, target);
            ClearArmedTarget();
            SelectCard(null);
            return;
        }

        if (_keyboardSelectedCard is not { } card) return;

        if (card.Definition.Target == CardTargetType.SingleEnemy)
        {
            if (_combat.Enemies.Count == 0) return;
            _armedCard = card;
            SetKeyboardTarget(_combat.Enemies[0]);
            return;
        }

        PlaySelectedCard(card, null);
        SelectCard(null);
    }

    // Routes the keyboard through the card's own view so it takes
    // CardView.TryPlayFromHand - the same reparent-then-resolve-tween path a
    // mouse drop takes. Calling CombatManager.TryPlayCard directly (what this
    // used to do) left the node parented under the hand area, so RefreshHand
    // saw it as gone-from-hand and flew it to the discard pile: a card played
    // with Space animated as if it had been discarded.
    //
    // The direct call survives as a fallback for a card with no view on
    // screen, which should not happen but must not mean an unplayable card.
    private bool PlaySelectedCard(CardInstance card, EnemyCombatant? target)
    {
        if (_cardViews.TryGetValue(card, out var view) && IsInstanceValid(view))
        {
            return view.TryPlayFromHand(target);
        }

        if (!_combat.TryPlayCard(card, target)) return false;
        AudioManager.Instance?.PlaySfx("card_play");
        return true;
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
        // Before clearing _armedCard, so the card's description reverts from
        // "what this does to that enemy" back to its untargeted numbers.
        SetKeyboardTarget(null);
        _armedCard = null;
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
        // The "Draw N · Discard N · Exhaust N" label that used to live here is
        // gone: PileCounterBar's cells now carry those three numbers, and
        // printing them twice was the same double-encoding the energy pips had.
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

        // One large orb, not `max` small pips. The pip row encoded the same
        // number twice - once as pip count, once in the "1/3" label under it -
        // while reading as neither at a glance. A single orb carrying the
        // number is the genre's standard and scales to any max energy without
        // the row re-flowing.
        _energyOrbTexture ??= BuildEnergyOrbTexture();
        const int orbScale = 5; // 16px gem -> 80px, integer per ART_SPEC section 2
        var orb = new TextureRect
        {
            Texture = _energyOrbTexture,
            CustomMinimumSize = PixelSpec.SizeFor(16, orbScale),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
            // Dim only when spent, so "no energy left" reads without having
            // to parse the number.
            Modulate = current > 0 ? Colors.White : new Color(1, 1, 1, 0.35f),
            MouseFilter = MouseFilterEnum.Ignore,
            PivotOffset = PixelSpec.SizeFor(16, orbScale) / 2f,
        };
        _energyRow.AddChild(orb);

        if (pulse && current > 0)
        {
            orb.Scale = Vector2.Zero;
            orb.CreateTween()
                .TweenProperty(orb, "scale", Vector2.One, 0.22)
                .SetTrans(Tween.TransitionType.Back);
        }
    }

    // Procedural radial glow (no external asset needed) standing in for the
    // "glowing blue crystal" energy pips until Phase 8's chrome pass.
    // A faceted 16x16 pixel gem, drawn pixel by pixel and rendered at 4x.
    //
    // Replaces a 48x48 smooth radial GradientTexture2D. That was a soft
    // anti-aliased blob - fine beside a serif UI, wrong beside 32x32 sprites
    // and bitmap type (docs/ART_SPEC.md sections 3 and 5). Drawn rather than
    // sourced because it is the one HUD element with no icon behind it, and
    // it doubles as a small preview of what tools/artgen will do in bulk.
    //
    // Shape is an octagon, not a circle and not a diamond. A pixel circle at
    // 16px needs hand-tuned edges to avoid looking lumpy. A diamond is exact
    // at any radius but has very little interior: the energy number sits *on*
    // the orb, and at a diamond's waist "1/3" overflowed the shape on both
    // sides. An octagon is equally exact and leaves roughly twice the usable
    // area at the same footprint.
    //
    // Lighting is a single diagonal ramp - upper-left facets catch the light,
    // lower-right fall into shadow.
    private static Texture2D BuildEnergyOrbTexture()
    {
        const int size = 16;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        const float centre = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float square = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                float corner = Mathf.Abs(dx) + Mathf.Abs(dy);
                // Octagon = square with the corners cut off.
                if (square > 7.5f || corner > 10.5f) continue;

                bool isEdge = square > 6.5f || corner > 9.5f;
                // diagonal: negative = upper-left (lit), positive = lower-right
                float diagonal = dx + dy;
                var colour = isEdge ? PixelSpec.Ramp.N0
                    : diagonal < -4.5f ? PixelSpec.Ramp.B5
                    : diagonal < 0.5f ? PixelSpec.Ramp.B4
                    : diagonal < 5.0f ? PixelSpec.Ramp.B3
                    : PixelSpec.Ramp.B2;
                image.SetPixel(x, y, colour);
            }
        }

        return ImageTexture.CreateFromImage(image);
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
                var anchor = isExhaust ? _pileCounters.ExhaustCenter : _pileCounters.DiscardCenter;
                view.PlayExitTween(anchor, isExhaust);
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
            // lower than the center one, like cards spread from a grip point
            // below the screen - center rides highest, the edges fall away.
            // yOffset is positive at both edges and Godot's +y is down, which
            // is what makes the arc a fall rather than a lift; FanBaseY's
            // comment carries what that costs at the bottom of the canvas.
            float t = n <= 1 ? 0.5f : (float)i / (n - 1);
            float centered = t - 0.5f;
            float rotationDeg = centered * 2f * MaxFanRotationDeg;
            float yOffset = FanArcHeight * (1f - Mathf.Cos(centered * Mathf.Pi));
            var pos = new Vector2(startX + i * spacing, FanBaseY + yOffset);
            cardView.SetHomeTransform(pos, rotationDeg, i);
            if (isNew)
            {
                cardView.PlayDrawTween(_pileCounters.DrawCenter, i * 0.04f);
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

        // Gone case: still tracked but no longer in Enemies (already stripped
        // by CombatManager.ResolveDeaths before CombatantsChanged fires) - play
        // an exit tween before removing.
        //
        // Which exit is not derivable from "absent from the list", so it is
        // read off the combatant: HasEscaped survives the strip precisely so
        // this can tell the two apart. Playing the death tween for a runaway
        // would tell the player they got a kill they did not get.
        foreach (var (enemyCombatant, view) in _enemyViews.ToList())
        {
            if (currentSet.Contains(enemyCombatant)) continue;
            _enemyViews.Remove(enemyCombatant);
            if (!IsInstanceValid(view)) continue;
            void Remove()
            {
                if (!IsInstanceValid(view)) return;
                if (view.GetParent() == _enemyRow) _enemyRow.RemoveChild(view);
                view.QueueFree();
            }
            if (enemyCombatant.HasEscaped) view.PlayEscapeTween(Remove);
            else view.PlayDeathTween(Remove);
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

        FitEnemiesToTheRow();
    }

    // An HBoxContainer never shrinks a child below its custom_minimum_size - it
    // runs past its own rect instead - and EnemyView's authored 220 fits three
    // in EnemyRow's 800px band with nothing to spare. Summons made a fourth
    // possible, so the width has to come from the row rather than from the
    // scene.
    //
    // Widening the row was the first attempt and is the wrong fix: the band is
    // bounded on the left by the relic bar and on the right by the pile counter
    // strip, and pushing it right put it under the counters -
    // DeckViewSmokeTest.pile_counter_strip_does_not_overlap_enemy_row caught
    // that, which is the alarm working. Narrowing every enemy to 196 in the
    // scene was the second and is worse in the common case, where there are one
    // or two enemies and the space is there.
    //
    // So EnemyViewMaxWidth is a *maximum* and the real width is whatever the row
    // can give. Note the direction: this is shrink-only, and for a long time the
    // maximum was 220, which made the sentence above true of four enemies and a
    // lie about one - a lone boss got 220 of an 800px band. Raising the cap is
    // what makes it true at every count:
    //
    //   1 enemy  -> 400 (the cap)      3 enemies -> 261
    //   2 enemies -> 396               4 enemies -> 194 (available/count binds,
    //                                     unchanged, and where TrimChar earns
    //                                     its keep - see EnemyView.NameLabel)
    private void FitEnemiesToTheRow()
    {
        int count = _enemyRow.GetChildCount();
        if (count == 0) return;

        float separation = _enemyRow.GetThemeConstant("separation");
        float available = _enemyRow.Size.X - separation * (count - 1);
        float width = Mathf.Min(EnemyViewMaxWidth, available / count);

        foreach (var child in _enemyRow.GetChildren())
        {
            // Through SetCellWidth rather than by assigning CustomMinimumSize
            // here: the name's font size is a function of this same number, and
            // EnemyView cannot derive it from its own Size (Refresh runs before
            // the container sorts).
            if (child is EnemyView view) view.SetCellWidth(width);
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

    // Always RunState.MaxPotionSlots slots, empty ones included. The belt used
    // to be a fixed-width panel containing only the potions you happened to
    // hold, so carrying one potion drew a wide bordered box with a single icon
    // at its left edge - dead space that looked like a layout bug. Drawing the
    // empty slots makes the same width read as capacity, and tells the player
    // how many more potions they can pick up, which nothing else in combat
    // does.
    private void RefreshPotions()
    {
        foreach (var child in _potionBelt.GetChildren())
        {
            _potionBelt.RemoveChild(child);
            child.QueueFree();
        }

        for (int i = 0; i < RunState.MaxPotionSlots; i++)
        {
            // Slot index, not hand index: the belt is fixed-length, so slot 2
            // is always the same key whether or not slots 0 and 1 are full.
            var slotKey = ScreenKeyboardNav.KeyHint(PotionSlotActions[i]);

            if (i < RunState.Potions.Count)
            {
                var potionView = _potionViewScene.Instantiate<PotionView>();
                _potionBelt.AddChild(potionView);
                potionView.SetHotkeySlot(i);
                potionView.SetPotionInstance(RunState.Potions[i]);
                continue;
            }

            var empty = new PanelContainer
            {
                CustomMinimumSize = PotionView.SlotSize,
                TooltipText = slotKey.Length > 0 ? $"Empty potion slot ({slotKey})" : "Empty potion slot",
                MouseFilter = MouseFilterEnum.Stop,
            };
            empty.AddThemeStyleboxOverride("panel", ChromeStyles.SlotStyle(filled: false));
            _potionBelt.AddChild(empty);
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

        if (_combat.State == CombatState.AwaitingTarget)
        {
            // Seed the potion's target so there is a glow to confirm the
            // instant the belt is used, matching how OnKeyboardConfirm arms a
            // single-enemy card on Enemies[0]. Mouse users just click past it.
            if (_keyboardTargetEnemy is null && _combat.Enemies.Count > 0)
            {
                SetKeyboardTarget(_combat.Enemies[0]);
            }
        }
        else if (_armedCard is null)
        {
            // Nothing armed and not aiming a potion means no target-lock glow
            // should be on screen - covers the potion resolving, which returns
            // to PlayerTurn without going through ClearArmedTarget.
            SetKeyboardTarget(null);
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

    // Guards the whole Continue handler against being run twice. There are two
    // independent ways in - the button's Pressed signal and hd_confirm /
    // hd_end_turn in _UnhandledInput, which is deliberately *not* focus-based
    // (see the CombatEnd branch there) - and leaving through
    // RunManager.ChangeScreen is not instant, because ScreenFade holds the
    // scene up for the length of the fade. A click plus an Enter inside that
    // window used to award CombatContext.GoldReward twice and grant *two*
    // relics, since GrantRewardRelic adds to RunState.Relics as it picks.
    //
    // This started life as _statsFolded, covering only FoldCombatStatsIntoRun.
    // The stats were the half that had a guard and the rewards were the half
    // that needed one.
    private bool _continueResolved;

    // Folds this fight's tallies into the run-long RunStats that RunScore
    // reads at the end of the run. Counted on a loss too - the enemies the
    // player killed in the fight that finally got them still happened.
    private void FoldCombatStatsIntoRun()
    {
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
        if (_continueResolved) return;
        _continueResolved = true;

        FoldCombatStatsIntoRun();

        if (_combat.Outcome == CombatOutcome.Win)
        {
            RunState.PlayerCurrentHp = _combat.Player.CurrentHp;
            RunState.PlayerMaxHp = _combat.Player.MaxHp;

            // Only the *final* act's boss ends the run. Any earlier boss
            // advances to the next act and then hands out a boss reward -
            // AdvanceAct regenerates the map first, so RewardScreen's exit to
            // Map lands on the new act with no extra screen or flag needed.
            if (CombatContext.IsBoss && RunState.IsFinalAct)
            {
                // Banked here rather than offered, because this is the one win
                // with no reward screen behind it to claim from. The final
                // act's BossGold happens to be 0 today; relying on that instead
                // of this line would make a data edit silently eat the reward.
                RunState.Gold += CombatContext.GoldReward;
                RunEndContext.Outcome = RunEndOutcome.Win;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Victory);
            }
            else
            {
                // Every field below is an *offer*. Nothing here touches
                // RunState - RewardScreen's list is what grants, one row at a
                // time. Gold used to be banked a few lines above this and the
                // relic granted as it was picked, which made two of the four
                // rows things the player already had.
                //
                // Rolled before AdvanceAct below, which moves RunState.ActIndex
                // and so changes which act's rates RollPotionDrop would read.
                // A boss never rolls, so the ordering is inert today - it is
                // written this way so it stays inert if that ever changes.
                RewardContext.PotionDrop = RollPotionDrop();

                // Assigned unconditionally, so an act-clear banner can't be
                // left over from the last boss onto an ordinary fight's reward.
                RewardContext.ActCleared = CombatContext.IsBoss ? RunState.AdvanceAct() : null;
                RewardContext.CardChoices = SampleCardChoices(RewardCardChoices);
                RewardContext.GoldAwarded = CombatContext.GoldReward;
                RewardContext.RelicChoices = PickRewardRelics();
                RewardContext.Claimed.Clear();
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Reward);
            }
        }
        else
        {
            RunEndContext.Outcome = RunEndOutcome.Lose;
            RunManager.Instance.ChangeScreen(RunManager.ScreenState.Defeat);
        }
    }

    // Elite and boss fights guarantee a relic on top of the usual card/gold
    // reward, sampled from the dedicated Shop RNG stream.
    //
    // The boss branch is the whole point of relic tiers. Both fights used to
    // call one uniform draw over the whole database, so the act's final fight
    // paid out of the same pool a chest did - which is the ROADMAP line about a
    // boss handing over what 150 gold would have. A boss now draws the Boss
    // tier and nothing else; an elite draws the ordinary ladder. Same shape as
    // RollPotionDrop below, which also branches on IsBoss first.
    //
    // Picks, and stops there. This was GrantRewardRelic and added to
    // RunState.Relics in the same breath, which is why a double Continue press
    // used to hand over two (see _continueResolved) - and why the reward screen
    // was announcing a relic the player already had. Claiming the row is what
    // grants it now.
    //
    // A boss offers three and the player takes one. Being handed the relic that
    // decides a run's identity is not the same event as choosing it, and a boss
    // is the one site with room for the question: it never rolls a potion, so
    // the reward list has a free slot either way. An elite still offers exactly
    // one - three ladder relics twice an act would let a deck steer itself into
    // its own build, which is a different feature with a different curve.
    private static List<RelicDefinition> PickRewardRelics()
    {
        if (CombatContext.IsBoss)
            return RelicPool.Sample(RelicSite.Boss, BossRelicChoices, RngStreams.Shop);

        if (CombatContext.IsElite)
            return RelicPool.Sample(RelicSite.Reward, 1, RngStreams.Shop);

        return new List<RelicDefinition>();
    }

    // Whether this fight offers a potion, and which one. Returns the offer;
    // RewardScreen's tile is what actually grants it, so nothing here touches
    // RunState and re-running it cannot hand out two.
    //
    // A boss never rolls. It already guarantees a relic and, for acts I and II,
    // an act-clear banner carrying a max-HP bonus and a heal - a third reward
    // on that screen makes the guaranteed one read as noise. Combat and Elite
    // roll at rates authored per act beside the gold they pay, because how much
    // a room gives is a pacing dial and act I is where the belt is emptiest.
    //
    // BalanceModel.NodePotionPercent mirrors this switch. The two have to agree
    // or the report prices a drop table the game does not play.
    private static PotionDefinition? RollPotionDrop()
    {
        if (CombatContext.IsBoss) return null;

        var act = RunState.CurrentAct;
        int authored = CombatContext.IsElite ? act.ElitePotionDropPercent : act.PotionDropPercent;

        // The ascension rung is applied here and mirrored in
        // BalanceModel.NodePotionPercent, which this method's own comment
        // already says has to keep mirroring it. Both go through
        // AscensionModifiers.PotionPercent rather than each subtracting, so the
        // mirror is one function rather than two subtractions that agree today.
        int percent = RunState.Ascension.PotionPercent(authored);
        if (RngStreams.Drops.Next(100) >= percent) return null;

        return PotionPool.SampleOne(PotionDatabase.All, RngStreams.Drops);
    }

    // The one site in the game that draws cards on a boosted table.
    //
    // Same unlock-filtered pool ShopScreen and the random-card event outcome
    // draw from (MetaProgressionManager.UnlockTrack), and the same rarity
    // weighting - this used to be a uniform shuffle here, which made a Rare
    // exactly as likely as a Strike.
    //
    // What is different is the streak: RunState.CardSkipStreak is how many
    // rewards in a row the player has declined, and it shifts this draw's
    // weights out of Common (CardPool.WeightOf). The other two grant sites pass
    // no streak and are unchanged, which is what makes the streak a property of
    // *skipping a fight reward* rather than a global rarity dial - a shop that
    // got richer because the player walked past two cards would be pricing
    // something it had no part in.
    private static List<CardDefinition> SampleCardChoices(int count)
    {
        return CardPool.Sample(MetaProgressionManager.Instance.UnlockedCards(), count,
            RngStreams.Shop, RunState.CardSkipStreak);
    }
}
