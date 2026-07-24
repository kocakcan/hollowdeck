using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Hover (scale tween) + manual drag on a Control-based card. Home position is
// tracked and laid out manually by whoever spawns this (CombatScreen), rather
// than via a real Container - that sidesteps the Container-vs-manual-Position
// conflict a real HBoxContainer would cause during drag (the seam Phase 0's
// prototype flagged), at the cost of the spawner doing its own row layout.
//
// On release: for a SingleEnemy card, hit-test EnemyView.Instances against
// the drop position and target whichever enemy (if any) the card was
// dropped on - same drag-to-target feel as the genre reference. Dropped
// with no enemy under it, or rejected by CombatManager (wrong state, not
// enough energy), it snaps back to home. If it resolved, it plays a quick
// resolve tween instead of just vanishing - see OnReleased for why it has
// to reparent itself out of the hand area first.
public partial class CardView : Panel
{
    private static readonly Vector2 HoverScale = new(1.15f, 1.15f);
    private static readonly Vector2 NormalScale = Vector2.One;

    public CardInstance? CardInstance { get; private set; }

    private Label _nameLabel = null!;
    private TextureRect _artIcon = null!;
    private Label _descriptionLabel = null!;
    private bool _dragging;
    private Vector2 _homePosition;
    private float _homeRotation;
    private int _restZIndex;
    private EnemyView? _targetLockedView;

    public override void _Ready()
    {
        PivotOffset = Size / 2f;
        _homePosition = Position;
        _nameLabel = GetNode<Label>("VBox/NameLabel");
        _artIcon = GetNode<TextureRect>("VBox/ArtIcon");
        _descriptionLabel = GetNode<Label>("VBox/DescriptionLabel");
        _nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public void SetCardInstance(CardInstance card)
    {
        CardInstance = card;
        if (_nameLabel is null) return;
        _nameLabel.Text = $"{card.Definition.Name} ({card.Definition.Cost})";
        _artIcon.Texture = ArtAssets.CardIcon(card.Definition.Id);
        AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(card.Definition.Type, card.Definition.Rarity, hovered: false));
        // Live player context (Strength/Weak) so the shown damage number is
        // always what would actually land, not stale hand-authored prose.
        _descriptionLabel.Text = EffectDescriptionFormatter.Describe(card.Definition.Effects, CombatManager.Instance?.Player);
    }

    // pos/rotationDeg are this card's resting slot in the fan (CombatScreen
    // computes the fan formula); zIndex is its paint order at rest so the
    // fan's overlap direction is consistent - hover/drag temporarily bump
    // above this, then restore it on release/exit.
    public void SetHomeTransform(Vector2 pos, float rotationDeg, int zIndex)
    {
        _homePosition = pos;
        _homeRotation = rotationDeg;
        _restZIndex = zIndex;
        if (_dragging) return;
        Position = pos;
        RotationDegrees = rotationDeg;
        ZIndex = zIndex;
    }

    private void OnMouseEntered()
    {
        if (_dragging) return;
        ZIndex = 100;
        var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine);
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", HoverScale, 0.12);
        tween.TweenProperty(this, "rotation_degrees", 0f, 0.12); // "stands up straight"
        if (CardInstance is not null) AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(CardInstance.Definition.Type, CardInstance.Definition.Rarity, hovered: true));
    }

    private void OnMouseExited()
    {
        if (_dragging) return;
        ZIndex = _restZIndex;
        var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine);
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", NormalScale, 0.12);
        tween.TweenProperty(this, "rotation_degrees", _homeRotation, 0.12);
        if (CardInstance is not null) AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(CardInstance.Definition.Type, CardInstance.Definition.Rarity, hovered: false));
    }

    // Cards animate in from the draw pile when newly added to hand -
    // staggerDelaySec cascades a multi-card draw instead of popping all at
    // once. Target position/rotation are whatever SetHomeTransform already
    // set immediately before this is called.
    public void PlayDrawTween(Vector2 fromGlobalPos, float staggerDelaySec)
    {
        var toPos = _homePosition;
        var toRotation = _homeRotation;
        Scale = Vector2.One * 0.6f;
        Modulate = new Color(1, 1, 1, 0f);
        GlobalPosition = fromGlobalPos;
        RotationDegrees = toRotation + (GD.Randf() * 20f - 10f);

        var tween = GetTree().CreateTween();
        tween.TweenInterval(staggerDelaySec);
        tween.SetParallel(true);
        tween.SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "position", toPos, 0.28);
        tween.TweenProperty(this, "rotation_degrees", toRotation, 0.28);
        tween.TweenProperty(this, "scale", Vector2.One, 0.28);
        tween.TweenProperty(this, "modulate:a", 1f, 0.18).SetTrans(Tween.TransitionType.Sine);
    }

    // Discard/exhaust without being played (end-of-turn hand clear, or a
    // future exhaust-from-hand effect) - flies to the given pile anchor and
    // fades out. Exhaust gets a faster, brighter/hotter flourish so it reads
    // as distinct from an ordinary discard.
    public void PlayExitTween(Vector2 toGlobalPos, bool isExhaust)
    {
        var screenRoot = GetTree().CurrentScene;
        var globalPos = GlobalPosition;
        GetParent().RemoveChild(this);
        screenRoot.AddChild(this);
        GlobalPosition = globalPos;
        ZIndex = 50;

        float duration = isExhaust ? 0.18f : 0.26f;
        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "global_position", toGlobalPos, duration).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "scale", Vector2.One * (isExhaust ? 0.1f : 0.3f), duration).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", isExhaust ? new Color(1.6f, 1.3f, 0.7f, 0f) : new Color(1f, 1f, 1f, 0f), duration);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    private void SnapHome()
    {
        ZIndex = _restZIndex;
        var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Back);
        tween.SetParallel(true);
        tween.TweenProperty(this, "position", _homePosition, 0.2);
        tween.TweenProperty(this, "rotation_degrees", _homeRotation, 0.2);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                ZIndex = mb.Pressed ? 200 : _restZIndex;
                if (mb.Pressed)
                {
                    RotationDegrees = 0f;
                }
                if (!mb.Pressed)
                {
                    OnReleased();
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _dragging:
                Position += motion.Relative;
                UpdateTargetHighlight();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // Continuously highlights whichever enemy is under the cursor while
    // dragging a SingleEnemy card - the drag-and-drop equivalent of the
    // hover glow potions already get via native Button hover (which can't
    // fire here since this card's own Panel occludes the enemy underneath).
    private void UpdateTargetHighlight()
    {
        EnemyView? underMouse = CardInstance?.Definition.Target == CardTargetType.SingleEnemy
            ? FindEnemyViewUnderMouse()
            : null;
        if (underMouse == _targetLockedView) return;
        if (GodotObject.IsInstanceValid(_targetLockedView)) _targetLockedView!.SetTargetLocked(false);
        underMouse?.SetTargetLocked(true);
        _targetLockedView = underMouse;
    }

    private void ClearTargetHighlight()
    {
        if (GodotObject.IsInstanceValid(_targetLockedView)) _targetLockedView!.SetTargetLocked(false);
        _targetLockedView = null;
    }

    public override void _ExitTree() => ClearTargetHighlight();

    private void OnReleased()
    {
        ClearTargetHighlight();

        if (CardInstance is null || CombatManager.Instance is null)
        {
            SnapHome();
            return;
        }

        EnemyCombatant? target = null;
        if (CardInstance.Definition.Target == CardTargetType.SingleEnemy)
        {
            target = FindEnemyViewUnderMouse()?.Combatant;
        }

        // Reparent out of the hand area BEFORE calling TryPlayCard: if it
        // resolves, TryPlayCard fires HandChanged synchronously, and
        // CombatScreen's rebuild only tears down whatever is still parented
        // under _handArea - moving out first is what lets the resolve tween
        // below actually get to play instead of the node being destroyed
        // out from under it in the same call.
        var handArea = GetParent();
        var screenRoot = GetTree().CurrentScene;
        var localPosition = Position;
        var globalPosition = GlobalPosition;
        handArea.RemoveChild(this);
        screenRoot.AddChild(this);
        GlobalPosition = globalPosition;

        bool resolved = CombatManager.Instance.TryPlayCard(CardInstance, target);
        if (resolved)
        {
            PlayResolveTween();
        }
        else
        {
            screenRoot.RemoveChild(this);
            handArea.AddChild(this);
            Position = localPosition;
            SnapHome();
        }
    }

    private void PlayResolveTween()
    {
        bool isExhaust = CardInstance?.Definition.Exhaust ?? false;
        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.One * (isExhaust ? 0.15f : 0.4f), 0.18).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", isExhaust ? new Color(1.6f, 1.3f, 0.7f, 0f) : new Color(1, 1, 1, 0f), 0.18);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    private EnemyView? FindEnemyViewUnderMouse()
    {
        var mousePos = GetGlobalMousePosition();
        foreach (var enemyView in EnemyView.Instances)
        {
            if (enemyView.GetGlobalRect().HasPoint(mousePos)) return enemyView;
        }
        return null;
    }
}
