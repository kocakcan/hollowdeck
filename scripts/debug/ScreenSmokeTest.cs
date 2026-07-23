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
        var card0 = screen.GetNode<Button>("CenterContainer/VBoxContainer/CardChoice0");
        var skip = screen.GetNode<Button>("CenterContainer/VBoxContainer/SkipButton");

        Check("reward_gold_label_shows_awarded_amount", goldLabel.Text.Contains("25"), $"text='{goldLabel.Text}'");
        Check("reward_card_button_shows_real_name", card0.Text.Contains("Strike"), $"text='{card0.Text}'");
        Check("reward_skip_button_has_a_handler", skip.GetSignalConnectionList("pressed").Count > 0, "no pressed connections");
        screen.QueueFree();
    }

    private void TestTreasureScreen()
    {
        RunState.Relics = new List<RelicInstance>();
        RunState.TreasureClaimed = false;

        var screen = LoadScene("res://scenes/TreasureScreen.tscn");
        var label = screen.GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");
        var continueButton = screen.GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");

        Check("treasure_label_updated_from_default", label.Text != "Treasure!", $"text='{label.Text}'");
        Check("treasure_marks_claimed", RunState.TreasureClaimed, "TreasureClaimed still false");
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
        screen.QueueFree();
    }
}
