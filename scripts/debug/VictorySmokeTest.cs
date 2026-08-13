using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// The one branch that decides whether a run can ever end: the final act's boss
// dying and the game going to RunEndScreen instead of handing out another
// reward and another map.
//
// It gets its own scene rather than another test in ActSmokeTest because a
// suite has exactly one scene change to spend. RunManager.ChangeScreen calls
// ChangeSceneToFile, which frees the tree's current scene - the suite node
// itself - at the end of the frame, so any test that needs frames after an
// earlier test has changed screens never gets them. ActSmokeTest spends its one
// on the act-1 boss (which advances the act); this spends its own on act 3's
// (which ends the run).
//
// Run via `godot --headless scenes/debug/VictorySmokeTest.tscn`.
public partial class VictorySmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override async void _Ready()
    {
        var tree = GetTree();

        // Step out of the way of the scene change this suite is here to
        // trigger. ChangeSceneToFile frees whatever the tree calls its *current
        // scene* - normally this node - which is why every other suite that
        // changes screens has to make it the last thing it ever does. This one
        // cannot: RunEndScreen deletes the run save and banks a score into the
        // meta save from its own _Ready, and that happens during the deferred
        // swap at the end of the frame, after any scoped guard here would have
        // restored. (Found the hard way: the first version of this test ate a
        // real in-progress run.)
        //
        // Handing the title to an empty stand-in means the swap deletes that
        // instead, this node lives on, and the guard can restore the files
        // after RunEndScreen has done its worst. Nothing else reads
        // CurrentScene for anything but "somewhere to parent an overlay"
        // (HoverTooltip, CardView's resolve tween), which the stand-in serves.
        // One frame first: during _Ready the root is still mid-adding this node,
        // and both AddChild and set_current_scene refuse (loudly) while it is.
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var standIn = new Control { Name = "CurrentSceneStandIn" };
        tree.Root.AddChild(standIn);
        tree.CurrentScene = standIn;

        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        await TestFinalActBossWinEndsTheRun(tree);

        GD.Print($"VictorySmokeTest: {_pass} passed, {_fail} failed");
        tree.Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private async Task TestFinalActBossWinEndsTheRun(SceneTree tree)
    {
        // The screen this lands on deletes the run save and banks a score into
        // the meta save, so both are snapshotted and put back. This only holds
        // because _Ready gave the "current scene" title away first - see there.
        using var saveGuard = RunSaveGuard.Protect();
        // And the change is pinned to a hard cut, so this suite doesn't behave
        // differently depending on the developer's Reduce Motion setting.
        using var cutGuard = HardCutGuard.Protect();

        RngStreams.Init(4242);
        RunState.InitNewRun();
        RunState.ActIndex = ActDatabase.Count - 1;
        Check("fixture_starts_on_the_final_act", RunState.IsFinalAct,
            $"actIndex={RunState.ActIndex} of {ActDatabase.Count} acts");

        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("strike") };

        var bossId = RunState.CurrentAct.BossIds[0];
        CombatContext.EnemyDefinitionIds = new List<string> { bossId };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = true;
        CombatContext.GoldReward = RunState.CurrentAct.BossGold;

        // Seeded to the wrong answer on purpose: RunEndContext is a static that
        // survives scene changes, so a branch that never runs would otherwise
        // be indistinguishable from one that set Win.
        RunEndContext.Outcome = RunEndOutcome.Lose;

        var instance = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(instance);
        var combat = instance.GetNode<CombatManager>("CombatManager");

        // The fight is started for real and then shortened - what's under test
        // is the branch after it, not the damage math.
        var boss = combat.Enemies[0];
        boss.CurrentHp = 4;
        while (!boss.IsDead && combat.State != CombatState.CombatEnd)
        {
            if (combat.State == CombatState.PlayerTurn)
            {
                if (combat.Player.Piles.Hand.Count > 0) combat.TryPlayCard(combat.Player.Piles.Hand[0], boss);
                else combat.TryEndTurn();
            }
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        Check("final_boss_fight_reaches_combat_end", combat.State == CombatState.CombatEnd,
            $"state={combat.State}");

        int actBefore = RunState.ActIndex;
        instance.GetNode<Button>("CombatEndPanel/ContinueButton").EmitSignal(Button.SignalName.Pressed);


        Check("final_boss_win_routes_to_the_victory_screen",
            RunManager.Instance.CurrentScreen == RunManager.ScreenState.Victory,
            $"screen={RunManager.Instance.CurrentScreen} - the run handed out another reward instead of ending");
        Check("final_boss_win_marks_the_run_won",
            RunEndContext.Outcome == RunEndOutcome.Win,
            $"outcome={RunEndContext.Outcome} - RunEndScreen would score this as a loss");
        Check("final_boss_win_does_not_advance_past_the_last_act",
            RunState.ActIndex == actBefore,
            $"actIndex moved {actBefore} -> {RunState.ActIndex}");
        Check("final_boss_win_counts_the_boss",
            RunState.Stats.BossesSlain == 1, $"bossesSlain={RunState.Stats.BossesSlain}");

        // Let the deferred swap actually happen, so the screen that ends a run
        // is proven to load rather than only to be routed to - and so the guard
        // restoring on the way out of this method is restoring over what
        // RunEndScreen wrote rather than ahead of it.
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var arrived = tree.CurrentScene;
        Check("victory_screen_actually_loads", arrived is RunEndScreen,
            $"current scene is '{arrived?.Name}' ({arrived?.GetType().Name})");
        Check("victory_screen_announces_a_win",
            arrived?.GetNodeOrNull<Label>("CenterContainer/Columns/VBoxContainer/OutcomeLabel")?.Text == "VICTORY",
            "the run-end screen came up but does not read VICTORY");

        // Torn down before the suite quits, and given the frame that actually
        // performs it: the combat screen is still parented here with live
        // tweens on it, and quitting out from under those aborts the process on
        // the way out (the suite's checks all pass, and the runner still reports
        // it as a failure - exit 134).
        instance.QueueFree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
