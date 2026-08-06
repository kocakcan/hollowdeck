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

    public override async void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        // Captured before the screen tests run, and used for Quit below - the
        // same trap ActSmokeTest and KeyboardSmokeTest document. TestRestScreen
        // drives a button into RunManager.ChangeScreen, which replaces the
        // tree's current scene (this test), so GetTree() on the now-detached
        // node comes back null and the run hangs with no summary. Harmless
        // while _Ready was synchronous; the moment the first await went in, the
        // continuation started running after that detachment.
        var tree = GetTree();

        await TestKeywordTooltipOnANonInteractiveCard();
        await TestRewardScreenOpensQuietly();
        TestRewardScreenActClearedBanner();
        TestRewardScreen();
        TestTreasureScreen();
        TestShopScreen();
        TestRestScreen();

        GD.Print($"ScreenSmokeTest: {_pass} passed, {_fail} failed");
        tree.Quit(_fail == 0 ? 0 : 1);
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

    // Every non-combat screen shows its cards with Interactive = false, and for
    // a long time that single flag was also what suppressed the keyword hover
    // panel: the !Interactive guard sat above ShowKeywordTooltip rather than
    // beside it, so Reward, Shop, the upgrade picker and the deck popup all
    // received the mouse_entered signal and threw the explanation away.
    //
    // Driven through focus rather than a synthesised mouse event because that
    // is the path with no OS input behind it - if the keyboard can raise the
    // panel, the mouse (which shares the code below the guard) can too.
    private async System.Threading.Tasks.Task TestKeywordTooltipOnANonInteractiveCard()
    {
        var tree = GetTree();
        var view = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(view);
        view.Interactive = false;
        // Bash applies Vulnerable, so its generated text carries a keyword.
        view.SetCardInstance(new CardInstance(CardDatabase.Get("bash")));
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        int before = CountTooltips();
        view.GrabFocus();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        int focused = CountTooltips();

        view.ReleaseFocus();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame); // QueueFree lands next frame
        int released = CountTooltips();

        Check("non_interactive_card_shows_a_keyword_tooltip_on_focus",
            before == 0 && focused > before,
            $"tooltips before={before}, focused={focused} - the Interactive guard is swallowing it again");
        Check("keyword_tooltip_is_freed_when_focus_leaves", released == 0,
            $"{released} tooltip(s) left on screen after the card lost focus");

        // The panel outranks PileViewPopup, which sets itself to 2000. At the
        // old ZIndex of 500 a deck-view tooltip rendered behind the very popup
        // that spawned it.
        Check("keyword_tooltip_outranks_the_deck_popup",
            HoverTooltipZIndexBeatsPopup(),
            "a tooltip raised from the deck view would paint underneath it");

        view.QueueFree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private int CountTooltips() =>
        GetTree().CurrentScene.GetChildren().OfType<HoverTooltip>().Count();

    private bool HoverTooltipZIndexBeatsPopup()
    {
        var probe = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(probe);
        probe.Interactive = false;
        probe.SetCardInstance(new CardInstance(CardDatabase.Get("bash")));
        var tooltip = HoverTooltip.Show(probe, Keywords.Find("Apply 2 Vulnerable."));
        bool beats = tooltip is not null && tooltip.ZIndex > 2000;
        tooltip?.Dismiss();
        probe.QueueFree();
        return beats;
    }

    // A screen taking focus for the player is not the player pointing at
    // anything. Every choice screen grabs focus for its first card on load, and
    // because CardView raised the keyword panel from OnFocusEntered
    // unconditionally, Reward/Shop/the pickers all came up with a panel already
    // floating over the layout before a key had been pressed - the leftmost
    // card explaining Vulnerable to nobody. The halo has to stay (it is how the
    // player finds the keyboard); only the panel waits.
    private async System.Threading.Tasks.Task TestRewardScreenOpensQuietly()
    {
        var tree = GetTree();
        RewardContext.ActCleared = null;
        RewardContext.GoldAwarded = 25;
        // Bash first, deliberately: it applies Vulnerable, so the card that
        // gets the automatic focus is one that *has* something to say. A
        // keywordless first card would pass this test no matter what.
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("bash"),
            CardDatabase.Get("defend"),
            CardDatabase.Get("strike"),
        };

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        // Two frames: ScreenKeyboardNav defers its grab, and HoverTooltip would
        // place itself on the frame after that.
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var cards = screen.GetNode<Control>("CardChoicesArea").GetChildren().OfType<CardView>().ToList();
        var focused = GetViewport().GuiGetFocusOwner();

        Check("reward_auto_focuses_its_first_card",
            cards.Count > 1 && ReferenceEquals(focused, cards[0]),
            $"focus landed on '{focused?.Name}' - the rest of this test needs it on a keyworded card");
        Check("reward_opens_without_a_keyword_tooltip", CountTooltips() == 0,
            $"{CountTooltips()} tooltip(s) on screen before the player touched anything");

        // ...and the suppression is scoped to that grab, not a general mute:
        // the player moving focus themselves still raises the panel.
        if (cards.Count > 1)
        {
            cards[1].GrabFocus();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Check("reward_raises_the_tooltip_when_the_player_moves_focus", CountTooltips() > 0,
                "focusing a second card by hand raised nothing - the suppression has become a mute");
        }

        screen.QueueFree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    // The reward after a boss is the only place the player is told an act
    // ended, which act starts next, and that clearing one just raised their max
    // HP and healed them. All of it was silent, and the screen kept its
    // "Victory Reward" title - so beating act 2's boss looked exactly like
    // beating the run, and the fresh map that followed looked like a restart.
    private void TestRewardScreenActClearedBanner()
    {
        RewardContext.GoldAwarded = 60;
        RewardContext.CardChoices = new List<CardDefinition> { CardDatabase.Get("strike") };
        RewardContext.ActCleared = new ActClear(
            ClearedNumber: 1, ClearedName: "The Sunken Ward",
            NextNumber: 2, NextName: "The Ember Reach",
            TotalActs: 3, MaxHpBonus: 8, Healed: 20);

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        var title = screen.GetNode<Label>("TitleBlock/TitleLabel");
        var act = screen.GetNode<Label>("TitleBlock/ActLabel");

        Check("act_cleared_retitles_the_reward_screen", title.Text == "Act 1 Cleared",
            $"title='{title.Text}' - a boss reward still reads like any other fight's");
        Check("act_cleared_names_the_next_act_and_the_bonus",
            act.Visible && act.Text.Contains("8") && act.Text.Contains("20")
            && act.Text.Contains("Act 2 of 3") && act.Text.Contains("The Ember Reach"),
            $"text='{act.Text}' (visible={act.Visible})");
        screen.QueueFree();

        // And an ordinary fight's reward is untouched by it.
        RewardContext.ActCleared = null;
        var plain = LoadScene("res://scenes/RewardScreen.tscn");
        Check("ordinary_reward_has_no_act_banner",
            !plain.GetNode<Label>("TitleBlock/ActLabel").Visible
            && plain.GetNode<Label>("TitleBlock/TitleLabel").Text == "Victory Reward",
            "the act-cleared banner leaked onto a non-boss reward");
        plain.QueueFree();
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

        // Card removal. It ships in the same phase as Curses on purpose -
        // adding a way to put dead cards in a deck without a way to take them
        // out is punishment rather than design - so this asserts the pairing
        // rather than just the button.
        //
        // A button beside Leave, not a fifth tile: five 260px tiles plus
        // separations come to 1364 against OffersRow's 1112 and the outer two
        // would clip. That is why the count above stays 4.
        var removeButton = screen.GetNode<Button>("RemoveCardButton");
        var picker = screen.GetNode<Control>("PickerCenterContainer");
        Check("shop_sells_card_removal", removeButton.Text.Contains("75"), $"text='{removeButton.Text}'");
        Check("shop_removal_picker_starts_hidden", !picker.Visible, "picker visible on load");

        // Two cards, so Selectable()'s one-card floor does not refuse.
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("defend"),
        };
        removeButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check("shop_removal_opens_a_card_picker", picker.Visible, "picker still hidden");
        // The shop underneath is hidden rather than merely covered: Godot's
        // focus navigation reaches controls behind an overlay perfectly
        // happily, so a visible-but-covered Buy button would still be tabbable.
        Check("shop_removal_picker_hides_the_shop_beneath_it",
            !cardRow.Visible && !offers.Visible, "offers still visible behind the picker");

        var pickerList = screen.GetNode<GridContainer>(
            "PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");
        Check("shop_removal_picker_shows_one_column_per_card",
            pickerList.GetChildCount() == 2, $"columns={pickerList.GetChildCount()}");

        // Cancel must not charge - gold is spent in the picker's callback, not
        // at the button press.
        int goldBefore = RunState.Gold;
        screen.GetNode<Button>("PickerCenterContainer/PickerVBox/PickerCancelButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        Check("shop_removal_cancel_is_free",
            !picker.Visible && RunState.Gold == goldBefore && RunState.Deck.Count == 2,
            $"gold {goldBefore}->{RunState.Gold}, deck={RunState.Deck.Count}");

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
