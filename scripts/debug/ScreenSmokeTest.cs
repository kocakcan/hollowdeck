using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check that the non-combat screens (Reward/Shop/Treasure) load
// their real .tscn files without throwing and actually populate their UI -
// this is exactly the class of bug a pure-logic test can't see (a GetNode
// path that doesn't match the scene's actual node nesting throws mid-_Ready
// and silently aborts everything after it, leaving default placeholder
// text on screen with no button wired up). Run via
// `godot --headless scenes/debug/ScreenSmokeTest.tscn`.
public partial class ScreenSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestRewardScreen();
        TestTreasureScreen();
        TestShopScreen();

        GD.Print($"ScreenSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private Node LoadScene(string path)
    {
        var packed = GD.Load<PackedScene>(path);
        var instance = packed.Instantiate();
        AddChild(instance);
        return instance;
    }

    private void TestRewardScreen()
    {
        RewardContext.GoldAwarded = 25;
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("strike"),
            CardDatabase.Get("defend"),
            CardDatabase.Get("bash"),
        };

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        var goldLabel = screen.GetNode<Label>("CenterContainer/VBoxContainer/GoldLabel");
        var choicesList = screen.GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
        var firstRow = choicesList.GetChild(0);
        var card0Button = firstRow.GetChild<Button>(0);
        var card0Description = firstRow.GetChild<Label>(1);
        var skip = screen.GetNode<Button>("CenterContainer/VBoxContainer/SkipButton");

        Check("reward_gold_label_shows_awarded_amount", goldLabel.Text.Contains("25"), $"text='{goldLabel.Text}'");
        Check("reward_has_a_row_per_choice", choicesList.GetChildCount() == 3, $"rows={choicesList.GetChildCount()}");
        Check("reward_card_button_shows_real_name", card0Button.Text.Contains("Strike"), $"text='{card0Button.Text}'");
        Check("reward_card_shows_mechanical_description", card0Description.Text.Contains("Deal 6 damage"),
            $"text='{card0Description.Text}'");
        Check("reward_skip_button_has_a_handler", skip.GetSignalConnectionList("pressed").Count > 0, "no pressed connections");
        screen.QueueFree();
    }

    private void TestTreasureScreen()
    {
        RunState.Relics = new List<RelicInstance>();
        int relicsBefore = RunState.Relics.Count;

        var screen = LoadScene("res://scenes/TreasureScreen.tscn");
        var label = screen.GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");
        var continueButton = screen.GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");

        Check("treasure_label_updated_from_default", label.Text != "Treasure!", $"text='{label.Text}'");
        Check("treasure_grants_a_relic", RunState.Relics.Count == relicsBefore + 1,
            $"relics={RunState.Relics.Count}");
        Check("treasure_continue_button_has_a_handler", continueButton.GetSignalConnectionList("pressed").Count > 0,
            "no pressed connections");
        screen.QueueFree();
    }

    private void TestShopScreen()
    {
        RunState.Gold = 200;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        var screen = LoadScene("res://scenes/ShopScreen.tscn");
        var goldLabel = screen.GetNode<Label>("GoldLabel");
        var offers = screen.GetNode<VBoxContainer>("OffersList");

        Check("shop_gold_label_shows_current_gold", goldLabel.Text.Contains("200"), $"text='{goldLabel.Text}'");
        Check("shop_has_offer_rows", offers.GetChildCount() == 8, $"rows={offers.GetChildCount()}");

        var firstRowDescription = offers.GetChild(0).GetChild<Label>(1);
        Check("shop_offer_shows_description", firstRowDescription.Text.Length > 0,
            $"text='{firstRowDescription.Text}'");
        screen.QueueFree();
    }
}
