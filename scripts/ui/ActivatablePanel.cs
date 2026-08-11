using System;
using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Gives a plain PanelContainer the same click-or-ui_accept activation
// CardView already has for its non-interactive state (CardView.cs's
// _GuiInput) - LibraryScreen's collection tiles and the reward screen's boss
// relic picker are PanelContainers with no input handling of their own, so
// ScreenChrome.FocusableFrame has nothing to wire a click to without this.
public partial class ActivatablePanel : PanelContainer
{
    public event Action? Activated;

    public override void _GuiInput(InputEvent @event)
    {
        bool released = @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
        if (released || @event.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            AudioManager.Instance?.PlaySfx("ui_click");
            Activated?.Invoke();
        }
    }
}
