using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RestScreen : Control
{
    private const float HealFraction = 0.3f;

    private Control _choicesView = null!;
    private Control _upgradeView = null!;
    private VBoxContainer _upgradeList = null!;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "dirt", new Color(0.5f, 0.42f, 0.35f));
        DeckViewButtons.Attach(this);
        GetNode<Label>("HpLabel").Text = $"HP: {RunState.PlayerCurrentHp}/{RunState.PlayerMaxHp}";

        _choicesView = GetNode<Control>("CenterContainer");
        _upgradeView = GetNode<Control>("UpgradeCenterContainer");
        _upgradeList = GetNode<VBoxContainer>("UpgradeCenterContainer/UpgradeVBox/ScrollContainer/UpgradeList");

        int healAmount = Mathf.RoundToInt(RunState.PlayerMaxHp * HealFraction);
        var healButton = GetNode<Button>("CenterContainer/VBoxContainer/HealButton");
        healButton.Text = $"Rest - Heal {healAmount} HP";
        healButton.Pressed += () => OnHealPressed(healAmount);

        var smithButton = GetNode<Button>("CenterContainer/VBoxContainer/SmithButton");
        smithButton.Disabled = !RunState.Deck.Any(c => !CardUpgrade.IsUpgraded(c));
        smithButton.Pressed += ShowUpgradeChoices;

        GetNode<Button>("CenterContainer/VBoxContainer/LeaveButton").Pressed += OnLeavePressed;
        GetNode<Button>("UpgradeCenterContainer/UpgradeVBox/CancelButton").Pressed += ShowMainChoices;
    }

    // Rest sites offer exactly one action (heal, upgrade, or neither) before
    // leaving - Smith swaps the view to a card picker instead of navigating
    // away immediately, since (unlike Heal) it needs a second choice of
    // *which* card first, and Cancel needs to come back here without
    // consuming the visit.
    private void ShowUpgradeChoices()
    {
        foreach (var child in _upgradeList.GetChildren())
        {
            _upgradeList.RemoveChild(child);
            child.QueueFree();
        }

        for (int i = 0; i < RunState.Deck.Count; i++)
        {
            var card = RunState.Deck[i];
            if (CardUpgrade.IsUpgraded(card)) continue;

            int index = i;
            var upgraded = CardUpgrade.Apply(card);

            var row = new VBoxContainer();
            var button = new Button { Text = $"{card.Name} ({card.Type}, cost {card.Cost})" };
            if (ArtAssets.CardIcon(card.Id) is { } icon)
            {
                button.Icon = icon;
                button.ExpandIcon = true;
                button.CustomMinimumSize = new Vector2(0, 36);
            }
            var before = new Label
            {
                Text = EffectDescriptionFormatter.Describe(card.Effects),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var after = new Label
            {
                Text = $"Becomes {upgraded.Name}: {EffectDescriptionFormatter.Describe(upgraded.Effects)}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(0.65f, 1f, 0.65f),
            };
            button.Pressed += () => OnCardUpgraded(index);
            row.AddChild(button);
            row.AddChild(before);
            row.AddChild(after);
            _upgradeList.AddChild(row);
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
        RunState.PlayerCurrentHp = Mathf.Min(RunState.PlayerMaxHp, RunState.PlayerCurrentHp + amount);
        OnLeavePressed();
    }

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
