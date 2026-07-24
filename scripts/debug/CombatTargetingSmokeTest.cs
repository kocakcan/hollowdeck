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

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
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

        Check("starts_unlocked", !enemyA.HasThemeStyleboxOverride("normal"), "expected no override before locking");

        enemyA.SetTargetLocked(true);
        Check("lock_adds_normal_override", enemyA.HasThemeStyleboxOverride("normal"), "expected override after locking");
        Check("locking_one_enemy_does_not_affect_the_other", !enemyB.HasThemeStyleboxOverride("normal"),
            "expected enemyB to be unaffected");

        enemyA.SetTargetLocked(false);
        Check("unlock_removes_normal_override", !enemyA.HasThemeStyleboxOverride("normal"),
            "expected override removed after unlocking");

        instance.QueueFree();

        GD.Print($"CombatTargetingSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
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
