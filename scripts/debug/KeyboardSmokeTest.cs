using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Coverage for playing the whole game without a mouse.
//
// Three separate things, because they can break independently:
//
//  1. The InputMap layer itself. Every hd_* action the code asks for by name
//     has to exist in project.godot, or IsActionPressed silently returns false
//     forever and a key just stops working - no error, no crash. Two actions
//     sharing a keycode fails the same way, quietly, from whichever handler
//     runs second.
//  2. Every non-combat screen having a focus owner after it loads. The screens
//     were always navigable; nothing ever called GrabFocus, so the first key
//     press went nowhere. This is the assertion that stays true.
//  3. Combat's own handler, which does not use focus at all - the card and
//     potion belts, the potion aiming sub-state, and Continue at CombatEnd.
//
// Input is delivered as InputEventAction straight into _UnhandledInput rather
// than pushed through the OS input pipeline, matching the convention
// CombatTargetingSmokeTest set for the drag path: assert the state a real key
// press would produce. Run via
// `godot --headless scenes/debug/KeyboardSmokeTest.tscn`.
public partial class KeyboardSmokeTest : Node
{
    private int _pass;
    private int _fail;

    // Every action the C# code references by name. If a handler learns a new
    // action, it belongs here too - that is the whole point of the list.
    private static readonly string[] SingletonActions =
    {
        "hd_confirm", "hd_cancel", "hd_end_turn", "hd_nav_left", "hd_nav_right",
        "hd_pile_deck", "hd_pile_draw", "hd_pile_discard", "hd_pile_exhaust",
    };

    public override async void _Ready()
    {
        // Captured before the combat test runs: its simulated Continue press
        // triggers ChangeSceneToFile, which replaces this node as the tree's
        // current scene - after that GetTree() on `this` is null and the run
        // would hang with no summary and no Quit. The SceneTree object itself
        // survives the swap. Same reasoning and workaround as ActSmokeTest.
        var tree = GetTree();

        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        TipDatabase.LoadAll();
        BlessingDatabase.LoadAll();

        TestInputMapLayer();
        TestFocusModeSplit();
        TestOnlyFocusModeExcludesAControlFromTheKeyboard();
        await TestEveryNonCombatScreenTakesFocus(tree);
        await TestRewardListFocus(tree);
        await TestEventPickerTakesFocus(tree);
        await TestRunSetupFocus(tree);
        TestPileKeysCannotReachAFocusedTextField();
        await TestRestPickerGridNavigation(tree);
        await TestCombatKeyboard(tree);

        GD.Print($"KeyboardSmokeTest: {_pass} passed, {_fail} failed");
        tree.Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    // --- 1. the InputMap layer -------------------------------------------

    private void TestInputMapLayer()
    {
        var expected = SingletonActions
            .Concat(Enumerable.Range(1, 10).Select(i => $"hd_card_{i}"))
            .Concat(Enumerable.Range(1, RunState.MaxPotionSlots).Select(i => $"hd_potion_{i}"))
            .ToList();

        foreach (var action in expected)
        {
            Check($"inputmap_has_{action}", InputMap.HasAction(action),
                "missing from project.godot [input] - IsActionPressed would silently never fire");
            if (!InputMap.HasAction(action)) continue;

            Check($"inputmap_{action}_is_bound", InputMap.ActionGetEvents(action).Count > 0,
                "action exists but has no events");
        }

        // One card action per hand slot the fan can badge (CardView
        // .SetHotkeyNumber stops at 10), and one potion action per belt slot.
        Check("inputmap_card_actions_cover_ten_slots",
            Enumerable.Range(1, 10).All(i => InputMap.HasAction($"hd_card_{i}")),
            "CombatScreen.CardSlotActions builds ten names and would throw on a missing one");
        Check("inputmap_potion_actions_match_belt_size",
            Enumerable.Range(1, RunState.MaxPotionSlots).All(i => InputMap.HasAction($"hd_potion_{i}"))
            && !InputMap.HasAction($"hd_potion_{RunState.MaxPotionSlots + 1}"),
            $"expected exactly {RunState.MaxPotionSlots} to match RunState.MaxPotionSlots");

        // A keycode bound to two hd_* actions is the failure that costs an
        // afternoon: both branches fire, or one wins depending on handler
        // order. Checked across hd_* only - overlapping Godot's built-in
        // ui_accept (Space/Enter) is deliberate and safe, because combat has
        // no focusable controls for ui_accept to reach.
        var seen = new Dictionary<Key, string>();
        foreach (var action in expected.Where(a => InputMap.HasAction(a)))
        {
            foreach (var ev in InputMap.ActionGetEvents(action))
            {
                if (ev is not InputEventKey key) continue;
                if (seen.TryGetValue(key.Keycode, out var owner) && owner != action)
                {
                    Check($"inputmap_no_duplicate_binding_{key.Keycode}", false,
                        $"{OS.GetKeycodeString(key.Keycode)} is bound to both {owner} and {action}");
                }
                seen[key.Keycode] = action;
            }
        }
        Check("inputmap_no_duplicate_keycodes_across_hd_actions", true,
            "reported per-collision above");

        // The on-screen hints are generated from these, so an unreadable
        // binding shows the player a blank pair of brackets.
        Check("keyhint_reads_back_every_pile_binding",
            new[] { "hd_pile_deck", "hd_pile_draw", "hd_pile_discard", "hd_pile_exhaust" }
                .All(a => ScreenKeyboardNav.KeyHint(a).Length > 0),
            "ScreenKeyboardNav.KeyHint returned empty for a pile action");
        Check("keyhint_reads_back_every_potion_binding",
            Enumerable.Range(1, RunState.MaxPotionSlots)
                .All(i => ScreenKeyboardNav.KeyHint($"hd_potion_{i}").Length > 0),
            "PotionView's badge would render blank");
    }

    // --- 2. who is allowed to hold focus ---------------------------------

    // Pins the engine behaviour MapScreen and ShopScreen both depend on, because
    // this repo has now been wrong about it twice in comments and once in code.
    //
    // The belief was that Godot's focus navigation skips Disabled controls, so
    // marking a map node or a shop offer Disabled was enough to keep the
    // keyboard off it. It is not: Disabled excludes nothing and does not even
    // release focus a control already holds. Only FocusModeEnum.None does
    // either. That is why both screens now set the two together, and why an
    // engine upgrade that changed it would need to be noticed here rather than
    // discovered as a focus ring sitting on a move the player cannot make.
    //
    // Asserting Godot rather than Hollowdeck is deliberate and rare: the wrong
    // version of this was load-bearing in two screens and invisible to every
    // other test in the suite.
    private void TestOnlyFocusModeExcludesAControlFromTheKeyboard()
    {
        // Under a real Control root. Buttons hung off a bare Node are not
        // reachable by focus navigation at all, which makes for a probe whose
        // control case is degenerate and whose result means nothing (measured -
        // it reported an exclusion that was really just an unreachable tree).
        var root = new Control { Name = "FocusProbeRoot", Size = new Vector2(400, 400) };
        AddChild(root);
        var a = new Button { Name = "A", Position = new Vector2(0, 0), Size = new Vector2(80, 30) };
        var b = new Button { Name = "B", Position = new Vector2(0, 50), Size = new Vector2(80, 30) };
        var c = new Button { Name = "C", Position = new Vector2(0, 100), Size = new Vector2(80, 30) };
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);

        // Control case first. Without it an exclusion below is indistinguishable
        // from navigation not working here at all.
        Check("focus_navigation_reaches_the_next_control_at_all",
            a.FindNextValidFocus() == b && a.FindValidFocusNeighbor(Side.Bottom) == b,
            $"tab={a.FindNextValidFocus()?.Name}, arrow={a.FindValidFocusNeighbor(Side.Bottom)?.Name}");

        b.Disabled = true;
        Check("disabled_alone_does_not_exclude_a_control_from_focus_navigation",
            a.FindNextValidFocus() == b && a.FindValidFocusNeighbor(Side.Bottom) == b,
            $"tab={a.FindNextValidFocus()?.Name}, arrow={a.FindValidFocusNeighbor(Side.Bottom)?.Name} - " +
            "if this now skips B, the engine changed and the FocusMode pairing in " +
            "MapScreen/ShopScreen can be simplified");

        b.GrabFocus();
        b.Disabled = true;
        Check("disabling_a_focused_control_does_not_release_its_focus",
            b.HasFocus(),
            "Disabled released focus - ShopScreen.RefreshOffers' Regrab is documented against this");

        b.Disabled = false;
        b.FocusMode = Control.FocusModeEnum.None;
        Check("focus_mode_none_excludes_a_control_and_releases_its_focus",
            !b.HasFocus() && a.FindNextValidFocus() == c && a.FindValidFocusNeighbor(Side.Bottom) == c,
            $"hasFocus={b.HasFocus()}, tab={a.FindNextValidFocus()?.Name}, " +
            $"arrow={a.FindValidFocusNeighbor(Side.Bottom)?.Name} - this is the mechanism both screens use");

        root.QueueFree();
    }

    private void TestFocusModeSplit()
    {
        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");

        var handCard = cardScene.Instantiate<CardView>();
        AddChild(handCard);
        Check("combat_hand_card_is_not_focusable",
            handCard.FocusMode == Control.FocusModeEnum.None,
            $"focusMode={handCard.FocusMode} - focus nav would fight CombatScreen's arrow keys");

        var choiceCard = cardScene.Instantiate<CardView>();
        AddChild(choiceCard);
        choiceCard.Interactive = false;
        Check("reward_and_shop_cards_are_focusable",
            choiceCard.FocusMode == Control.FocusModeEnum.All,
            $"focusMode={choiceCard.FocusMode} - the reward pick would be mouse-only");

        handCard.QueueFree();
        choiceCard.QueueFree();

        // The pile strip is FocusModeEnum.None in combat for the same reason
        // the hand cards are, and focusable everywhere else.
        var combatHost = new Control();
        AddChild(combatHost);
        var combatBar = new PileCounterBar(combatHost, includeCombatPiles: true);
        combatHost.AddChild(combatBar);

        var screenHost = new Control();
        AddChild(screenHost);
        var screenBar = new PileCounterBar(screenHost, includeCombatPiles: false);
        screenHost.AddChild(screenBar);

        Check("pile_counters_are_not_focusable_in_combat",
            combatBar.GetChildren().OfType<PileCounterCell>().All(c => c.FocusMode == Control.FocusModeEnum.None),
            "a combat pile cell would steal arrow-key input from the hand");
        Check("pile_counters_are_focusable_outside_combat",
            screenBar.GetChildren().OfType<PileCounterCell>().All(c => c.FocusMode == Control.FocusModeEnum.All),
            "the deck counter is unreachable by Tab on Map/Shop/Rest/Reward/Treasure/Event");

        combatHost.QueueFree();
        screenHost.QueueFree();
    }

    // --- 3. every non-combat screen starts somewhere ---------------------

    private async Task TestEveryNonCombatScreenTakesFocus(SceneTree tree)
    {
        // RunEndScreen banks a score into the meta save and deletes the run
        // save just by loading; Map/Reward/Shop/Rest/Treasure/Event are all in
        // RunManager.AutoSaveScreens. Both files go back afterwards.
        using var saveGuard = RunSaveGuard.Protect();
        using var cutGuard = HardCutGuard.Protect();

        RngStreams.Init(4242);
        RunState.InitNewRun();
        RunState.Gold = 200;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 30;
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("defend") };
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RewardContext.GoldAwarded = 25;
        RewardContext.RelicChoices = new List<RelicDefinition>();
        RewardContext.PotionDrop = null;
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
        };
        RunEndContext.Outcome = RunEndOutcome.Lose;

        // Asserting *which* control, not merely that something has focus:
        // "a focus owner exists" passes just as happily when the keyboard
        // lands on Back instead of the thing the screen is for.
        var screens = new (string Path, string Expect, System.Func<Control, Node, bool> Predicate)[]
        {
            ("res://scenes/MainMenu.tscn", "Continue or Start",
                (f, _) => f.Name.ToString() is "ContinueButton" or "StartButton"),
            ("res://scenes/MapScreen.tscn", "a reachable map node",
                (f, screen) => f.GetParent() == screen.GetNode("NodeButtons") && f is BaseButton { Disabled: false }),
            // The first unclaimed reward row, not a card: the reward screen is a
            // list you claim from now, and the card fan lives behind the "Add a
            // card to your deck" row rather than being the screen itself.
            ("res://scenes/RewardScreen.tscn", "the first unclaimed reward row",
                (f, _) => f is Button { Disabled: false } && f.Name.ToString().EndsWith("Row")),
            ("res://scenes/ShopScreen.tscn", "an affordable Buy button",
                (f, _) => f is Button { Disabled: false } b && b.Text.StartsWith("Buy")),
            ("res://scenes/RestScreen.tscn", "HealButton", (f, _) => f.Name.ToString() == "HealButton"),
            ("res://scenes/TreasureScreen.tscn", "ContinueButton", (f, _) => f.Name.ToString() == "ContinueButton"),
            ("res://scenes/EventScreen.tscn", "an event choice",
                (f, screen) => f.GetParent() == screen.GetNode("CenterContainer/VBoxContainer/ChoicesList")),
            ("res://scenes/RunEndScreen.tscn", "RestartButton", (f, _) => f.Name.ToString() == "RestartButton"),
            ("res://scenes/MetaProgressionScreen.tscn", "BackButton", (f, _) => f.Name.ToString() == "BackButton"),
            ("res://scenes/SettingsScreen.tscn", "VolumeSlider", (f, _) => f.Name.ToString() == "VolumeSlider"),
            // The offer, not the seed field above it. The seed is a tool; the
            // blessing is the decision the screen exists for, and a keyboard
            // player landing in a text box would have to know to leave it.
            ("res://scenes/RunSetupScreen.tscn", "a blessing tile",
                (f, screen) => f.GetParent() == screen.GetNode("CenterContainer/VBoxContainer/OfferGrid")),
        };

        foreach (var (path, expect, predicate) in screens)
        {
            var instance = GD.Load<PackedScene>(path).Instantiate();
            AddChild(instance);
            // Two frames: ScreenKeyboardNavListener defers its grab so the
            // containers have finished sizing and enabling their children.
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var focused = GetViewport().GuiGetFocusOwner();
            var name = path.GetFile().GetBaseName();
            Check($"{name}_has_a_focus_owner_on_load", focused is not null,
                "nothing focused - the first key press would go nowhere");
            Check($"{name}_focuses_{expect.Replace(' ', '_')}",
                focused is not null && predicate(focused, instance),
                $"focus landed on '{focused?.Name}' ({focused?.GetType().Name}), expected {expect}");

            instance.QueueFree();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    // The reward list is the second place a non-combat screen swaps views
    // mid-visit (the event picker below is the first), and it does it twice:
    // claiming a row disables the control the player is standing on, and the
    // card row opens a modal fan over the list. Both are moments a screen ends
    // up with focus nowhere, or - worse, because it looks like it works - with
    // the ring on a control the player cannot see.
    private async Task TestRewardListFocus(SceneTree tree)
    {
        using var saveGuard = RunSaveGuard.Protect();
        using var cutGuard = HardCutGuard.Protect();

        RunState.Gold = 0;
        RunState.Potions = new List<PotionInstance>();
        RunState.Relics = new List<RelicInstance>();
        RewardContext.ActCleared = null;
        RewardContext.GoldAwarded = 25;
        // A boss's payout, so both of the overlay's views are reachable in one
        // visit to the screen. That is the case worth driving here rather than
        // in two fixtures: the fan and the relic picker share a Control, a dim
        // and a Back button, so a bug in one view's teardown only shows up once
        // the other has been opened after it.
        RewardContext.RelicChoices = new List<RelicDefinition>
        {
            RelicDatabase.Get("clockwork_gear"),
            RelicDatabase.Get("reapers_tally"),
            RelicDatabase.Get("hollow_crown"),
        };
        RewardContext.PotionDrop = PotionDatabase.Get("fire_potion");
        RewardContext.Claimed.Clear();
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
        };

        var screen = GD.Load<PackedScene>("res://scenes/RewardScreen.tscn").Instantiate();
        AddChild(screen);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Button? Row(RewardKind kind) =>
            screen.FindChild(RewardScreen.RowName(kind), recursive: true, owned: false) as Button;

        // Claiming the row the ring is standing on. RefreshRows sets
        // FocusModeEnum.None on it, which *releases* the focus - so without the
        // Regrab that follows, the next arrow key would go nowhere.
        Row(RewardKind.Gold)!.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var afterClaim = GetViewport().GuiGetFocusOwner();
        Check("reward_claiming_a_row_leaves_focus_somewhere_legal",
            ScreenKeyboardNav.CanTakeFocus(afterClaim),
            $"focus is on '{afterClaim?.Name}' - claiming a row left the ring nowhere");
        Check("reward_claimed_row_does_not_keep_focus",
            !ReferenceEquals(afterClaim, Row(RewardKind.Gold)),
            "the ring stayed on the row that was just claimed and disabled");

        // Opening the card fan is a modal: focus has to move *into* it, and the
        // list behind the dim has to leave the focus chain or Tab walks back
        // out onto rows the player cannot see.
        Row(RewardKind.Card)!.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Check("reward_card_overlay_takes_the_focus", GetViewport().GuiGetFocusOwner() is CardView,
            $"focus is on '{GetViewport().GuiGetFocusOwner()?.Name}', not a card in the fan");
        Check("reward_list_leaves_the_focus_chain_behind_the_overlay",
            Row(RewardKind.Potion)!.FocusMode == Control.FocusModeEnum.None
            && screen.GetNode<Button>("SkipButton").FocusMode == Control.FocusModeEnum.None,
            "a row behind the dim can still be tabbed to");

        // Backing out restores it, in both directions.
        screen.GetNode<Button>(RewardScreen.OverlayCancelPath).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Check("reward_closing_the_overlay_returns_focus_to_the_list",
            GetViewport().GuiGetFocusOwner() is Button { Disabled: false } b
            && b.Name.ToString().EndsWith("Row"),
            $"focus is on '{GetViewport().GuiGetFocusOwner()?.Name}' after backing out of the fan");
        Check("reward_closing_the_overlay_restores_the_list_to_the_focus_chain",
            Row(RewardKind.Potion)!.FocusMode == Control.FocusModeEnum.All
            && screen.GetNode<Button>("SkipButton").FocusMode == Control.FocusModeEnum.All,
            "the list stayed out of the focus chain after the fan closed");

        // The boss relic picker is the overlay's other view and has to satisfy
        // the same contract. It is opened *after* the fan on purpose: the fan's
        // CardViews were freed on the way out, and a picker that inherited a
        // stale focus owner from them would look correct in a fixture that only
        // ever opened one view.
        Row(RewardKind.Relic)!.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Check("reward_relic_picker_takes_the_focus",
            GetViewport().GuiGetFocusOwner() is ActivatablePanel,
            $"focus is on '{GetViewport().GuiGetFocusOwner()?.Name}', not a tile in the picker");
        Check("reward_list_leaves_the_focus_chain_behind_the_relic_picker",
            Row(RewardKind.Potion)!.FocusMode == Control.FocusModeEnum.None
            && screen.GetNode<Button>("SkipButton").FocusMode == Control.FocusModeEnum.None,
            "a row behind the relic picker can still be tabbed to");

        screen.GetNode<Button>(RewardScreen.OverlayCancelPath).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Check("reward_closing_the_relic_picker_returns_focus_to_the_list",
            GetViewport().GuiGetFocusOwner() is Button { Disabled: false } back
            && back.Name.ToString().EndsWith("Row"),
            $"focus is on '{GetViewport().GuiGetFocusOwner()?.Name}' after backing out of the picker");
        Check("reward_closing_the_relic_picker_restores_the_list_to_the_focus_chain",
            Row(RewardKind.Potion)!.FocusMode == Control.FocusModeEnum.All
            && screen.GetNode<Button>("SkipButton").FocusMode == Control.FocusModeEnum.All,
            "the list stayed out of the focus chain after the picker closed");

        screen.QueueFree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    // An event's card picker is the one place a non-combat screen swaps its
    // whole set of controls out mid-visit: the choice button that was pressed
    // is freed, and the grid that replaces it is built in code. That is
    // exactly the moment a screen is left with no focus owner, so it gets its
    // own check rather than riding on the on-load sweep above.
    // RunSetup is the third screen that swaps views mid-visit, and the only one
    // carrying a text field - which makes it the only place two separate
    // keyboard rules can collide.
    //
    //  - Claiming a blessing has to take the seed controls out of the focus
    //    chain, not merely disable them: re-seeding after a claim calls
    //    InitNewRun and silently erases the blessing, and Disabled excludes a
    //    control from neither Tab nor arrow navigation (TestOnlyFocusMode...
    //    above pins that engine behaviour).
    //  - The seed field has to swallow its own keystrokes. D/Q/W/E are bound to
    //    the four pile views and DeckViewButtons is attached to this screen, so
    //    a LineEdit that let those through would open a deck overlay while the
    //    player was typing. That one is pinned structurally in
    //    TestPileKeysCannotReachAFocusedTextField below rather than by driving a
    //    keystroke, because what makes it true is which handler the listener
    //    overrides.
    private async Task TestRunSetupFocus(SceneTree tree)
    {
        using var saveGuard = RunSaveGuard.Protect();
        using var cutGuard = HardCutGuard.Protect();

        var packed = GD.Load<PackedScene>("res://scenes/RunSetupScreen.tscn");

        // The screen draws its three offers off RngStreams.Shop in _Ready, so
        // the seed decides which are on it. Walk seeds until one offers a
        // picker blessing - the case worth driving, since it is the one that
        // opens a second view.
        for (int seed = 1; seed <= 60; seed++)
        {
            RunManager.Instance.BeginRun(seed);

            var instance = packed.Instantiate();
            AddChild(instance);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var grid = instance.GetNode<GridContainer>("CenterContainer/VBoxContainer/OfferGrid");
            var seedField = instance.GetNode<LineEdit>("CenterContainer/VBoxContainer/SeedRow/SeedField");
            var reroll = instance.GetNode<Button>("CenterContainer/VBoxContainer/SeedRow/RerollButton");

            Check($"runsetup_seed_field_shows_the_run_seed_{seed}",
                seedField.Text == seed.ToString(), $"field='{seedField.Text}', seed={seed}");

            int pickerTile = OfferedTileIndex(grid,
                b => b.Outcomes.Any(s => EventOutcomeRegistry.PickerFor(s) is not null));
            if (pickerTile < 0)
            {
                instance.QueueFree();
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                continue;
            }

            grid.GetChild<Control>(pickerTile).GrabFocus();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            grid.GetChild<ActivatablePanel>(pickerTile)._GuiInput(
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var focused = GetViewport().GuiGetFocusOwner();
            var pickerList = instance.GetNode<GridContainer>(
                "PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");
            var pickerCards = pickerList.GetChildren().SelectMany(c => c.GetChildren())
                .OfType<CardView>().ToList();

            Check("runsetup_picker_focuses_one_of_its_cards",
                focused is CardView card && pickerCards.Contains(card),
                $"focus landed on '{focused?.Name}' ({focused?.GetType().Name})");

            // The claim has already happened by the time the grid is open, so
            // the seed controls are out of play behind it.
            Check("runsetup_claiming_takes_the_seed_field_out_of_the_focus_chain",
                seedField.FocusMode == Control.FocusModeEnum.None && !seedField.Editable,
                $"focusMode={seedField.FocusMode}, editable={seedField.Editable}");
            Check("runsetup_claiming_takes_reroll_out_of_the_focus_chain",
                reroll.FocusMode == Control.FocusModeEnum.None && reroll.Disabled,
                $"focusMode={reroll.FocusMode}, disabled={reroll.Disabled}");

            // Pick a card and come back: the result view has to end up with a
            // focus owner, and it has to be the Begin button rather than a
            // freed CardView or the seed row behind it.
            pickerCards[0]._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var after = GetViewport().GuiGetFocusOwner();
            Check("runsetup_returning_from_the_picker_focuses_begin",
                after is not null && after.Name.ToString() == "BeginButton",
                $"focus landed on '{after?.Name}' ({after?.GetType().Name})");

            instance.QueueFree();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            return;
        }

        Check("runsetup_picker_focuses_one_of_its_cards", false,
            "no seed in 1..60 offered a picker blessing - the pool may have lost them");
    }

    // D, Q, W and E open the four pile views, and RunSetupScreen puts a text
    // field on the same screen as the listener that owns them. What keeps a
    // typed "d" out of the deck overlay is Godot's input order: a focused
    // Control consumes the event in _GuiInput, and _UnhandledKeyInput only ever
    // sees what nothing consumed.
    //
    // So the property is "which handler the listener overrides", and that is
    // what this asserts. Overriding _Input instead - the obvious edit, since it
    // reads as "handle input" - would fire *before* the field ever sees the
    // key, and would do it silently: every existing screen would still work,
    // because none of them has a text field.
    private void TestPileKeysCannotReachAFocusedTextField()
    {
        var listener = typeof(DeckViewButtons).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "DeckViewKeybindListener");
        Check("deck_view_keybind_listener_exists", listener is not null,
            "renamed or removed - this assertion is now looking at nothing");
        if (listener is null) return;

        const System.Reflection.BindingFlags Declared =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;

        Check("pile_keys_are_read_from_unhandled_input",
            listener.GetMethod("_UnhandledKeyInput", Declared) is not null,
            "the listener no longer overrides _UnhandledKeyInput");
        Check("pile_keys_do_not_pre_empt_a_focused_control",
            listener.GetMethod("_Input", Declared) is null
            && listener.GetMethod("_GuiInput", Declared) is null,
            "the listener overrides _Input or _GuiInput, which run before a focused LineEdit");
    }

    // Which of the three tiles on screen is a blessing matching `predicate`.
    // Matched by the tile's own heading text rather than by re-running the
    // draw, so this reads what the player is actually looking at.
    private static int OfferedTileIndex(GridContainer grid, System.Func<BlessingDefinition, bool> predicate)
    {
        for (int i = 0; i < grid.GetChildCount(); i++)
        {
            var heading = grid.GetChild(i).GetChild(0).GetChild<Label>(0).Text;
            var blessing = BlessingDatabase.All.FirstOrDefault(b => b.Label == heading);
            if (blessing is not null && predicate(blessing)) return i;
        }
        return -1;
    }

    private async Task TestEventPickerTakesFocus(SceneTree tree)
    {
        using var saveGuard = RunSaveGuard.Protect();
        using var cutGuard = HardCutGuard.Protect();

        var packed = GD.Load<PackedScene>("res://scenes/EventScreen.tscn");

        // EventScreen rolls its own event in _Ready, so instantiate until one
        // carrying a picker comes up - the same approach EventSmokeTest takes,
        // and it terminates for the same reason (RngStreams.Shop advances).
        for (int attempt = 0; attempt < 80; attempt++)
        {
            RunState.Gold = 0;
            RunState.PlayerMaxHp = 50;
            RunState.PlayerCurrentHp = 50;
            RunState.Relics = new List<RelicInstance>();
            RunState.Potions = new List<PotionInstance>();
            // Deliberately more than the grid's 5 columns, so the picker wraps
            // to a second row and the scroll-into-view check below has
            // something below the fold to reach for. Three cards fit on one
            // row, which is exactly the case that hid the bug.
            RunState.Deck = new List<CardDefinition>
            {
                CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
                CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
                CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
            };

            var instance = packed.Instantiate();
            AddChild(instance);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var title = instance.GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel").Text;
            var definition = EventDatabase.All.FirstOrDefault(e => e.Title == title);
            int pickerChoice = definition?.Choices.FindIndex(
                c => c.Specs.Any(s => EventOutcomeRegistry.PickerFor(s) is not null)) ?? -1;

            if (pickerChoice < 0)
            {
                instance.QueueFree();
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                continue;
            }

            var choicesList = instance.GetNode<VBoxContainer>("CenterContainer/VBoxContainer/ChoicesList");
            var pickerList = instance.GetNode<GridContainer>(
                "PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");

            choicesList.GetChild<Button>(pickerChoice).EmitSignal(Button.SignalName.Pressed);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var focused = GetViewport().GuiGetFocusOwner();
            var columns = pickerList.GetChildren().SelectMany(c => c.GetChildren()).ToList();
            var pickerCards = columns.OfType<CardView>().ToList();
            var pickerButtons = columns.OfType<Button>().ToList();

            Check("event_picker_has_a_focus_owner", focused is not null,
                "nothing focused - the grid opened and the keyboard had nowhere to go");
            // The card, not the Choose button under it. Both used to be focus
            // stops, which cost two arrow presses per card to walk the grid and
            // left Enter-on-a-card doing nothing at all. CardPicker now makes
            // the card the only stop and subscribes its Clicked.
            Check("event_picker_focuses_one_of_its_cards",
                focused is CardView card && pickerCards.Contains(card),
                $"focus landed on '{focused?.Name}' ({focused?.GetType().Name})");
            Check("event_picker_buttons_are_not_focus_stops",
                pickerButtons.Count > 0 && pickerButtons.All(b => b.FocusMode == Control.FocusModeEnum.None),
                $"{pickerButtons.Count(b => b.FocusMode != Control.FocusModeEnum.None)} of {pickerButtons.Count} "
                + "buttons still take focus - arrow keys will alternate card/button down each column");

            // The grid was only walkable past row one with a mouse wheel: focus
            // moved onto a card below the fold and nothing scrolled to it. A
            // column is ~330px tall inside a 424px viewport, so row two was
            // effectively unreachable from the keyboard. ScreenKeyboardNav now
            // answers GuiFocusChanged by walking up to the nearest
            // ScrollContainer - which is why this asserts on scroll offset
            // rather than on any code the picker itself runs.
            var scroll = instance.GetNode<ScrollContainer>(
                "PickerCenterContainer/PickerVBox/ScrollContainer");
            var secondRow = pickerCards.Skip(pickerList.Columns).FirstOrDefault();

            Check("event_picker_wraps_to_a_second_row",
                secondRow is not null,
                $"only {pickerCards.Count} cards in a {pickerList.Columns}-column grid - "
                + "the scroll check below would pass vacuously");

            if (secondRow is not null)
            {
                int restingScroll = scroll.ScrollVertical;
                secondRow.GrabFocus();
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                Check("event_picker_scrolls_to_follow_keyboard_focus",
                    scroll.ScrollVertical > restingScroll,
                    $"scroll stayed at {scroll.ScrollVertical} after focusing a row-2 card - "
                    + "the grid is keyboard-unreachable below the fold");
            }

            instance.QueueFree();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            return;
        }

        Check("event_screen_rolls_a_picker_event_within_80_attempts", false,
            "no picker event rolled - are any still authored?");
    }

    // The rest site's Smith grid is the same CardPicker as above plus one
    // thing: a Cancel button under the ScrollContainer. That button is enough
    // to break the grid, and the way it broke is why this test walks the whole
    // thing instead of checking one step.
    //
    // Godot's directional focus search is a nearest-edge distance test over
    // everything focusable on screen. From row one it answered Down correctly,
    // because row two is partly visible under it. But focus moving there
    // scrolls the grid to show it (ScreenKeyboardNav.OnFocusChanged), and from
    // there on the next row is clipped entirely out of the viewport while
    // Cancel is sitting right below the fold - so Down went to Cancel, every
    // time, from row two onward. A player with a 21-card deck could reach two
    // rows of five and was then thrown out of the grid, which is exactly the
    // "it jumps to Cancel instead of scrolling" report. CardPicker now wires
    // the neighbours from the grid's own order, so the answer doesn't depend on
    // what happens to be on screen.
    //
    // Asserted through FindValidFocusNeighbor - the engine's own answer to
    // "where does Down go from here" - rather than by reading back the property
    // this test's subject just wrote, and with focus really moved (and a frame
    // given to the scroll) at each step so the geometry is the geometry a
    // player would be navigating.
    private async Task TestRestPickerGridNavigation(SceneTree tree)
    {
        using var saveGuard = RunSaveGuard.Protect();
        using var cutGuard = HardCutGuard.Protect();

        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 40;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        // 21 unupgraded cards against a 5-column grid: five rows, four of them
        // below the fold. A two-row deck passes this test even unfixed.
        RunState.Deck = Enumerable.Range(0, 7)
            .SelectMany(_ => new[] { CardDatabase.Get("strike"), CardDatabase.Get("defend"), CardDatabase.Get("bash") })
            .ToList();

        var instance = GD.Load<PackedScene>("res://scenes/RestScreen.tscn").Instantiate();
        AddChild(instance);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        instance.GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/SmithButton")
            .EmitSignal(Button.SignalName.Pressed);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var grid = instance.GetNode<GridContainer>(
            "UpgradeCenterContainer/UpgradeVBox/ScrollContainer/UpgradeList");
        var cancel = instance.GetNode<Button>("UpgradeCenterContainer/UpgradeVBox/CancelButton");
        var cards = grid.GetChildren().SelectMany(c => c.GetChildren()).OfType<CardView>().ToList();
        int columns = grid.Columns;
        int rows = (cards.Count + columns - 1) / columns;

        Check("rest_picker_fills_more_rows_than_fit_on_screen", rows >= 3,
            $"{cards.Count} cards in a {columns}-column grid is only {rows} row(s) - "
            + "the walk below would never scroll, which is the case that passes while broken");

        // Down the first column, one row per press, all the way out to Cancel.
        var expected = new List<Control>();
        for (int i = columns; i < cards.Count; i += columns) expected.Add(cards[i]);
        expected.Add(cancel);

        Control current = cards[0];
        ScreenKeyboardNav.GrabFocusQuietly(current);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var walked = new List<string>();
        bool onCourse = true;
        foreach (var want in expected)
        {
            var next = current.FindValidFocusNeighbor(Side.Bottom);
            walked.Add(next == cancel ? "Cancel" : next is CardView cv ? $"card{cards.IndexOf(cv)}" : $"'{next?.Name}'");
            if (!ReferenceEquals(next, want)) { onCourse = false; break; }

            current = next!;
            ScreenKeyboardNav.GrabFocusQuietly(current);
            // Two frames: one for the focus change to reach the ScrollContainer,
            // one for the scroll it performs to land in the layout.
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        Check("rest_picker_walks_down_every_row_before_reaching_cancel", onCourse,
            $"Down from card0 went {string.Join(" -> ", walked)}; expected one card per row then Cancel. "
            + "Landing on Cancel early is the grid dropping the player out instead of scrolling");

        Check("rest_picker_right_walks_the_grid",
            ReferenceEquals(cards[0].FindValidFocusNeighbor(Side.Right), cards[1]),
            $"Right from the first card went to '{cards[0].FindValidFocusNeighbor(Side.Right)?.Name}'");

        var back = cancel.FindValidFocusNeighbor(Side.Top);
        Check("rest_picker_up_from_cancel_returns_to_the_grid",
            back is CardView returned && cards.Contains(returned),
            $"Up from Cancel went to '{back?.Name}' ({back?.GetType().Name})");

        instance.QueueFree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    // --- 4. combat's own handler ------------------------------------------

    private async Task TestCombatKeyboard(SceneTree tree)
    {
        using var saveGuard = RunSaveGuard.Protect();
        using var cutGuard = HardCutGuard.Protect();

        RngStreams.Init(99);
        RunState.InitNewRun();
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("strike"),
            CardDatabase.Get("defend"), CardDatabase.Get("defend"),
            CardDatabase.Get("strike"), CardDatabase.Get("defend"),
        };
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get("fire_potion")) };

        CombatContext.EnemyDefinitionIds = new List<string> { "cultist", "cultist" };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = false;
        CombatContext.GoldReward = 10;

        var instance = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(instance);
        var screen = (CombatScreen)instance;
        var combat = instance.GetNode<CombatManager>("CombatManager");
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        Check("combat_starts_on_player_turn", combat.State == CombatState.PlayerTurn, $"state={combat.State}");

        // Selecting by slot, then confirming, plays the card - the two-press
        // sequence for a self-target card.
        int handBefore = combat.Player.Piles.Hand.Count;
        int slot = combat.Player.Piles.Hand.FindIndex(c => c.Definition.Target != CardTargetType.SingleEnemy);
        Check("opening_hand_has_a_non_targeted_card", slot >= 0, "no Skill in the opening hand to test with");
        if (slot >= 0)
        {
            Press(screen, $"hd_card_{slot + 1}");
            Press(screen, "hd_confirm");
            Check("number_key_then_confirm_plays_a_card",
                combat.Player.Piles.Hand.Count == handBefore - 1,
                $"hand went {handBefore} -> {combat.Player.Piles.Hand.Count}");
        }

        // A potion that needs a target enters AwaitingTarget, which used to be
        // reachable only by clicking an enemy - the keyboard could do nothing
        // there but cancel.
        var enemy = combat.Enemies[0];
        int enemyHpBefore = enemy.CurrentHp;
        combat.TryUsePotion(RunState.Potions[0]);
        Check("single_target_potion_enters_awaiting_target",
            combat.State == CombatState.AwaitingTarget, $"state={combat.State}");
        Check("awaiting_target_seeds_a_keyboard_target",
            instance.GetNode("EnemyRow").GetChildren().OfType<EnemyView>().Any(v => v.IsTargetLocked),
            "no enemy is glowing, so there is nothing to confirm");

        Press(screen, "hd_confirm");
        Check("confirm_resolves_the_potion_on_the_aimed_enemy",
            combat.Enemies.Any(e => e.CurrentHp < enemyHpBefore) || combat.Enemies.Count < 2,
            $"no enemy took the potion's damage (was {enemyHpBefore})");
        Check("potion_returns_to_player_turn",
            combat.State == CombatState.PlayerTurn, $"state={combat.State}");

        // Ending the turn from the keyboard, then the thing this whole change
        // started from: Continue at the end of a fight.
        await RunToCombatEnd(tree, combat);
        Check("combat_reaches_combat_end", combat.State == CombatState.CombatEnd, $"state={combat.State}");

        var continueButton = instance.GetNode<Button>("CombatEndPanel/ContinueButton");
        Check("combat_end_panel_is_showing",
            instance.GetNode<Control>("CombatEndPanel").Visible, "CombatEndPanel hidden at CombatEnd");
        Check("continue_button_names_its_key",
            continueButton.Text.Contains(ScreenKeyboardNav.KeyHint("hd_confirm")),
            $"text='{continueButton.Text}'");

        // Deliberately last, and with nothing awaited after it: this replaces
        // the tree's current scene - which is *this test* - so anything past
        // here would run on a detached node. One harmless "Parent node is busy
        // adding/removing children" engine error comes with it, the same
        // accepted quirk ActSmokeTest's boss-win test documents.
        Press(screen, "hd_confirm");
        Check("confirm_at_combat_end_presses_continue",
            RunManager.Instance.CurrentScreen == RunManager.ScreenState.Reward,
            $"screen={RunManager.Instance.CurrentScreen} - the fight would end mouse-only");
    }

    // Plays the fight out for real (the enemies are shortened, not skipped) so
    // the state machine reaches CombatEnd the way it does in a run.
    private async Task RunToCombatEnd(SceneTree tree, CombatManager combat)
    {
        foreach (var enemy in combat.Enemies) enemy.CurrentHp = 3;

        int guard = 0;
        while (combat.State != CombatState.CombatEnd && guard++ < 400)
        {
            if (combat.State == CombatState.PlayerTurn)
            {
                var target = combat.Enemies.FirstOrDefault(e => !e.IsDead);
                var card = combat.Player.Piles.Hand.FirstOrDefault(c =>
                    c.Definition.Target == CardTargetType.SingleEnemy &&
                    c.Definition.Cost <= combat.Player.CurrentEnergy);
                if (card is not null && target is not null) combat.TryPlayCard(card, target);
                else combat.TryEndTurn();
            }
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    // An InputEventAction matches IsActionPressed(name) without going near the
    // OS keyboard, which headless has none of.
    private static void Press(CombatScreen screen, string action) =>
        screen._UnhandledInput(new InputEventAction { Action = action, Pressed = true });
}
