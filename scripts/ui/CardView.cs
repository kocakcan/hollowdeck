using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Hover (scale tween) + manual drag on a Control-based card. Home position is
// tracked and laid out manually by whoever spawns this (CombatScreen), rather
// than via a real Container - that sidesteps the Container-vs-manual-Position
// conflict a real HBoxContainer would cause during drag (the seam Phase 0's
// prototype flagged), at the cost of the spawner doing its own row layout.
//
// On release: if CombatManager rejects the play, or the play requires enemy
// targeting (hasn't left the hand yet), snap back to home. If it resolved
// synchronously, do nothing - CombatManager's HandChanged rebuild will free
// this node.
public partial class CardView : Panel
{
    private static readonly Vector2 HoverScale = new(1.08f, 1.08f);
    private static readonly Vector2 NormalScale = Vector2.One;

    public CardInstance? CardInstance { get; private set; }

    private Label _nameLabel = null!;
    private bool _dragging;
    private Vector2 _homePosition;

    public override void _Ready()
    {
        PivotOffset = Size / 2f;
        _homePosition = Position;
        _nameLabel = GetNode<Label>("NameLabel");
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public void SetCardInstance(CardInstance card)
    {
        CardInstance = card;
        if (_nameLabel is not null) _nameLabel.Text = card.Definition.Name;
    }

    public void SetHomePosition(Vector2 pos)
    {
        _homePosition = pos;
        Position = pos;
    }

    private void OnMouseEntered()
    {
        if (_dragging) return;
        GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine)
            .TweenProperty(this, "scale", HoverScale, 0.12);
        ZIndex = 1;
    }

    private void OnMouseExited()
    {
        if (_dragging) return;
        GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine)
            .TweenProperty(this, "scale", NormalScale, 0.12);
        ZIndex = 0;
    }

    private void SnapHome()
    {
        GetTree().CreateTween().SetTrans(Tween.TransitionType.Back)
            .TweenProperty(this, "position", _homePosition, 0.2);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                ZIndex = mb.Pressed ? 2 : 0;
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

        bool resolved = CombatManager.Instance.TryPlayCard(CardInstance);
        if (!resolved) SnapHome();
    }
}
