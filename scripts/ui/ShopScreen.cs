using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
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

    // Wide enough for two lines of a relic description at the body size, and
    // narrow enough that four tiles plus separations clear the design width.
    // 236 + 24 of frame padding = 260 each, so 4 tiles and 3 gaps come to
    // 1088 inside OffersRow's 1112 - at 256 they came to 1168 and the outer
    // two tiles were clipped by the screen edges.
    private const int TileWidth = 236;

    private HBoxContainer _offersRow = null!;
    private HBoxContainer _cardOffersRow = null!;

    // Every buy button on the screen with its price, so RefreshOffers can
    // grey out what the player can no longer afford after each purchase.
    // Previously a too-expensive click just silently returned, which read as
    // "this item is broken" rather than "you don't have the gold".
    private readonly List<(Button Button, int Price)> _offerButtons = new();

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "cobble", new Color(0.7f, 0.7f, 0.75f));
        DeckViewButtons.Attach(this);
        ScreenChrome.AddTitle(this, "Shop");
        ScreenChrome.AddRunStatus(this);

        _offersRow = GetNode<HBoxContainer>("OffersRow");
        _cardOffersRow = GetNode<HBoxContainer>("CardOffersRow");
        var leaveButton = GetNode<Button>("LeaveButton");
        leaveButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        leaveButton.Pressed += OnLeavePressed;

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

        RefreshOffers();
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
        button.Text = "Sold";
        _offerButtons.RemoveAll(o => o.Button == button);
    }

    private void RefreshOffers()
    {
        ScreenChrome.RefreshRunStatus(this);
        foreach (var (button, price) in _offerButtons)
        {
            button.Disabled = RunState.Gold < price;
        }
    }

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
