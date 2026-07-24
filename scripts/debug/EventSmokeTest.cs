using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for the Event node system: EventDatabase loads, every
// authored outcome key resolves, per-outcome behavior (gold/HP/relic/card),
// map generation actually produces Event nodes, and EventScreen.tscn loads
// and resolves a choice. Run via
// `godot --headless scenes/debug/EventSmokeTest.tscn`.
public partial class EventSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        EventDatabase.LoadAll();

        TestEventDatabaseLoads();
        TestEveryOutcomeKeyIsRegistered();
        TestGainAndLoseGold();
        TestHealAndLoseHp();
        TestGainRandomCard();
        TestGainAndLoseRelic();
        TestMapGeneratorProducesEventNodes();
        TestEventScreenLoadsAndResolvesAChoice();

        GD.Print($"EventSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void TestEventDatabaseLoads()
    {
        Check("event_database_loads_five_events", EventDatabase.All.Count == 5, $"count={EventDatabase.All.Count}");
    }

    private void TestEveryOutcomeKeyIsRegistered()
    {
        bool anyUnregistered = false;
        string detail = "";
        foreach (var def in EventDatabase.All)
        {
            foreach (var choice in def.Choices)
            {
                // A quick membership check via reflection on the private
                // dictionary avoids relying on parsing PushError log output.
                var field = typeof(EventOutcomeRegistry).GetField("Outcomes",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var outcomes = (System.Collections.IDictionary)field!.GetValue(null)!;
                if (!outcomes.Contains(choice.Outcome))
                {
                    anyUnregistered = true;
                    detail = $"{def.Id}: unknown outcome '{choice.Outcome}'";
                }
            }
        }
        Check("every_event_choice_outcome_is_registered", !anyUnregistered, detail);
    }

    private void TestGainAndLoseGold()
    {
        RunState.Gold = 10;
        EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "gain_gold", Amount = 20, ResultText = "" });
        Check("gain_gold", RunState.Gold == 30, $"gold={RunState.Gold}");

        EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "lose_gold", Amount = 100, ResultText = "" });
        Check("lose_gold_floors_at_zero", RunState.Gold == 0, $"gold={RunState.Gold}");
    }

    private void TestHealAndLoseHp()
    {
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 30;
        EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "heal", Amount = 100, ResultText = "" });
        Check("heal_caps_at_max_hp", RunState.PlayerCurrentHp == 50, $"hp={RunState.PlayerCurrentHp}");

        EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "lose_hp", Amount = 100, ResultText = "" });
        Check("lose_hp_floors_at_one", RunState.PlayerCurrentHp == 1, $"hp={RunState.PlayerCurrentHp}");
    }

    private void TestGainRandomCard()
    {
        RunState.Deck = new List<CardDefinition>();
        EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "gain_random_card", Amount = 0, ResultText = "" });
        Check("gain_random_card_adds_exactly_one", RunState.Deck.Count == 1, $"count={RunState.Deck.Count}");
    }

    private void TestGainAndLoseRelic()
    {
        RunState.Relics = new List<RelicInstance>();
        var message = EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "gain_relic", Amount = 0, ResultText = "default" });
        Check("gain_relic_adds_exactly_one", RunState.Relics.Count == 1, $"count={RunState.Relics.Count}");
        Check("gain_relic_uses_default_result_text_when_pool_nonempty", message == "default", $"message='{message}'");

        var loseMessage = EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "lose_relic", Amount = 0, ResultText = "default" });
        Check("lose_relic_removes_exactly_one", RunState.Relics.Count == 0, $"count={RunState.Relics.Count}");
        Check("lose_relic_uses_default_result_text_when_something_to_lose", loseMessage == "default", $"message='{loseMessage}'");

        var emptyPoolMessage = EventOutcomeRegistry.Resolve(new EventChoice { Outcome = "lose_relic", Amount = 0, ResultText = "default" });
        Check("lose_relic_overrides_message_when_nothing_owned", emptyPoolMessage == "You have nothing to lose.",
            $"message='{emptyPoolMessage}'");
    }

    private void TestMapGeneratorProducesEventNodes()
    {
        bool anyEventFound = false;
        for (int seed = 0; seed < 25 && !anyEventFound; seed++)
        {
            var nodes = MapGenerator.Generate(new Random(seed));
            if (nodes.Any(n => n.Type == MapNodeType.Event)) anyEventFound = true;
        }
        Check("map_generator_produces_event_nodes_across_seeds", anyEventFound, "no Event node found in 25 seeds");
    }

    private void TestEventScreenLoadsAndResolvesAChoice()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RunState.Deck = new List<CardDefinition>();

        var packed = GD.Load<PackedScene>("res://scenes/EventScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var choicesList = instance.GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        var resultLabel = instance.GetNode<Label>("CenterContainer/VBoxContainer/ResultLabel");
        var continueButton = instance.GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");

        Check("event_screen_populates_choices", choicesList.GetChildCount() > 0, $"count={choicesList.GetChildCount()}");
        Check("result_and_continue_hidden_before_choosing", !resultLabel.Visible && !continueButton.Visible, "expected both hidden");

        var firstChoiceButton = choicesList.GetChild<Button>(0);
        firstChoiceButton.EmitSignal(Button.SignalName.Pressed);

        Check("choosing_reveals_result_and_continue", resultLabel.Visible && continueButton.Visible, "expected both visible");
        Check("choosing_clears_the_choice_list", choicesList.GetChildCount() == 0, $"count={choicesList.GetChildCount()}");

        instance.QueueFree();
    }
}
