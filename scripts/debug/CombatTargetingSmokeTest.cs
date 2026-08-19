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
        AscensionDatabase.LoadAll();
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
        Check("lock_attaches_exactly_one_glow_ring",
            enemyA.GetChildren().OfType<GlowRing>().Count() == 1,
            $"a locked EnemyView carries {enemyA.GetChildren().OfType<GlowRing>().Count()} GlowRing(s), " +
            "expected exactly one - the lock is an animated ring now, not a static box");
        Check("locking_one_enemy_does_not_affect_the_other", !enemyB.IsTargetLocked,
            "expected enemyB to be unaffected");

        enemyA.SetTargetLocked(false);
        Check("unlock_restores_empty_stylebox", !enemyA.IsTargetLocked,
            "expected override removed after unlocking");

        // The lock is a running GlowRing now, so unlocking has to stop a driver
        // and not just install a box over it: Godot runs a parent before its
        // children, so a ring still parented here ticks *after* the unlock in
        // the same frame, and if that tick is the one that crosses
        // GlowRing.FrameSeconds it repaints "normal" over the StyleBoxEmpty just
        // installed. IsTargetLocked is defined as "not StyleBoxEmpty", so the
        // result is an enemy that reports itself targeted with nothing aimed at
        // it - intermittently, about one unlock in fourteen at 60fps, which is
        // the worst frequency a bug can have.
        //
        // Asserted structurally rather than by ticking, and that distinction
        // cost a mutation test: the first version of this check waited three
        // frames and then re-read IsTargetLocked, which cannot fail either way,
        // because three frames is 48ms against a 220ms frame time. What
        // separates a real Stop from a bare QueueFree is observable *now* -
        // QueueFree defers to the end of the frame, so the child is still
        // attached when this line runs.
        Check("unlock_detaches_the_glow_ring",
            enemyA.GetChildren().OfType<GlowRing>().Count() == 0,
            "a GlowRing is still parented to an unlocked EnemyView - GlowRing.Stop has to " +
            "detach now rather than defer, or the driver gets one more tick after the unlock");

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
        await TestInspectPeekOpensAndCloses();
        await TestSelectingACardDoesNotOpenAPeek();
        await TestAPreemptedCardCannotCloseTheNewPeek();
        await TestClickingAnEnemyResolvesAnAimedPotion();
        await TestHitTestSkipsCorpsesAndIgnoresUntargetedCards();
        await TestASummonBuildsAnEnemyViewMidFight();
        TestTheBlockedBeatNeedsAnAbsorbedHit();

        GD.Print($"CombatTargetingSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // The gate on the "Blocked!" beat and its ward burst.
    //
    // Both combatants clear their own Block at the top of their turn, so an
    // expiry and an absorbed hit move HP and Block identically - which is why
    // this gated on `blockDelta < 0 && hpDelta == 0` for several phases and
    // fired every turn either side had leftover Block and nothing had attacked.
    // Small enough to miss as a text pop; not small at all once it became a
    // 160x160 ward burst on the player sprite.
    //
    // Asserted against CombatScreen.IsAbsorbedHit rather than by driving a real
    // fight, because reaching PopupDelta needs turns to pass and the enemy turn
    // is wall-clock paced - the same reason BalanceModel is static analysis
    // rather than a simulator. The rule is the part that was wrong.
    private void TestTheBlockedBeatNeedsAnAbsorbedHit()
    {
        Check("blocked_beat_fires_on_an_absorbed_hit",
            CombatScreen.IsAbsorbedHit(hpDelta: 0, absorbedDelta: 1),
            "a hit Block ate whole must play the blocked beat");

        Check("blocked_beat_ignores_expiring_block",
            !CombatScreen.IsAbsorbedHit(hpDelta: 0, absorbedDelta: 0),
            "Block falling with nothing absorbed is a turn boundary, not a blocked hit - " +
            "gating on the Block delta fires this beat every turn either side carried Block");

        Check("blocked_beat_ignores_a_hit_that_got_through",
            !CombatScreen.IsAbsorbedHit(hpDelta: -3, absorbedDelta: 1),
            "Block that absorbed part of a hit still lost HP, which is the ordinary hit " +
            "reaction - two beats for one hit otherwise");
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
    //
    // The widest encounter is four since summons: three is what the encounter
    // *table* fields, but a summon takes a three-enemy group to
    // CombatManager.MaxEnemies. EnemyRow's own band did not move for that
    // (still x 176-976); FitEnemiesToTheRow narrows the columns inside it
    // instead, which packs the leftmost enemy tighter against the relic bar
    // this is measuring. Built from four rather than three for exactly that
    // reason; a summon is not reproducible from a static encounter id, so the
    // group is authored at the cap directly.
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

        CombatContext.EnemyDefinitionIds =
            Enumerable.Repeat("cultist", CombatManager.MaxEnemies).ToList();
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

        Check("a_full_roster_is_present", enemyRow.GetChildCount() == CombatManager.MaxEnemies,
            $"got {enemyRow.GetChildCount()}");

        // The row has to actually hold them, not overflow. An HBoxContainer
        // does not shrink a child below its custom_minimum_size - it runs past
        // its own rect instead - so four 220px enemies in an 800px band would
        // have looked fine in every assertion below while the rightmost one
        // hung outside the band entirely.
        var rowRect = enemyRow.GetGlobalRect();
        int index = 0;
        foreach (var child in enemyRow.GetChildren())
        {
            if (child is not EnemyView enemy) continue;
            Check($"enemy_{index}_fits_inside_the_row",
                rowRect.Encloses(enemy.GetGlobalRect()),
                $"enemy at {enemy.GetGlobalRect()} is not inside EnemyRow at {rowRect}");
            foreach (var painted in HudRects(topLeft))
            {
                Check($"hud_clear_of_enemy_{index}_{painted.Name}",
                    !painted.Rect.Intersects(enemy.GetGlobalRect()),
                    $"{painted.Name} at {painted.Rect} overlaps an enemy at {enemy.GetGlobalRect()} - " +
                    "the target-lock glow is that enemy's own background and would be painted over");
            }
            index++;
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

    // Card inspect, driven through the same CardView entry point both the mouse
    // dwell and the held hd_inspect land on.
    //
    // The keyword tooltip half is asserted with it rather than separately,
    // because the two are one rule: HoverTooltip sits at ZIndex 2500 and the
    // peek at 2200, so a keyword box left up while the peek is open paints on
    // top of the card it is quoting.
    private async System.Threading.Tasks.Task TestInspectPeekOpensAndCloses()
    {
        // **Bash, not Strike**, and that is the whole assertion below working.
        // Strike reads "Deal 6 damage." and mentions no keyword, so
        // ShowKeywordTooltip returns before assigning and _keywordTooltip is
        // null whether or not the peek hides anything. Measured: with Strike,
        // deleting the `if (_inspecting) HideKeywordTooltip()` arm this check is
        // named after left the suite at 147 passed, 0 failed. Bash applies
        // Vulnerable, so the panel is really up and really has to go.
        var (screen, _, handArea) = await StartFight("bash");
        var card = FirstCard(handArea);

        Check("no_peek_before_inspect", !CardInspectView.IsOpen, "a peek was already open");

        card.SetHighlighted(true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // The transition, not the end state. Asserting only that the panel is
        // down while the peek is up passes for free on any card that never
        // raised one.
        Check("a_keyword_card_raises_its_panel_when_looked_at",
            Private<HoverTooltip?>(card, "_keywordTooltip") is not null,
            "Bash mentions Vulnerable but raised no keyword panel - this fixture can no longer " +
            "tell whether the peek hides one");

        card.BeginInspect();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("inspect_opens_a_peek", CardInspectView.IsOpen, "BeginInspect raised nothing");
        Check("the_peek_hides_the_keyword_panel",
            Private<HoverTooltip?>(card, "_keywordTooltip") is null,
            "the keyword panel is still up under the peek");

        card.EndInspect();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("release_closes_the_peek", !CardInspectView.IsOpen, "the peek outlived the hold");

        await EndFight(screen);
    }

    // The two input paths can be on different cards at once, and a Show that
    // replaced one peek with another used to leave the old raiser believing it
    // still owned one - so that card's own mouse-exit killed the *new* card's
    // peek while its key was still held, unrecoverably, since IsActionPressed
    // fires on the press edge only.
    private async System.Threading.Tasks.Task TestAPreemptedCardCannotCloseTheNewPeek()
    {
        // Bash again, so the keyword-panel half below has a panel to be about.
        var (screen, _, handArea) = await StartFight("bash");
        var cards = handArea.GetChildren().OfType<CardView>().ToList();
        var a = cards[0];
        var b = cards[1];

        // A is hovered as well as inspecting, which is the state the mouse
        // leaves a card in - and the only state in which the panel returning is
        // observable at all.
        a.SetHighlighted(true);
        a.BeginInspect();
        b.BeginInspect();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("the_second_card_owns_the_peek", CardInspectView.RaisedBy(b),
            "the peek is not owned by the card that raised it last");

        // The quieter half of the same bug. A card that lost the peek but still
        // believes it owns one keeps suppressing its own keyword panel, with no
        // peek left to justify the suppression - a panel the player cannot get
        // back by any means, on the card the mouse is resting on.
        Check("a_preempted_card_gets_its_keyword_panel_back",
            Private<HoverTooltip?>(a, "_keywordTooltip") is not null,
            "the pre-empted card is still hiding its keyword panel for a peek it no longer owns");

        a.EndInspect();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("a_preempted_card_does_not_close_the_new_peek", CardInspectView.IsOpen,
            "the card that lost the peek took the replacement down with it");

        b.EndInspect();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("the_owner_still_closes_the_peek", !CardInspectView.IsOpen, "the peek outlived its owner");

        await EndFight(screen);
    }

    // The dwell is deliberately not on the shared hover path. SetHighlighted
    // routes through the same visual as a mouse hover so the two read alike,
    // and an arrow-keyed card stays selected indefinitely - so a dwell hung
    // there opens a full-screen peek 0.4s after every keyboard selection, over
    // a fight the player is still choosing in. It shipped that way for one
    // build and a screenshot fixture is what found it.
    //
    // Waited well past DwellSeconds rather than one frame, or this passes for
    // the wrong reason.
    private async System.Threading.Tasks.Task TestSelectingACardDoesNotOpenAPeek()
    {
        var (screen, _, handArea) = await StartFight("strike");
        var card = FirstCard(handArea);

        card.SetHighlighted(true);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);

        Check("selecting_a_card_does_not_open_a_peek", !CardInspectView.IsOpen,
            "an arrow-key selection grew a peek nobody asked for");

        await EndFight(screen);
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

        // Third of the same family as TestHudNeverPaintsOverAnEnemy and the
        // intent tooltip's keep-off-the-hand check, and the one a playtest
        // caught: the hint used to sit at y=238..284, inside EnemyRow, so
        // aiming a potion wrote two lines of instructions across the name and
        // HP bar of the enemy being aimed at. It lives in the band between the
        // enemy row and the top of the fan now, which is 24px tall - hence one
        // line - and both edges of that band are asserted here.
        //
        // HighestHoveredCardTopY, not HighestCardTopY: the lower edge of that
        // band is not where a card rests but where it reaches when the player
        // looks at it, CardView.HoverLiftPx higher and painted at ZIndex 100
        // over anything underneath. That was half of a 1.15x scale bump and is
        // a lift now; the number is the same 18 either way, which is what the
        // lift was chosen to preserve. Aiming a potion and then moving the mouse to the enemy
        // crosses the fan on the way, so a card lifting is not a corner case -
        // it is the ordinary path through this state.
        var hintRect = hint.GetGlobalRect();
        var painted = screen.GetNode("EnemyRow").GetChildren().OfType<EnemyView>()
            .Where(e => e.GetGlobalRect().Intersects(hintRect))
            .Select(e => e.Combatant.Definition.Name)
            .ToList();
        Check("the_target_hint_paints_over_no_enemy", painted.Count == 0,
            $"hint at {hintRect} covers {string.Join(", ", painted)}");
        Check("the_target_hint_clears_the_top_of_a_hovered_card",
            hintRect.End.Y <= CombatScreen.HighestHoveredCardTopY,
            $"hint reaches y={hintRect.End.Y}, past the top a hovered card reaches " +
            $"y={CombatScreen.HighestHoveredCardTopY}");

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
        //
        // The runaway sits between them for the same reason, and is the harder
        // of the two: it is *alive*, so an IsDead check waves it straight
        // through while its view is sliding off the board. Both exits from a
        // fight leave a rect behind for the length of a tween, which is why the
        // hit test reads IsGone rather than either flag on its own.
        var corpse = SpawnEnemyOverTheMouse("cultist", alive: false);
        var runaway = SpawnEnemyOverTheMouse("cultist", alive: true);
        runaway.Combatant.HasEscaped = true;
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
        Check("hit_test_skips_the_corpse_and_the_runaway", ReferenceEquals(found, live),
            "an enemy that has left the fight won the hit test; a drag over it locks a target that "
            + "is fading out, which reads to the player as no glow at all");

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

        // Kill the survivor too: with every candidate gone the hit test has to
        // come back empty rather than falling back to the nearest corpse.
        live.Combatant.CurrentHp = 0;
        var afterAllDead = strike.GetType()
            .GetMethod("FindEnemyViewUnderMouse",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(strike, null);
        Check("hit_test_returns_nothing_when_every_candidate_is_gone", afterAllDead is null,
            "a corpse was returned once nothing was targetable");

        foreach (var node in new Node[] { strike, block, corpse, runaway, live }) node.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // RefreshEnemies has always instantiated a view for anything in Enemies it
    // has no view for, but until summons existed that set only ever shrank, so
    // the growth branch had never run against a live screen. Two things are
    // being pinned: the view gets built at all, and it is *appended* to
    // EnemyView.Instances - which is the list the drag hit test walks in order,
    // so a newcomer inserted anywhere else would silently reorder targeting.
    private async System.Threading.Tasks.Task TestASummonBuildsAnEnemyViewMidFight()
    {
        Check("enemy_instances_clean_before_the_summon", EnemyView.Instances.Count == 0,
            $"{EnemyView.Instances.Count} EnemyViews leaked from an earlier test");

        CombatContext.EnemyDefinitionIds = new List<string> { "cultist" };
        var instance = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(instance);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var enemyRow = instance.GetNode<Control>("EnemyRow");
        var combat = CombatManager.Instance!;
        Check("one_enemy_before_the_summon", enemyRow.GetChildCount() == 1,
            $"got {enemyRow.GetChildCount()}");

        var existing = EnemyView.Instances.ToList();
        combat.SummonEnemy("slime", 1);
        // The screen rebuilds off CombatantsChanged, which SummonEnemy does not
        // raise itself - the resolution site that called it does. Driving that
        // here rather than adding an event to the manager keeps the assertion
        // about the view layer instead of about who notifies whom.
        combat.TryEndTurn();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("the_summon_gets_a_view", enemyRow.GetChildCount() == 2,
            $"got {enemyRow.GetChildCount()} children in EnemyRow");
        Check("the_new_view_is_appended_to_instances",
            EnemyView.Instances.Count == existing.Count + 1
                && EnemyView.Instances.Take(existing.Count).SequenceEqual(existing),
            "a summon reordered EnemyView.Instances, which is the order the drag hit test walks");

        instance.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private EnemyView SpawnEnemyOverTheMouse(string enemyId, bool alive)
    {
        var view = GD.Load<PackedScene>("res://scenes/EnemyView.tscn").Instantiate<EnemyView>();
        view.Combatant = EnemyFactory.Create(EnemyDatabase.Get(enemyId));
        if (!alive) view.Combatant.CurrentHp = 0;
        AddChild(view);

        // Straddle wherever the mouse actually is, asked of the view itself.
        // This used to be a literal (-40, -40) on the reasoning that a headless
        // Godot pins the mouse at (0, 0) - true of the *window*, and it was
        // true of the canvas too for as long as the project stretched with
        // aspect="expand".
        //
        // Letterboxing (ART_SPEC section 4) broke that equality rather than the
        // hit test: "keep" insets the canvas behind bars whenever the window is
        // not 16:9, so the window's top-left corner maps to a *negative* point
        // in canvas space and the mouse sat outside a rect pinned at -40. Both
        // sides of the comparison in FindEnemyViewUnderMouse are canvas-space
        // (GetGlobalMousePosition against GetGlobalRect), so asking a
        // CanvasItem for the position - rather than assuming a value for it -
        // is right under either setting. The suite's own class is a plain Node
        // and has no such method, which is why this asks the view.
        view.GlobalPosition = view.GetGlobalMousePosition() - new Vector2(40, 40);
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
