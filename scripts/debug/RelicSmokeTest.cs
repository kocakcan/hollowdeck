using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public override async void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestOnCombatStart_AnchorStone();
        await TestOnTurnStart_WardedBracer();
        TestOnTurnEnd_FrugalSatchel();
        TestOnCardPlayed_SkirmishersSash();
        TestOnDamageDealt_VampireFang();
        TestCardTargeting_NoTargetRejected_ExplicitTargetResolves();
        await TestOnDamageTaken_ThornedCarapace_MidRoundDeath();
        TestOnCombatEnd_SecondWindAndScavengersCharm();
        TestLimit_EveryNth_MomentumToken();
        TestLimit_OncePerCombat_LedgerOfRuin();
        TestCondition_CardTypePower_ConduitSigil();
        TestTarget_AllEnemies_OssuaryBell();
        await TestLimits_OnDamageTaken_PerTurnVersusPerCombat();

        GD.Print($"RelicSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // CombatManager now paces enemy turns with real delays between actions
    // (see CombatManager.ResolveEnemyTurnAsync) instead of resolving them all
    // synchronously in one call, so any test asserting on post-enemy-turn
    // state has to wait for the turn to actually finish first.
    private async Task WaitForEnemyTurnToResolve(CombatManager combat)
    {
        while (combat.State is CombatState.EnemyTurn or CombatState.ResolvingEnemyIntent)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
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

    private async Task TestOnTurnStart_WardedBracer()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("warded_bracer") });

        Check("warded_bracer_grants_block_on_first_turn", player.Block == 3, $"block={player.Block}");

        // Second turn: end turn (enemy resolves), block should reset to 0
        // then gain another 3 at the next BeginPlayerTurn.
        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);
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

    private async Task TestOnDamageTaken_ThornedCarapace_MidRoundDeath()
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
        await WaitForEnemyTurnToResolve(combat);
        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);

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

    // ---------------------------------------------------------------------
    // The vocabulary the eight checks above don't reach. Those eight drive
    // all seven hooks through relics that used to be bespoke C# classes and
    // are now data rows, so they are the regression proof for the conversion
    // itself; these cover the target/condition/limit keys that made it
    // possible.
    // ---------------------------------------------------------------------

    private void TestLimit_EveryNth_MomentumToken()
    {
        var combat = NewCombat();
        var player = MakePlayer(energy: 9);
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("momentum_token") });

        int hpBefore = enemy.CurrentHp;
        // Defend is a Skill, so nothing here damages the enemy except the
        // relic - which is what makes the 3rd card's 4 damage unambiguous.
        for (int i = 0; i < 2; i++)
        {
            var card = new CardInstance(CardDatabase.Get("defend"));
            player.Piles.Hand.Add(card);
            combat.TryPlayCard(card);
        }
        Check("momentum_token_silent_before_the_third_card", enemy.CurrentHp == hpBefore,
            $"hp={enemy.CurrentHp} expected={hpBefore}");

        var third = new CardInstance(CardDatabase.Get("defend"));
        player.Piles.Hand.Add(third);
        combat.TryPlayCard(third);
        Check("momentum_token_fires_on_the_third_card", enemy.CurrentHp == hpBefore - 4,
            $"hp={enemy.CurrentHp} expected={hpBefore - 4}");
        combat.QueueFree();
    }

    private void TestLimit_OncePerCombat_LedgerOfRuin()
    {
        var combat = NewCombat();
        var player = MakePlayer(energy: 9);
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("ledger_of_ruin") });

        for (int i = 0; i < 3; i++)
        {
            var strike = new CardInstance(CardDatabase.Get("strike"));
            player.Piles.Hand.Add(strike);
            combat.TryPlayCard(strike, enemy);
        }

        Check("ledger_of_ruin_pays_out_once_across_three_attacks",
            player.GetStatus(StatusType.Strength) == 1,
            $"strength={player.GetStatus(StatusType.Strength)}");
        combat.QueueFree();
    }

    private void TestCondition_CardTypePower_ConduitSigil()
    {
        var combat = NewCombat();
        var player = MakePlayer(energy: 3);
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance> { Relic("conduit_sigil") });

        // A Skill first: the filter has to reject it, or "Power" means
        // nothing and the relic is just "gain 1 Energy per card".
        player.CurrentEnergy = 3;
        var defend = new CardInstance(CardDatabase.Get("defend"));
        player.Piles.Hand.Add(defend);
        combat.TryPlayCard(defend);
        Check("conduit_sigil_ignores_a_skill", player.CurrentEnergy == 2,
            $"energy={player.CurrentEnergy}");

        // Inflame costs 2 and is a Power: 2 - 2 + 1 = 1.
        player.CurrentEnergy = 2;
        var inflame = new CardInstance(CardDatabase.Get("inflame"));
        player.Piles.Hand.Add(inflame);
        combat.TryPlayCard(inflame);
        Check("conduit_sigil_refunds_on_a_power", player.CurrentEnergy == 1,
            $"energy={player.CurrentEnergy}");
        combat.QueueFree();
    }

    private void TestTarget_AllEnemies_OssuaryBell()
    {
        var combat = NewCombat();
        var player = MakePlayer();
        var enemy1 = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        var enemy2 = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy1, enemy2 },
            new List<RelicInstance> { Relic("ossuary_bell") });

        // OnTurnEnd fires before the EnemyTurn transition, and the enemy turn
        // then ticks and decays the Poison - so snapshot at the transition,
        // the same trick TestOnTurnEnd_FrugalSatchel uses for Block.
        int poison1 = -1, poison2 = -1;
        combat.StateChanged += state =>
        {
            if (state != CombatState.EnemyTurn) return;
            poison1 = enemy1.GetStatus(StatusType.Poison);
            poison2 = enemy2.GetStatus(StatusType.Poison);
        };
        combat.TryEndTurn();

        Check("ossuary_bell_poisons_every_enemy", poison1 == 1 && poison2 == 1,
            $"enemy1={poison1} enemy2={poison2}");
        combat.QueueFree();
    }

    // Both OnDamageTaken limits at once, measured as a DIFFERENCE against an
    // unrelicked control rather than against hardcoded numbers - the cultist's
    // damage is data, and a balance tweak to enemies.json should not be able
    // to break a test about firing limits.
    //
    // Over two damaging rounds with three attackers: Bulwark Charm's 4 Block
    // lands once per turn (8 total), Rusted Portcullis's 10 lands once for the
    // whole combat (10 total). An unlimited version of either would absorb on
    // every hit and beat both numbers, which is the failure being ruled out.
    private async Task TestLimits_OnDamageTaken_PerTurnVersusPerCombat()
    {
        int control = await DamageTakenOverTwoAttackingRounds();
        int bulwark = await DamageTakenOverTwoAttackingRounds("bulwark_charm");
        int portcullis = await DamageTakenOverTwoAttackingRounds("rusted_portcullis");

        Check("bulwark_charm_absorbs_once_per_turn_and_resets", control - bulwark == 8,
            $"control={control} bulwark={bulwark} diff={control - bulwark}");
        Check("rusted_portcullis_absorbs_once_for_the_whole_combat", control - portcullis == 10,
            $"control={control} portcullis={portcullis} diff={control - portcullis}");
    }

    private async Task<int> DamageTakenOverTwoAttackingRounds(string? relicId = null)
    {
        var combat = NewCombat();
        var player = MakePlayer(hp: 300);
        var enemies = new List<EnemyCombatant>
        {
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
        };
        var relics = relicId is null ? new List<RelicInstance>() : new List<RelicInstance> { Relic(relicId) };
        combat.StartCombat(player, enemies, relics);

        // Round 1 is the Cultist's Strength buff (sequential, loopFromIndex 1),
        // so it deals no damage; rounds 2 and 3 are the two attacking rounds.
        for (int round = 0; round < 3; round++)
        {
            combat.TryEndTurn();
            await WaitForEnemyTurnToResolve(combat);
        }

        int taken = player.MaxHp - player.CurrentHp;
        combat.QueueFree();
        return taken;
    }
}
