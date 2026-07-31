using System.Collections.Generic;
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
