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
    private static readonly Vector2 HoverScale = new(1.08f, 1.08f);
    private static readonly Vector2 NormalScale = Vector2.One;

    public CardInstance? CardInstance { get; private set; }

    private Label _nameLabel = null!;
    private TextureRect _artIcon = null!;
    private Label _descriptionLabel = null!;
    private bool _dragging;
    private Vector2 _homePosition;
    private float _homeRotation;
    private int _restZIndex;

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
        AddThemeStyleboxOverride("panel", FrameStyle(card.Definition.Type));
        // Live player context (Strength/Weak) so the shown damage number is
        // always what would actually land, not stale hand-authored prose.
        _descriptionLabel.Text = EffectDescriptionFormatter.Describe(card.Definition.Effects, CombatManager.Instance?.Player);
    }

    // Attack/Skill get distinct frame colors so card type reads at a glance,
    // matching the genre convention of color-coded card frames.
    private static StyleBoxFlat FrameStyle(CardType type)
    {
        var isAttack = type == CardType.Attack;
        var style = new StyleBoxFlat
        {
            BgColor = isAttack ? new Color(0.32f, 0.13f, 0.13f) : new Color(0.12f, 0.26f, 0.22f),
            BorderColor = isAttack ? new Color(0.65f, 0.32f, 0.28f) : new Color(0.3f, 0.55f, 0.45f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        return style;
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
        GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine)
            .TweenProperty(this, "scale", HoverScale, 0.12);
        ZIndex = 100;
    }

    private void OnMouseExited()
    {
        if (_dragging) return;
        GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine)
            .TweenProperty(this, "scale", NormalScale, 0.12);
        ZIndex = _restZIndex;
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
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void OnReleased()
    {
        if (CardInstance is null || CombatManager.Instance is null)
        {
            SnapHome();
            return;
        }

        EnemyCombatant? target = null;
        if (CardInstance.Definition.Target == CardTargetType.SingleEnemy)
        {
            target = FindEnemyUnderMouse();
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
        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.One * 0.4f, 0.18).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate:a", 0f, 0.18);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    private EnemyCombatant? FindEnemyUnderMouse()
    {
        var mousePos = GetGlobalMousePosition();
        foreach (var enemyView in EnemyView.Instances)
        {
            if (enemyView.GetGlobalRect().HasPoint(mousePos)) return enemyView.Combatant;
        }
        return null;
    }
}
