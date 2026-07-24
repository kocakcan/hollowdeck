using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for the new Phase 4 mechanics: Poison ticking, the lose_hp
// effect, the boss's phase_threshold enrage picker, and the elite guaranteed-
// relic reward. Run via `godot --headless scenes/debug/Phase4ContentSmokeTest.tscn`.
public partial class Phase4ContentSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override async void _Ready()
    {
        // Captured before TestEliteRewardGrantsGuaranteedRelic runs: that
        // test's simulated Continue click triggers ChangeSceneToFile, which
        // replaces this node as the tree's current scene - after that,
        // calling GetTree() on `this` throws because `this` is no longer
        // attached. The SceneTree object itself survives the scene swap, so
        // grabbing it up front and reusing it for the final Quit() sidesteps
        // that instead of relying on GetTree() still working at the end.
        var tree = GetTree();

        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        await TestPoisonTickBypassesBlockAndDecays();
        TestLoseHpEffect();
        TestEnragePickerSwitchesAtThreshold();
        await TestEliteRewardGrantsGuaranteedRelic();

        GD.Print($"Phase4ContentSmokeTest: {_pass} passed, {_fail} failed");
        tree.Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    // CombatManager now paces enemy turns with real delays between actions
    // (see CombatManager.ResolveEnemyTurnAsync) instead of resolving them
    // synchronously in one call, so tests asserting post-enemy-turn state
    // have to wait for the turn to actually finish first.
    private async Task WaitForEnemyTurnToResolve(CombatManager combat)
    {
        while (combat.State is CombatState.EnemyTurn or CombatState.ResolvingEnemyIntent)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task TestPoisonTickBypassesBlockAndDecays()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("strike") }),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        // ApplyPoisonTick(enemy) runs before the per-turn "enemy.Block = 0"
        // reset (see CombatManager.ResolveNextEnemyTurn), so Block set here
        // is still present at the moment the tick fires - if poison went
        // through the same path as deal_damage it would be fully absorbed
        // (Block 10 > Poison 5) and CurrentHp would stay unchanged; direct
        // HP loss instead proves it bypasses Block entirely.
        enemy.Block = 10;
        enemy.AddStatus(StatusType.Poison, 5);

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());
        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);

        Check("poison_deals_direct_hp_loss_bypassing_block", enemy.CurrentHp == enemy.MaxHp - 5,
            $"expected {enemy.MaxHp - 5}, got {enemy.CurrentHp}");
        Check("poison_decays_by_one", enemy.GetStatus(StatusType.Poison) == 4,
            $"poison={enemy.GetStatus(StatusType.Poison)}");
        combat.QueueFree();
    }

    private void TestLoseHpEffect()
    {
        var player = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 20 };
        var ctx = new EffectContext { Source = player, Targets = new List<Combatant> { player }, Combat = null! };

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "lose_hp", Amount = 5 });
        Check("lose_hp_reduces_hp", player.CurrentHp == 15, $"hp={player.CurrentHp}");

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "lose_hp", Amount = 100 });
        Check("lose_hp_clamps_at_zero_not_negative", player.CurrentHp == 0, $"hp={player.CurrentHp}");
    }

    private void TestEnragePickerSwitchesAtThreshold()
    {
        var picker = new PhaseThresholdIntentPicker();
        var boss = new EnemyCombatant
        {
            Name = "Boss", MaxHp = 150, CurrentHp = 150,
            Definition = EnemyDatabase.Get("hollow_king"),
        };
        boss.IntentPicker = picker;

        var firstMove = picker.PickNext(boss);
        Check("enrage_picker_starts_in_normal_phase",
            boss.Definition.Moves.Exists(m => m.MoveId == firstMove.MoveId),
            $"got moveId={firstMove.MoveId}");

        boss.CurrentHp = 74; // 74/150 <= 50% -> enrage should kick in on the next pick
        var enragedMove = picker.PickNext(boss);
        Check("enrage_picker_switches_to_enrage_moves_at_threshold",
            boss.Definition.EnrageMoves.Exists(m => m.MoveId == enragedMove.MoveId),
            $"got moveId={enragedMove.MoveId}");
    }

    private async Task TestEliteRewardGrantsGuaranteedRelic()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("strike") };
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        CombatContext.EnemyDefinitionIds = new List<string> { "slime" };
        CombatContext.IsElite = true;
        CombatContext.IsBoss = false;

        var packed = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var combat = instance.GetNode<CombatManager>("CombatManager");
        // Drag-to-target normally drives this; directly killing the enemy
        // exercises the same win path CombatScreen.OnContinuePressed reads.
        var enemy = combat.Enemies[0];
        while (!enemy.IsDead && combat.State != CombatState.CombatEnd)
        {
            if (combat.State == CombatState.PlayerTurn)
            {
                if (combat.Player.Piles.Hand.Count > 0) combat.TryPlayCard(combat.Player.Piles.Hand[0], enemy);
                else combat.TryEndTurn();
            }
            // Enemy-turn resolution is now paced with real delays (see
            // CombatManager.ResolveEnemyTurnAsync), so yield a frame each
            // iteration instead of busy-spinning while it catches up.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Check("elite_fight_reaches_combat_end", combat.State == CombatState.CombatEnd,
            $"state={combat.State}");

        // Simulate the win-screen's Continue click (OnContinuePressed is
        // private; the button's own Pressed signal is the real entry point
        // a player uses, so drive it the same way). RewardContext.
        // GuaranteedRelic/RunState.Relics are both set before
        // OnContinuePressed calls ChangeScreen, so the checks below are
        // accurate even though this logs one harmless "parent busy" engine
        // error - ChangeSceneToFile doesn't like being called on the
        // current scene from inside this test's own _Ready() call stack,
        // which none of the other debug smoke tests trigger.
        int relicsBefore = RunState.Relics.Count;
        var continueButton = instance.GetNode<Button>("CombatEndPanel/ContinueButton");
        continueButton.EmitSignal(Button.SignalName.Pressed);

        Check("elite_reward_grants_a_guaranteed_relic", RewardContext.GuaranteedRelic is not null,
            "RewardContext.GuaranteedRelic was null");
        Check("elite_reward_relic_actually_added_to_run", RunState.Relics.Count == relicsBefore + 1,
            $"relics={RunState.Relics.Count}");

        instance.QueueFree();
    }
}
