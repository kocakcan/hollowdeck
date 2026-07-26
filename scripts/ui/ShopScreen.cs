using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class ShopScreen : Control
{
    private const int CardPrice = 50;
    private const int RelicPrice = 150;
    private const int PotionPrice = 40;

    private Label _goldLabel = null!;
    private VBoxContainer _offersList = null!;
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
        _goldLabel = GetNode<Label>("GoldLabel");
        _offersList = GetNode<VBoxContainer>("OffersScroll/OffersList");
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
        // equivalent, so they stay on the generic offer-row treatment.
        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        foreach (var card in Sample(MetaProgressionManager.Instance.UnlockedCards().ToList(), 4, rng))
        {
            AddCardOffer(card, cardScene);
        }

        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var availableRelics = RelicDatabase.All
            .Where(r => !ownedRelicIds.Contains(r.Id) && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();
        foreach (var relic in Sample(availableRelics, 2, rng))
        {
            AddOfferRow($"{relic.Name} (relic) - {RelicPrice}g", relic.Description, RelicPrice,
                () => RunState.Relics.Add(new RelicInstance(relic)), ArtAssets.RelicIcon(relic.Id));
        }

        // All potions unlocked too - same reasoning as cards above.
        foreach (var potion in Sample(PotionDatabase.All.ToList(), 2, rng))
        {
            AddOfferRow($"{potion.Name} (potion) - {PotionPrice}g", EffectDescriptionFormatter.Describe(potion.Effects),
                PotionPrice, () =>
            {
                if (RunState.Potions.Count >= RunState.MaxPotionSlots) return false;
                RunState.Potions.Add(new PotionInstance(potion));
                return true;
            }, ArtAssets.PotionIcon(potion.Id));
        }

        RefreshOffers();
    }

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
        _cardOffersRow.AddChild(column);

        var view = cardScene.Instantiate<CardView>();
        column.AddChild(view);
        view.Interactive = false;
        view.SetCardInstance(new CardInstance(card));

        var buyButton = new Button { Text = $"Buy ({CardPrice}g)" };
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

    private void AddOfferRow(string label, string description, int price, System.Action onBuy, Texture2D? icon = null) =>
        AddOfferRow(label, description, price, () => { onBuy(); return true; }, icon);

    private void AddOfferRow(string label, string description, int price, System.Func<bool> onBuy, Texture2D? icon = null)
    {
        var row = new VBoxContainer();
        var button = new Button { Text = label };
        if (icon is not null)
        {
            button.Icon = icon;
            button.ExpandIcon = true;
            button.CustomMinimumSize = new Vector2(0, 36);
        }
        var descriptionLabel = new Label
        {
            Text = description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        row.AddChild(button);
        row.AddChild(descriptionLabel);
        _offersList.AddChild(row);
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
        _goldLabel.Text = $"Gold: {RunState.Gold}";
        foreach (var (button, price) in _offerButtons)
        {
            button.Disabled = RunState.Gold < price;
        }
    }

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
