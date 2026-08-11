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
        TipDatabase.LoadAll();

        // Captured before the screen tests run, and used for Quit below - the
        // same trap ActSmokeTest and KeyboardSmokeTest document. TestRestScreen
        // drives a button into RunManager.ChangeScreen, which replaces the
        // tree's current scene (this test), so GetTree() on the now-detached
        // node comes back null and the run hangs with no summary. Harmless
        // while _Ready was synchronous; the moment the first await went in, the
        // continuation started running after that detachment.
        var tree = GetTree();

        // One guard over the whole sequence rather than one per test that
        // happens to save. Claiming any reward row calls MarkClaimed, which
        // re-saves to user://run_save.json by design - so three of the tests
        // below overwrite the developer's real in-progress run and every one of
        // them passes while doing it. TestRestScreen has carried its own guard
        // for a while and could not fix this: it runs last, so it snapshots the
        // already-clobbered file and faithfully restores the damage. Hoisted
        // here so a test added later inherits the protection instead of having
        // to remember it.
        using var saveGuard = RunSaveGuard.Protect();

        await TestKeywordTooltipOnANonInteractiveCard();
        await TestKeywordTooltipFollowsHoverAndFocusIndependently();
        await TestRewardScreenOpensQuietly();
        TestRewardScreenActClearedBanner();
        TestRewardScreen();
        TestRewardBossRelicChoice();
        await TestRewardBossRelicTilesStayInTheirBand();
        TestRewardPotionDrop();
        await TestRewardScreenTip();
        TestTreasureScreen();
        TestShopScreen();
        await TestShopOffersClearTheRunStatusBlock();
        TestShopUnaffordableOffersAreUnfocusable();
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

    // A reward row by the reward it is for. owned: false because the rows are
    // built in code and so have no scene owner - the default (true) finds
    // nothing, which reads as "the row is missing" rather than as a bad query.
    private static Button? Row(Node screen, RewardKind kind) =>
        screen.FindChild(RewardScreen.RowName(kind), recursive: true, owned: false) as Button;

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

    // The reported bug: on every choice screen, the leftmost card's keyword
    // panel came up on the first hover and then never went away, so hovering a
    // second card left two on screen. That card is the one ScreenKeyboardNav
    // focuses on load, and hiding on mouse-exit was suppressed for a card that
    // still had focus - a guard that is right for a panel *focus* raised and
    // wrong for one hover raised, which one flag could not tell apart.
    //
    // Hover is driven by emitting the signals CardView subscribes to rather
    // than by moving the cursor: a headless Godot pins the mouse at (0, 0) and
    // ignores WarpMouse (see CombatTargetingSmokeTest), so there is no real
    // hover to be had here.
    private async System.Threading.Tasks.Task TestKeywordTooltipFollowsHoverAndFocusIndependently()
    {
        var tree = GetTree();
        var view = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(view);
        view.Interactive = false;
        view.SetCardInstance(new CardInstance(CardDatabase.Get("bash")));
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        // The screen taking focus for the player, exactly as a choice screen
        // does on load: the panel stays down, and the card still holds focus.
        ScreenKeyboardNav.GrabFocusQuietly(view);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        view.EmitSignal(Control.SignalName.MouseEntered);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        int hovered = CountTooltips();

        view.EmitSignal(Control.SignalName.MouseExited);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame); // QueueFree lands next frame
        int afterHover = CountTooltips();

        Check("hover_raises_the_panel_on_a_quietly_focused_card", hovered == 1,
            $"tooltips while hovered={hovered}");
        Check("unhovering_a_quietly_focused_card_takes_its_panel_down", afterHover == 0,
            $"{afterHover} tooltip(s) left after the mouse left - the panel is stuck again");

        // The other direction, which the fix must not break: a panel the player
        // raised by focusing the card themselves survives the mouse crossing it
        // and leaving. Release first, so the grab below is a fresh focus event.
        view.ReleaseFocus();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        view.GrabFocus();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        view.EmitSignal(Control.SignalName.MouseEntered);
        view.EmitSignal(Control.SignalName.MouseExited);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Check("a_focus_raised_panel_survives_a_hover_round_trip", CountTooltips() == 1,
            $"tooltips={CountTooltips()} - the mouse leaving closed a panel focus is still holding open");

        view.QueueFree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
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
        RewardContext.PotionDrop = null;
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

        // The fan is behind the "Add a card to your deck" row now rather than
        // being the screen itself, so the quiet-open property has to be checked
        // where the cards actually are: on the overlay that row opens. Same
        // grab, one view further in.
        Row(screen, RewardKind.Card)!.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var cards = screen.GetNode<Control>(RewardScreen.CardChoicesPath)
            .GetChildren().OfType<CardView>().ToList();
        var focused = GetViewport().GuiGetFocusOwner();

        Check("reward_card_overlay_auto_focuses_its_first_card",
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
        RewardContext.PotionDrop = null;
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

    // The reward list: one row per reward the fight actually offered, claimed
    // one at a time. Nothing here is granted before the screen loads any more -
    // gold used to be banked in CombatScreen and the relic granted as it was
    // picked, so two of the four rows were things the player already had.
    private void TestRewardScreen()
    {
        RunState.Gold = 0;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RewardContext.ActCleared = null;
        RewardContext.GoldAwarded = 25;
        RewardContext.RelicChoices = new List<RelicDefinition> { RelicDatabase.Get("anchor_stone") };
        RewardContext.PotionDrop = null;
        RewardContext.Claimed.Clear();
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("strike"),
            CardDatabase.Get("defend"),
            CardDatabase.Get("bash"),
        };

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        var skip = screen.GetNode<Button>("SkipButton");

        Check("reward_lists_a_row_per_offered_reward",
            Row(screen, RewardKind.Gold) is not null && Row(screen, RewardKind.Relic) is not null
            && Row(screen, RewardKind.Card) is not null,
            "gold, relic and card rows should all be present");
        // The one this fight did not drop has no row at all, rather than a
        // disabled row explaining an absence.
        Check("reward_omits_a_row_for_a_reward_not_offered", Row(screen, RewardKind.Potion) is null,
            "a potion row appeared for a fight that dropped nothing");
        Check("reward_gold_row_shows_the_amount",
            Row(screen, RewardKind.Gold)!.FindChildren("*", "Label", recursive: true, owned: false)
                .OfType<Label>().Any(l => l.Text.Contains("25")),
            "the gold row does not name the amount");

        // The tier has to be readable, the same way the potion row's rarity is
        // and for a stronger reason: an elite and a boss pay from different
        // pools now, and this row is the only place the player is told which.
        // anchor_stone is Common.
        var relicLabels = Row(screen, RewardKind.Relic)!
            .FindChildren("*", "Label", recursive: true, owned: false).OfType<Label>().ToList();
        Check("reward_relic_row_shows_its_name_and_tier",
            relicLabels.Any(l => l.Text.Contains("Anchor Stone")) && relicLabels.Any(l => l.Text.Contains("Common")),
            string.Join(" | ", relicLabels.Select(l => l.Text)));
        Check("reward_skip_button_has_a_handler", skip.GetSignalConnectionList("pressed").Count > 0,
            "no pressed connections");

        // Claiming pays out exactly once. The second press is the real check:
        // RewardContext.Claimed is what refuses it, so the payout cannot be
        // repeated even by a row that is somehow still enabled.
        Row(screen, RewardKind.Gold)!.EmitSignal(BaseButton.SignalName.Pressed);
        int afterFirst = RunState.Gold;
        Row(screen, RewardKind.Gold)!.EmitSignal(BaseButton.SignalName.Pressed);
        Check("reward_gold_is_only_banked_when_claimed", afterFirst == 25 && RunState.Gold == 25,
            $"gold={RunState.Gold} after two presses of a 25 gold row");
        Check("reward_claimed_row_refuses_further_focus",
            Row(screen, RewardKind.Gold) is { Disabled: true, FocusMode: Control.FocusModeEnum.None },
            $"disabled={Row(screen, RewardKind.Gold)?.Disabled} focus={Row(screen, RewardKind.Gold)?.FocusMode}");

        Row(screen, RewardKind.Relic)!.EmitSignal(BaseButton.SignalName.Pressed);
        Check("reward_relic_is_only_granted_when_claimed",
            RunState.Relics.Count == 1 && RunState.Relics[0].Definition.Id == "anchor_stone",
            $"relics={RunState.Relics.Count}");

        // The card row opens the fan rather than resolving in place, and
        // picking there closes it and returns to the list. A pick that left the
        // screen would forfeit every row the player had not reached yet, which
        // is the whole reason this screen is a list.
        var overlay = screen.GetNode<Control>(RewardScreen.OverlayName);
        Check("reward_card_overlay_starts_closed", !overlay.Visible, "the fan is open before the row was pressed");

        Row(screen, RewardKind.Card)!.EmitSignal(BaseButton.SignalName.Pressed);
        var cardViews = screen.GetNode<Control>(RewardScreen.CardChoicesPath)
            .GetChildren().OfType<CardView>().ToList();
        Check("reward_card_row_opens_the_fan", overlay.Visible && cardViews.Count == 3,
            $"visible={overlay.Visible} cards={cardViews.Count}");
        Check("reward_card_views_are_non_interactive", cardViews.All(c => !c.Interactive),
            "a reward CardView still has Interactive=true (would try to drag-to-play)");
        Check("reward_first_card_is_strike",
            cardViews.Count > 0 && cardViews[0].CardInstance?.Definition.Id == "strike",
            $"id='{cardViews.ElementAtOrDefault(0)?.CardInstance?.Definition.Id}'");

        int deckBefore = RunState.Deck.Count;
        cardViews[0]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });

        Check("reward_card_pick_adds_to_the_deck", RunState.Deck.Count == deckBefore + 1,
            $"deck went {deckBefore} -> {RunState.Deck.Count}");
        Check("reward_card_pick_closes_the_fan", !overlay.Visible, "the fan stayed open after a pick");
        // WATCHDOG NOTE: this one fails as a TIMEOUT rather than a FAIL.
        // LoadScene parents the screen under this test node, so a pick that
        // wrongly called Advance() would run ChangeSceneToFile and replace the
        // test scene itself - the suite hangs, and the last PASS line printed
        // names the check before the culprit.
        Check("reward_card_pick_does_not_leave_the_screen", screen.IsInsideTree(),
            "picking a card advanced the screen - every unclaimed row would be forfeited");
        Check("reward_exit_button_reads_continue_once_everything_is_claimed",
            skip.Text == "Continue", $"text='{skip.Text}' with nothing left to skip");
        screen.QueueFree();
    }

    // A boss offers three relics and the row asks rather than pays. Everything
    // the one-relic row above proves is a different code path from here: that
    // row grants on press, this one opens a second view and the claim lands in
    // it, so "granted exactly once" has to be established again on this side.
    private void TestRewardBossRelicChoice()
    {
        var offered = new List<RelicDefinition>
        {
            RelicDatabase.Get("clockwork_gear"),
            RelicDatabase.Get("reapers_tally"),
            RelicDatabase.Get("hollow_crown"),
        };

        RunState.Gold = 0;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RewardContext.ActCleared = null;
        RewardContext.GoldAwarded = 0;
        RewardContext.PotionDrop = null;
        RewardContext.RelicChoices = offered;
        RewardContext.Claimed.Clear();
        RewardContext.CardChoices = new List<CardDefinition>();

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        var overlay = screen.GetNode<Control>(RewardScreen.OverlayName);
        var relicArea = screen.GetNode<Control>(RewardScreen.RelicChoicesPath);
        var tipLine = screen.GetNode<Control>("TipLine");

        // The row cannot be any one of the three, so it has to say what it is.
        // A row still naming a relic would mean the count branch did not fire
        // and the first press would hand that relic over.
        var rowLabels = Row(screen, RewardKind.Relic)!
            .FindChildren("*", "Label", recursive: true, owned: false).OfType<Label>().ToList();
        Check("reward_boss_relic_row_asks_rather_than_names_one",
            rowLabels.Any(l => l.Text.Contains("Choose"))
            && !rowLabels.Any(l => offered.Any(r => l.Text.Contains(r.Name))),
            string.Join(" | ", rowLabels.Select(l => l.Text)));

        Check("reward_boss_relic_overlay_starts_closed", !overlay.Visible,
            "the picker is open before the row was pressed");

        Row(screen, RewardKind.Relic)!.EmitSignal(BaseButton.SignalName.Pressed);

        var tiles = relicArea.FindChildren("*", recursive: true, owned: false)
            .OfType<ActivatablePanel>().ToList();
        Check("reward_boss_relic_row_opens_the_picker", overlay.Visible && tiles.Count == 3,
            $"visible={overlay.Visible} tiles={tiles.Count}");
        Check("reward_boss_relic_row_grants_nothing_on_press", RunState.Relics.Count == 0,
            $"relics={RunState.Relics.Count} - opening the picker paid out");
        Check("reward_boss_relic_picker_hides_the_card_fan",
            !screen.GetNode<Control>(RewardScreen.CardChoicesPath).Visible,
            "both overlay views are visible at once");

        // Same as the card fan: the tip sits directly under the Back button, and
        // a dimmed line of body text behind a modal reads as something the
        // player failed to dismiss.
        Check("reward_boss_relic_picker_hides_the_tip", !tipLine.Visible,
            "the tip is still on screen behind the relic picker");

        var tileLabels = tiles.SelectMany(t => t.FindChildren("*", "Label", recursive: true, owned: false)
            .OfType<Label>()).Select(l => l.Text).ToList();
        Check("reward_boss_relic_tiles_name_every_offer",
            offered.All(r => tileLabels.Any(t => t.Contains(r.Name))),
            string.Join(" | ", tileLabels));

        // Activating a tile is the claim. ActivatablePanel resolves a left
        // *release*, the same synthetic event the card pick above uses.
        tiles[1]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });

        Check("reward_boss_relic_pick_grants_the_one_chosen",
            RunState.Relics.Count == 1 && RunState.Relics[0].Definition.Id == offered[1].Id,
            $"relics=[{string.Join(",", RunState.Relics.Select(r => r.Definition.Id))}], expected {offered[1].Id}");
        Check("reward_boss_relic_pick_closes_the_picker", !overlay.Visible,
            "the picker stayed open after a pick");
        // WATCHDOG NOTE: fails as a TIMEOUT rather than a FAIL, exactly like
        // reward_card_pick_does_not_leave_the_screen above - a wrongful
        // Advance() replaces this test's own scene.
        Check("reward_boss_relic_pick_does_not_leave_the_screen", screen.IsInsideTree(),
            "picking a relic advanced the screen - every unclaimed row would be forfeited");
        Check("reward_boss_relic_row_is_claimed_after_the_pick",
            Row(screen, RewardKind.Relic) is { Disabled: true, FocusMode: Control.FocusModeEnum.None },
            $"disabled={Row(screen, RewardKind.Relic)?.Disabled}");

        // Re-opening and picking again pays nothing. The guard is
        // RewardContext.Claimed, not the freed tile, so this holds even though
        // the tiles the player is clicking have been queued for deletion.
        Row(screen, RewardKind.Relic)!.EmitSignal(BaseButton.SignalName.Pressed);
        tiles[0]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Check("reward_boss_relic_pick_is_not_repeatable", RunState.Relics.Count == 1,
            $"relics={RunState.Relics.Count} after a second pick");

        screen.QueueFree();
    }

    // Three tiles share an 800px band, and a tile is as wide as its widest
    // child. ScreenChrome.Heading does not wrap, so a relic name wider than the
    // tile does not overflow that tile - it widens the column, which widens the
    // grid, which the CenterContainer then hangs off both ends of the band.
    //
    // Driven with a name longer than anything authored rather than with the
    // longest Boss relic in the content, because the content one passes today
    // with about three characters to spare and would go on passing right up
    // until the row that broke it. This is the "CROWN REA" failure with the
    // tiles standing in for the enemy row (ROADMAP Phase 11).
    private async System.Threading.Tasks.Task TestRewardBossRelicTilesStayInTheirBand()
    {
        var stretched = new RelicDefinition
        {
            Id = "smoke_test_long_name",
            Name = "The Sundered Reliquary of Endless Night",
            Description = "Whenever a hit you deal kills an enemy, gain 1 Strength for the rest of the fight.",
            Tier = RelicTier.Boss,
        };

        RunState.Relics = new List<RelicInstance>();
        RewardContext.ActCleared = null;
        RewardContext.GoldAwarded = 0;
        RewardContext.PotionDrop = null;
        RewardContext.CardChoices = new List<CardDefinition>();
        RewardContext.Claimed.Clear();
        RewardContext.RelicChoices = new List<RelicDefinition>
        {
            stretched, RelicDatabase.Get("clockwork_gear"), RelicDatabase.Get("hollow_crown"),
        };

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        // The band is anchored, so the screen has to be at the design size for
        // RelicChoicesArea to have its real width.
        ((Control)screen).Size = new Vector2(1152, 648);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Row(screen, RewardKind.Relic)!.EmitSignal(BaseButton.SignalName.Pressed);

        // Containers sort deferred, so every rect below is zero until a frame
        // has run. Without these waits this whole test passes on tiles of width
        // 0 - which is how the first version of it passed against the very bug
        // it was written for.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var area = screen.GetNode<Control>(RewardScreen.RelicChoicesPath);
        var tiles = area.FindChildren("*", recursive: true, owned: false)
            .OfType<ActivatablePanel>().ToList();

        float widest = tiles.Count == 0 ? 0f : tiles.Max(t => t.GetGlobalRect().Size.X);
        float total = tiles.Sum(t => t.GetGlobalRect().Size.X);
        // The `widest > 0` term is the guard on the guard: a layout that never
        // ran reports every tile at zero and satisfies every inequality here.
        Check("reward_boss_relic_tiles_fit_their_band",
            tiles.Count == 3 && widest > 0f && total <= area.Size.X,
            $"{tiles.Count} tiles totalling {total}px in a {area.Size.X}px band (widest {widest})");

        // Equal widths, which is the half TextFit does *not* buy and the half
        // that regresses silently. Measured: with the ladder alone the long
        // name lands a 269px tile beside two 248s - inside the band, so nothing
        // above this fails, and a visibly ragged row. The name label's own
        // width bound is what makes all three the same.
        Check("reward_boss_relic_tiles_are_the_same_width",
            widest > 0f && tiles.All(t => Mathf.Abs(t.GetGlobalRect().Size.X - widest) < 0.5f),
            string.Join("/", tiles.Select(t => $"{t.GetGlobalRect().Size.X:F0}")));

        // TextFit's own contribution, which is invisible to every width check
        // above it: the autowrap holds the band whatever size the name renders
        // at, so without this the ladder could be deleted with a green sweep.
        // The stretched name steps down a rung; the two authored ones do not.
        var names = tiles
            .Select(t => t.FindChildren("*", "Label", recursive: true, owned: false).OfType<Label>().First())
            .ToList();
        Check("reward_boss_relic_long_name_steps_down_a_font_rung",
            names.Count == 3
            && names[0].GetThemeFontSize("font_size") == UiTheme.Fonts.Small
            && names.Skip(1).All(l => l.GetThemeFontSize("font_size") == UiTheme.Fonts.Body),
            string.Join(" | ", names.Select(l => $"{l.Text.Split('\n')[0]}={l.GetThemeFontSize("font_size")}")));

        // And every tile stays inside it, which the sum alone does not prove:
        // a CenterContainer holding an oversized grid hangs off both ends.
        var band = area.GetGlobalRect();
        Check("reward_boss_relic_tiles_are_not_clipped_by_the_band",
            widest > 0f && tiles.All(t => t.GetGlobalRect().Position.X >= band.Position.X - 0.5f
                && t.GetGlobalRect().End.X <= band.End.X + 0.5f),
            string.Join(" | ", tiles.Select(t => $"{t.GetGlobalRect().Position.X:F0}..{t.GetGlobalRect().End.X:F0}"))
                + $" against {band.Position.X:F0}..{band.End.X:F0}");

        screen.QueueFree();
    }

    // The potion drop's own row, and the belt cap it has to respect.
    private void TestRewardPotionDrop()
    {
        RunState.Gold = 0;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RewardContext.ActCleared = null;
        RewardContext.GoldAwarded = 0;
        RewardContext.RelicChoices = new List<RelicDefinition>();
        RewardContext.CardChoices = new List<CardDefinition>();
        RewardContext.PotionDrop = PotionDatabase.Get("fire_potion");
        RewardContext.Claimed.Clear();

        var screen = LoadScene("res://scenes/RewardScreen.tscn");
        var row = Row(screen, RewardKind.Potion);
        var labels = row?.FindChildren("*", "Label", recursive: true, owned: false).OfType<Label>().ToList()
            ?? new List<Label>();

        Check("reward_shows_a_potion_row_when_one_dropped", row is not null, "no potion row");
        // The tier has to be readable, or PotionDefinition.Rarity is a number
        // that exists only for the sampler. Common is fire_potion's tier.
        Check("reward_potion_row_shows_its_name_and_rarity",
            labels.Any(l => l.Text.Contains("Fire Potion")) && labels.Any(l => l.Text.Contains("Common")),
            string.Join(" | ", labels.Select(l => l.Text)));

        row!.EmitSignal(BaseButton.SignalName.Pressed);
        int afterFirst = RunState.Potions.Count;
        row.EmitSignal(BaseButton.SignalName.Pressed);
        Check("reward_potion_claim_adds_it_to_the_belt",
            afterFirst == 1 && RunState.Potions[0].DefinitionId == "fire_potion",
            $"count={afterFirst} first='{RunState.Potions.FirstOrDefault()?.DefinitionId}'");
        Check("reward_potion_claim_is_not_repeatable", RunState.Potions.Count == 1,
            $"a second press left {RunState.Potions.Count} potions on the belt");
        screen.QueueFree();

        // A drop against a belt that is already full. The row still renders -
        // silently dropping the offer is what the shop does and it reads as a
        // broken button - but it refuses and says why. Both flags together:
        // Disabled alone excludes a control from neither Tab nor arrow nav.
        RunState.Potions = new List<PotionInstance>
        {
            new(PotionDatabase.Get("fire_potion")),
            new(PotionDatabase.Get("block_potion")),
            new(PotionDatabase.Get("swift_potion")),
        };
        RewardContext.Claimed.Clear();
        var fullBelt = LoadScene("res://scenes/RewardScreen.tscn");
        var fullRow = Row(fullBelt, RewardKind.Potion);

        Check("reward_full_belt_still_shows_the_potion_row", fullRow is not null, "no potion row");
        Check("reward_full_belt_disables_the_claim",
            fullRow is { Disabled: true, FocusMode: Control.FocusModeEnum.None },
            $"disabled={fullRow?.Disabled} focusMode={fullRow?.FocusMode}");
        // On the row, not only in the tooltip. A blocked row is
        // FocusModeEnum.None, so a keyboard player can never reach it to raise
        // one - asserting the tooltip alone would pass a version of this screen
        // that explains itself to the mouse and to nobody else.
        var fullLabels = fullRow!.FindChildren("*", "Label", recursive: true, owned: false)
            .OfType<Label>().Select(l => l.Text).ToList();
        Check("reward_full_belt_says_why_on_the_row",
            fullLabels.Any(t => t.Contains("Belt full")),
            string.Join(" | ", fullLabels));

        fullRow!.EmitSignal(BaseButton.SignalName.Pressed);
        Check("reward_full_belt_claim_grants_nothing", RunState.Potions.Count == RunState.MaxPotionSlots,
            $"belt went to {RunState.Potions.Count} against a cap of {RunState.MaxPotionSlots}");
        fullBelt.QueueFree();

        // Deliberately last, and deliberately after the two above set it:
        // RewardContext is a static that outlives a screen, so a fight that
        // dropped nothing rendering a potion row is a *leak*, and nulling the
        // field in the same test that reads it would prove nothing.
        RunState.Potions = new List<PotionInstance>();
        RewardContext.PotionDrop = null;
        RewardContext.Claimed.Clear();
        var noDrop = LoadScene("res://scenes/RewardScreen.tscn");
        Check("reward_has_no_potion_row_when_none_dropped", Row(noDrop, RewardKind.Potion) is null,
            "a potion row survived from the previous fight");
        noDrop.QueueFree();
    }

    // The tip line under the reward list. Four separate claims, because three
    // of them fail silently: a tip that never changes, a tip that is not one of
    // the authored ones, and a tip overlapping the Skip button all render as a
    // screen that looks fine in the one screenshot anyone takes.
    private async System.Threading.Tasks.Task TestRewardScreenTip()
    {
        RewardContext.ActCleared = null;
        RewardContext.RelicChoices = new List<RelicDefinition>();
        RewardContext.PotionDrop = null;
        RewardContext.GoldAwarded = 25;
        RewardContext.Claimed.Clear();
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
        };
        RunState.VisitedNodeIds = new HashSet<string> { "n0" };

        var screen = (Control)LoadScene("res://scenes/RewardScreen.tscn");
        var tipLine = screen.GetNode<Control>("TipLine");
        var tipLabel = screen.GetNode<Label>($"TipLine/{RewardScreen.TipLabelName}");

        screen.Size = new Vector2(1152, 648);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("reward_shows_a_tip", tipLine.Visible && tipLabel.Text.Length > 0,
            $"visible={tipLine.Visible} text='{tipLabel.Text}'");

        // From the authored pool, not from a fallback string. Compared after
        // the same key substitution the screen applies, so a tip naming a key
        // is not counted as "not one of ours".
        var authored = TipDatabase.All.Select(t => ScreenKeyboardNav.ResolveKeyHints(t.Text)).ToHashSet();
        Check("reward_tip_comes_from_the_authored_pool", authored.Contains(tipLabel.Text),
            $"'{tipLabel.Text}' is not in tips.json");

        // Captured before the overflow check below overwrites the label.
        string firstTip = tipLabel.Text;

        // The tip must not run into the Skip button below it. This is the
        // failure mode the whole "a constant that fits the worst case is not a
        // constant that fits the best one" list in ROADMAP is about, and the
        // longest authored tip is the case that finds it.
        var skipRect = screen.GetNode<Control>("SkipButton").GetGlobalRect();
        var longest = TipDatabase.All.OrderByDescending(t => t.Text.Length).First();
        tipLabel.Text = ScreenKeyboardNav.ResolveKeyHints(longest.Text);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var tipRect = tipLine.GetGlobalRect();
        Check("the_longest_tip_clears_the_skip_button", !tipRect.Intersects(skipRect),
            $"longest tip '{longest.Id}' at {tipRect} against Skip at {skipRect}");
        Check("the_longest_tip_fits_its_line_on_one_row",
            tipLabel.GetGlobalRect().Size.X <= tipLine.Size.X,
            $"'{longest.Id}' measures {tipLabel.GetGlobalRect().Size.X} in a {tipLine.Size.X} line");

        // Gone, not dimmed, while the card fan is up: it sits directly under
        // the fan's Back button, and a faded line of body text behind a modal
        // reads as something the player failed to dismiss.
        Row(screen, RewardKind.Card)!.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("reward_tip_hides_behind_the_card_fan", !tipLine.Visible,
            "the tip is still on screen under the modal");

        screen.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // It rotates. Same seed, a run one node further along, and the tip has
        // to have moved - a tip that never changes is the whole feature not
        // working, and nothing else here would notice.
        RewardContext.Claimed.Clear();
        RunState.VisitedNodeIds = new HashSet<string> { "n0", "n1" };
        var later = LoadScene("res://scenes/RewardScreen.tscn");
        string secondTip = later.GetNode<Label>($"TipLine/{RewardScreen.TipLabelName}").Text;
        Check("reward_tip_advances_with_the_run", secondTip != firstTip && secondTip.Length > 0,
            $"both visits showed '{secondTip}'");
        later.QueueFree();

        RunState.VisitedNodeIds = new HashSet<string>();
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

    // The shop counterpart of MapSmokeTest.TestNodesClearTheRunStatusBlock,
    // and a shipped bug: CardOffersRow is four 176px cards centred on the
    // design width, so it starts at x=194, while the run-status block's relic
    // grid ran to x=280 at the shared six-column default - a playtest
    // screenshot has relic icons painted straight over the first card's name
    // banner. ShopScreen asks for three columns now, and this is what says it
    // still does.
    //
    // Thirteen relics for the same reason the map test uses it: three rows at
    // six columns, five at three, so the block is tall enough to reach the
    // merchandise row if the narrowing is ever undone by widening instead.
    private async System.Threading.Tasks.Task TestShopOffersClearTheRunStatusBlock()
    {
        RunState.Gold = 200;
        RunState.Potions = new List<PotionInstance>();
        var relicsBefore = RunState.Relics;
        RunState.Relics = RelicDatabase.All.Take(13).Select(r => new RelicInstance(r)).ToList();

        var screen = (Control)LoadScene("res://scenes/ShopScreen.tscn");

        // Real rects, not minimums: both the block and the merchandise are
        // container-laid-out and every one of these rows is centred inside a
        // box far wider than its contents (CardOffersRow's own rect is 1080px
        // for 764px of cards), so measuring the *rows* would report an overlap
        // that is not on screen. Two frames for the deferred sort pass, the
        // same wait CombatTargetingSmokeTest takes before it measures the HUD.
        screen.Size = new Vector2(1152, 648);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // The block's two rows, not the block: it is a VBoxContainer, so its
        // own rect is the union of a 280px-wide HP/gold row that ends at y=56
        // and a relic grid a third that wide running far below it. The union
        // is a rectangle covering neither, and testing it reports the first
        // card as buried when nothing is on top of it.
        var blockRows = screen.GetNode<Control>("RunStatusBar").GetChildren()
            .OfType<Control>().Select(c => c.GetGlobalRect()).ToList();

        var covered = new List<string>();
        foreach (string path in new[] { "CardOffersRow", "OffersRow" })
        {
            foreach (var child in screen.GetNode<Control>(path).GetChildren().OfType<Control>())
            {
                var childRect = child.GetGlobalRect();
                foreach (var row in blockRows.Where(r => r.Intersects(childRect)))
                {
                    covered.Add($"{path} child at {childRect} under the block row at {row}");
                }
            }
        }

        Check("shop_offers_clear_the_relic_grid", covered.Count == 0,
            string.Join(", ", covered));

        RunState.Relics = relicsBefore;
        screen.QueueFree();
    }

    // The keyboard must not stop on an offer the player cannot buy. Disabled
    // does not achieve that on its own - Godot's focus navigation filters on
    // FocusMode and visibility, and BaseButton keeps FocusModeEnum.All when
    // disabled - so ShopScreen.RefreshOffers pairs the two, and this is the
    // assertion that says it stayed paired. Same rule, same wrong belief
    // corrected, as MapScreen's unreachable nodes.
    //
    // Gold is 60 so both populations are guaranteed non-empty whatever the
    // relic/potion stock rolls: the four card offers at 50 are affordable and
    // the 75g removal service is not. An invariant asserted over an all-
    // affordable shop would pass without testing anything.
    private void TestShopUnaffordableOffersAreUnfocusable()
    {
        RunState.Gold = 60;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        var screen = LoadScene("res://scenes/ShopScreen.tscn");
        var buyButtons = screen.GetNode<Control>("CardOffersRow").GetChildren()
            .SelectMany(c => c.GetChildren()).OfType<Button>()
            .Concat(screen.GetNode<Control>("OffersRow").GetChildren()
                .SelectMany(c => c.GetChildren()).SelectMany(c => c.GetChildren()).OfType<Button>())
            .Append(screen.GetNode<Button>("RemoveCardButton"))
            .ToList();

        var affordable = buyButtons.Where(b => !b.Disabled).ToList();
        var unaffordable = buyButtons.Where(b => b.Disabled).ToList();

        Check("shop_has_both_affordable_and_unaffordable_offers",
            affordable.Count > 0 && unaffordable.Count > 0,
            $"affordable={affordable.Count}, unaffordable={unaffordable.Count}");
        Check("shop_unaffordable_offers_refuse_focus",
            unaffordable.All(b => b.FocusMode == Control.FocusModeEnum.None),
            $"still focusable=[{string.Join(",", unaffordable.Where(b => b.FocusMode != Control.FocusModeEnum.None).Select(b => b.Text))}]");
        Check("shop_affordable_offers_keep_focus",
            affordable.All(b => b.FocusMode != Control.FocusModeEnum.None),
            $"unfocusable=[{string.Join(",", affordable.Where(b => b.FocusMode == Control.FocusModeEnum.None).Select(b => b.Text))}]");

        // Buying drops the button out of _offerButtons entirely, so the refresh
        // loop never sees it again - MarkSold has to do this itself or a "Sold"
        // offer stays tabbable for the rest of the visit.
        var bought = affordable.First(b => b.Text.StartsWith("Buy"));
        bought.EmitSignal(BaseButton.SignalName.Pressed);
        Check("shop_sold_offers_refuse_focus",
            bought.Text == "Sold" && bought.Disabled
            && bought.FocusMode == Control.FocusModeEnum.None,
            $"text='{bought.Text}', disabled={bought.Disabled}, focus={bought.FocusMode}");

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
