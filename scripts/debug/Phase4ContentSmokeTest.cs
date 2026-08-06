using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;
using Hollowdeck.UI;

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
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        await TestPoisonTickBypassesBlockAndDecays();
        TestLoseHpEffect();
        TestEnragePickerSwitchesAtThreshold();
        TestEveryIntentTelegraphsWhatItResolves();
        TestNoEnemyMoveUsesACardOnlyScope();
        TestIntentLabelsAreReadFromTheMove();
        TestEveryMoveDescribesItselfInTheEnemyVoice();
        await TestEnemyPowersPayOutEachTurn();
        await TestFervorAndForesightPayOutEachTurn();
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

    // A move's telegraph is one authored number (EnemyIntent.DisplayAmount)
    // sitting beside the effects that actually resolve, and nothing but this
    // stops the two drifting. A drifted telegraph is precisely the bug the
    // intent system exists to prevent: the player commits a turn against a
    // number the game then doesn't honour. Swept over every move of every
    // enemy, including enrage lists, because the cost of a miss is highest on
    // exactly the moves seen least often.
    private void TestEveryIntentTelegraphsWhatItResolves()
    {
        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All)
        {
            foreach (var move in def.Moves.Concat(def.EnrageMoves))
            {
                var backing = move.Intent.Type switch
                {
                    IntentType.Attack => move.Effects.FirstOrDefault(e => e.Action == "deal_damage"),
                    IntentType.Defend => move.Effects.FirstOrDefault(e => e.Action == "gain_block"),
                    IntentType.Buff => move.Effects.FirstOrDefault(e =>
                        e.Scope == EffectScope.Self && (e.Action == "apply_status" || e.Action == "heal")),
                    IntentType.Debuff => move.Effects.FirstOrDefault(e =>
                        e.Scope == EffectScope.Target && e.Action == "apply_status"),
                    _ => null,
                };

                if (backing is null)
                {
                    problems.Add($"{def.Id}/{move.MoveId}: {move.Intent.Type} intent with no effect behind it");
                }
                else if (backing.Amount != move.Intent.DisplayAmount)
                {
                    problems.Add($"{def.Id}/{move.MoveId}: telegraphs {move.Intent.DisplayAmount}, resolves {backing.Amount}");
                }
            }
        }

        Check("every_intent_telegraphs_what_it_resolves", problems.Count == 0, string.Join("; ", problems));
    }

    // The guard on the check above rather than a check of its own subject.
    //
    // Phase 7 gave EffectSpec two scopes the telegraph cannot express and one
    // flag it cannot read. RandomEnemy is un-telegraphable by definition - the
    // target is chosen at resolution, after the player has already committed a
    // turn against the label. AllEnemies on an enemy move means its own side,
    // which the enemy voice would print as "to you". And PerX has no amount
    // until a card is played, which an enemy never does.
    //
    // All three resolve coherently if authored (see CombatManager.ScopedTargets
    // and EffectContext.AmountFor), so nothing would crash - the move would
    // just quietly do something other than what it announced. A drifted
    // telegraph is the canonical bad bug in this genre, so this is an
    // assertion rather than a comment in enemies.json.
    private void TestNoEnemyMoveUsesACardOnlyScope()
    {
        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All)
        {
            foreach (var move in def.Moves.Concat(def.EnrageMoves))
            {
                foreach (var spec in move.Effects)
                {
                    if (spec.Scope is EffectScope.AllEnemies or EffectScope.RandomEnemy)
                    {
                        problems.Add($"{def.Id}/{move.MoveId}: scope {spec.Scope}");
                    }
                    if (spec.PerX) problems.Add($"{def.Id}/{move.MoveId}: perX");
                }
            }
        }

        Check("no_enemy_move_uses_a_card_only_scope", problems.Count == 0,
            string.Join("; ", problems) + " - EnemyView cannot telegraph these, "
            + "so the move would resolve as something other than its label");
    }

    // The label's *other* half is derived rather than authored - how many hits
    // a move is, and which status a buff grants, are facts about its effects.
    // These pin the four shapes that derivation has to produce.
    private void TestIntentLabelsAreReadFromTheMove()
    {
        var target = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };

        string Label(string enemyId, string moveId)
        {
            var def = EnemyDatabase.Get(enemyId);
            var move = def.Moves.Concat(def.EnrageMoves).First(m => m.MoveId == moveId);
            return EnemyView.FormatIntent(move, EnemyFactory.Create(def), target);
        }

        Check("single_hit_attack_telegraphs_a_bare_number",
            Label("cultist", "dark_strike") == "6", $"got '{Label("cultist", "dark_strike")}'");
        Check("multi_hit_attack_telegraphs_its_hit_count",
            Label("drowned_thrall", "flailing_grasp") == "4 x2", $"got '{Label("drowned_thrall", "flailing_grasp")}'");
        Check("strength_buff_keeps_its_short_name",
            Label("cultist", "incantation") == "+3 Str", $"got '{Label("cultist", "incantation")}'");
        Check("non_strength_buff_names_its_own_status",
            Label("gaol_rat", "bristle") == "+2 Metal", $"got '{Label("gaol_rat", "bristle")}'");
        Check("self_heal_buff_reads_as_hp",
            Label("mire_leech", "engorge") == "+5 HP", $"got '{Label("mire_leech", "engorge")}'");
        Check("debuff_intent_telegraphs_its_amount",
            Label("mire_leech", "sap_will") == "2", $"got '{Label("mire_leech", "sap_will")}'");
        Check("defend_intent_leaves_the_number_to_its_icon",
            Label("bog_troll", "hardened_hide") == "", $"got '{Label("bog_troll", "hardened_hide")}'");
    }

    // The intent row tells the player how much; the hover panel tells them what
    // kind of move it is, in prose. That prose is generated from the same
    // EffectSpecs the row's number is derived from, so it inherits the same
    // no-lying property - but only if every move actually produces some. A move
    // whose only action has no formatter arm renders as an empty string, which
    // on a card would be a blank rules box and here is a tooltip that explains
    // nothing.
    private void TestEveryMoveDescribesItselfInTheEnemyVoice()
    {
        var target = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        var silent = new List<string>();
        var firstPerson = new List<string>();

        foreach (var def in EnemyDatabase.All)
        {
            var source = EnemyFactory.Create(def);
            foreach (var move in def.Moves.Concat(def.EnrageMoves))
            {
                string prose = EnemyView.DescribeMove(move, source, target);
                if (string.IsNullOrWhiteSpace(prose)) { silent.Add($"{def.Id}/{move.MoveId}"); continue; }

                // The imperative forms are the player's voice. An enemy
                // telegraph reading "Deal 12 damage." is an instruction to the
                // player to do it, which is exactly backwards.
                if (prose.StartsWith("Deal ") || prose.StartsWith("Gain ")
                    || prose.StartsWith("Apply ") || prose.StartsWith("Heal "))
                {
                    firstPerson.Add($"{def.Id}/{move.MoveId}: {prose}");
                }
            }
        }

        Check("every_enemy_move_describes_itself", silent.Count == 0,
            $"no hover prose for: {string.Join(", ", silent)} - a formatter arm is missing");
        Check("every_enemy_move_reads_in_the_enemy_voice", firstPerson.Count == 0,
            $"still imperative: {string.Join("; ", firstPerson)}");

        // Pinned end to end on one attack and one debuff, because the wording
        // is the deliverable here, not just its non-emptiness.
        var cultist = EnemyDatabase.Get("cultist");
        string darkStrike = EnemyView.DescribeMove(
            cultist.Moves.First(m => m.MoveId == "dark_strike"), EnemyFactory.Create(cultist), target);
        Check("attack_move_prose_names_damage_and_recipient",
            darkStrike == "Deals 6 damage to you.", $"got '{darkStrike}'");

        var leech = EnemyDatabase.Get("mire_leech");
        string sapWill = EnemyView.DescribeMove(
            leech.Moves.First(m => m.MoveId == "sap_will"), EnemyFactory.Create(leech), target);
        Check("debuff_move_prose_names_the_status",
            sapWill == "Applies 2 Frail to you.", $"got '{sapWill}'");
        // …and that status is what the shared roster raises a keyword box for,
        // so a card and an enemy explain Frail with the same sentence. Frail is
        // also one of the seven statuses that had no card-side explanation at
        // all before Keywords replaced CardView's five-entry local roster.
        Check("debuff_move_prose_raises_a_keyword_box",
            Keywords.Find(sapWill).Any(e => e.Keyword == "Frail"),
            $"no Frail keyword found in '{sapWill}'");
    }

    // The enemy half of ApplyTurnStartGrants. A Power-style status on an enemy
    // is what the Buff telegraph above exists to make authorable, and the
    // ordering it depends on is the fragile part: the grant has to land *after*
    // the enemy's own Block clear or it is wiped the instant it is given.
    private async Task TestEnemyPowersPayOutEachTurn()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 80, CurrentHp = 80, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("defend") }),
        };
        // gaol_rat opens on bristle (Metallicize 2) and never returns to it
        // (loopFromIndex 1), so turn one grants and turn two has to pay out.
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("gaol_rat"));

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);
        Check("enemy_buff_move_applies_its_status", enemy.GetStatus(StatusType.Metallicize) == 2,
            $"metallicize={enemy.GetStatus(StatusType.Metallicize)}");
        Check("enemy_grant_does_not_pay_out_on_the_turn_it_lands", enemy.Block == 0,
            $"block={enemy.Block}");

        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);
        Check("enemy_metallicize_pays_out_on_the_next_turn", enemy.Block == 2,
            $"block={enemy.Block}");
        combat.QueueFree();
    }

    // The player-only pair, and the ordering that makes them different from
    // the other three grants: energy and hand size are *assigned* at turn
    // start, so these are folded into the assignments in BeginPlayerTurn
    // rather than added in ApplyTurnStartGrants - where they would be
    // overwritten a line later and the card would do nothing at all.
    private async Task TestFervorAndForesightPayOutEachTurn()
    {
        var deck = new List<CardDefinition>();
        for (int i = 0; i < 12; i++) deck.Add(CardDatabase.Get("defend"));

        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 80, CurrentHp = 80, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(deck),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("slime"));

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        Check("opening_hand_is_the_base_size",
            player.Piles.Hand.Count == CombatManager.BaseHandSize,
            $"hand={player.Piles.Hand.Count}");

        // Granted directly rather than by playing Bloodpact/Second Sight: the
        // cards cost 3 and 1, and what is under test is the turn-start payout,
        // not whether a Power resolves (TestPowerCardsLeavePlay covers that).
        player.AddStatus(StatusType.Fervor, 1);
        player.AddStatus(StatusType.Foresight, 2);

        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);

        Check("fervor_adds_to_the_energy_the_turn_assigns",
            player.CurrentEnergy == player.MaxEnergy + 1,
            $"energy={player.CurrentEnergy}, maxEnergy={player.MaxEnergy}");
        Check("foresight_adds_to_the_hand_the_turn_draws",
            player.Piles.Hand.Count == CombatManager.BaseHandSize + 2,
            $"hand={player.Piles.Hand.Count}");
        Check("neither_grant_wears_off",
            player.GetStatus(StatusType.Fervor) == 1 && player.GetStatus(StatusType.Foresight) == 2,
            $"fervor={player.GetStatus(StatusType.Fervor)}, foresight={player.GetStatus(StatusType.Foresight)}");
        combat.QueueFree();
    }

    private async Task TestEliteRewardGrantsGuaranteedRelic()
    {
        // The simulated Continue click below routes through
        // RunManager.ChangeScreen(Reward), and Reward is an auto-save screen -
        // so without this, running this test overwrites the developer's real
        // in-progress run save with this test's fixture state.
        using var saveGuard = RunSaveGuard.Protect();
        // And pin the screen change to a hard cut, so this test does not
        // depend on whether the machine running it has Reduce Motion set.
        using var cutGuard = HardCutGuard.Protect();

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
