using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RewardScreen : Control
{
    public override void _Ready()
    {
        ScreenBackground.Attach(this, "crypt", new Color(0.6f, 0.6f, 0.65f));
        GetNode<Label>("CenterContainer/VBoxContainer/GoldLabel").Text =
            $"You found {RewardContext.GoldAwarded} gold.";

        var relicLabel = GetNode<Label>("CenterContainer/VBoxContainer/RelicLabel");
        if (RewardContext.GuaranteedRelic is { } relic)
        {
            relicLabel.Visible = true;
            relicLabel.Text = $"Relic: {relic.Name} - {relic.Description}";
        }
        else
        {
            relicLabel.Visible = false;
        }

        var choicesList = GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        foreach (var card in RewardContext.CardChoices)
        {
            var row = new VBoxContainer();
            var button = new Button { Text = $"{card.Name} ({card.Type}, cost {card.Cost})" };
            if (ArtAssets.CardIcon(card.Id) is { } icon)
            {
                button.Icon = icon;
                button.ExpandIcon = true;
                button.CustomMinimumSize = new Vector2(0, 36);
            }
            var description = new Label
            {
                Text = EffectDescriptionFormatter.Describe(card.Effects),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            button.Pressed += () => OnCardChosen(card);
            row.AddChild(button);
            row.AddChild(description);
            choicesList.AddChild(row);
        }

        GetNode<Button>("CenterContainer/VBoxContainer/SkipButton").Pressed += OnSkipPressed;
    }

    private void OnCardChosen(CardDefinition card)
    {
        RunState.Deck.Add(card);
        Advance();
    }

    private void OnSkipPressed() => Advance();

    private static void Advance() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
