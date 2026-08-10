using System;
using System.Collections.Generic;
using System.Linq;
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
// works. What was missing was never the navigation, it was that nothing ever
// called GrabFocus, so no screen had a focus owner and the first key press
// went nowhere.
//
// This used to add "and already skips Disabled controls", and it does not.
// Measured on 4.7.1 (and pinned by
// KeyboardSmokeTest.TestOnlyFocusModeExcludesAControlFromTheKeyboard, because
// the wrong version of this was load-bearing in two screens): Disabled excludes
// a control from neither Tab nor arrow navigation, and does not even release
// focus it already holds - BaseButton keeps FocusModeEnum.All throughout. So an
// inert control takes focus and paints the focus ring on a choice that cannot
// be made, and can keep it after going inert. Excluding one costs an explicit
// FocusModeEnum.None, which MapScreen.BuildButtons sets on every unreachable
// node and ShopScreen.RefreshOffers on every offer the player cannot afford.
// Those are the two screens with illegal-but-present choices; everywhere else
// every control on screen is a legal one. CanTakeFocus below is the same rule
// for the *initial* owner, and is why nothing regressed while the belief was
// wrong - the screens still opened on something legal, they just let the
// keyboard wander onto something that wasn't.
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

    // KeyHint for authored prose: replaces every {hd_some_action} in `text`
    // with the key currently bound to it. Written for the reward screen's tip
    // line, where the alternative is a data file carrying "Press D" that no
    // rebinding would ever reach.
    //
    // An unknown action resolves to its own token rather than to an empty
    // string, so "Press {hd_typo}" stays visibly wrong on screen instead of
    // silently becoming "Press ". The suite is what should catch it - see the
    // tip audit in EffectSmokeTest - and this is the fallback for when it does
    // not.
    public static string ResolveKeyHints(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{(hd_[a-z0-9_]+)\}", match =>
        {
            string hint = KeyHint(match.Groups[1].Value);
            return hint.Length > 0 ? hint : match.Value;
        });

    /// Every hd_* action a piece of authored text asks for. The audit half of
    /// ResolveKeyHints above: a test can ask what a string references without
    /// re-deriving the pattern.
    public static IEnumerable<string> KeyHintTokens(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{(hd_[a-z0-9_]+)\}")
            .Select(m => m.Groups[1].Value);

    // True only for the duration of a GrabFocusQuietly call - i.e. while *code*
    // is placing focus rather than the player moving it. Godot emits
    // focus_entered synchronously from grab_focus(), so a handler reading this
    // is reading it about its own focus change.
    //
    // It exists for one distinction a Control cannot otherwise make: a card
    // that has focus because the screen just opened is not a card the player is
    // asking about. Without it, every choice screen came up with its leftmost
    // card's keyword panel already floating over the layout (CardView
    // .OnFocusEntered).
    internal static bool GrantingFocus { get; private set; }

    // The focus grab a screen does *for* the player. The glow still moves - the
    // player has to be able to see where the keyboard is - but anything that
    // means "the player is looking at this" stays quiet until they act.
    public static void GrabFocusQuietly(Control control)
    {
        GrantingFocus = true;
        try { control.GrabFocus(); }
        finally { GrantingFocus = false; }
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

    public override void _Ready()
    {
        GetViewport().GuiFocusChanged += OnFocusChanged;
        Regrab();
    }

    public override void _ExitTree()
    {
        if (GetViewport() is { } viewport) viewport.GuiFocusChanged -= OnFocusChanged;
    }

    // The other half of this file's thesis. Arrow-key navigation itself was
    // never missing - Godot's geometric focus nav has always worked, and what
    // was missing was that nothing called GrabFocus. This is the same shape of
    // omission one level down: focus *moved* onto a control below the fold and
    // nothing scrolled to it, so the rest site's upgrade grid could only be
    // walked past row one with a mouse wheel. A picker column is ~330px tall
    // inside a 424px viewport, so row two was effectively unreachable.
    //
    // Viewport-wide rather than per-screen even though the listener is scoped
    // to one screen: GuiFocusChanged fires for anything focusable under that
    // viewport, so an overlay the screen spawned (PileViewPopup's card grid)
    // is covered by the same handler with no wiring of its own.
    private void OnFocusChanged(Control control)
    {
        for (Node? node = control.GetParent(); node is not null; node = node.GetParent())
        {
            if (node is ScrollContainer scroll)
            {
                scroll.EnsureControlVisible(control);
                return;
            }
        }
    }

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
        if (ScreenKeyboardNav.CanTakeFocus(target)) ScreenKeyboardNav.GrabFocusQuietly(target!);
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
