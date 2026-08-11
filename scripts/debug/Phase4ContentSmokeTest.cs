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
        TestWakePickerSleepsUntilDamaged();
        TestEveryAiTypeMapsToItsOwnPicker();
        TestNoWeightedMoveRunsPastTheCap();
        TestAOneMovePickerTerminates();
        TestNoDormantMoveGrantsBlock();
        TestDormantAndWakeOnDamageImplyEachOther();
        TestEveryIntentTelegraphsWhatItResolves();
        TestNoEnemyMoveUsesACardOnlyScope();
        TestEverySummonNamesARealEnemyAndTerminates();
        TestIntentLabelsAreReadFromTheMove();
        await TestSummonJoinsTheFightWithoutActingThisTurn();
        await TestDamageWakesTheSleeperOnThePlayersTurn();
        await TestWakingMidEnemyTurnDoesNotChangeAnAlreadyTelegraphedMove();
        await TestEscapeRemovesAnEnemyWithoutCountingAKill();
        await TestDeathBeatsEscapeWhenBothLandInOneMove();
        await TestOnDeathResolvesBeforeTheFightIsScored();
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

    // The enrage picker's mirror image, and the three properties that are not
    // obvious from reading it: the wake is an HP *loss*, not an attack; it is
    // permanent once it happens; and the dormant list loops rather than falling
    // through to the awake one on its own.
    private void TestWakePickerSleepsUntilDamaged()
    {
        var picker = new WakeOnDamageIntentPicker();
        var husk = new EnemyCombatant
        {
            Name = "Husk", MaxHp = 92, CurrentHp = 92,
            Definition = EnemyDatabase.Get("gilded_husk"),
        };
        husk.IntentPicker = picker;

        // Picked into a list first, deliberately: PickNext advances the picker,
        // so calling it inside a List.Exists predicate runs it once per
        // candidate move and asserts against a different pick each time.
        var dormant = new List<string>();
        for (int i = 0; i < 5; i++) dormant.Add(picker.PickNext(husk).MoveId);
        Check("wake_picker_loops_its_dormant_list_while_untouched",
            dormant.TrueForAll(id => husk.Definition.Moves.Exists(m => m.MoveId == id)),
            $"a full-HP sleeper played {string.Join(", ", dormant)}");

        Check("wake_picker_does_not_wake_on_a_hit_its_block_ate",
            !picker.TryAdvancePhase(husk), "woke with no HP lost");

        husk.CurrentHp = 90;
        Check("wake_picker_wakes_on_hp_actually_lost", picker.TryAdvancePhase(husk),
            "still dormant after losing HP");
        Check("wake_picker_reports_the_transition_only_once",
            !picker.TryAdvancePhase(husk), "asked to re-telegraph twice for one wake");

        // Driven through a synthetic multi-move dormant list because the only
        // authored sleeper has one, which parks the picker's cursor at 0 and
        // hides the whole question. On a longer dormant list a wake that does
        // not reset the index enters the awake list mid-way - or past its end,
        // if the awake list is the shorter of the two.
        var deepSleeper = new EnemyCombatant
        {
            Name = "Deep Sleeper", MaxHp = 40, CurrentHp = 40,
            Definition = new EnemyDefinition
            {
                Id = "deep_sleeper",
                AiType = "wake_on_damage",
                Moves = new List<EnemyMove> { Dormant("z1"), Dormant("z2"), Dormant("z3") },
                // As long as the dormant list, so a carried-over cursor lands on
                // a real move and this reports which one. Shorter would read off
                // the end instead, which is the same bug arriving as a thrown
                // exception - a red suite either way, but a TIMEOUT rather than
                // a sentence naming the move it played.
                EnrageMoves = new List<EnemyMove>
                {
                    Dormant("awake_first"), Dormant("awake_second"), Dormant("awake_third"),
                },
            },
        };
        var deepPicker = new WakeOnDamageIntentPicker();
        deepPicker.PickNext(deepSleeper);
        deepPicker.PickNext(deepSleeper);
        deepSleeper.CurrentHp = 39;
        deepPicker.TryAdvancePhase(deepSleeper);
        var firstAwake = deepPicker.PickNext(deepSleeper).MoveId;
        Check("waking_restarts_the_awake_list_from_its_first_move", firstAwake == "awake_first",
            $"played {firstAwake} - the dormant cursor carried into the awake list");

        // Healed back to full: the latch is what keeps it awake. Nothing heals
        // an enemy today, so this is the guard for the first thing that does.
        husk.CurrentHp = 92;
        var played = new List<string>();
        for (int i = 0; i < 5; i++) played.Add(picker.PickNext(husk).MoveId);
        Check("wake_picker_stays_awake_once_woken",
            played.TrueForAll(id => husk.Definition.EnrageMoves.Exists(m => m.MoveId == id)),
            $"played {string.Join(", ", played)}");
    }

    private static EnemyMove Dormant(string moveId) => new()
    {
        MoveId = moveId,
        Intent = new EnemyIntent { Type = IntentType.Dormant, DisplayAmount = 1 },
        Effects = new List<EffectSpec>(),
    };

    // EnemyFactory falls back to sequential on an unknown aiType, which is the
    // right behaviour at runtime (a typo must not throw out of a fight) and a
    // silent no-op at authoring time: the enemy simply plays its list in order
    // and every suite stays green. Driving the real factory covers the arm and
    // the string in one assertion.
    private void TestEveryAiTypeMapsToItsOwnPicker()
    {
        var problems = EnemyDatabase.All
            .Where(d => d.AiType != "sequential"
                        && EnemyFactory.Create(d).IntentPicker is SequentialLoopingIntentPicker)
            .Select(d => $"{d.Id}: aiType '{d.AiType}' fell back to sequential")
            .ToList();

        Check("every_aitype_maps_to_its_own_picker", problems.Count == 0, string.Join("; ", problems));
    }

    // The run cap is the whole of WeightedRandomIntentPicker's anti-repeat rule
    // now, and it is the one property of it a player can actually observe.
    // Driven over every authored weighted_random enemy rather than a chosen one,
    // because the two rules this replaced were each correct at one move count
    // and wrong at the other - a sweep is what notices a cap that only binds at
    // three moves. 400 picks per enemy is far past the point an uncapped chain
    // would have run one move MaxRun + 1 times.
    private void TestNoWeightedMoveRunsPastTheCap()
    {
        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All.Where(d => d.AiType == "weighted_random"))
        {
            var picker = new WeightedRandomIntentPicker();
            var enemy = new EnemyCombatant { Name = def.Name, Definition = def };

            string lastId = "";
            int run = 0;
            int longest = 0;
            for (int i = 0; i < 400; i++)
            {
                string id = picker.PickNext(enemy).MoveId;
                run = id == lastId ? run + 1 : 1;
                lastId = id;
                if (run > longest) longest = run;
            }

            if (longest > WeightedRandomIntentPicker.MaxRun)
                problems.Add($"{def.Id}: ran '{lastId}' {longest} times, cap is {WeightedRandomIntentPicker.MaxRun}");

            // A cap that binds every turn is alternation wearing a new name -
            // the exact bug the old rule shipped. Two moves must both be live.
            if (def.Moves.Count > 1 && longest < 2)
                problems.Add($"{def.Id}: never repeated a move in 400 picks - the cap is behaving as alternation");
        }

        Check("no_weighted_move_runs_past_the_cap", problems.Count == 0, string.Join("; ", problems));
    }

    // The cap has nothing to exclude into when an enemy has one move, and the
    // candidate filter would empty the list rather than yield. Nothing in
    // enemies.json reaches this today, which is exactly why it is worth an
    // assertion: a one-move weighted enemy is authorable and would hang or
    // throw on its second pick.
    private void TestAOneMovePickerTerminates()
    {
        var lonely = new EnemyDefinition
        {
            Id = "lonely",
            Name = "Lonely",
            MaxHp = 10,
            AiType = "weighted_random",
            Moves = new List<EnemyMove> { Dormant("only_move") },
        };
        var picker = new WeightedRandomIntentPicker();
        var enemy = new EnemyCombatant { Name = lonely.Name, Definition = lonely };

        var played = new List<string>();
        for (int i = 0; i < 5; i++) played.Add(picker.PickNext(enemy).MoveId);

        Check("a_one_move_weighted_picker_keeps_playing_its_only_move",
            played.TrueForAll(id => id == "only_move"),
            $"played {string.Join(", ", played)}");
    }

    // The one authoring rule WakeOnDamageIntentPicker cannot enforce for itself,
    // and the reason it is worth a suite: HP loss is what wakes a sleeper, so a
    // dormant move that grants Block compounds every turn it is left alone. Once
    // that Block passes the player's per-hit damage the sleeper cannot be woken,
    // cannot be killed, and the fight has no exit at all - there is no flee.
    // Every other dormant grant is a cost to the player; a defensive one is a
    // soft-lock waiting for a slow deck.
    private void TestNoDormantMoveGrantsBlock()
    {
        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All)
        {
            foreach (var move in def.Moves.Concat(def.EnrageMoves))
            {
                if (move.Intent.Type != IntentType.Dormant) continue;
                foreach (var spec in move.Effects)
                {
                    bool defensive = spec.Action == "gain_block"
                        || (spec.Action == "apply_status"
                            && spec.Status is "Metallicize" or "Plating");
                    if (defensive) problems.Add($"{def.Id}/{move.MoveId}: {spec.Action} {spec.Status}");
                }
            }
        }

        Check("no_dormant_move_grants_block", problems.Count == 0,
            string.Join("; ", problems) + " - a sleeper whose Block outgrows the "
            + "player's damage can never be woken and never killed");
    }

    // The intent type and the picker are two halves of one mechanic joined by
    // nothing but authoring, and each half is silent without the other. A
    // Dormant move on a sequential enemy telegraphs "hit me to wake me" about an
    // enemy that was never asleep; a wake_on_damage enemy whose dormant list is
    // labelled Buff is asleep with no way for the player to know it. Both
    // compile, both render, and every other assertion in the repo passes.
    private void TestDormantAndWakeOnDamageImplyEachOther()
    {
        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All)
        {
            bool sleeper = def.AiType == "wake_on_damage";

            // A Dormant move anywhere else, including in a sleeper's own *awake*
            // list - a second phase that telegraphs sleep has already woken.
            foreach (var move in def.EnrageMoves.Concat(sleeper ? new List<EnemyMove>() : def.Moves))
            {
                if (move.Intent.Type == IntentType.Dormant)
                {
                    problems.Add($"{def.Id}/{move.MoveId}: Dormant outside a sleeper's dormant list");
                }
            }

            if (!sleeper) continue;
            foreach (var move in def.Moves.Where(m => m.Intent.Type != IntentType.Dormant))
            {
                problems.Add($"{def.Id}/{move.MoveId}: {move.Intent.Type} in a dormant list");
            }
        }

        Check("dormant_and_wake_on_damage_imply_each_other", problems.Count == 0,
            string.Join("; ", problems));
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
                    // The authored number is the copy count, the same reading
                    // add_card's Amount has.
                    IntentType.Summon => move.Effects.FirstOrDefault(e => e.Action == "summon_enemy"),
                    // An escape's number is the gold it takes, which is
                    // authored *negative* on the spec (gain_gold is one action
                    // rather than one per sign) and shown positive on the row.
                    // A theft-free escape falls back to the escape spec itself,
                    // whose Amount is 0 - which is then what it must telegraph.
                    IntentType.Escape => move.Effects.FirstOrDefault(e => e.Action == "gain_gold")
                        ?? move.Effects.FirstOrDefault(e => e.Action == "escape"),
                    // Same backing as a Buff, and requiring one is the point
                    // rather than an inherited detail: it is what stops a
                    // dormant move being a free turn for the player. A sleeper
                    // has to charge for the turns it is left alone, or leaving
                    // it asleep is strictly better than waking it and the
                    // decision the mechanic exists for is not a decision.
                    IntentType.Dormant => move.Effects.FirstOrDefault(e =>
                        e.Scope == EffectScope.Self && (e.Action == "apply_status" || e.Action == "heal")),
                    _ => null,
                };

                if (backing is null)
                {
                    problems.Add($"{def.Id}/{move.MoveId}: {move.Intent.Type} intent with no effect behind it");
                    continue;
                }

                // Escape is the one intent whose spec and label legitimately
                // differ by a sign - the theft is authored as the player losing
                // gold and displayed as the amount lost. Every other intent
                // compares raw, so an Attack authored -5 stays a failure rather
                // than being waved through by a blanket Abs.
                int resolves = move.Intent.Type == IntentType.Escape
                    ? System.Math.Abs(backing.Amount)
                    : backing.Amount;

                if (resolves != move.Intent.DisplayAmount)
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

    // summon_enemy is the first effect whose id is resolved against a database
    // *during* an enemy turn rather than at load, so a typo in it is silent:
    // SummonEnemyEffect logs and returns, the move plays, and the fight is
    // simply one enemy lighter than it was authored to be. This is the sweep
    // that turns that into a red suite.
    //
    // The second half is a termination guard, and it is the more important one.
    // A summons B and B summons A is a fight that never ends: the roster cap
    // stops it filling memory but not from being unwinnable, and the cap is
    // there to hold a layout, not to catch an authoring loop. Refusing a summon
    // whose target itself summons keeps the chain one deep by construction.
    private void TestEverySummonNamesARealEnemyAndTerminates()
    {
        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All)
        {
            foreach (var move in def.Moves.Concat(def.EnrageMoves))
            {
                foreach (var spec in move.Effects.Concat(def.OnDeath).Where(e => e.Action == "summon_enemy"))
                {
                    if (spec.EnemyId is not { Length: > 0 } id)
                    {
                        problems.Add($"{def.Id}/{move.MoveId}: summon_enemy with no enemyId");
                        continue;
                    }

                    var summoned = EnemyDatabase.Find(id);
                    if (summoned is null)
                    {
                        problems.Add($"{def.Id}/{move.MoveId}: summons unknown enemy '{id}'");
                        continue;
                    }

                    if (spec.Amount < 1) problems.Add($"{def.Id}/{move.MoveId}: summons {spec.Amount} of '{id}'");

                    bool summonsBack = summoned.Moves.Concat(summoned.EnrageMoves)
                        .SelectMany(m => m.Effects)
                        .Concat(summoned.OnDeath)
                        .Any(e => e.Action == "summon_enemy");
                    if (summonsBack) problems.Add($"{def.Id}/{move.MoveId}: summons '{id}', which itself summons");
                }
            }
        }

        Check("every_summon_names_a_real_enemy_and_terminates", problems.Count == 0,
            string.Join("; ", problems));
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

    // ward_acolyte opens on call_the_faithful, so one ended turn is the whole
    // setup. Two things are being pinned, and the second is the one that would
    // ship broken quietly:
    //
    //  - the roster actually grows, and the newcomer arrives already
    //    telegraphing rather than sitting on a blank intent for a turn;
    //  - it does *not* act on the turn it lands. That is a property of
    //    ResolveEnemyTurnAsync iterating the _enemyTurnOrder snapshot taken in
    //    TryEndTurn, not of anything in SummonEnemy, so a well-meaning edit
    //    that walks Enemies directly would hit the player with a move they
    //    were never shown and no other assertion would notice.
    private async Task TestSummonJoinsTheFightWithoutActingThisTurn()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 90, CurrentHp = 90, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("defend") }),
        };
        var acolyte = EnemyFactory.Create(EnemyDatabase.Get("ward_acolyte"));

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { acolyte }, new List<RelicInstance>());

        Check("summoner_telegraphs_the_summon_before_it_happens",
            acolyte.CurrentMove?.Intent.Type == IntentType.Summon,
            $"opening intent was {acolyte.CurrentMove?.Intent.Type}");

        int hpBefore = player.CurrentHp;
        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);

        Check("summon_grows_the_roster_mid_fight", combat.Enemies.Count == 2,
            $"{combat.Enemies.Count} enemies after the summon");

        var minion = combat.Enemies.FirstOrDefault(e => e != acolyte);
        Check("the_summoned_enemy_is_what_the_move_named",
            minion?.Definition.Id == "slime", $"summoned {minion?.Definition.Id}");
        Check("the_summoned_enemy_arrives_already_telegraphing",
            minion?.CurrentMove is not null,
            "a blank intent panel is the telegraph bug in its most literal form");
        Check("the_summoned_enemy_does_not_act_on_the_turn_it_lands",
            player.CurrentHp == hpBefore,
            $"player took {hpBefore - player.CurrentHp} damage on the summon turn - the "
            + "_enemyTurnOrder snapshot in TryEndTurn is what prevents this");

        combat.QueueFree();
    }

    // gaol_rat's fourth move is snatch_and_flee. Driving four turns costs four
    // enemy-turn waits, which is cheap next to what the alternative would be:
    // asserting EscapeEffect sets a bool, which proves nothing about the two
    // things that actually matter - that the enemy leaves, and that leaving is
    // not scored as a kill.
    // The half of waking that lives in CombatManager rather than in the picker,
    // and the whole reason the mechanic reads: the intent flips *while the
    // player still holds the turn*. If it waited for the enemy's own turn
    // boundary, hitting a sleeper would look exactly like hitting anything else
    // and the decision would only exist in the rules.
    private async Task TestDamageWakesTheSleeperOnThePlayersTurn()
    {
        var husk = EnemyFactory.Create(EnemyDatabase.Get("gilded_husk"));
        var strike = CardDatabase.Get("strike");
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 200, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(Enumerable.Repeat(strike, 8).ToList()),
        };

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { husk }, new List<RelicInstance>());

        Check("a_sleeper_opens_the_fight_telegraphing_dormant",
            husk.CurrentMove?.Intent.Type == IntentType.Dormant,
            $"opening intent was {husk.CurrentMove?.Intent.Type}");

        var card = player.Piles.Hand.First(c => c.Definition.Id == "strike");
        combat.TryPlayCard(card, husk);

        Check("striking_a_sleeper_wakes_it_before_the_turn_ends",
            husk.CurrentMove is { } move && husk.Definition.EnrageMoves.Exists(m => m.MoveId == move.MoveId),
            $"still telegraphing {husk.CurrentMove?.MoveId} after taking a hit");
        Check("the_woken_intent_is_reachable_while_the_player_still_has_the_turn",
            combat.State == CombatState.PlayerTurn, $"state={combat.State}");

        combat.QueueFree();
    }

    // The other side of that gate, and the one a future edit is likeliest to
    // break. An enemy can be woken during the *enemy* turn - a Poison tick, a
    // Thorns prick, a relic retaliating - and at that point it may already be
    // holding a telegraph the player has committed a turn against. Waking must
    // not re-pick there, or the roster resolves a move nobody was shown.
    //
    // Staged with Poison, which ApplyPoisonTick applies at the top of the
    // sleeper's own turn in ResolveEnemyTurnAsync: it loses HP and then acts,
    // all inside one enemy turn.
    private async Task TestWakingMidEnemyTurnDoesNotChangeAnAlreadyTelegraphedMove()
    {
        var husk = EnemyFactory.Create(EnemyDatabase.Get("gilded_husk"));
        husk.AddStatus(StatusType.Poison, 5);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 200, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("defend") }),
        };

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { husk }, new List<RelicInstance>());

        var telegraphed = husk.CurrentMove!;
        int hpBefore = player.CurrentHp;
        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);

        Check("the_poison_woke_it", husk.CurrentHp < husk.MaxHp, $"hp={husk.CurrentHp}");
        // The dormant move is the one that resolved: it grants Strength and
        // deals nothing, so an awake move landing instead would have taken HP.
        Check("an_enemy_woken_mid_turn_still_resolves_what_it_telegraphed",
            player.CurrentHp == hpBefore && husk.GetStatus(StatusType.Strength) > 0,
            $"player hp {hpBefore} -> {player.CurrentHp}, telegraphed {telegraphed.MoveId}");
        // The *first* awake move, not merely an awake one. "Some EnrageMoves
        // entry" cannot fail: without the ResolvingCard gate the settle pass
        // wakes it mid-enemy-turn and the regular AdvanceEnemyIntent below runs
        // a second time, which still lands on an awake move - just one move
        // further on, with the opener silently eaten.
        Check("and_telegraphs_its_new_phase_before_the_player_acts_again",
            husk.CurrentMove?.MoveId == husk.Definition.EnrageMoves[0].MoveId,
            $"telegraphing {husk.CurrentMove?.MoveId}, expected {husk.Definition.EnrageMoves[0].MoveId} "
            + "- an awake move further down the list means the phase advanced twice");

        combat.QueueFree();
    }

    private async Task TestEscapeRemovesAnEnemyWithoutCountingAKill()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 200, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("defend") }),
        };
        var rat = EnemyFactory.Create(EnemyDatabase.Get("gaol_rat"));

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { rat }, new List<RelicInstance>());

        int goldBefore = RunState.Gold = 100;

        // bristle, gnaw, filth_bite, then snatch_and_flee.
        for (int turn = 0; turn < 4 && combat.State != CombatState.CombatEnd; turn++)
        {
            combat.TryEndTurn();
            await WaitForEnemyTurnToResolve(combat);
        }

        Check("an_escaping_enemy_leaves_the_fight", combat.Enemies.Count == 0,
            $"{combat.Enemies.Count} enemies still present");
        Check("an_escaped_enemy_is_alive_not_dead", !rat.IsDead && rat.HasEscaped,
            $"hp={rat.CurrentHp}, hasEscaped={rat.HasEscaped}");
        Check("escaping_is_not_scored_as_a_kill", combat.EnemiesKilled == 0,
            $"EnemiesKilled={combat.EnemiesKilled} - the tally feeds RunScore, and a fight "
            + "you failed to finish must not pay out as one you did");
        // Read off the move rather than restated, so retuning the theft is one
        // edit in enemies.json. It was a literal 25 and the retune to 40 broke
        // this check, which is the drift a derived number cannot have.
        int stolen = -EnemyDatabase.Get("gaol_rat").Moves
            .First(m => m.MoveId == "snatch_and_flee").Effects
            .Where(e => e.Action == "gain_gold" && e.Amount < 0)
            .Sum(e => e.Amount);
        Check("the_thief_leaves_with_the_gold", RunState.Gold == goldBefore - stolen,
            $"gold={RunState.Gold}, expected {goldBefore - stolen} (theft of {stolen})");
        Check("an_emptied_board_still_ends_the_fight", combat.Outcome == CombatOutcome.Win,
            $"outcome={combat.Outcome}");

        combat.QueueFree();
    }

    // Death wins over escape, driven through the one arrangement that can
    // produce both flags on one enemy in one move: a hit-and-run thief that
    // deals damage before it flees, against a player holding Thorns. The
    // retaliation resolves inside ExecuteEffect for the damage spec and kills
    // the thief, and the escape spec then runs on a corpse.
    //
    // Synthetic because nothing authors it yet - snatch_and_flee deals no
    // damage, so today this is latent. A rule that only holds while the content
    // happens not to reach it is not a rule, and the obvious next thief is
    // exactly this move.
    //
    // What the guard buys, concretely: without it the HasEscaped sweep in
    // ResolveDeaths claims the body before the IsDead sweep, so a kill the
    // player earned never reaches EnemiesKilled - and from there RunScore -
    // while CombatScreen plays the runaway tween over a death.
    private async Task TestDeathBeatsEscapeWhenBothLandInOneMove()
    {
        var thief = new EnemyDefinition
        {
            Id = "test_hit_and_run",
            Name = "Test Thief",
            MaxHp = 3,
            Moves =
            {
                new EnemyMove
                {
                    MoveId = "snatch",
                    Intent = new EnemyIntent { Type = IntentType.Escape, DisplayAmount = 0 },
                    Effects =
                    {
                        new EffectSpec { Action = "deal_damage", Amount = 1, Scope = EffectScope.Target },
                        new EffectSpec { Action = "escape", Scope = EffectScope.Self },
                    },
                },
            },
        };

        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("defend") }),
        };
        // More Thorns than the thief has HP, so the retaliation is lethal on the
        // damage spec - i.e. before the escape spec in the same move runs.
        player.AddStatus(StatusType.Thorns, 5);

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { EnemyFactory.Create(thief) },
            new List<RelicInstance>());
        var runaway = combat.Enemies[0];

        combat.TryEndTurn();
        await WaitForEnemyTurnToResolve(combat);

        Check("thorns_kills_the_thief_mid_move", runaway.IsDead,
            $"hp={runaway.CurrentHp} - the test is meaningless unless the retaliation "
            + "actually lands before the escape spec resolves");
        Check("a_corpse_cannot_escape", !runaway.HasEscaped,
            "EscapeEffect flagged an enemy that was already dead - the guard is what "
            + "keeps the two RemoveAll sweeps in ResolveDeaths disjoint");
        Check("a_thorns_kill_still_counts_as_a_kill", combat.EnemiesKilled == 1,
            $"EnemiesKilled={combat.EnemiesKilled} - the escape sweep runs first, so an "
            + "enemy flagged both ways is removed without ever reaching the kill tally, "
            + "and RunScore silently loses the points");

        combat.QueueFree();
    }

    // The ordering rule in ResolveDeathsAndSettle, driven rather than asserted
    // about: a slime bursts into Poison as it dies, and if the burst is what
    // finishes the player then the fight is a loss even though the board is
    // empty. Win-first would have made that onDeath a silent no-op on the one
    // enemy it matters most on - the last one alive.
    private async Task TestOnDeathResolvesBeforeTheFightIsScored()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("strike") }),
        };
        var slime = EnemyFactory.Create(EnemyDatabase.Get("slime"));
        slime.CurrentHp = 1;

        var combat = new CombatManager();
        AddChild(combat);
        combat.StartCombat(player, new List<EnemyCombatant> { slime }, new List<RelicInstance>());

        var strike = player.Piles.Hand.First(c => c.Definition.Id == "strike");
        combat.TryPlayCard(strike, slime);

        Check("killing_the_last_enemy_still_fires_its_on_death",
            player.GetStatus(StatusType.Poison) == 2,
            $"poison={player.GetStatus(StatusType.Poison)} - a Win checked before onDeath "
            + "would leave this at 0 and nothing would throw");
        Check("the_fight_is_still_won_when_the_on_death_is_survivable",
            combat.Outcome == CombatOutcome.Win, $"outcome={combat.Outcome}");
        combat.QueueFree();

        // The ordering itself, which the authored content deliberately cannot
        // reach: every onDeath in enemies.json is a Poison the player still
        // gets a turn to answer. A synthetic definition is the honest way to
        // pin the *rule* without authoring a parting blow nobody can play
        // around - the rule has to hold whether or not content uses it.
        var lethalBurst = new EnemyDefinition
        {
            Id = "test_lethal_burst",
            Name = "Test Burst",
            MaxHp = 1,
            Moves = { new EnemyMove { MoveId = "wait", Intent = new EnemyIntent() } },
            OnDeath = { new EffectSpec { Action = "lose_hp", Amount = 99, Scope = EffectScope.Target } },
        };

        var dying = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 4, MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(new List<CardDefinition> { CardDatabase.Get("strike") }),
        };
        var second = new CombatManager();
        AddChild(second);
        second.StartCombat(dying, new List<EnemyCombatant> { EnemyFactory.Create(lethalBurst) },
            new List<RelicInstance>());

        second.TryPlayCard(dying.Piles.Hand.First(c => c.Definition.Id == "strike"),
            second.Enemies[0]);

        Check("a_lethal_on_death_loses_the_fight_the_kill_would_have_won",
            second.Outcome == CombatOutcome.Lose,
            $"outcome={second.Outcome}, playerHp={dying.CurrentHp} - the board was emptied and "
            + "the player died in the same pass; ResolveDeathsAndSettle checks Lose first");
        second.QueueFree();

        await Task.CompletedTask;
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
        // a player uses, so drive it the same way). RewardContext is fully
        // assigned before OnContinuePressed calls ChangeScreen, so the checks
        // below are accurate even though this logs one harmless "parent busy"
        // engine error - ChangeSceneToFile doesn't like being called on the
        // current scene from inside this test's own _Ready() call stack, which
        // none of the other debug smoke tests trigger.
        int relicsBefore = RunState.Relics.Count;
        int goldBefore = RunState.Gold;
        var continueButton = instance.GetNode<Button>("CombatEndPanel/ContinueButton");
        continueButton.EmitSignal(Button.SignalName.Pressed);

        // Exactly one, which is the elite half of the count that decides how the
        // reward row behaves. A boss offers three and the row opens a picker; an
        // elite offers one and the row hands it over. An elite drifting to three
        // would be silent here without the ==, since it is still "not null".
        Check("elite_reward_offers_a_guaranteed_relic", RewardContext.RelicChoices.Count == 1,
            $"RewardContext.RelicChoices had {RewardContext.RelicChoices.Count} entries, expected 1");

        // Offered, not granted - and that is the assertion now, not an
        // accident of where the test stops. Every reward on that screen is a
        // row the player claims, so a relic already in RunState by the time the
        // screen loads would be a row announcing something they already had.
        // ScreenSmokeTest.reward_relic_is_only_granted_when_claimed covers the
        // other half.
        Check("elite_reward_relic_is_not_granted_before_the_screen",
            RunState.Relics.Count == relicsBefore, $"relics={RunState.Relics.Count}");
        // Only that it was not banked. Whether this fixture seeded a nonzero
        // CombatContext.GoldReward is not this test's business - the amount is
        // ScreenSmokeTest's, on the row that shows it.
        Check("elite_reward_gold_is_not_banked_before_the_screen", RunState.Gold == goldBefore,
            $"gold went {goldBefore} -> {RunState.Gold} before the player claimed anything");
        Check("elite_reward_starts_with_nothing_claimed", RewardContext.Claimed.Count == 0,
            $"claimed={string.Join(",", RewardContext.Claimed)} - a stale claim from the last fight");

        instance.QueueFree();
    }
}
