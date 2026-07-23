using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check that CombatScreen.tscn itself (not just the plain-C# combat
// logic EffectSmokeTest covers) loads, wires its script/node paths correctly,
// and finishes StartCombat with the expected UI populated. Catches scene-file
// mistakes (e.g. a child node missing its script) that pure-logic tests can't
// see. Run via `godot --headless scenes/debug/CombatSmokeTest.tscn`.
public partial class CombatSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        CombatContext.EnemyDefinitionIds = new List<string> { "cultist" };
        CombatContext.IsFinalEncounter = false;

        var packed = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var enemyRow = instance.GetNode("EnemyRow");
        var handArea = instance.GetNode("HandArea");
        var hpLabel = instance.GetNode<Label>("PlayerInfoPanel/HpLabel");
        var energyLabel = instance.GetNode<Label>("PlayerInfoPanel/EnergyLabel");

        Check("enemy_row_has_one_view", enemyRow.GetChildCount() == 1, $"got {enemyRow.GetChildCount()}");
        Check("hand_area_has_five_cards", handArea.GetChildCount() == 5, $"got {handArea.GetChildCount()}");
        Check("hp_label_reflects_player", hpLabel.Text == "HP 50/50", $"got '{hpLabel.Text}'");
        Check("energy_label_reflects_player", energyLabel.Text == "Energy 3/3", $"got '{energyLabel.Text}'");

        GD.Print($"CombatSmokeTest: {_pass} passed, {_fail} failed");
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
