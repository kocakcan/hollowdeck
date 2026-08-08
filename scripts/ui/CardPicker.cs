using System;
using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// "Choose one of these cards" - a grid of real CardViews, each over its own
// button. Extracted from RestScreen's Smith picker when EventScreen needed the
// same thing; the fiddly parts it carries are all lessons from that screen,
// not new decisions.
//
// Renders real CardViews rather than rows of text because the deck is the
// thing being chosen from, and comparing cards is exactly what a CardView is
// for - the same argument ShopScreen and RewardScreen already make for their
// offers.
public static class CardPicker
{
    // CardView's own width (ROADMAP Phase 2 set cards to 176x240). Every
    // column is exactly this: a column is only as wide as its widest child,
    // and a button reading "Upgrade to Cleave+" once pushed five columns past
    // the scroll viewport, which then clipped the outer two.
    public const int CardColumnWidth = 176;

    /// Fills `grid` with one column per entry in `indices`.
    ///
    /// `preview` is what the column shows - the card as it will be *after*
    /// the choice, which is not always the card at that deck index (the
    /// upgrade picker shows the "+" version). `subtitle` is the before-line
    /// under the button, or null for pickers where nothing changes about the
    /// card itself.
    ///
    /// `exit` is the control the grid leads *out* to - RestScreen's Cancel
    /// button. It is wired as the bottom neighbour of the last row (and only
    /// the last row); pass null on a picker that has no way out, which is what
    /// EventScreen's deliberately is.
    ///
    /// Returns the first card, so the caller can hand it to ScreenKeyboardNav
    /// as the focus owner - a grid nobody can tab into is the failure mode this
    /// screen family has hit before.
    public static CardView? Populate(
        GridContainer grid,
        IEnumerable<int> indices,
        string buttonText,
        Func<int, CardDefinition> preview,
        Func<int, string?> subtitle,
        Action<int> onChosen,
        Control? exit = null)
    {
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }

        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        var cards = new List<CardView>();

        foreach (int deckIndex in indices)
        {
            int index = deckIndex; // Captured per iteration, not per loop.

            void Choose()
            {
                AudioManager.Instance?.PlaySfx("reward_pickup");
                onChosen(index);
            }

            var column = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(CardColumnWidth, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);
            grid.AddChild(column);

            var view = cardScene.Instantiate<CardView>();
            column.AddChild(view);
            // Interactive = false is what makes a CardView focusable (its
            // setter inverts: no drag-to-play, so it can hold focus instead).
            view.Interactive = false;
            view.Clicked += _ => Choose();
            view.SetCardInstance(new CardInstance(preview(index)));

            var button = new Button { Text = buttonText };
            ChromeStyles.ApplyEmphasisButtonStyle(button);
            button.Pressed += Choose;
            // The card is the keyboard stop, not the button. Both used to be,
            // which cost two presses per card to walk the grid and made "down
            // to the next row" ambiguous - and Enter on the card did nothing at
            // all, because Clicked (which CardView already fires for a focused
            // non-interactive card) was never subscribed. Now the thing that is
            // focused, the thing that glows, and the thing that shows the
            // keyword tooltip are the same card. The button stays for the
            // mouse, which is the only thing that ever used it.
            button.FocusMode = Control.FocusModeEnum.None;
            column.AddChild(button);
            cards.Add(view);

            if (subtitle(index) is { } line)
            {
                column.AddChild(new Label
                {
                    Text = line,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CustomMinimumSize = new Vector2(CardColumnWidth, 0),
                    Modulate = new Color(0.7f, 0.7f, 0.7f),
                });
            }
        }

        WireGridNavigation(grid, cards, exit);
        return cards.Count > 0 ? cards[0] : null;
    }

    // Arrow keys walk the grid by *index*, not by geometry. Godot's own
    // directional focus search is a nearest-edge distance test over everything
    // focusable on screen, and on the rest site it kept answering Down from row
    // one with the Cancel button under the ScrollContainer rather than the card
    // below the fold - so the second row could only be reached with a mouse
    // wheel, and the grid's own scroll-into-view never got a focus change to
    // act on. EventScreen's picker hid it, because that one has no Cancel for
    // the search to find.
    //
    // Left/Right deliberately wrap across rows: the choice is "one of these
    // cards", which reads as a list, and wrapping means the whole grid is
    // walkable on one axis. Only the last row leads out to `exit`, and only
    // downward - Up from Cancel comes back in.
    // internal rather than private: LibraryScreen reuses this for its
    // relic/potion tile grids too, not just CardViews - IReadOnlyList<Control>
    // is what lets both callers share it (List<CardView> already satisfies it
    // covariantly, so this call site needed no change).
    internal static void WireGridNavigation(GridContainer grid, IReadOnlyList<Control> tiles, Control? exit)
    {
        if (tiles.Count == 0) return;

        int columns = Mathf.Max(1, grid.Columns);
        for (int i = 0; i < tiles.Count; i++)
        {
            var card = tiles[i];
            if (i > 0) card.FocusNeighborLeft = card.GetPathTo(tiles[i - 1]);
            if (i + 1 < tiles.Count) card.FocusNeighborRight = card.GetPathTo(tiles[i + 1]);
            if (i >= columns) card.FocusNeighborTop = card.GetPathTo(tiles[i - columns]);

            // Tab follows the same order, so the two ways of walking a grid
            // can't disagree about what comes next.
            card.FocusPrevious = i > 0 ? card.GetPathTo(tiles[i - 1]) : new NodePath();
            card.FocusNext = i + 1 < tiles.Count ? card.GetPathTo(tiles[i + 1]) : new NodePath();

            Control? below = i + columns < tiles.Count ? tiles[i + columns] : exit;
            card.FocusNeighborBottom = below is null ? new NodePath() : card.GetPathTo(below);
        }

        if (exit is null) return;
        var lastRowFirst = tiles[tiles.Count - 1 - (tiles.Count - 1) % columns];
        exit.FocusNeighborTop = exit.GetPathTo(lastRowFirst);
        exit.FocusPrevious = exit.GetPathTo(lastRowFirst);
    }

    /// The "was: ..." line the upgrade pickers put under their button. The
    /// column above shows the upgraded card, so this spells out the before -
    /// button text alone ("Upgrade to Strike+") never said what changed.
    public static string WasLine(CardDefinition original) =>
        "was: " + EffectDescriptionFormatter.Describe(
            original.Effects, new DescribeContext(TargetType: original.Target));
}
