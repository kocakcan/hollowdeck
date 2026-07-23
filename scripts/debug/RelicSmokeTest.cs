using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check that relic hooks actually fire through CombatManager (not
// just that RelicRegistry.Create doesn't throw - EffectSmokeTest covers
// that). Run via `godot --headless scenes/debug/RelicSmokeTest.tscn`.
public partial class RelicSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestOnCombatStart_AnchorStone();
        TestOnTurnStart_WardedBracer();
        TestOnTurnEnd_FrugalSatchel();
        TestOnCardPlayed_SkirmishersSash();
        TestOnDamageDealt_VampireFang();
        TestCardTargeting_NoTargetRejected_ExplicitTargetResolves();
        TestOnDamageTaken_ThornedCarapace_MidRoundDeath();
        TestOnCombatEnd_SecondWindAndScavengersCharm();

        GD.Print($"RelicSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private static RelicInstance Relic(string id) => new(RelicDatabase.Get(id));

    private static PlayerCombatant MakePlayer(int hp = 50, int energy = 3)
    {
        return new PlayerCombatant
        {
            Name = "Player", MaxHp = hp, CurrentHp = hp,
            MaxEnergy = energy, CurrentEnergy = energy,
            Piles = new PileManager(CardDatabase.All),
        };
    }

    private CombatManager NewCombat()
    {
        var combat = new CombatManager();
        AddChild(combat);
        return combat;
    }

    private void TestOnCombatStart_AnchorStone()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("anchor_stone") });

        Check("anchor_stone_grants_block_on_combat_start", player.Block == 8, $"block={player.Block}");
        combat.QueueFree();
    }

    private void TestOnTurnStart_WardedBracer()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("warded_bracer") });

        Check("warded_bracer_grants_block_on_first_turn", player.Block == 3, $"block={player.Block}");

        // Second turn: end turn (enemy resolves), block should reset to 0
        // then gain another 3 at the next BeginPlayerTurn.
        combat.TryEndTurn();
        Check("warded_bracer_grants_block_again_next_turn", player.Block == 3, $"block={player.Block}");
        combat.QueueFree();
    }

    private void TestOnTurnEnd_FrugalSatchel()
    {
        var combat = NewCombat();
        var player = MakePlayer(energy: 3);
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("frugal_satchel") });

        // TryEndTurn cascades synchronously through the whole enemy round
        // (block correctly clears again once the next player turn begins),
        // so snapshot Block right at the EnemyTurn transition - the moment
        // OnTurnEnd's bonus should be visible but not yet reset.
        int blockAtEnemyTurn = -1;
        combat.StateChanged += state =>
        {
            if (state == CombatState.EnemyTurn) blockAtEnemyTurn = player.Block;
        };
        combat.TryEndTurn();

        Check("frugal_satchel_grants_block_when_energy_unspent", blockAtEnemyTurn == 2,
            $"block at EnemyTurn={blockAtEnemyTurn}");
        combat.QueueFree();
    }

    private void TestOnCardPlayed_SkirmishersSash()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("skirmishers_sash") });

        var defend = player.Piles.Hand.FirstOrDefault(c => c.Definition.Id == "defend")
            ?? new CardInstance(CardDatabase.Get("defend"));
        if (!player.Piles.Hand.Contains(defend)) player.Piles.Hand.Add(defend);

        int blockBefore = player.Block;
        combat.TryPlayCard(defend); // Defend itself grants 5 block; relic adds 1 more
        Check("skirmishers_sash_grants_bonus_block_on_skill_play", player.Block == blockBefore + 5 + 1,
            $"before={blockBefore} after={player.Block}");
        combat.QueueFree();
    }

    private void TestOnDamageDealt_VampireFang()
    {
        var combat = NewCombat();
        var player = MakePlayer(hp: 50);
        player.CurrentHp = 40;
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("vampire_fang") });

        var strike = new CardInstance(CardDatabase.Get("strike"));
        player.Piles.Hand.Add(strike);
        combat.TryPlayCard(strike, enemy);

        Check("vampire_fang_heals_on_damage_dealt", player.CurrentHp == 41, $"hp={player.CurrentHp}");
        combat.QueueFree();
    }

    private void TestCardTargeting_NoTargetRejected_ExplicitTargetResolves()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        var strike1 = new CardInstance(CardDatabase.Get("strike"));
        player.Piles.Hand.Add(strike1);
        bool resolvedWithNoTarget = combat.TryPlayCard(strike1, null);
        Check("single_enemy_card_with_no_target_is_rejected",
            !resolvedWithNoTarget && player.Piles.Hand.Contains(strike1),
            $"resolved={resolvedWithNoTarget} stillInHand={player.Piles.Hand.Contains(strike1)}");

        var strike2 = new CardInstance(CardDatabase.Get("strike"));
        player.Piles.Hand.Add(strike2);
        int enemyHpBefore = enemy.CurrentHp;
        bool resolvedWithTarget = combat.TryPlayCard(strike2, enemy);
        Check("single_enemy_card_with_explicit_target_resolves",
            resolvedWithTarget && !player.Piles.Hand.Contains(strike2) && enemy.CurrentHp == enemyHpBefore - 6,
            $"resolved={resolvedWithTarget} enemyHp={enemy.CurrentHp}");
        combat.QueueFree();
    }

    private void TestOnDamageTaken_ThornedCarapace_MidRoundDeath()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy1 = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        enemy1.MaxHp = 2; enemy1.CurrentHp = 2; // dies to a single 2-damage retaliation
        var enemy2 = EnemyFactory.Create(EnemyDatabase.Get("cultist"));

        combat.StartCombat(player, new List<EnemyCombatant> { enemy1, enemy2 }, new List<RelicInstance> { Relic("thorned_carapace") });

        // Cultist's first intent is a Strength buff (no damage) - burn
        // through that round so both enemies are queued on their actual
        // "dark_strike" (damage) move for round 2, which is what should
        // trigger the retaliation-kills-enemy1-mid-round scenario.
        combat.TryEndTurn();
        combat.TryEndTurn();

        Check("enemy1_died_to_retaliation_mid_round", enemy1.IsDead, $"enemy1.hp={enemy1.CurrentHp}");
        Check("enemy2_still_resolved_after_enemy1_died", enemy2.CurrentMove is not null, "enemy2 has no move");
        Check("combat_manager_pruned_dead_enemy", combat.Enemies.Count == 1 && combat.Enemies[0] == enemy2,
            $"enemies remaining={combat.Enemies.Count}");
        Check("state_recovered_to_player_turn", combat.State == CombatState.PlayerTurn, $"state={combat.State}");
        combat.QueueFree();
    }

    private void TestOnCombatEnd_SecondWindAndScavengersCharm()
    {
        var combat = NewCombat();
        var player = MakePlayer(hp: 50);
        player.CurrentHp = 40; // >50% max, so Scavenger's Charm should pay out
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        enemy.CurrentHp = 1;

        int goldBefore = RunState.Gold;
        combat.StartCombat(player, new List<EnemyCombatant> { enemy },
            new List<RelicInstance> { Relic("second_wind"), Relic("scavengers_charm") });

        var strike = new CardInstance(CardDatabase.Get("strike"));
        player.Piles.Hand.Add(strike);
        combat.TryPlayCard(strike, enemy); // kills the 1-hp enemy -> triggers EndCombat(Win)

        Check("combat_ended_in_win", combat.State == CombatState.CombatEnd && combat.Outcome == CombatOutcome.Win,
            $"state={combat.State} outcome={combat.Outcome}");
        Check("second_wind_healed_on_win", player.CurrentHp == 46, $"hp={player.CurrentHp}");
        Check("scavengers_charm_paid_gold_on_win", RunState.Gold == goldBefore + 5,
            $"gold before={goldBefore} after={RunState.Gold}");
        combat.QueueFree();
    }
}
