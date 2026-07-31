using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RestScreen : Control
{
    private const float HealFraction = 0.3f;

    // CardView's own width (ROADMAP Phase 2 set cards to 176x240). Every
    // column in the upgrade picker is exactly this, so five of them plus the
    // grid's separations fit the scroll viewport with no horizontal scroll.
    private const int CardColumnWidth = 176;

    private Control _choicesView = null!;
    private Control _upgradeView = null!;
    private GridContainer _upgradeList = null!;

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
        var healButton = GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/HealButton");
        healButton.Text = $"Rest - Heal {healAmount} HP";
        ChromeStyles.ApplyEmphasisButtonStyle(healButton);
        healButton.Pressed += () => OnHealPressed(healAmount);

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

        var cancelButton = GetNode<Button>("UpgradeCenterContainer/UpgradeVBox/CancelButton");
        cancelButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        cancelButton.Pressed += ShowMainChoices;
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
    // The picker renders real CardViews rather than the three stacked text
    // rows (button, current rules, green "becomes..." line) it used to: the
    // deck is the thing being chosen from, and this is the one screen outside
    // combat where the player is asked to compare cards. "One card component
    // everywhere" is the same argument ShopScreen and RewardScreen already
    // make for their offers.
    private void ShowUpgradeChoices()
    {
        foreach (var child in _upgradeList.GetChildren())
        {
            _upgradeList.RemoveChild(child);
            child.QueueFree();
        }

        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        for (int i = 0; i < RunState.Deck.Count; i++)
        {
            var card = RunState.Deck[i];
            if (CardUpgrade.IsUpgraded(card)) continue;

            int index = i;
            var upgraded = CardUpgrade.Apply(card);

            // Pinned to the card's own width. A column is only as narrow as
            // its widest child, and the button used to read "Upgrade to
            // Cleave+" - wide enough in the display face to push five columns
            // past the scroll viewport, which then clipped the outer two.
            var column = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(CardColumnWidth, 0),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            };
            column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);
            _upgradeList.AddChild(column);

            // Just "Upgrade": the card above it already carries the upgraded
            // name in its own title.
            var button = new Button { Text = "Upgrade" };
            ChromeStyles.ApplyEmphasisButtonStyle(button);
            button.Pressed += () =>
            {
                AudioManager.Instance?.PlaySfx("reward_pickup");
                OnCardUpgraded(index);
            };

            // The upgraded card is what is shown, not the current one: the
            // choice being made is which card to *end up with*, and the button
            // text alone ("Upgrade to Strike+") never said what changed. The
            // delta line below spells out the before, so both halves are
            // visible without a hover.
            var view = cardScene.Instantiate<CardView>();
            column.AddChild(view);
            view.Interactive = false;
            view.SetCardInstance(new CardInstance(upgraded));

            column.AddChild(button);
            column.AddChild(new Label
            {
                Text = "was: " + EffectDescriptionFormatter.Describe(
                    card.Effects, new DescribeContext(TargetType: card.Target)),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(CardColumnWidth, 0),
                Modulate = new Color(0.7f, 0.7f, 0.7f),
            });
        }

        _choicesView.Visible = false;
        _upgradeView.Visible = true;
    }

    private void ShowMainChoices()
    {
        _upgradeView.Visible = false;
        _choicesView.Visible = true;
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
