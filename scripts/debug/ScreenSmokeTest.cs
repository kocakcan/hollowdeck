using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Headless check that the non-combat screens (Reward/Shop/Treasure/Rest)
// load their real .tscn files without throwing and actually populate their
// UI - this is exactly the class of bug a pure-logic test can't see (a GetNode
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
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestRewardScreen();
        TestTreasureScreen();
        TestShopScreen();
        TestRestScreen();

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
        var goldLabel = screen.GetNode<Label>("TitleBlock/GoldLabel");
        var choicesArea = screen.GetNode<Control>("CardChoicesArea");
        var cardViews = choicesArea.GetChildren().OfType<CardView>().ToList();
        var skip = screen.GetNode<Button>("SkipButton");

        Check("reward_gold_label_shows_awarded_amount", goldLabel.Text.Contains("25"), $"text='{goldLabel.Text}'");
        Check("reward_has_a_card_view_per_choice", cardViews.Count == 3, $"cards={cardViews.Count}");
        Check("reward_card_views_are_non_interactive", cardViews.All(c => !c.Interactive),
            "a reward CardView still has Interactive=true (would try to drag-to-play)");
        Check("reward_first_card_is_strike", cardViews.Count > 0 && cardViews[0].CardInstance?.Definition.Id == "strike",
            $"id='{cardViews.ElementAtOrDefault(0)?.CardInstance?.Definition.Id}'");
        Check("reward_skip_button_has_a_handler", skip.GetSignalConnectionList("pressed").Count > 0, "no pressed connections");
        screen.QueueFree();
    }

    private void TestTreasureScreen()
    {
        RunState.Relics = new List<RelicInstance>();
        int relicsBefore = RunState.Relics.Count;

        var screen = LoadScene("res://scenes/TreasureScreen.tscn");
        // The relic's name moved to its own display-face label above the
        // description when the screen gained an art plinth; OutcomeLabel is
        // now the description alone.
        var nameLabel = screen.GetNode<Label>("CenterContainer/VBoxContainer/NameLabel");
        var label = screen.GetNode<Label>("CenterContainer/VBoxContainer/OutcomeLabel");
        var artSlot = screen.GetNode<CenterContainer>("CenterContainer/VBoxContainer/ArtSlot");
        var continueButton = screen.GetNode<Button>("CenterContainer/VBoxContainer/ContinueButton");

        Check("treasure_names_the_relic_it_granted",
            nameLabel.Text != "Treasure" && RunState.Relics.Any(r => r.Definition.Name == nameLabel.Text),
            $"name='{nameLabel.Text}'");
        Check("treasure_label_updated_from_default", label.Text != "Treasure!", $"text='{label.Text}'");
        // The whole point of the screen's rework: the relic is shown, not just
        // described. A missing icon file would silently drop back to text.
        Check("treasure_shows_the_relic_art", artSlot.GetChildCount() == 1,
            $"art children={artSlot.GetChildCount()}");
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
        // Gold moved into the shared run-status block, and the relic/potion
        // stock out of a scrolling list into a row of framed tiles.
        var goldLabel = screen.GetNode<Label>(ScreenChrome.GoldLabelPath);
        var cardRow = screen.GetNode<HBoxContainer>("CardOffersRow");
        var offers = screen.GetNode<HBoxContainer>("OffersRow");

        Check("shop_gold_label_shows_current_gold", goldLabel.Text.Contains("200"), $"text='{goldLabel.Text}'");
        var cardViews = cardRow.GetChildren().SelectMany(c => c.GetChildren()).OfType<CardView>().ToList();
        Check("shop_has_a_card_view_per_card_offer", cardViews.Count == 4, $"cards={cardViews.Count}");
        Check("shop_card_offers_are_non_interactive", cardViews.All(c => !c.Interactive),
            "a shop CardView still has Interactive=true (would try to drag-to-play)");
        Check("shop_has_relic_and_potion_offer_rows", offers.GetChildCount() == 4, $"rows={offers.GetChildCount()}");

        // Every tile is Frame(VBox) - the description is the label with the
        // longest text, since name/kind are one word each.
        var firstTileLabels = offers.GetChild(0).GetChild(0).GetChildren().OfType<Label>().ToList();
        Check("shop_offer_shows_description", firstTileLabels.Any(l => l.Text.Length > 12),
            $"labels=[{string.Join(" | ", firstTileLabels.Select(l => l.Text))}]");
        // The tiles are the reason the stock left the ItemList: each shows the
        // thing it sells, not just its name.
        Check("shop_offer_shows_its_icon",
            offers.GetChild(0).GetChild(0).GetChildren().OfType<CenterContainer>().Count() == 1,
            "offer tile has no icon slot");
        screen.QueueFree();
    }

    private void TestRestScreen()
    {
        // Picking a card to upgrade leaves the screen (ChangeScreen(Map)), and
        // Map is an auto-save screen - so without this, running this test writes
        // this test's 2-card fixture over the developer's real in-progress run
        // save. That is exactly what used to happen on every suite run.
        using var saveGuard = RunSaveGuard.Protect();
        // And pin the screen change to a hard cut, so this test does not
        // depend on whether the machine running it has Reduce Motion set.
        using var cutGuard = HardCutGuard.Protect();

        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 20;
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("defend") };

        var screen = LoadScene("res://scenes/RestScreen.tscn");
        // HP moved out of a bare top-left Label into the shared run-status
        // block ScreenChrome builds for every non-combat screen.
        var hpLabel = screen.GetNode<Label>(ScreenChrome.HpLabelPath);
        Check("rest_shows_current_hp", hpLabel.Text.Contains("20") && hpLabel.Text.Contains("50"),
            $"text='{hpLabel.Text}'");

        var choicesView = screen.GetNode<Control>("CenterContainer");
        var upgradeView = screen.GetNode<Control>("UpgradeCenterContainer");
        Check("rest_starts_on_main_choices", choicesView.Visible && !upgradeView.Visible,
            $"choices visible={choicesView.Visible}, upgrade visible={upgradeView.Visible}");
        Check("rest_shows_the_campfire",
            screen.GetNode<CenterContainer>("CenterContainer/VBoxContainer/ArtSlot").GetChildCount() == 1,
            "the rest site has no campfire art");

        var smithButton = screen.GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/SmithButton");
        Check("rest_smith_button_enabled_with_unupgraded_cards", !smithButton.Disabled, "SmithButton was disabled");
        smithButton.EmitSignal(Button.SignalName.Pressed);
        Check("rest_smith_switches_to_upgrade_view", !choicesView.Visible && upgradeView.Visible,
            $"choices visible={choicesView.Visible}, upgrade visible={upgradeView.Visible}");

        // A GridContainer of CardView columns now, not a VBox of text rows.
        var upgradeList = screen.GetNode<GridContainer>("UpgradeCenterContainer/UpgradeVBox/ScrollContainer/UpgradeList");
        Check("rest_upgrade_list_has_a_row_per_card", upgradeList.GetChildCount() == 2,
            $"rows={upgradeList.GetChildCount()}");

        var strikeRow = upgradeList.GetChild(0);
        // Each column renders the *upgraded* card, which is what the player is
        // choosing to end up with.
        var strikeView = strikeRow.GetChildren().OfType<CardView>().FirstOrDefault();
        Check("rest_upgrade_choice_previews_the_upgraded_card",
            strikeView?.CardInstance?.Definition.Id == "strike+",
            $"id='{strikeView?.CardInstance?.Definition.Id}'");
        var strikeButton = strikeRow.GetChildren().OfType<Button>().First();
        int deckCountBefore = RunState.Deck.Count;
        // Picking a card calls OnLeavePressed -> ChangeSceneToFile on the
        // scene currently on the call stack, which logs one harmless
        // "parent busy" engine error - same accepted quirk documented on
        // Phase4ContentSmokeTest's elite-reward Continue-click test. Doesn't
        // affect RunState.Deck, which is what's actually being checked here.
        strikeButton.EmitSignal(Button.SignalName.Pressed);

        Check("rest_upgrading_keeps_deck_size", RunState.Deck.Count == deckCountBefore,
            $"count={RunState.Deck.Count}");
        Check("rest_upgrading_marks_exactly_one_card_upgraded",
            RunState.Deck.Count(CardUpgrade.IsUpgraded) == 1,
            $"upgraded count={RunState.Deck.Count(CardUpgrade.IsUpgraded)}");
        Check("rest_upgrading_leaves_the_other_card_alone",
            RunState.Deck.Count(c => !CardUpgrade.IsUpgraded(c)) == 1,
            $"un-upgraded count={RunState.Deck.Count(c => !CardUpgrade.IsUpgraded(c))}");

        screen.QueueFree();
    }
}
