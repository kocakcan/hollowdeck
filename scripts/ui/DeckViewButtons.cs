using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Attaches a small top-right "Deck" (plus, mid-combat, "Draw"/"Discard"/
// "Exhaust") button row that opens a read-only PileViewPopup, plus matching
// D/Q/W/E keybinds - Slay-the-Spire-style pile inspection. Built and attached
// in code from a screen's _Ready(), mirroring ScreenBackground/ChromeStyles'
// one-line-per-screen convention rather than editing every screen's .tscn.
public static class DeckViewButtons
{
    public static void Attach(Control screen, bool includeCombatPiles = false)
    {
        // Stacked vertically rather than in a row: on CombatScreen, EnemyRow
        // extends out to x=976 of the 1152-wide layout, leaving only a ~176px
        // clear margin at the top-right corner - a horizontal row of 4
        // buttons (~330px) doesn't fit there and used to paint over the
        // rightmost enemy's intent icon. A vertical stack of the same
        // buttons is only as wide as the single longest label ("Discard"/
        // "Exhaust", ~110px), which does.
        var stack = new VBoxContainer();
        screen.AddChild(stack);

        stack.AddChild(MakeButton("Deck", () => OpenDeck(screen)));
        if (includeCombatPiles)
        {
            stack.AddChild(MakeButton("Draw", () => OpenPile(screen, "Draw Pile", CombatManager.Instance.Player.Piles.DrawPile)));
            stack.AddChild(MakeButton("Discard", () => OpenPile(screen, "Discard Pile", CombatManager.Instance.Player.Piles.Discard)));
            stack.AddChild(MakeButton("Exhaust", () => OpenPile(screen, "Exhaust Pile", CombatManager.Instance.Player.Piles.Exhaust)));
        }

        // Anchored to the top-right corner and sized exactly to fit its
        // buttons (via GetCombinedMinimumSize, queried after they're added)
        // rather than a hardcoded pixel size - a fixed width once clipped a
        // button's text off the edge of the screen, and would do the same
        // again for any future localization/label-length change.
        stack.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        var stackSize = stack.GetCombinedMinimumSize();
        stack.OffsetRight = -12;
        stack.OffsetLeft = stack.OffsetRight - stackSize.X;
        stack.OffsetTop = 12;
        stack.OffsetBottom = 12 + stackSize.Y;

        screen.AddChild(new DeckViewKeybindListener(screen, includeCombatPiles));
    }

    private static Button MakeButton(string text, System.Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += onPressed;
        return button;
    }

    // Mid-combat, the "real" deck is whatever's actually rotating through
    // this fight (draw+hand+discard+exhaust) rather than RunState.Deck -
    // the two normally agree (PileManager is rebuilt fresh from RunState
    // .Deck at combat start, see RunManager.cs), but this stays correct even
    // if a future mid-combat effect ever diverges from it.
    internal static void OpenDeck(Node screenRoot)
    {
        if (CombatManager.Instance is { } combat)
        {
            var piles = combat.Player.Piles;
            var all = piles.DrawPile.Concat(piles.Hand).Concat(piles.Discard).Concat(piles.Exhaust)
                .Select(c => c.Definition).ToList();
            PileViewPopup.Open(screenRoot, $"Deck ({all.Count})", all, combat.Player);
        }
        else
        {
            PileViewPopup.Open(screenRoot, $"Deck ({RunState.Deck.Count})", RunState.Deck);
        }
    }

    internal static void OpenPile(Node screenRoot, string label, List<CardInstance> pile)
    {
        var defs = pile.Select(c => c.Definition).ToList();
        PileViewPopup.Open(screenRoot, $"{label} ({defs.Count})", defs, CombatManager.Instance?.Player);
    }
}

// Scoped to whichever screen called DeckViewButtons.Attach() - freed
// automatically when RunManager.ChangeScreen swaps the scene, so this needs
// no autoload or project.godot [input] entry.
public partial class DeckViewKeybindListener : Node
{
    private readonly Control _screen;
    private readonly bool _includeCombatPiles;

    public DeckViewKeybindListener(Control screen, bool includeCombatPiles)
    {
        _screen = screen;
        _includeCombatPiles = includeCombatPiles;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            case Key.D:
                DeckViewButtons.OpenDeck(_screen);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Q when _includeCombatPiles:
                DeckViewButtons.OpenPile(_screen, "Draw Pile", CombatManager.Instance.Player.Piles.DrawPile);
                GetViewport().SetInputAsHandled();
                break;
            case Key.W when _includeCombatPiles:
                DeckViewButtons.OpenPile(_screen, "Discard Pile", CombatManager.Instance.Player.Piles.Discard);
                GetViewport().SetInputAsHandled();
                break;
            case Key.E when _includeCombatPiles:
                DeckViewButtons.OpenPile(_screen, "Exhaust Pile", CombatManager.Instance.Player.Piles.Exhaust);
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}
