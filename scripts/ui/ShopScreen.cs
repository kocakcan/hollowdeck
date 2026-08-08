using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
// For RemoveChosenCardOutcome. Hollowdeck.UI already reaches into
// Hollowdeck.Events for exactly this reason at RestScreen's Smith picker: the
// outcome classes are where "what does removing/upgrading a card mean" lives,
// and a screen re-deriving it would be a second answer.
using Hollowdeck.Events;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Two rows of merchandise across the full width: four cards on top, the
// relic/potion stock below, priced and framed the same way.
//
// The relics and potions used to sit in a 476px-wide ScrollContainer pinned to
// the bottom-left with a visible scrollbar, rendering each item as a stock
// Button with a 36px-tall stretched icon and a wrapped description under it -
// while the right half of the screen was empty (ROADMAP Phase 4). They are now
// tiles the same shape as each other, which is also what makes "these four
// things are all for sale" read at a glance.
public partial class ShopScreen : Control
{
    private const int CardPrice = 50;
    private const int RelicPrice = 150;
    private const int PotionPrice = 40;

    // Deck thinning, priced above a card and well below a relic: removing a
    // Strike is worth more than adding one, and a run that could buy four
    // removals a shop would out-thin every other lever in the game. Once per
    // visit for the same reason, which MarkSold already enforces by dropping
    // the button out of _offerButtons.
    //
    // This ships in the same phase as Curses on purpose. Adding a way to put
    // dead cards in a deck without adding a way to take them out is punishment
    // rather than design, and deck-thinning existed as exactly one random
    // event (the_confessor) before this.
    private const int RemovalPrice = 75;

    // Wide enough for two lines of a relic description at the body size, and
    // narrow enough that four tiles plus separations clear the design width.
    // 236 + 24 of frame padding = 260 each, so 4 tiles and 3 gaps come to
    // 1088 inside OffersRow's 1112 - at 256 they came to 1168 and the outer
    // two tiles were clipped by the screen edges.
    private const int TileWidth = 236;

    private HBoxContainer _offersRow = null!;
    private HBoxContainer _cardOffersRow = null!;

    // The removal service. It is a button beside Leave rather than a fifth
    // tile in OffersRow: a tile is 236 wide plus 24 of frame, so five of them
    // plus separations come to 1364 against OffersRow's 1112 and the outer two
    // would be clipped - the exact failure the 236 in TileWidth was chosen to
    // avoid. It also is not merchandise, and reads better for not sitting in
    // the merchandise row.
    private Button _removeCardButton = null!;
    private Control _pickerView = null!;
    private GridContainer _pickerList = null!;
    private Button _pickerCancelButton = null!;
    private Button _leaveButton = null!;

    // The picker's first card, so keyboard focus lands inside the grid rather
    // than on whatever the screen was focusing before it opened.
    private CardView? _pickerFirstCard;

    // Shared with the rest site and the two event pickers - Prompt, the
    // deck-count floor in Selectable(), and Apply all come from there rather
    // than being restated here.
    private readonly RemoveChosenCardOutcome _removal = new();

    // Every buy button on the screen with its price, so RefreshOffers can
    // grey out what the player can no longer afford after each purchase.
    // Previously a too-expensive click just silently returned, which read as
    // "this item is broken" rather than "you don't have the gold".
    private readonly List<(Button Button, int Price)> _offerButtons = new();

    private ScreenKeyboardNavListener? _keyboardNav;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "cobble", new Color(0.7f, 0.7f, 0.75f));
        DeckViewButtons.Attach(this);
        ScreenChrome.AddTitle(this, "Shop");
        // Three relic columns rather than the default six, the same 3-wide grid
        // CombatScreen's RelicBar uses and for the same reason: this screen's
        // content comes further left than the centred title does. CardOffersRow
        // is four 176px cards centred on the design width, so it starts at
        // x=194, and a six-column block reaches x=280 - a playtest screenshot
        // has the relic grid painted straight over the first card's name
        // banner. Three columns is 3*40 + 2*4 = 128, ending at x=148.
        //
        // Narrowing is what there is: vertically the shop is full (cards
        // 64..348, merchandise 360..580, buttons 596..634), so there is nothing
        // to push down. ScreenSmokeTest asserts the clearance at a 13-relic
        // haul, which is five rows and still ends well above the tile row.
        ScreenChrome.AddRunStatus(this, relicColumns: 3);

        _offersRow = GetNode<HBoxContainer>("OffersRow");
        _cardOffersRow = GetNode<HBoxContainer>("CardOffersRow");
        _leaveButton = GetNode<Button>("LeaveButton");
        _leaveButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _leaveButton.Pressed += OnLeavePressed;

        _removeCardButton = GetNode<Button>("RemoveCardButton");
        _pickerView = GetNode<Control>("PickerCenterContainer");
        _pickerList = GetNode<GridContainer>("PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");
        _pickerCancelButton = GetNode<Button>("PickerCenterContainer/PickerVBox/PickerCancelButton");
        ChromeStyles.ApplyEmphasisButtonStyle(_removeCardButton);
        _removeCardButton.Text = $"Remove a Card ({RemovalPrice}g)";
        _removeCardButton.Pressed += OnRemoveCardPressed;
        _pickerCancelButton.Pressed += ClosePicker;

        var rng = RngStreams.Shop;

        // Cards get the real CardView renderer ("one card component
        // everywhere") with a separate gold-price Buy button beneath, since
        // the card's own cost badge shows its Energy cost, not its Gold
        // price - those are two different numbers that would be confusing
        // conflated into a single badge. Relics/potions have no CardView
        // equivalent, so they get the tile treatment below.
        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        // Cards go through CardPool so the stock is rarity-weighted the same
        // way a fight reward is; relics and potions keep the plain uniform
        // Sample below, since neither carries a rarity tier today.
        foreach (var card in CardPool.Sample(MetaProgressionManager.Instance.UnlockedCards(), 4, rng))
        {
            AddCardOffer(card, cardScene);
        }

        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var availableRelics = RelicDatabase.All
            .Where(r => !ownedRelicIds.Contains(r.Id) && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();
        foreach (var relic in Sample(availableRelics, 2, rng))
        {
            AddOfferTile(relic.Name, "Relic", relic.Description, RelicPrice,
                () => RunState.Relics.Add(new RelicInstance(relic)), ArtAssets.RelicIcon(relic.Id));
        }

        // All potions unlocked too - same reasoning as cards above.
        foreach (var potion in Sample(PotionDatabase.All.ToList(), 2, rng))
        {
            AddOfferTile(potion.Name, "Potion",
                EffectDescriptionFormatter.Describe(potion.Effects, new DescribeContext(TargetType: potion.Target)),
                PotionPrice, () =>
            {
                if (RunState.Potions.Count >= RunState.MaxPotionSlots) return false;
                RunState.Potions.Add(new PotionInstance(potion));
                return true;
            }, ArtAssets.PotionIcon(potion.Id));
        }

        // The removal service is registered like any other offer so
        // RefreshOffers greys it when the gold runs out, but it is NOT bought
        // through AddOfferTile's handler: that deducts gold and marks the
        // offer sold the moment it is pressed, and cancelling out of the
        // picker must not charge for a card that never left the deck.
        _offerButtons.Add((_removeCardButton, RemovalPrice));

        // Attached before RefreshOffers so its first Regrab happens with the
        // affordable/unaffordable state already applied - starting focus on a
        // button that is about to be disabled would immediately lose it.
        _keyboardNav = ScreenKeyboardNav.Attach(this, PreferredFocus, OnCancelRequested);

        RefreshOffers();
    }

    // While the picker is open it owns the keyboard, and the shop underneath
    // is hidden rather than merely covered - Godot's focus navigation reaches
    // controls behind an overlay perfectly happily, so a visible-but-covered
    // Buy button would still be tabbable.
    private Control PreferredFocus()
    {
        if (_pickerView.Visible) return (Control?)_pickerFirstCard ?? _pickerCancelButton;
        return _offerButtons.FirstOrDefault(o => !o.Button.Disabled).Button ?? _leaveButton;
    }

    private void OnCancelRequested()
    {
        if (_pickerView.Visible) ClosePicker();
        else OnLeavePressed();
    }

    private void OnRemoveCardPressed()
    {
        if (RunState.Gold < RemovalPrice) return;
        AudioManager.Instance?.PlaySfx("ui_click");

        // Selectable() carries the one-card floor: an empty deck draws nothing
        // and the next fight is unwinnable and unquittable.
        var selectable = _removal.Selectable().ToList();
        if (selectable.Count == 0) return;

        _pickerFirstCard = CardPicker.Populate(
            _pickerList, selectable, "Remove",
            index => RunState.Deck[index],
            // No subtitle: nothing changes about the card, it just goes. The
            // upgrade pickers need a "was:" line because their column shows a
            // card the deck does not contain yet.
            _ => null,
            OnCardRemoved,
            _pickerCancelButton);

        SetShopVisible(false);
        _pickerView.Visible = true;
        _keyboardNav?.Regrab();
    }

    private void OnCardRemoved(int deckIndex)
    {
        // Gold is spent here rather than at the button press, so Cancel is
        // free. Both this and MarkSold have to happen before ClosePicker's
        // Regrab, or focus is handed back to a button that is about to change.
        _removal.Apply(deckIndex);
        RunState.Gold -= RemovalPrice;
        MarkSold(_removeCardButton);
        _removeCardButton.Text = "Removed";
        ClosePicker();
    }

    private void ClosePicker()
    {
        _pickerView.Visible = false;
        _pickerFirstCard = null;
        SetShopVisible(true);
        RefreshOffers();
    }

    private void SetShopVisible(bool visible)
    {
        _cardOffersRow.Visible = visible;
        _offersRow.Visible = visible;
        _leaveButton.Visible = visible;
        _removeCardButton.Visible = visible;
    }

    // Uniform sampling, for the pools that have no rarity to weight by.
    private static List<T> Sample<T>(List<T> pool, int count, System.Random rng)
    {
        var copy = new List<T>(pool);
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.Take(count).ToList();
    }

    private void AddCardOffer(CardDefinition card, PackedScene cardScene)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Sm);
        _cardOffersRow.AddChild(column);

        var view = cardScene.Instantiate<CardView>();
        column.AddChild(view);
        view.Interactive = false;
        view.SetCardInstance(new CardInstance(card));

        var buyButton = new Button { Text = $"Buy ({CardPrice}g)" };
        ChromeStyles.ApplyEmphasisButtonStyle(buyButton);
        column.AddChild(buyButton);
        _offerButtons.Add((buyButton, CardPrice));
        buyButton.Pressed += () =>
        {
            if (RunState.Gold < CardPrice) return;
            AudioManager.Instance?.PlaySfx("reward_pickup");
            RunState.Deck.Add(card);
            RunState.Gold -= CardPrice;
            MarkSold(buyButton);
            RefreshOffers();
        };
    }

    private void AddOfferTile(string name, string kind, string description, int price,
        System.Action onBuy, Texture2D? icon = null) =>
        AddOfferTile(name, kind, description, price, () => { onBuy(); return true; }, icon);

    // Icon, name, kind, rules text, price button - in that order, in a framed
    // panel of a fixed width. The kind ("Relic"/"Potion") is its own muted
    // line rather than a parenthetical inside the name, because the name is
    // the thing being scanned for.
    private void AddOfferTile(string name, string kind, string description, int price,
        System.Func<bool> onBuy, Texture2D? icon = null)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(TileWidth, 0) };
        column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);

        if (icon is not null)
        {
            var slot = new CenterContainer();
            // 2x, matching the map's node icons. 1x is the HUD size and gets
            // lost inside a 256px tile; 3x would not leave room for two lines
            // of description in the height the row has.
            slot.AddChild(ScreenChrome.PixelIcon(icon, 2));
            column.AddChild(slot);
        }

        column.AddChild(ScreenChrome.Heading(name, UiTheme.Fonts.Body));

        var kindLabel = ScreenChrome.Body(kind);
        kindLabel.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Small);
        kindLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N5);
        column.AddChild(kindLabel);

        var descriptionLabel = ScreenChrome.Body(description);
        descriptionLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(descriptionLabel);

        var button = new Button { Text = $"Buy ({price}g)" };
        ChromeStyles.ApplyEmphasisButtonStyle(button);
        column.AddChild(button);

        _offersRow.AddChild(ScreenChrome.Frame(column, UiTheme.Spacing.Md));
        _offerButtons.Add((button, price));

        button.Pressed += () =>
        {
            if (RunState.Gold < price) return;
            if (!onBuy()) return;
            AudioManager.Instance?.PlaySfx("reward_pickup");
            RunState.Gold -= price;
            MarkSold(button);
            RefreshOffers();
        };
    }

    // A sold offer drops out of _offerButtons entirely so RefreshOffers can
    // never re-enable it when gold later goes back up (it can't here, but
    // that's a trap worth closing rather than relying on).
    private void MarkSold(Button button)
    {
        button.Disabled = true;
        // Set here as well as in RefreshOffers, not instead of it: this button
        // is about to leave _offerButtons, so the loop below never sees it
        // again and would leave a "Sold" offer tabbable for the rest of the
        // visit. See RefreshOffers for why FocusMode is doing anything here.
        button.FocusMode = FocusModeEnum.None;
        button.Text = "Sold";
        _offerButtons.RemoveAll(o => o.Button == button);
    }

    private void RefreshOffers()
    {
        ScreenChrome.RefreshRunStatus(this);
        foreach (var (button, price) in _offerButtons)
        {
            bool affordable = RunState.Gold >= price;
            button.Disabled = !affordable;

            // Disabled alone does not keep the keyboard off an offer you cannot
            // buy. Measured on 4.7.1: FindNextValidFocus and
            // FindValidFocusNeighbor filter on FocusMode and visibility, and
            // BaseButton keeps FocusModeEnum.All when disabled - so tabbing
            // stopped on a greyed-out Buy button and painted the focus ring on
            // a purchase the player could not make. Same fix, and same
            // correction to the same wrong belief, as MapScreen's unreachable
            // nodes; ScreenKeyboardNav's comment has the details.
            //
            // Reversible rather than one-way, because this loop runs on every
            // refresh: gold cannot go back up inside a shop today, but an offer
            // that became affordable again must become focusable again, or the
            // mouse and the keyboard would disagree about what is buyable.
            button.FocusMode = affordable ? FocusModeEnum.All : FocusModeEnum.None;
        }

        // Buying takes offers out of reach, including the one just pressed, so
        // without this a purchase leaves the keyboard stranded mid-screen. Must
        // stay *after* the loop, which is what makes something to re-grab from.
        //
        // The symptom was right and the stated cause was not, and both halves
        // are worth keeping. This used to read "Godot drops focus off a control
        // the moment it becomes Disabled" - measured on 4.7.1, it does not:
        // disabling a focused control leaves it holding focus. So the original
        // failure was not an *absent* focus owner but an inert one, focus stuck
        // on a greyed-out "Sold" button where Enter did nothing.
        //
        // Since the loop above now also sets FocusModeEnum.None, and
        // Control.SetFocusMode *does* release focus, the pressed button is
        // genuinely dropped and this Regrab is load-bearing for the reason it
        // was always credited with. Both behaviours are pinned by
        // KeyboardSmokeTest.TestOnlyFocusModeExcludesAControlFromTheKeyboard.
        _keyboardNav?.Regrab();
    }

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
