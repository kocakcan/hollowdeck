using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The relic you just found, shown rather than described. This screen used to
// print "You found: Vampire Fang" as two lines of body text centred on an
// otherwise empty backdrop, while vampire_fang.png - which ArtAssets already
// resolves by id - went unused (ROADMAP Phase 4).
public partial class TreasureScreen : Control
{
    public override void _Ready()
    {
        ScreenBackground.Attach(this, "gulch", new Color(0.8f, 0.85f, 0.8f));
        DeckViewButtons.Attach(this);
        ScreenChrome.AddTitle(this, "Treasure");
        ScreenChrome.AddRunStatus(this);

        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var available = RelicDatabase.All
            .Where(r => !ownedRelicIds.Contains(r.Id) && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();

        var column = GetNode<VBoxContainer>("CenterContainer/VBoxContainer");
        var artSlot = GetNode<CenterContainer>("CenterContainer/VBoxContainer/ArtSlot");
        var nameLabel = GetNode<Label>("CenterContainer/VBoxContainer/NameLabel");
        var descriptionLabel = GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");

        nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        nameLabel.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Heading);
        nameLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        descriptionLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);

        if (available.Count == 0)
        {
            // The empty case gets the chest itself, so the screen still has a
            // subject. Falling back to no art at all is what made "the chest
            // is empty" read as a bug rather than an outcome.
            if (ArtAssets.MapIcon(MapNodeType.Treasure) is { } chest)
            {
                artSlot.AddChild(ScreenChrome.ArtPlinth(chest));
            }
            nameLabel.Text = "Empty";
            descriptionLabel.Text = "The treasure chest has already been picked clean.";
        }
        else
        {
            var picked = available[RngStreams.Shop.Next(available.Count)];
            RunState.Relics.Add(new RelicInstance(picked));

            if (ArtAssets.RelicIcon(picked.Id) is { } icon)
            {
                artSlot.AddChild(ScreenChrome.ArtPlinth(icon));
            }
            nameLabel.Text = picked.Name;
            // Description only. This label used to be the whole screen and
            // carried "You found: {name}\n{description}"; with the name set in
            // the display face above it, repeating it here just printed the
            // relic's name twice, six lines apart.
            descriptionLabel.Text = picked.Description;
            AudioManager.Instance?.PlaySfx("reward_pickup");
            PlayEntrance(artSlot);
        }

        var continueButton = GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");
        ChromeStyles.ApplyEmphasisButtonStyle(continueButton);
        continueButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        continueButton.Pressed += OnContinuePressed;

        column.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    // A short rise-and-settle on the relic, matching the overshoot easing the
    // rest of the game's reward moments use. Gated the same way every other
    // ambient animation is - a player who has turned motion off has said they
    // do not want the screen moving on arrival.
    private static void PlayEntrance(Control art)
    {
        if (SettingsManager.Instance.ReduceMotion) return;

        art.Modulate = new Color(1, 1, 1, 0);
        art.Position += new Vector2(0, 12);
        var tween = art.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(art, "modulate:a", 1f, UiTheme.Motion.Slow);
        tween.TweenProperty(art, "position:y", art.Position.Y - 12, UiTheme.Motion.Slow)
            .SetTrans(UiTheme.Motion.EaseOvershoot);
    }

    private void OnContinuePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
