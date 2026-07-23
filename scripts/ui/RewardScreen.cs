using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RewardScreen : Control
{
    public override void _Ready()
    {
        GetNode<Label>("CenterContainer/VBoxContainer/GoldLabel").Text = $"You found {RewardContext.GoldAwarded} gold.";

        var cardButtons = new[]
        {
            GetNode<Button>("CenterContainer/VBoxContainer/CardChoice0"),
            GetNode<Button>("CenterContainer/VBoxContainer/CardChoice1"),
            GetNode<Button>("CenterContainer/VBoxContainer/CardChoice2"),
        };

        for (int i = 0; i < cardButtons.Length; i++)
        {
            if (i >= RewardContext.CardChoices.Count)
            {
                cardButtons[i].Visible = false;
                continue;
            }

            var card = RewardContext.CardChoices[i];
            cardButtons[i].Text = $"{card.Name} ({card.Type}, cost {card.Cost})";
            cardButtons[i].Pressed += () => OnCardChosen(card);
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
