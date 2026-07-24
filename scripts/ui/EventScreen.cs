using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Simple choice-based text events - one authored EventDefinition rolled at
// random per visit (same "roll fresh in _Ready, don't thread node data"
// pattern TreasureScreen already uses for its relic pick - no MapNode
// event field or mailbox class needed). Each choice resolves through
// EventOutcomeRegistry, a small self-contained system distinct from
// EffectRegistry, whose EffectContext requires a live CombatManager/
// Combatant that doesn't exist outside a fight.
public partial class EventScreen : Control
{
    private VBoxContainer _choicesList = null!;
    private Label _resultLabel = null!;
    private Button _continueButton = null!;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "demonic", new Color(0.7f, 0.65f, 0.75f));

        var events = EventDatabase.All.ToList();
        var picked = events[RngStreams.Shop.Next(events.Count)];

        GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel").Text = picked.Title;
        GetNode<Label>("CenterContainer/VBoxContainer/DescriptionLabel").Text = picked.Description;
        _choicesList = GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        _resultLabel = GetNode<Label>("CenterContainer/VBoxContainer/ResultLabel");
        _continueButton = GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");
        _continueButton.Pressed += () => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);

        foreach (var choice in picked.Choices)
        {
            var button = new Button { Text = choice.Label };
            button.Pressed += () => OnChoiceChosen(choice);
            _choicesList.AddChild(button);
        }
    }

    private void OnChoiceChosen(EventChoice choice)
    {
        foreach (var child in _choicesList.GetChildren())
        {
            _choicesList.RemoveChild(child);
            child.QueueFree();
        }
        _resultLabel.Text = EventOutcomeRegistry.Resolve(choice);
        _resultLabel.Visible = true;
        _continueButton.Visible = true;
    }
}
