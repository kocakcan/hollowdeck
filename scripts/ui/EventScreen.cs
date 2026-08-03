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
    private ScreenKeyboardNavListener? _keyboardNav;

    // The card-picker half, for outcomes that have to ask which card
    // (remove_chosen_card, upgrade_chosen_card). Hidden until one is rolled,
    // and there is no Cancel: see ShowPicker.
    //
    // _mainView is the event's own column, and the two are mutually exclusive:
    // both are full-rect CenterContainers with transparent backgrounds, so
    // leaving the main one visible does not put the picker "on top of" it - it
    // interleaves the event's description and choice buttons through the gaps
    // in the card grid, which is exactly how this shipped in the first shot.
    private Control _mainView = null!;
    private Control _pickerView = null!;
    private Label _pickerTitle = null!;
    private GridContainer _pickerList = null!;

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

        _mainView = GetNode<Control>("CenterContainer");
        _pickerView = GetNode<Control>("PickerCenterContainer");
        _pickerTitle = GetNode<Label>("PickerCenterContainer/PickerVBox/PickerTitleLabel");
        _pickerTitle.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        _pickerList = GetNode<GridContainer>("PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");

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

        // No cancel action: an event is a decision you have to make, and the
        // choices are the only way out. Focus follows the same rule as the
        // screen does - the choices while there are any, then Continue.
        _keyboardNav = ScreenKeyboardNav.Attach(this, PreferredFocus);
    }

    // Three states, checked in the order they can appear: the picker while it
    // is open, then the choices while there are any, then Continue.
    private Control? PreferredFocus()
    {
        if (_pickerView.Visible)
        {
            return _pickerList.GetChildren().SelectMany(c => c.GetChildren())
                .OfType<Button>().FirstOrDefault();
        }
        return _choicesList.GetChildren().OfType<Button>().FirstOrDefault() ?? (Control?)_continueButton;
    }

    private void OnChoiceChosen(EventChoice choice)
    {
        foreach (var child in _choicesList.GetChildren())
        {
            _choicesList.RemoveChild(child);
            child.QueueFree();
        }

        var resolution = EventOutcomeRegistry.Begin(choice);
        if (resolution.Pending is { } picker)
        {
            ShowPicker(picker, resolution.Text);
            return;
        }

        ShowResult(resolution.Text);
    }

    // Deliberately no Cancel, unlike RestScreen's Smith picker. Rest offers
    // the upgrade as one of three actions and backing out costs nothing;
    // here the choice has already been made and its other outcomes have
    // already resolved, so the only way out is to pick.
    private void ShowPicker(ICardPickerOutcome picker, string textSoFar)
    {
        _pickerTitle.Text = picker.Prompt;
        CardPicker.Populate(
            _pickerList,
            picker.Selectable(),
            "Choose",
            // Only the upgrade picker changes the card; the remove picker
            // shows the card as it is, because what is being chosen is which
            // one to lose.
            index => picker is UpgradeChosenCardOutcome
                ? CardUpgrade.Apply(RunState.Deck[index])
                : RunState.Deck[index],
            index => picker is UpgradeChosenCardOutcome
                ? CardPicker.WasLine(RunState.Deck[index])
                : null,
            index =>
            {
                string message = picker.Apply(index);
                _pickerView.Visible = false;
                _mainView.Visible = true;
                ShowResult(Combine(textSoFar, message));
            });

        _mainView.Visible = false;
        _pickerView.Visible = true;
        // Hiding a view removes its controls from focus navigation, and the
        // choice button that was just pressed has already been freed - so
        // focus has to move with the swap, both into the picker and back out.
        _keyboardNav?.Regrab();
    }

    private void ShowResult(string text)
    {
        _resultLabel.Text = text;
        _resultLabel.Visible = true;
        _continueButton.Visible = true;
        // The outcome can change HP or gold, and the status block is a
        // snapshot taken in _Ready - without this it would keep showing the
        // numbers the player walked in with.
        ScreenChrome.RefreshRunStatus(this);
        // The button that was just pressed is one of the ones freed above, so
        // the screen is left with no focus owner at exactly the moment
        // Continue appears. PreferredFocus now finds no choices and returns it.
        _keyboardNav?.Regrab();
    }

    // A compound choice's earlier outcomes may have had nothing to say, in
    // which case the picker's own message stands alone rather than being
    // appended to the authored ResultText - which was written for the whole
    // choice and would read as a contradiction next to "Strike is gone".
    private static string Combine(string textSoFar, string message) =>
        textSoFar.Length == 0 ? message : $"{textSoFar} {message}";
}
