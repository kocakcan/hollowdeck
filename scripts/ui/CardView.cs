using Godot;

namespace Hollowdeck.UI;

// Phase 0 prototype: hover (scale tween) + manual drag on a Control-based
// card. Not wired into a hand/CombatManager yet - exists to de-risk Control
// layering/z-index for drag+targeting before that machinery gets built.
//
// Uses manual _GuiInput tracking rather than Godot's _GetDragData/drop-target
// API, which targets a different scenario (Phase 1's enemy targeting).
//
// On release it always snaps back to its home position - there's no concept
// of a valid drop target yet (that's Phase 1's targeting/play logic).
//
// Known seam left for Phase 1: this card is a free child, not inside a
// container. Once cards live in a hand HBoxContainer, direct Position +=
// during drag will need to reconcile with container layout (e.g. setting
// TopLevel = true while dragging).
public partial class CardView : Panel
{
    private static readonly Vector2 HoverScale = new(1.08f, 1.08f);
    private static readonly Vector2 NormalScale = Vector2.One;

    private bool _dragging;
    private Vector2 _homePosition;

    public override void _Ready()
    {
        PivotOffset = Size / 2f;
        _homePosition = Position;
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
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

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                ZIndex = mb.Pressed ? 2 : 0;
                if (!mb.Pressed)
                {
                    GetTree().CreateTween().SetTrans(Tween.TransitionType.Back)
                        .TweenProperty(this, "position", _homePosition, 0.2);
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _dragging:
                Position += motion.Relative;
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}
