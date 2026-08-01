using System;
using Godot;

namespace Hollowdeck.UI;

// Keyboard furniture shared by every non-combat screen: pick a focus owner on
// load, re-pick one after the screen rebuilds its controls, and handle
// hd_cancel. Attached in one line from a screen's _Ready(), the same way
// ScreenBackground and DeckViewButtons.Attach already are, rather than editing
// ten .tscn files.
//
// The screens themselves need almost nothing else, because every control on
// them is a stock Button sitting at Godot's default FocusModeEnum.All - the
// engine's own focus navigation (Tab, and arrow keys by geometry) already
// works and already skips Disabled controls. That last part is why the map
// costs nothing: MapScreen.BuildButtons sets Disabled = !isReachable, so
// tabbing lands only on the legal next nodes. What was missing was never the
// navigation, it was that nothing ever called GrabFocus, so no screen had a
// focus owner and the first key press went nowhere.
//
// Combat deliberately does NOT use this. Its cards are fanned Panels rather
// than Buttons and its targeting is a CombatState sub-state, so CombatScreen
// keeps its own _UnhandledInput and its buttons stay FocusModeEnum.None (see
// CombatScreen._Ready for why: focus navigation would fight the arrow-key
// card cycling).
public static class ScreenKeyboardNav
{
    // initialFocus is a callback rather than a Control because several screens
    // decide it from state that changes - the first *reachable* map node, the
    // first *enabled* shop offer - and because Regrab re-runs it after a
    // rebuild, when the control it returned last time may be freed.
    public static ScreenKeyboardNavListener Attach(
        Control screen, Func<Control?> initialFocus, Action? onCancel = null)
    {
        var listener = new ScreenKeyboardNavListener(screen, initialFocus, onCancel);
        screen.AddChild(listener);
        return listener;
    }

    // Human-readable binding for a hd_* action, read back out of the InputMap
    // rather than written as a literal beside the control it labels. A hint
    // that can silently drift from project.godot is worse than no hint, and
    // these are the strings the player is told to press.
    public static string KeyHint(StringName action)
    {
        foreach (var ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey key) return OS.GetKeycodeString(key.Keycode);
        }
        return "";
    }

    // A Control that can actually hold focus - GrabFocus refuses (and logs an
    // error) otherwise. Disabled is the common case: a shop offer the player
    // can't afford, a map node that isn't reachable. Hidden is the other one:
    // RestScreen swaps its two halves by Visible.
    internal static bool CanTakeFocus(Control? control) =>
        control is not null
        && GodotObject.IsInstanceValid(control)
        && control.IsInsideTree()
        && control.IsVisibleInTree()
        && control.FocusMode != Control.FocusModeEnum.None
        && control is not BaseButton { Disabled: true };
}

// Scoped to whichever screen called Attach() - freed automatically when
// RunManager.ChangeScreen swaps the scene, so this needs no autoload. Same
// lifetime trick DeckViewKeybindListener uses.
public partial class ScreenKeyboardNavListener : Node
{
    private readonly Control _screen;
    private readonly Func<Control?> _initialFocus;
    private readonly Action? _onCancel;

    public ScreenKeyboardNavListener(Control screen, Func<Control?> initialFocus, Action? onCancel)
    {
        _screen = screen;
        _initialFocus = initialFocus;
        _onCancel = onCancel;
    }

    public override void _Ready() => Regrab();

    // Deferred, always. Containers size and enable their children in the
    // layout pass *after* _Ready, and a screen that calls this from a button
    // handler (ShopScreen after a purchase, EventScreen after a choice) is
    // often mid-teardown of the very node that currently holds focus. Running
    // a frame later means _initialFocus() sees the finished screen.
    public void Regrab() => CallDeferred(MethodName.RegrabNow);

    private void RegrabNow()
    {
        if (!IsInstanceValid(_screen) || !_screen.IsInsideTree()) return;

        // Leave an existing focus owner alone: the player may have tabbed
        // somewhere deliberately, or a PileViewPopup may have just handed
        // focus back on close. Only a screen with *nothing* focused needs
        // rescuing, which is also exactly the post-rebuild case - Godot drops
        // focus when the focused control is freed or disabled.
        var current = _screen.GetViewport()?.GuiGetFocusOwner();
        if (ScreenKeyboardNav.CanTakeFocus(current)) return;

        var target = _initialFocus();
        if (ScreenKeyboardNav.CanTakeFocus(target)) target!.GrabFocus();
    }

    // _UnhandledKeyInput rather than _UnhandledInput: hd_cancel also carries
    // right-click (for combat's targeting), and right-clicking a shop should
    // not walk out of it.
    //
    // An open PileViewPopup consumes hd_cancel in its own _UnhandledInput,
    // which runs first, so Escape closes the popup instead of leaving the
    // screen behind it.
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_onCancel is null) return;
        if (!@event.IsActionPressed("hd_cancel")) return;

        GetViewport().SetInputAsHandled();
        _onCancel();
    }
}
