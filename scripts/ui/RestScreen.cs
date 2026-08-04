using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RestScreen : Control
{
    private const float HealFraction = 0.3f;

    private Control _choicesView = null!;
    private Control _upgradeView = null!;
    private GridContainer _upgradeList = null!;

    private ScreenKeyboardNavListener? _keyboardNav;
    private Button _healButton = null!;
    private Button _cancelButton = null!;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "dirt", new Color(0.5f, 0.42f, 0.35f));
        DeckViewButtons.Attach(this);
        ScreenChrome.AddTitle(this, "Rest Site");
        ScreenChrome.AddRunStatus(this);

        _choicesView = GetNode<Control>("CenterContainer");
        _upgradeView = GetNode<Control>("UpgradeCenterContainer");
        _upgradeList = GetNode<GridContainer>("UpgradeCenterContainer/UpgradeVBox/ScrollContainer/UpgradeList");

        BuildCampfire();

        var title = GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel");
        title.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);

        int healAmount = Mathf.RoundToInt(RunState.PlayerMaxHp * HealFraction);
        _healButton = GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/HealButton");
        _healButton.Text = $"Rest - Heal {healAmount} HP";
        ChromeStyles.ApplyEmphasisButtonStyle(_healButton);
        _healButton.Pressed += () => OnHealPressed(healAmount);

        var smithButton = GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/SmithButton");
        smithButton.Disabled = !RunState.Deck.Any(c => !CardUpgrade.IsUpgraded(c));
        ChromeStyles.ApplyEmphasisButtonStyle(smithButton);
        smithButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        smithButton.Pressed += ShowUpgradeChoices;

        var leaveButton = GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/LeaveButton");
        leaveButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        leaveButton.Pressed += OnLeavePressed;

        GetNode<Label>("UpgradeCenterContainer/UpgradeVBox/UpgradeTitleLabel")
            .AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);

        _cancelButton = GetNode<Button>("UpgradeCenterContainer/UpgradeVBox/CancelButton");
        _cancelButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _cancelButton.Pressed += ShowMainChoices;

        // Two views swapped by Visible, so where focus belongs depends on
        // which one is showing - a hidden control drops out of focus
        // navigation entirely. Escape backs out of the upgrade picker first
        // and only leaves the site from the main choices, which is the same
        // shape as the two Cancel/Leave buttons.
        _keyboardNav = ScreenKeyboardNav.Attach(this, PreferredFocus, OnCancelRequested);
    }

    // The card, not the Upgrade button under it - CardPicker makes the card the
    // grid's only focus stop so arrow keys walk it one press per card.
    private Control? PreferredFocus() => _upgradeView.Visible
        ? _upgradeList.GetChildren().SelectMany(c => c.GetChildren()).OfType<CardView>().FirstOrDefault()
          ?? (Control)_cancelButton
        : _healButton;

    private void OnCancelRequested()
    {
        if (_upgradeView.Visible) ShowMainChoices();
        else OnLeavePressed();
    }

    // The campfire the map already draws for this node type, at sprite scale.
    // Rest was the emptiest screen in the game - three stock buttons on a bare
    // tiled floor - and the fire it is named after existed as an asset the
    // whole time, just nowhere except the map (ROADMAP Phase 4).
    private void BuildCampfire()
    {
        if (ArtAssets.MapIcon(MapNodeType.Rest) is not { } fire) return;

        var plinth = ScreenChrome.ArtPlinth(fire);
        GetNode<CenterContainer>("CenterContainer/VBoxContainer/ArtSlot").AddChild(plinth);

        if (SettingsManager.Instance.ReduceMotion) return;

        // A slow brightness flicker rather than a scale or position tween: the
        // fire is the only light source the composition implies, and a fire
        // that physically bobs reads as a floating object. Modulate is a
        // multiply, so this only ever darkens - the plinth's own bezel goes
        // with it, which is what sells it as light falling off.
        var tween = plinth.CreateTween();
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(plinth, "modulate", new Color(0.86f, 0.86f, 0.9f), 1.3);
        tween.TweenProperty(plinth, "modulate", Colors.White, 0.9);
        tween.TweenProperty(plinth, "modulate", new Color(0.93f, 0.92f, 0.94f), 0.6);
        tween.TweenProperty(plinth, "modulate", Colors.White, 1.1);
    }

    // Rest sites offer exactly one action (heal, upgrade, or neither) before
    // leaving - Smith swaps the view to a card picker instead of navigating
    // away immediately, since (unlike Heal) it needs a second choice of
    // *which* card first, and Cancel needs to come back here without
    // consuming the visit.
    //
    // The grid itself is CardPicker, shared with EventScreen's own card
    // pickers - it renders real CardViews rather than stacked text rows
    // because the deck is the thing being chosen from, which is the same
    // argument ShopScreen and RewardScreen make for their offers.
    //
    // The upgraded card is what each column *shows*, not the current one: the
    // choice is which card to end up with. The "was:" line under the button
    // spells out the before, since the button text alone ("Upgrade to
    // Strike+") never said what changed.
    private void ShowUpgradeChoices()
    {
        CardPicker.Populate(
            _upgradeList,
            UpgradeRandomCardOutcome.Upgradable(),
            "Upgrade",
            index => CardUpgrade.Apply(RunState.Deck[index]),
            index => CardPicker.WasLine(RunState.Deck[index]),
            OnCardUpgraded,
            // Down off the last row lands on Cancel - and nowhere else does,
            // which is the whole point: it used to win the geometric focus
            // search from row one and strand the rest of the grid.
            _cancelButton);

        _choicesView.Visible = false;
        _upgradeView.Visible = true;
        // Hiding a view frees nothing but does remove its controls from focus
        // navigation, and the Smith button that was just pressed is now in the
        // hidden half - so focus has to move with the swap, both ways.
        _keyboardNav?.Regrab();
    }

    private void ShowMainChoices()
    {
        _upgradeView.Visible = false;
        _choicesView.Visible = true;
        _keyboardNav?.Regrab();
    }

    // Replaces just this one list entry, not the shared CardDefinition
    // reference every same-named copy in the deck points to - RunState.Deck
    // holds N separate slots for "5x Strike", not 5 references into a
    // shared pool, so this only upgrades the specific copy the player
    // clicked.
    private void OnCardUpgraded(int index)
    {
        RunState.Deck[index] = CardUpgrade.Apply(RunState.Deck[index]);
        OnLeavePressed();
    }

    private void OnHealPressed(int amount)
    {
        AudioManager.Instance?.PlaySfx("heal");
        RunState.PlayerCurrentHp = Mathf.Min(RunState.PlayerMaxHp, RunState.PlayerCurrentHp + amount);
        OnLeavePressed();
    }

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
