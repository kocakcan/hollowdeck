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
    /// Returns the first button, so the caller can hand it to
    /// ScreenKeyboardNav as the focus owner - a grid nobody can tab into is
    /// the failure mode this screen family has hit before.
    public static Button? Populate(
        GridContainer grid,
        IEnumerable<int> indices,
        string buttonText,
        Func<int, CardDefinition> preview,
        Func<int, string?> subtitle,
        Action<int> onChosen)
    {
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }

        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        Button? first = null;

        foreach (int deckIndex in indices)
        {
            int index = deckIndex; // Captured per iteration, not per loop.

            var column = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(CardColumnWidth, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);
            grid.AddChild(column);

            var view = cardScene.Instantiate<CardView>();
            column.AddChild(view);
            view.Interactive = false;
            view.SetCardInstance(new CardInstance(preview(index)));

            var button = new Button { Text = buttonText };
            ChromeStyles.ApplyEmphasisButtonStyle(button);
            button.Pressed += () =>
            {
                AudioManager.Instance?.PlaySfx("reward_pickup");
                onChosen(index);
            };
            column.AddChild(button);
            first ??= button;

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

        return first;
    }

    /// The "was: ..." line the upgrade pickers put under their button. The
    /// column above shows the upgraded card, so this spells out the before -
    /// button text alone ("Upgrade to Strike+") never said what changed.
    public static string WasLine(CardDefinition original) =>
        "was: " + EffectDescriptionFormatter.Describe(
            original.Effects, new DescribeContext(TargetType: original.Target));
}
