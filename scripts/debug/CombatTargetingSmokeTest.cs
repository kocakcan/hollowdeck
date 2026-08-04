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
