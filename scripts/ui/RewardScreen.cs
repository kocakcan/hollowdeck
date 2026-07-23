using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RewardScreen : Control
{
    public override void _Ready()
    {
        GetNode<Label>("CenterContainer/VBoxContainer/GoldLabel").Text =
            $"You found {RewardContext.GoldAwarded} gold.";

        var choicesList = GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        foreach (var card in RewardContext.CardChoices)
        {
            var row = new VBoxContainer();
            var button = new Button { Text = $"{card.Name} ({card.Type}, cost {card.Cost})" };
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

    private static void Advance()
    {
        RunManager.Instance.AdvanceEncounter();
        RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
    }
}
