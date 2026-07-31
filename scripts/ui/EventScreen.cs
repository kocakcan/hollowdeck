using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Simple choice-based text events - one authored EventDefinition rolled at
// random per visit (same "roll fresh in _Ready, don't thread node data"
// pattern TreasureScreen already uses for its relic pick - no MapNode
// event field or mailbox class needed). Each choice resolves through
// EventOutcomeRegistry, a small self-contained system distinct from
// EffectRegistry, whose EffectContext requires a live CombatManager/
// Combatant that doesn't exist outside a fight.
//
// Each event now has an illustration keyed to its id (ROADMAP Phase 4 - the
// screen previously had none at all, which made five different encounters
// look like the same wall of centred text). ArtAssets.EventIcon falls back to
// the map's scroll, so an event authored without art still gets a screen with
// a subject.
public partial class EventScreen : Control
{
    private VBoxContainer _choicesList = null!;
    private Label _resultLabel = null!;
    private Button _continueButton = null!;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "demonic", new Color(0.7f, 0.65f, 0.75f));
        DeckViewButtons.Attach(this);
        // Deliberately no ScreenChrome.AddTitle here, unlike the other five.
        // The event's own name is the title and it belongs under the
        // illustration it names; a generic banner above it just gave the
        // screen two headings, one of which said nothing.
        ScreenChrome.AddRunStatus(this);

        var events = EventDatabase.All.ToList();
        var picked = events[RngStreams.Shop.Next(events.Count)];

        if (ArtAssets.EventIcon(picked.Id) is { } art)
        {
            GetNode<CenterContainer>("CenterContainer/VBoxContainer/ArtSlot")
                .AddChild(ScreenChrome.ArtPlinth(art));
        }

        var title = GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel");
        title.Text = picked.Title;
        title.ThemeTypeVariation = "CombatDisplayLabel";
        title.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Heading);
        title.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);

        var description = GetNode<Label>("CenterContainer/VBoxContainer/DescriptionLabel");
        description.Text = picked.Description;
        description.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);

        _choicesList = GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        _resultLabel = GetNode<Label>("CenterContainer/VBoxContainer/ResultLabel");
        _resultLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGold);
        _continueButton = GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");
        ChromeStyles.ApplyEmphasisButtonStyle(_continueButton);
        _continueButton.Pressed += () => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);

        foreach (var choice in picked.Choices)
        {
            var button = new Button { Text = choice.Label };
            ChromeStyles.ApplyEmphasisButtonStyle(button);
            button.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
            button.Pressed += () => OnChoiceChosen(choice);
            _choicesList.AddChild(button);
        }
    }

    private void OnChoiceChosen(EventChoice choice)
    {
        foreach (var child in _choicesList.GetChildren())
        {
            _choicesList.RemoveChild(child);
            child.QueueFree();
        }
        _resultLabel.Text = EventOutcomeRegistry.Resolve(choice);
        _resultLabel.Visible = true;
        _continueButton.Visible = true;
        // The outcome can change HP or gold, and the status block is a
        // snapshot taken in _Ready - without this it would keep showing the
        // numbers the player walked in with.
        ScreenChrome.RefreshRunStatus(this);
    }
}
