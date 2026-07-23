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

    public override void _Ready()
    {
        _goldLabel = GetNode<Label>("GoldLabel");
        _offersList = GetNode<VBoxContainer>("OffersList");
        GetNode<Button>("LeaveButton").Pressed += OnLeavePressed;

        var rng = RngStreams.Shop;

        foreach (var card in Sample(CardDatabase.All.ToList(), 4, rng))
        {
            AddOfferRow($"{card.Name} (card) - {CardPrice}g", EffectDescriptionFormatter.Describe(card.Effects),
                CardPrice, () => RunState.Deck.Add(card));
        }

        var ownedRelicIds = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        var availableRelics = RelicDatabase.All.Where(r => !ownedRelicIds.Contains(r.Id)).ToList();
        foreach (var relic in Sample(availableRelics, 2, rng))
        {
            AddOfferRow($"{relic.Name} (relic) - {RelicPrice}g", relic.Description, RelicPrice,
                () => RunState.Relics.Add(new RelicInstance(relic)));
        }

        foreach (var potion in Sample(PotionDatabase.All.ToList(), 2, rng))
        {
            AddOfferRow($"{potion.Name} (potion) - {PotionPrice}g", EffectDescriptionFormatter.Describe(potion.Effects),
                PotionPrice, () =>
            {
                if (RunState.Potions.Count >= RunState.MaxPotionSlots) return false;
                RunState.Potions.Add(new PotionInstance(potion));
                return true;
            });
        }

        RefreshGoldLabel();
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

    private void AddOfferRow(string label, string description, int price, System.Action onBuy) =>
        AddOfferRow(label, description, price, () => { onBuy(); return true; });

    private void AddOfferRow(string label, string description, int price, System.Func<bool> onBuy)
    {
        var row = new VBoxContainer();
        var button = new Button { Text = label };
        var descriptionLabel = new Label
        {
            Text = description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        row.AddChild(button);
        row.AddChild(descriptionLabel);
        _offersList.AddChild(row);

        button.Pressed += () =>
        {
            if (RunState.Gold < price) return;
            if (!onBuy()) return;
            RunState.Gold -= price;
            button.Disabled = true;
            RefreshGoldLabel();
        };
    }

    private void RefreshGoldLabel() => _goldLabel.Text = $"Gold: {RunState.Gold}";

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
