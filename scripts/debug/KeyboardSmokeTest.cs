using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
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

        TestInputMapLayer();
        TestFocusModeSplit();
        await TestEveryNonCombatScreenTakesFocus(tree);
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
        RewardContext.GuaranteedRelic = null;
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
            ("res://scenes/RewardScreen.tscn", "a card choice", (f, _) => f is CardView),
            ("res://scenes/ShopScreen.tscn", "an affordable Buy button",
                (f, _) => f is Button { Disabled: false } b && b.Text.StartsWith("Buy")),
            ("res://scenes/RestScreen.tscn", "HealButton", (f, _) => f.Name.ToString() == "HealButton"),
            ("res://scenes/TreasureScreen.tscn", "ContinueButton", (f, _) => f.Name.ToString() == "ContinueButton"),
            ("res://scenes/EventScreen.tscn", "an event choice",
                (f, screen) => f.GetParent() == screen.GetNode("CenterContainer/VBoxContainer/ChoicesList")),
            ("res://scenes/RunEndScreen.tscn", "RestartButton", (f, _) => f.Name.ToString() == "RestartButton"),
            ("res://scenes/MetaProgressionScreen.tscn", "BackButton", (f, _) => f.Name.ToString() == "BackButton"),
            ("res://scenes/SettingsScreen.tscn", "VolumeSlider", (f, _) => f.Name.ToString() == "VolumeSlider"),
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
