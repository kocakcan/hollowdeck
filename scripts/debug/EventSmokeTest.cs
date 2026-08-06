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
// authored outcome key resolves, per-outcome behavior (gold/HP/relic/card/
// potion/max-HP), the two schema rules compound choices have to obey, map
// generation actually produces Event nodes, and EventScreen.tscn loads and
// resolves both a plain choice and a card-picker one. Run via
// `godot --headless scenes/debug/EventSmokeTest.tscn`.
public partial class EventSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        EventDatabase.LoadAll();

        TestEventDatabaseLoads();
        TestEveryOutcomeKeyIsRegistered();
        TestPickerOutcomesAreAlwaysLast();
        TestGamblesContainNoPickers();
        TestGainAndLoseGold();
        TestHealAndLoseHp();
        TestGainRandomCard();
        TestGainAndLoseRelic();
        TestMaxHpOutcomes();
        TestGainPotion();
        TestUpgradeRandomCard();
        TestGamble();
        TestCardPickerOutcomes();
        TestCompoundOutcomesResolveInOrder();
        TestAddCardOutcome();
        TestEveryAuthoredAddCardNamesARealCardAndACount();
        TestMapGeneratorProducesEventNodes();
        TestEventScreenLoadsAndResolvesAChoice();
        TestEventScreenOpensAndResolvesAPicker();

        GD.Print($"EventSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    // Deliberately an exact count rather than a floor. It is the one check
    // that notices an event silently failing to deserialize - a malformed row
    // drops out of the list without throwing.
    private void TestEventDatabaseLoads()
    {
        Check("event_database_loads_every_authored_event", EventDatabase.All.Count == 15,
            $"count={EventDatabase.All.Count}");
    }

    private void TestEveryOutcomeKeyIsRegistered()
    {
        var unregistered = new List<string>();
        foreach (var def in EventDatabase.All)
        {
            foreach (var spec in def.Choices.SelectMany(AllSpecs))
            {
                if (!EventOutcomeRegistry.IsRegistered(spec.Outcome))
                {
                    unregistered.Add($"{def.Id}: '{spec.Outcome}'");
                }
            }
        }
        Check("every_event_choice_outcome_is_registered", unregistered.Count == 0,
            string.Join(", ", unregistered));
    }

    // A picker opens a modal grid, and Begin() returns the moment it hits one.
    // Anything authored after it would never resolve, so this is the check
    // that stops a silently-dead outcome shipping.
    private void TestPickerOutcomesAreAlwaysLast()
    {
        var offenders = new List<string>();
        foreach (var def in EventDatabase.All)
        {
            foreach (var choice in def.Choices)
            {
                var specs = choice.Specs;
                for (int i = 0; i < specs.Count - 1; i++)
                {
                    if (EventOutcomeRegistry.PickerFor(specs[i]) is not null)
                    {
                        offenders.Add($"{def.Id}: '{specs[i].Outcome}' at {i} of {specs.Count}");
                    }
                }
            }
        }
        Check("picker_outcomes_are_always_the_last_spec", offenders.Count == 0,
            string.Join(", ", offenders));
    }

    // A gamble that stops to ask a question is not a gamble - and mechanically
    // it could not work anyway, since GambleOutcome runs inside Resolve, well
    // past the point where Begin could hand a picker back to the screen.
    private void TestGamblesContainNoPickers()
    {
        var offenders = new List<string>();
        foreach (var def in EventDatabase.All)
        {
            foreach (var spec in def.Choices.SelectMany(c => c.Specs))
            {
                foreach (var alt in spec.Alternatives)
                {
                    if (EventOutcomeRegistry.PickerFor(alt) is not null)
                    {
                        offenders.Add($"{def.Id}: gamble -> '{alt.Outcome}'");
                    }
                }
            }
        }
        Check("gamble_alternatives_contain_no_pickers", offenders.Count == 0,
            string.Join(", ", offenders));

        // And a gamble with nothing to roll only shows up as a PushError at
        // the moment a player picks it - which is to say, never, in testing.
        // This is also what catches `alternatives` failing to deserialize.
        var empty = EventDatabase.All
            .SelectMany(d => d.Choices.SelectMany(c => c.Specs).Select(s => (d.Id, s)))
            .Where(pair => pair.s.Outcome == "gamble" && pair.s.Alternatives.Count == 0)
            .Select(pair => pair.Id)
            .ToList();
        Check("every_authored_gamble_has_alternatives", empty.Count == 0, string.Join(", ", empty));
    }

    private static IEnumerable<EventOutcomeSpec> AllSpecs(EventChoice choice) =>
        choice.Specs.SelectMany(s => new[] { s }.Concat(s.Alternatives));

    private static string Resolve(string outcome, int amount, string resultText = "") =>
        EventOutcomeRegistry.Begin(new EventChoice
        {
            Outcome = outcome,
            Amount = amount,
            ResultText = resultText,
        }).Text;

    // The outcome that turns an event from a trade into a risk. Before it,
    // every downside an event could author was HP, gold, max HP or a relic -
    // all of which are resources the player already spends - so an event was
    // never able to cost something that follows the run.
    private void TestAddCardOutcome()
    {
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike") };

        EventOutcomeRegistry.Begin(new EventChoice
        {
            Outcomes = new List<EventOutcomeSpec>
            {
                new() { Outcome = "add_card", CardId = "wound", Amount = 2 },
            },
        });

        Check("add_card_outcome_adds_the_named_card_twice",
            RunState.Deck.Count(c => c.Id == "wound") == 2,
            $"deck={string.Join(",", RunState.Deck.Select(c => c.Id))}");

        // Deliberately not routed through CardPool: this outcome names its
        // card, and CardPool would refuse an unplayable one.
        Check("add_card_outcome_can_deliver_an_unplayable_card",
            RunState.Deck.Any(c => !c.IsPlayable), "no unplayable card reached the deck");

        // An unknown id pushes an error and adds nothing, rather than throwing
        // out of an event screen. The error is expected output for this suite.
        int before = RunState.Deck.Count;
        EventOutcomeRegistry.Begin(new EventChoice
        {
            Outcomes = new List<EventOutcomeSpec>
            {
                new() { Outcome = "add_card", CardId = "no_such_card", Amount = 1 },
            },
        });
        Check("add_card_outcome_with_an_unknown_id_adds_nothing",
            RunState.Deck.Count == before, $"deck grew to {RunState.Deck.Count}");
    }

    // The authoring audit. AddCardOutcome does not clamp a missing count to 1,
    // so an amount of 0 is a choice whose label promises a cost it never pays -
    // and the label is the only place the player would ever see the difference.
    private void TestEveryAuthoredAddCardNamesARealCardAndACount()
    {
        var bad = new List<string>();
        foreach (var def in EventDatabase.All)
        {
            foreach (var spec in def.Choices.SelectMany(AllSpecs).Where(s => s.Outcome == "add_card"))
            {
                if (CardDatabase.Find(spec.CardId) is null) bad.Add($"{def.Id}: id '{spec.CardId}'");
                else if (spec.Amount < 1) bad.Add($"{def.Id}: amount {spec.Amount}");
            }
        }

        Check("every_authored_event_add_card_names_a_real_card_and_a_count",
            bad.Count == 0, string.Join(", ", bad));
    }

    private void TestGainAndLoseGold()
    {
        RunState.Gold = 10;
        Resolve("gain_gold", 20);
        Check("gain_gold", RunState.Gold == 30, $"gold={RunState.Gold}");

        Resolve("lose_gold", 100);
        Check("lose_gold_floors_at_zero", RunState.Gold == 0, $"gold={RunState.Gold}");
    }

    private void TestHealAndLoseHp()
    {
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 30;
        Resolve("heal", 100);
        Check("heal_caps_at_max_hp", RunState.PlayerCurrentHp == 50, $"hp={RunState.PlayerCurrentHp}");

        Resolve("lose_hp", 100);
        Check("lose_hp_floors_at_one", RunState.PlayerCurrentHp == 1, $"hp={RunState.PlayerCurrentHp}");
    }

    private void TestGainRandomCard()
    {
        RunState.Deck = new List<CardDefinition>();
        Resolve("gain_random_card", 0);
        Check("gain_random_card_adds_exactly_one", RunState.Deck.Count == 1, $"count={RunState.Deck.Count}");
    }

    private void TestGainAndLoseRelic()
    {
        RunState.Relics = new List<RelicInstance>();
        var message = Resolve("gain_relic", 0, "default");
        Check("gain_relic_adds_exactly_one", RunState.Relics.Count == 1, $"count={RunState.Relics.Count}");
        Check("gain_relic_uses_default_result_text_when_pool_nonempty", message == "default", $"message='{message}'");

        var loseMessage = Resolve("lose_relic", 0, "default");
        Check("lose_relic_removes_exactly_one", RunState.Relics.Count == 0, $"count={RunState.Relics.Count}");
        Check("lose_relic_uses_default_result_text_when_something_to_lose", loseMessage == "default", $"message='{loseMessage}'");

        var emptyPoolMessage = Resolve("lose_relic", 0, "default");
        Check("lose_relic_overrides_message_when_nothing_owned", emptyPoolMessage == "You have nothing to lose.",
            $"message='{emptyPoolMessage}'");
    }

    private void TestMaxHpOutcomes()
    {
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 40;
        Resolve("gain_max_hp", 6);
        Check("gain_max_hp_raises_both_max_and_current",
            RunState.PlayerMaxHp == 56 && RunState.PlayerCurrentHp == 46,
            $"max={RunState.PlayerMaxHp} cur={RunState.PlayerCurrentHp}");

        // Current sits above the new max, so it must be pulled down with it -
        // every HP bar renders current/max and would otherwise show 46/40.
        Resolve("lose_max_hp", 16);
        Check("lose_max_hp_clamps_current_to_the_new_max",
            RunState.PlayerMaxHp == 40 && RunState.PlayerCurrentHp == 40,
            $"max={RunState.PlayerMaxHp} cur={RunState.PlayerCurrentHp}");

        // An event must never end the run from a menu.
        Resolve("lose_max_hp", 9999);
        Check("lose_max_hp_can_never_kill",
            RunState.PlayerMaxHp >= 1 && RunState.PlayerCurrentHp >= 1,
            $"max={RunState.PlayerMaxHp} cur={RunState.PlayerCurrentHp}");
    }

    private void TestGainPotion()
    {
        RunState.Potions = new List<PotionInstance>();
        Resolve("gain_potion", 0, "default");
        Check("gain_potion_adds_exactly_one", RunState.Potions.Count == 1, $"count={RunState.Potions.Count}");

        while (RunState.Potions.Count < RunState.MaxPotionSlots)
        {
            Resolve("gain_potion", 0, "default");
        }
        var fullMessage = Resolve("gain_potion", 0, "default");
        Check("gain_potion_respects_the_belt_cap",
            RunState.Potions.Count == RunState.MaxPotionSlots && fullMessage != "default",
            $"count={RunState.Potions.Count} message='{fullMessage}'");
    }

    private void TestUpgradeRandomCard()
    {
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("defend") };
        Resolve("upgrade_random_card", 0);
        Check("upgrade_random_card_upgrades_exactly_one",
            RunState.Deck.Count(CardUpgrade.IsUpgraded) == 1,
            string.Join(", ", RunState.Deck.Select(c => c.Id)));

        RunState.Deck = new List<CardDefinition> { CardUpgrade.Apply(CardDatabase.Get("strike")) };
        var message = Resolve("upgrade_random_card", 0, "default");
        Check("upgrade_random_card_overrides_message_when_nothing_upgradable",
            message != "default", $"message='{message}'");
    }

    private void TestGamble()
    {
        RunState.Gold = 100;
        var choice = new EventChoice
        {
            Outcome = "gamble",
            ResultText = "default",
            // One alternative, so the roll is deterministic and the check is
            // about plumbing rather than about which face came up.
            Alternatives = new List<EventOutcomeSpec> { new() { Outcome = "gain_gold", Amount = 25 } },
        };
        var text = EventOutcomeRegistry.Begin(choice).Text;
        Check("gamble_resolves_its_rolled_alternative", RunState.Gold == 125, $"gold={RunState.Gold}");
        // A gamble always reports what actually landed - the authored
        // ResultText was written for the act of rolling, not for the result.
        Check("gamble_reports_the_branch_that_landed", text != "default", $"text='{text}'");
    }

    private void TestCardPickerOutcomes()
    {
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("defend") };

        var remove = new RemoveChosenCardOutcome();
        Check("remove_chosen_card_offers_every_card", remove.Selectable().Count() == 2,
            $"count={remove.Selectable().Count()}");
        remove.Apply(0);
        Check("remove_chosen_card_removes_the_chosen_slot",
            RunState.Deck.Count == 1 && RunState.Deck[0].Id == "defend",
            string.Join(", ", RunState.Deck.Select(c => c.Id)));

        // The deck must never empty out: a 0-card deck draws nothing and the
        // next fight is unwinnable and unquittable.
        Check("remove_chosen_card_offers_nothing_at_one_card", !remove.Selectable().Any(),
            $"count={remove.Selectable().Count()}");

        // ...and with nothing selectable it is not pending, it resolves to its
        // own message like any other outcome.
        var resolution = EventOutcomeRegistry.Begin(new EventChoice
        {
            Outcome = "remove_chosen_card",
            ResultText = "default",
        });
        Check("empty_picker_resolves_instead_of_going_pending",
            resolution.Pending is null && resolution.Text != "default",
            $"pending={resolution.Pending is not null} text='{resolution.Text}'");

        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardUpgrade.Apply(CardDatabase.Get("defend")) };
        var upgrade = new UpgradeChosenCardOutcome();
        Check("upgrade_chosen_card_skips_already_upgraded", upgrade.Selectable().SequenceEqual(new[] { 0 }),
            string.Join(", ", upgrade.Selectable()));
        upgrade.Apply(0);
        Check("upgrade_chosen_card_upgrades_the_chosen_slot", CardUpgrade.IsUpgraded(RunState.Deck[0]),
            RunState.Deck[0].Id);

        // A picker with something to offer goes pending rather than resolving,
        // which is what makes EventScreen open the grid.
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("defend") };
        var pending = EventOutcomeRegistry.Begin(new EventChoice { Outcome = "upgrade_chosen_card" });
        Check("non_empty_picker_goes_pending", pending.Pending is not null, "expected a pending picker");
    }

    private void TestCompoundOutcomesResolveInOrder()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;

        var text = EventOutcomeRegistry.Begin(new EventChoice
        {
            ResultText = "authored",
            Outcomes = new List<EventOutcomeSpec>
            {
                new() { Outcome = "gain_gold", Amount = 40 },
                new() { Outcome = "lose_max_hp", Amount = 10 },
            },
        }).Text;

        Check("compound_choice_resolves_every_spec",
            RunState.Gold == 40 && RunState.PlayerMaxHp == 40,
            $"gold={RunState.Gold} max={RunState.PlayerMaxHp}");
        Check("compound_choice_keeps_authored_text_when_nothing_overrode",
            text == "authored", $"text='{text}'");
    }

    private void TestMapGeneratorProducesEventNodes()
    {
        bool anyEventFound = false;
        for (int seed = 0; seed < 25 && !anyEventFound; seed++)
        {
            var nodes = MapGenerator.Generate(new Random(seed), ActDatabase.At(0));
            if (nodes.Any(n => n.Type == MapNodeType.Event)) anyEventFound = true;
        }
        Check("map_generator_produces_event_nodes_across_seeds", anyEventFound, "no Event node found in 25 seeds");
    }

    private void TestEventScreenLoadsAndResolvesAChoice()
    {
        ResetRunState();

        var packed = GD.Load<PackedScene>("res://scenes/EventScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var choicesList = instance.GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        var resultLabel = instance.GetNode<Label>("CenterContainer/VBoxContainer/ResultLabel");
        var continueButton = instance.GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");
        var pickerView = instance.GetNode<Control>("PickerCenterContainer");

        Check("event_screen_populates_choices", choicesList.GetChildCount() > 0, $"count={choicesList.GetChildCount()}");
        Check("result_and_continue_hidden_before_choosing", !resultLabel.Visible && !continueButton.Visible, "expected both hidden");
        Check("picker_hidden_before_choosing", !pickerView.Visible, "expected picker hidden");

        var firstChoiceButton = choicesList.GetChild<Button>(0);
        firstChoiceButton.EmitSignal(Button.SignalName.Pressed);

        Check("choosing_clears_the_choice_list", choicesList.GetChildCount() == 0, $"count={choicesList.GetChildCount()}");
        // Which event got rolled is up to RngStreams.Shop, so this asserts the
        // invariant that holds either way: the screen is never left showing
        // nothing at all.
        Check("choosing_leaves_the_screen_showing_something",
            (resultLabel.Visible && continueButton.Visible) || pickerView.Visible,
            "expected either a result or an open picker");

        instance.QueueFree();
    }

    // Drives the picker path end to end through the real screen.
    //
    // EventScreen rolls its own event in _Ready and there is no seam to inject
    // one, so this instantiates until a picker event comes up rather than
    // reaching into the screen. RngStreams.Shop advances on every roll, so it
    // terminates - two of the fifteen events carry a picker, and the cap is
    // there to fail loudly rather than hang if that ever stops being true.
    private void TestEventScreenOpensAndResolvesAPicker()
    {
        var packed = GD.Load<PackedScene>("res://scenes/EventScreen.tscn");

        for (int attempt = 0; attempt < 80; attempt++)
        {
            ResetRunState();
            RunState.Deck = new List<CardDefinition>
            {
                CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
            };

            var instance = packed.Instantiate();
            AddChild(instance);

            var title = instance.GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel").Text;
            var definition = EventDatabase.All.FirstOrDefault(e => e.Title == title);
            int pickerChoice = definition?.Choices.FindIndex(
                c => c.Specs.Any(s => EventOutcomeRegistry.PickerFor(s) is not null)) ?? -1;

            if (pickerChoice < 0)
            {
                instance.QueueFree();
                continue;
            }

            var choicesList = instance.GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
            var pickerView = instance.GetNode<Control>("PickerCenterContainer");
            var pickerList = instance.GetNode<GridContainer>("PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");
            var pickerTitle = instance.GetNode<Label>("PickerCenterContainer/PickerVBox/PickerTitleLabel");
            var resultLabel = instance.GetNode<Label>("CenterContainer/VBoxContainer/ResultLabel");
            var continueButton = instance.GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");

            choicesList.GetChild<Button>(pickerChoice).EmitSignal(Button.SignalName.Pressed);

            var mainView = instance.GetNode<Control>("CenterContainer");

            Check("picker_choice_opens_the_card_grid", pickerView.Visible, "expected the picker visible");
            // Both views are full-rect and transparent, so leaving the event's
            // own column visible interleaves its description and buttons
            // through the gaps in the card grid rather than sitting behind it.
            // That is how this shipped in its first screenshot.
            Check("picker_hides_the_event_column", !mainView.Visible,
                "the event's own text shows through the card grid");
            Check("picker_grid_has_one_column_per_selectable_card",
                pickerList.GetChildCount() == 3, $"count={pickerList.GetChildCount()}");
            Check("picker_shows_its_own_prompt",
                pickerTitle.Text.Length > 0 && pickerTitle.Text != "Choose a card.", $"prompt='{pickerTitle.Text}'");
            Check("result_still_hidden_while_the_picker_is_open", !resultLabel.Visible, "expected result hidden");

            // Each column is a CardView over its button, so the button is a
            // grandchild of the grid - the same shape RestScreen's own
            // PreferredFocus walks.
            var firstCardButton = pickerList.GetChildren()
                .SelectMany(c => c.GetChildren()).OfType<Button>().First();
            firstCardButton.EmitSignal(Button.SignalName.Pressed);

            Check("choosing_a_card_closes_the_picker", !pickerView.Visible, "expected the picker hidden");
            // ...and brings the event's column back, since the result label
            // and Continue button both live inside it.
            Check("choosing_a_card_restores_the_event_column", mainView.Visible,
                "the result and Continue button live in the column the picker hid");
            Check("choosing_a_card_reveals_result_and_continue",
                resultLabel.Visible && continueButton.Visible, "expected both visible");
            Check("choosing_a_card_reports_what_happened",
                resultLabel.Text.Length > 0, $"text='{resultLabel.Text}'");

            instance.QueueFree();
            return;
        }

        Check("event_screen_rolls_a_picker_event_within_80_attempts", false,
            "no picker event rolled - are any still authored?");
    }

    private static void ResetRunState()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RunState.Deck = new List<CardDefinition>();
    }
}
