using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Coverage for the target-lock glow toggle EnemyView exposes for the
// card-drag path (Phase 5's "unify/justify split targeting model" item).
// Doesn't simulate raw InputEventMouseMotion through Godot's input
// pipeline (nothing in this codebase's smoke tests does that) - instead
// asserts the resulting stylebox-override state a real drag would produce,
// matching the existing smoke-test convention. Run via
// `godot --headless scenes/debug/CombatTargetingSmokeTest.tscn`.
public partial class CombatTargetingSmokeTest : Node
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

        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Deck = new List<CardDefinition>(CardDatabase.All);
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        CombatContext.EnemyDefinitionIds = new List<string> { "cultist", "cultist" };
        CombatContext.IsBoss = false;

        var packed = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var enemyRow = instance.GetNode("EnemyRow");
        Check("two_enemies_present", enemyRow.GetChildCount() == 2, $"got {enemyRow.GetChildCount()}");

        var enemyA = enemyRow.GetChild<EnemyView>(0);
        var enemyB = enemyRow.GetChild<EnemyView>(1);

        Check("starts_unlocked", !enemyA.IsTargetLocked, "expected unlocked before locking");

        enemyA.SetTargetLocked(true);
        Check("lock_sets_lock_stylebox", enemyA.IsTargetLocked, "expected lock stylebox after locking");
        Check("locking_one_enemy_does_not_affect_the_other", !enemyB.IsTargetLocked,
            "expected enemyB to be unaffected");

        enemyA.SetTargetLocked(false);
        Check("unlock_restores_empty_stylebox", !enemyA.IsTargetLocked,
            "expected override removed after unlocking");

        instance.QueueFree();

        await TestHudNeverPaintsOverAnEnemy();
        await TestIntentTooltipStaysOffTheHand(2);
        await TestIntentTooltipStaysOffTheHand(4);

        // The drag/targeting layer itself - CLAUDE.md risk 5, and until now
        // the thinnest coverage in the repo. Everything above drives
        // EnemyView.SetTargetLocked directly or measures layout; every other
        // combat suite calls CombatManager.TryPlayCard, which is the layer
        // *below* the one carrying the risk. These drive CardView.
        await TestRejectedDropReturnsTheCardToTheHand();
        await TestSuccessfulPlayReparentsBeforeResolving();
        await TestPlayRejectionGatesLeaveTheHandUntouched();
        await TestExitTreeClearsTheGlow();
        await TestDescriptionChangesAgainstAVulnerableTarget();
        await TestCancelTargetingRestoresACleanBoard();
        await TestClickingAnEnemyResolvesAnAimedPotion();
        await TestHitTestSkipsCorpsesAndIgnoresUntargetedCards();

        GD.Print($"CombatTargetingSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // The glow is EnemyView's own Button background, so anything drawn over
    // the enemy erases it - and the stylebox assertions above all still pass
    // while that happens, which is exactly how this shipped.
    //
    // The real bug: TopLeftColumn is declared after EnemyRow (so it paints on
    // top) and its relic bar grew rightward at 48px per relic, reaching the
    // leftmost enemy of a 3-enemy fight from 5 relics on. Worst case is
    // therefore the widest encounter and a late-run relic count, which is what
    // this builds.
    private async System.Threading.Tasks.Task TestHudNeverPaintsOverAnEnemy()
    {
        RunState.Relics = new List<RelicInstance>();
        foreach (var definition in RelicDatabase.All)
        {
            RunState.Relics.Add(new RelicInstance(definition));
            if (RunState.Relics.Count == 8) break;
        }
        Check("worst_case_relic_count_available", RunState.Relics.Count == 8,
            $"only {RunState.Relics.Count} relics in the database");

        CombatContext.EnemyDefinitionIds = new List<string> { "cultist", "cultist", "cultist" };
        var packed = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var screen = (Control)instance;
        var enemyRow = instance.GetNode<Control>("EnemyRow");
        var topLeft = instance.GetNode<Control>("TopLeftColumn");

        // Containers lay out on a deferred pass, so the rects are only real
        // after a frame - the same wait DeckViewSmokeTest uses before it
        // measures anything.
        screen.Size = new Vector2(1152, 648);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("three_enemies_present", enemyRow.GetChildCount() == 3, $"got {enemyRow.GetChildCount()}");

        foreach (var child in enemyRow.GetChildren())
        {
            if (child is not EnemyView enemy) continue;
            foreach (var painted in HudRects(topLeft))
            {
                Check($"hud_clear_of_{enemy.Combatant.Definition.Id}_{painted.Name}",
                    !painted.Rect.Intersects(enemy.GetGlobalRect()),
                    $"{painted.Name} at {painted.Rect} overlaps an enemy at {enemy.GetGlobalRect()} - " +
                    "the target-lock glow is that enemy's own background and would be painted over");
            }
        }

        instance.QueueFree();
    }

    // Locking a target raises the intent's hover panel, and that panel has to
    // land somewhere that isn't the hand. It didn't: HoverTooltip's card
    // placement is "above the anchor, flipping below when it doesn't fit", and
    // an EnemyView is 220x300 with its intent row on the top edge, so it always
    // flipped - onto the fanned cards the player is reading it in order to
    // choose between. Now it goes beside the enemy, outward from the middle of
    // the screen.
    //
    // Run at both the narrowest and the widest encounter: four enemies is where
    // the outermost one has the least room to its outside, and therefore where
    // the clamp is most likely to push the panel back over the row.
    private async System.Threading.Tasks.Task TestIntentTooltipStaysOffTheHand(int enemyCount)
    {
        RunState.Relics = new List<RelicInstance>();
        CombatContext.EnemyDefinitionIds = Enumerable.Repeat("cultist", enemyCount).ToList();

        var instance = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(instance);
        var screen = (Control)instance;
        screen.Size = new Vector2(1152, 648);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var handArea = instance.GetNode<Control>("HandArea").GetGlobalRect();
        var viewport = screen.GetViewportRect();

        foreach (var child in instance.GetNode<Control>("EnemyRow").GetChildren())
        {
            if (child is not EnemyView enemy) continue;

            enemy.SetTargetLocked(true);
            // One frame for the panel's own layout pass to give it a real Size,
            // one more for the _Process that positions it from that Size.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // Under the current scene, not under the combat screen: a tooltip
            // parents itself to the scene root so a ScrollContainer or clipping
            // panel between it and its anchor can't clip it. In the real game
            // those are the same node; in here the current scene is this test.
            var tooltip = GetTree().CurrentScene.GetChildren().OfType<HoverTooltip>().FirstOrDefault();
            string who = $"{enemyCount}_enemies_{enemy.GetIndex()}";
            Check($"intent_tooltip_appears_{who}", tooltip is not null,
                "target-locking an enemy raised no intent panel at all");

            if (tooltip is not null)
            {
                var rect = tooltip.GetGlobalRect();
                var enemyRect = enemy.GetGlobalRect();

                Check($"intent_tooltip_clear_of_the_hand_{who}", !rect.Intersects(handArea),
                    $"panel at {rect} overlaps the hand at {handArea}");

                // The load-bearing one. "Clear of the hand" alone passes even
                // with the old below-the-enemy placement, because a two-box
                // panel happens to fit in the gap between the enemy row and the
                // hand - and then a three-box one doesn't, which is what the
                // player actually saw. Staying level with the enemy is the
                // property that holds however tall the panel gets.
                Check($"intent_tooltip_sits_beside_its_enemy_{who}",
                    rect.Position.Y < enemyRect.End.Y,
                    $"panel at {rect} starts below the enemy at {enemyRect} - it is under the creature, "
                    + "in the strip the hand grows into, not beside it");

                Check($"intent_tooltip_stays_on_screen_{who}", viewport.Encloses(rect),
                    $"panel at {rect} runs outside the {viewport.Size} viewport");
            }

            enemy.SetTargetLocked(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        instance.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // ------------------------------------------------------- the drag layer

    // Builds a real CombatScreen with a known hand: RunState.Deck is set to
    // `handSize` copies of one card, so the opening draw is deterministic and
    // a test can say "the first card in the hand is a Strike" without reaching
    // into the shuffle.
    private async System.Threading.Tasks.Task<(Node Screen, CombatManager Combat, Control HandArea)>
        StartFight(string cardId, int enemies = 2, int handSize = 5)
    {
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();
        RunState.PlayerCurrentHp = RunState.PlayerMaxHp = 50;
        RunState.Deck = Enumerable.Repeat(CardDatabase.Get(cardId), handSize).ToList();
        CombatContext.EnemyDefinitionIds = Enumerable.Repeat("cultist", enemies).ToList();
        CombatContext.IsBoss = false;

        var screen = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(screen);
        ((Control)screen).Size = new Vector2(1152, 648);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        return (screen, CombatManager.Instance!, screen.GetNode<Control>("HandArea"));
    }

    private async System.Threading.Tasks.Task EndFight(Node screen)
    {
        screen.QueueFree();
        // Two frames so the freed EnemyViews actually run _ExitTree and drop
        // out of EnemyView.Instances before the next test looks at it.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static CardView FirstCard(Control handArea) =>
        handArea.GetChildren().OfType<CardView>().First();

    private static T Private<T>(object target, string field) =>
        (T)target.GetType()
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(target)!;

    private static void Invoke(object target, string method) =>
        target.GetType()
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(target, null);

    // The highest-value check in the set, because the failure is silent and
    // permanent. TryPlayFromHand reparents out of the hand area *before*
    // asking CombatManager whether the play is legal; if the play is refused,
    // the reparent has to be undone. Nothing checked that it was, and a card
    // left under CurrentScene is invisible to RefreshHand forever - it is
    // still in Piles.Hand, so the player is holding a card they can never see
    // or play again.
    private async System.Threading.Tasks.Task TestRejectedDropReturnsTheCardToTheHand()
    {
        var (screen, combat, handArea) = await StartFight("strike");

        var card = FirstCard(handArea);
        var home = card.Position;
        int handBefore = combat.Player.Piles.Hand.Count;
        int energyBefore = combat.Player.CurrentEnergy;

        // A SingleEnemy card dropped on empty space - the everyday miss.
        bool played = card.TryPlayFromHand(null);

        Check("rejected_drop_reports_failure", !played, "TryPlayFromHand(null) claimed a Strike resolved");
        Check("rejected_drop_reparents_back_under_the_hand", card.GetParent() == handArea,
            $"card is parented to {card.GetParent()?.Name} - RefreshHand only tears down what is under "
            + "HandArea, so this card is now invisible and unplayable for the rest of the fight");
        Check("rejected_drop_restores_the_home_position", card.Position.IsEqualApprox(home),
            $"at {card.Position}, home is {home}");
        Check("rejected_drop_spends_no_energy", combat.Player.CurrentEnergy == energyBefore,
            $"{combat.Player.CurrentEnergy} vs {energyBefore}");
        Check("rejected_drop_leaves_the_card_in_hand", combat.Player.Piles.Hand.Count == handBefore,
            $"{combat.Player.Piles.Hand.Count} vs {handBefore}");

        await EndFight(screen);
    }

    // The other side of the same reparent. A successful play has to leave the
    // node under CurrentScene with _leavingHand set, which is what lets
    // PlayResolveTween run at all - the comment at CardView.TryPlayFromHand
    // exists because the alternative is the card animating as *discarded*,
    // and that bug is invisible unless you are watching for it.
    private async System.Threading.Tasks.Task TestSuccessfulPlayReparentsBeforeResolving()
    {
        var (screen, combat, handArea) = await StartFight("strike");

        var card = FirstCard(handArea);
        var enemy = combat.Enemies[0];
        int hpBefore = enemy.CurrentHp;
        int energyBefore = combat.Player.CurrentEnergy;

        bool played = card.TryPlayFromHand(enemy);

        Check("successful_play_reports_success", played, "TryPlayFromHand refused a legal Strike");
        Check("successful_play_reparents_out_of_the_hand", card.GetParent() != handArea,
            "card is still under HandArea - RefreshHand will free it mid-tween and it will fly to the "
            + "discard counter as though it had been discarded");
        Check("successful_play_parents_to_the_scene_root", card.GetParent() == GetTree().CurrentScene,
            $"parented to {card.GetParent()?.Name}");
        Check("successful_play_sets_leaving_hand", Private<bool>(card, "_leavingHand"),
            "_leavingHand is false, so a mouse-exit or SelectCard(null) can start a competing "
            + "scale tween against PlayResolveTween");
        Check("successful_play_actually_damaged_the_target", enemy.CurrentHp < hpBefore,
            $"{enemy.CurrentHp} vs {hpBefore}");
        Check("successful_play_spent_energy", combat.Player.CurrentEnergy < energyBefore,
            $"{combat.Player.CurrentEnergy} vs {energyBefore}");

        await EndFight(screen);
    }

    // TryPlayCard's four rejection gates, asserted as a table and - the part
    // that was missing - asserting the hand is *unchanged* in all four. Only
    // the null-target case was incidentally covered anywhere, and "returns
    // false" is the cheap half of the contract; "changes nothing" is the half
    // a player notices.
    private async System.Threading.Tasks.Task TestPlayRejectionGatesLeaveTheHandUntouched()
    {
        var (screen, combat, handArea) = await StartFight("strike");
        var card = FirstCard(handArea).CardInstance!;
        var enemy = combat.Enemies[0];

        // Gate 1: not the player's turn. AwaitingTarget is the non-PlayerTurn
        // state reachable synchronously - ending the turn would hand control
        // to the async enemy loop and make the assertion a race.
        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get("fire_potion")) };
        combat.TryUsePotion(RunState.Potions[0]);
        Check("gate_wrong_state_is_set_up", combat.State == CombatState.AwaitingTarget,
            $"expected AwaitingTarget, got {combat.State}");
        AssertGateChangesNothing("wrong_state", combat, () => combat.TryPlayCard(card, enemy));
        combat.CancelTargeting();

        // Gate 2: not enough energy.
        combat.Player.CurrentEnergy = 0;
        AssertGateChangesNothing("no_energy", combat, () => combat.TryPlayCard(card, enemy));
        combat.Player.CurrentEnergy = combat.Player.MaxEnergy;

        // Gate 3: a SingleEnemy card with nothing under the cursor.
        AssertGateChangesNothing("null_target", combat, () => combat.TryPlayCard(card, null));

        // Gate 4: an unplayable card. Checked through the drag path rather
        // than only through CardKeywordSmokeTest's direct call, because this
        // is the layer a player actually reaches it from - a Curse in hand is
        // draggable, and the rejection has to snap it back like any other.
        var curse = new CardInstance(CardDatabase.Get("pain"));
        combat.Player.Piles.Hand.Add(curse);
        AssertGateChangesNothing("unplayable", combat, () => combat.TryPlayCard(curse, enemy));
        combat.Player.Piles.Hand.Remove(curse);

        await EndFight(screen);
    }

    private void AssertGateChangesNothing(string gate, CombatManager combat, Func<bool> play)
    {
        int energy = combat.Player.CurrentEnergy;
        var hand = combat.Player.Piles.Hand.ToList();
        var hp = combat.Enemies.Select(e => e.CurrentHp).ToList();

        Check($"gate_{gate}_refuses_the_play", !play(), "TryPlayCard returned true");
        Check($"gate_{gate}_spends_no_energy", combat.Player.CurrentEnergy == energy,
            $"{combat.Player.CurrentEnergy} vs {energy}");
        Check($"gate_{gate}_leaves_the_hand_identical", combat.Player.Piles.Hand.SequenceEqual(hand),
            $"hand went from {hand.Count} to {combat.Player.Piles.Hand.Count} cards");
        Check($"gate_{gate}_deals_no_damage",
            combat.Enemies.Select(e => e.CurrentHp).SequenceEqual(hp), "an enemy lost HP");
    }

    // A card freed mid-drag must take its glow with it, or the enemy keeps a
    // target lock nobody is aiming at for the rest of the fight. _ExitTree is
    // the only thing standing between that and the player.
    private async System.Threading.Tasks.Task TestExitTreeClearsTheGlow()
    {
        var (screen, combat, handArea) = await StartFight("strike");

        var card = FirstCard(handArea);
        var enemyView = screen.GetNode("EnemyRow").GetChild<EnemyView>(0);

        // Put the card in the state a drag over that enemy would leave it in,
        // without needing a mouse: the field UpdateTargetHighlight would have
        // set, plus the glow it would have painted.
        typeof(CardView)
            .GetField("_targetLockedView",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(card, enemyView);
        enemyView.SetTargetLocked(true);
        Check("glow_is_set_up_before_the_card_leaves", enemyView.IsTargetLocked, "enemy never locked");

        handArea.RemoveChild(card);

        Check("exit_tree_clears_the_enemy_glow", !enemyView.IsTargetLocked,
            "the enemy is still lit after the card left the tree - nothing is aiming at it");
        Check("exit_tree_forgets_the_locked_view",
            Private<EnemyView?>(card, "_targetLockedView") is null, "_targetLockedView still set");

        card.QueueFree();
        await EndFight(screen);
    }

    // The "drag over a target and see the real number" promise. Pure string
    // comparison, and it is the only assertion in the repo that the live
    // preview responds to the target at all.
    private async System.Threading.Tasks.Task TestDescriptionChangesAgainstAVulnerableTarget()
    {
        var (screen, combat, handArea) = await StartFight("strike");

        var card = FirstCard(handArea);
        var label = card.GetNode<RichTextLabel>("VBox/DescriptionPanel/DescriptionLabel");
        var enemy = combat.Enemies[0];

        card.RefreshDescriptionForTarget(null);
        string untargeted = label.Text;

        enemy.AddStatus(StatusType.Vulnerable, 2);
        card.RefreshDescriptionForTarget(enemy);
        string vsVulnerable = label.Text;

        Check("description_is_not_empty", untargeted.Length > 0, "the card renders no rules text at all");
        Check("description_changes_against_a_vulnerable_target", vsVulnerable != untargeted,
            $"'{vsVulnerable}' is identical with and without Vulnerable on the target - the number the "
            + "player is shown while aiming is not the number that will land");

        card.RefreshDescriptionForTarget(null);
        Check("description_reverts_when_the_target_is_dropped", label.Text == untargeted,
            $"'{label.Text}' vs '{untargeted}'");

        await EndFight(screen);
    }

    // Aiming a potion and thinking better of it has to put the board back
    // exactly as it was. RefreshStateUi's clear-on-exit had no test, and the
    // expensive half of the failure is the potion: a cancel that consumed it
    // is a lost item with no undo.
    private async System.Threading.Tasks.Task TestCancelTargetingRestoresACleanBoard()
    {
        var (screen, combat, handArea) = await StartFight("strike");
        var hint = screen.GetNode<Label>("TargetHintLabel");

        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get("fire_potion")) };
        var potion = RunState.Potions[0];

        bool resolved = combat.TryUsePotion(potion);
        Check("aiming_a_potion_does_not_resolve_it", !resolved, "a SingleEnemy potion resolved with no target");
        Check("aiming_a_potion_enters_awaiting_target", combat.State == CombatState.AwaitingTarget,
            $"state is {combat.State}");
        Check("aiming_a_potion_shows_the_target_hint", hint.Visible, "TargetHintLabel stayed hidden");

        combat.CancelTargeting();

        Check("cancel_returns_to_the_player_turn", combat.State == CombatState.PlayerTurn,
            $"state is {combat.State}");
        Check("cancel_keeps_the_potion", RunState.Potions.Contains(potion),
            "the potion was consumed by cancelling out of aiming it");
        Check("cancel_hides_the_target_hint", !hint.Visible, "TargetHintLabel is still up");
        Check("cancel_leaves_no_enemy_locked",
            screen.GetNode("EnemyRow").GetChildren().OfType<EnemyView>().All(e => !e.IsTargetLocked),
            "an enemy is still lit after cancelling");

        await EndFight(screen);
    }

    // The mouse half of AwaitingTarget. EnemyView.OnPressed is private and
    // wired to Button.Pressed, so emitting the signal is the honest way in -
    // it is exactly what a click does. KeyboardSmokeTest covers the other half.
    private async System.Threading.Tasks.Task TestClickingAnEnemyResolvesAnAimedPotion()
    {
        var (screen, combat, handArea) = await StartFight("strike");

        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get("fire_potion")) };
        var potion = RunState.Potions[0];
        combat.TryUsePotion(potion);

        var views = screen.GetNode("EnemyRow").GetChildren().OfType<EnemyView>().ToList();
        var target = views[1].Combatant;
        var bystander = views[0].Combatant;
        int targetHp = target.CurrentHp;
        int bystanderHp = bystander.CurrentHp;

        views[1].EmitSignal(Button.SignalName.Pressed);

        Check("clicking_an_enemy_resolves_the_potion", target.CurrentHp < targetHp,
            $"{target.CurrentHp} vs {targetHp} - the click did not land the potion");
        Check("clicking_an_enemy_spares_the_others", bystander.CurrentHp == bystanderHp,
            $"{bystander.CurrentHp} vs {bystanderHp}");
        Check("resolving_a_potion_returns_to_the_player_turn", combat.State == CombatState.PlayerTurn,
            $"state is {combat.State}");
        Check("resolving_a_potion_consumes_it", !RunState.Potions.Contains(potion),
            "the potion is still in the belt after being used");

        await EndFight(screen);
    }

    // FindEnemyViewUnderMouse and UpdateTargetHighlight, the two functions the
    // drag path is built on and neither of which appeared in any test.
    //
    // Built standalone rather than through CombatScreen because a headless
    // Godot pins the mouse at (0,0) and ignores both Viewport.WarpMouse and
    // Input.WarpMouse - measured, not assumed. So instead of moving the mouse
    // to the enemies, the enemies are placed over the mouse. The hit test is
    // `rect.HasPoint(mousePos)` either way, and this has the side benefit of
    // controlling the order of EnemyView.Instances outright, which is the
    // whole point of the corpse check.
    private async System.Threading.Tasks.Task TestHitTestSkipsCorpsesAndIgnoresUntargetedCards()
    {
        Check("enemy_instances_start_clean", EnemyView.Instances.Count == 0,
            $"{EnemyView.Instances.Count} EnemyViews leaked from an earlier test - the hit-test order "
            + "below would be measuring them instead");

        // The corpse goes in first deliberately: it is first in Instances, so
        // it wins the naive hit test. That is the bug this guards, and it has
        // shipped once already.
        var corpse = SpawnEnemyOverTheMouse("cultist", alive: false);
        var live = SpawnEnemyOverTheMouse("cultist", alive: true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var strike = SpawnLooseCard("strike");
        var block = SpawnLooseCard("defend");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var found = strike.GetType()
            .GetMethod("FindEnemyViewUnderMouse",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(strike, null);

        Check("hit_test_finds_an_enemy_under_the_mouse", found is not null,
            "no enemy matched - the setup is wrong, not the code under test");
        Check("hit_test_skips_the_corpse", ReferenceEquals(found, live),
            "a dead enemy won the hit test; a drag over it locks a target that is fading out, which "
            + "reads to the player as no glow at all");

        // A Self-targeted card must not light anything up, and the Strike
        // beside it must - without that control this passes trivially.
        Invoke(strike, "UpdateTargetHighlight");
        Check("drag_of_a_single_enemy_card_locks_the_target", live.IsTargetLocked,
            "a Strike dragged over a live enemy lit nothing");

        Invoke(block, "UpdateTargetHighlight");
        Check("drag_of_a_self_card_locks_nothing",
            Private<EnemyView?>(block, "_targetLockedView") is null,
            "a Self-targeted Defend locked an enemy it can never hit");

        Invoke(strike, "ClearTargetHighlight");
        Check("clearing_the_highlight_unlocks_the_enemy", !live.IsTargetLocked, "enemy still lit");

        // Kill the survivor too: with every candidate dead the hit test has to
        // come back empty rather than falling back to the nearest corpse.
        live.Combatant.CurrentHp = 0;
        var afterAllDead = strike.GetType()
            .GetMethod("FindEnemyViewUnderMouse",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(strike, null);
        Check("hit_test_returns_nothing_when_every_candidate_is_dead", afterAllDead is null,
            "a corpse was returned once nothing was alive");

        foreach (var node in new Node[] { strike, block, corpse, live }) node.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private EnemyView SpawnEnemyOverTheMouse(string enemyId, bool alive)
    {
        var view = GD.Load<PackedScene>("res://scenes/EnemyView.tscn").Instantiate<EnemyView>();
        view.Combatant = EnemyFactory.Create(EnemyDatabase.Get(enemyId));
        if (!alive) view.Combatant.CurrentHp = 0;
        AddChild(view);
        // Straddle the origin, where the headless mouse sits.
        view.GlobalPosition = new Vector2(-40, -40);
        return view;
    }

    private CardView SpawnLooseCard(string cardId)
    {
        var card = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(card);
        card.SetCardInstance(new CardInstance(CardDatabase.Get(cardId)));
        return card;
    }

    // Every descendant of TopLeftColumn that actually draws something. The
    // column itself is transparent, so its own rect is not the thing to check.
    private static IEnumerable<(string Name, Rect2 Rect)> HudRects(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is not Control control) continue;
            if (control is PanelContainer or Label) yield return (control.Name, control.GetGlobalRect());
            foreach (var nested in HudRects(control)) yield return nested;
        }
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _pass++;
            GD.Print($"PASS {name}");
        }
        else
        {
            _fail++;
            GD.Print($"FAIL {name}: {detail}");
        }
    }
}
