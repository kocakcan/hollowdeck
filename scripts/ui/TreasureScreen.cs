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
        ScreenBackground.AttachRoom(this, ScreenBackground.BackdropRoom.Strongroom);
        DeckViewButtons.Attach(this);
        ScreenChrome.AddTitle(this, "Treasure");
        ScreenChrome.AddRunStatus(this);

        // A chest draws the ordinary Common/Uncommon/Rare ladder - the owned
        // and unlock filters that used to be spelled out here live in
        // RelicPool now, along with the tier weighting.
        var picked = RelicPool.SampleOne(RelicSite.Treasure, RngStreams.Shop);

        var column = GetNode<VBoxContainer>("CenterContainer/VBoxContainer");
        var artSlot = GetNode<CenterContainer>("CenterContainer/VBoxContainer/ArtSlot");
        var nameLabel = GetNode<Label>("CenterContainer/VBoxContainer/NameLabel");
        var descriptionLabel = GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");

        nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        nameLabel.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Heading);
        nameLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        descriptionLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);

        if (picked is null)
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
        ScreenKeyboardNav.Attach(this, () => continueButton);

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
        // Alpha never takes Land: Back overshoots past its destination, and an
        // alpha that overshoots 1 is clamped rather than sprung, so the art
        // would simply arrive early and sit there.
        tween.TweenTo(art, "modulate:a", 1f, Motion.Fade);
        tween.TweenTo(art, "position:y", art.Position.Y - 12, Motion.Land.Over(Motion.Fade.Seconds));
    }

    private void OnContinuePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
