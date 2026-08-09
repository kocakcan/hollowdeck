using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for multi-act runs: acts.json loads and is internally sane,
// RunState.AdvanceAct moves a run to the next chapter without losing the deck
// it was built with, and only the final act's boss ends the run.
//
// MapSmokeTest covers the generated graph per act (shape, connectivity, boss
// pools); this suite is about the run-level progression around it. Run via
// `godot --headless scenes/debug/ActSmokeTest.tscn`.
public partial class ActSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override async void _Ready()
    {
        // Captured before the boss-win test runs: its simulated Continue click
        // triggers ChangeSceneToFile, which replaces this node as the tree's
        // current scene - after that, GetTree() on `this` throws because `this`
        // is detached, and the run would hang with no summary and no Quit. The
        // SceneTree object itself survives the swap. Same reasoning (and the
        // same workaround) as Phase4ContentSmokeTest._Ready.
        var tree = GetTree();

        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestActsLoad();
        TestEveryActAuthorsItsPotionDropRates();
        TestActContentIsDistinct();
        TestNoSummonCrossesAnAct();
        TestNewRunStartsInFirstAct();
        TestAdvanceActKeepsProgressAndReplacesMap();
        TestFinalActDoesNotAdvance();
        TestFloorsAccumulateAcrossActs();
        TestEveryEnemyIsReachableFromSomeAct();
        await TestBossWinAdvancesTheActThroughCombatScreen(tree);

        GD.Print($"ActSmokeTest: {_pass} passed, {_fail} failed");
        tree.Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void TestActsLoad()
    {
        Check("acts_load", ActDatabase.Count == 3, $"count={ActDatabase.Count}");

        var ids = ActDatabase.All.Select(a => a.Id).ToList();
        Check("act_ids_are_unique", ids.Distinct().Count() == ids.Count, $"ids=[{string.Join(",", ids)}]");
        Check("every_act_is_named", ActDatabase.All.All(a => a.Name.Length > 0), "an act has no name");

        // 3 is the floor (a Combat floor, the forced Rest, the Boss) - anything
        // shorter and MapGenerator's fixed end-of-act floors would collide.
        Check("every_act_is_long_enough", ActDatabase.All.All(a => a.FloorCount >= 3),
            $"floorCounts=[{string.Join(",", ActDatabase.All.Select(a => a.FloorCount))}]");

        // Backdrops degrade to no backdrop if the tile is missing, which is
        // silent - so the ids are checked here instead.
        var missingArt = ActDatabase.All
            .SelectMany(a => new[] { a.MapBackground, a.CombatBackground })
            .Where(tile => !ResourceLoader.Exists($"res://assets/backgrounds/{tile}.png"))
            .ToList();
        Check("every_act_backdrop_tile_exists", missingArt.Count == 0, string.Join(", ", missingArt));
    }

    // The potion drop rates, and the reason these three checks exist at all:
    // both fields default to 0, and unlike every other absent-is-zero field in
    // the data layer, 0 here is not a benign reading of an older act - it
    // silently switches the whole drop feature off for that act. Nothing
    // throws, no screen changes, and every other suite stays green. A typo in
    // one key in acts.json is the entire failure mode.
    private void TestEveryActAuthorsItsPotionDropRates()
    {
        var unauthored = ActDatabase.All
            .Where(a => a.PotionDropPercent <= 0 || a.ElitePotionDropPercent <= 0)
            .Select(a => $"{a.Id} {a.PotionDropPercent}/{a.ElitePotionDropPercent}")
            .ToList();
        Check("every_act_authors_a_potion_drop_rate", unauthored.Count == 0,
            string.Join(", ", unauthored));

        var overHundred = ActDatabase.All
            .Where(a => a.PotionDropPercent > 100 || a.ElitePotionDropPercent > 100)
            .Select(a => $"{a.Id} {a.PotionDropPercent}/{a.ElitePotionDropPercent}")
            .ToList();
        Check("potion_drop_rates_are_percentages", overHundred.Count == 0,
            string.Join(", ", overHundred));

        // Catches the transposed pair, which is otherwise invisible: both keys
        // present, both in range, both plausible, and elites quietly the
        // stingier room. An elite is where a potion actually gets spent.
        var inverted = ActDatabase.All
            .Where(a => a.ElitePotionDropPercent < a.PotionDropPercent)
            .Select(a => $"{a.Id} normal={a.PotionDropPercent} elite={a.ElitePotionDropPercent}")
            .ToList();
        Check("elite_potion_drops_at_least_as_often_as_a_normal_fight", inverted.Count == 0,
            string.Join(", ", inverted));
    }

    // Three acts that draw from the same enemies would make a longer run feel
    // like the same act three times, which is the whole point of adding them.
    private void TestActContentIsDistinct()
    {
        var perAct = ActDatabase.All
            .Select(a => a.NormalEncounters.Concat(a.EliteEncounters).SelectMany(g => g).ToHashSet())
            .ToList();

        for (int i = 0; i < perAct.Count; i++)
        {
            for (int j = i + 1; j < perAct.Count; j++)
            {
                var shared = perAct[i].Intersect(perAct[j]).ToList();
                Check($"acts_{i + 1}_and_{j + 1}_share_no_enemies", shared.Count == 0,
                    $"shared=[{string.Join(",", shared)}]");
            }
        }

        var allBosses = ActDatabase.All.SelectMany(a => a.BossIds).ToList();
        Check("no_boss_appears_in_two_acts", allBosses.Distinct().Count() == allBosses.Count,
            $"bosses=[{string.Join(",", allBosses)}]");

        // Enemy HP should climb act over act, or "later act" means nothing
        // mechanically. Compared on the average of each pool rather than a
        // single enemy, so one tanky early elite doesn't fail it.
        var averageHp = ActDatabase.All
            .Select(a => a.NormalEncounters.SelectMany(g => g).Distinct()
                .Average(id => EnemyDatabase.Get(id).MaxHp))
            .ToList();
        bool climbing = averageHp.Zip(averageHp.Skip(1), (earlier, later) => later > earlier).All(x => x);
        Check("later_acts_have_tougher_normal_enemies", climbing,
            $"averageHp=[{string.Join(", ", averageHp.Select(h => h.ToString("0.0")))}]");

        // Variety, as a floor rather than a note in the roadmap. An act whose
        // normal pool is four enemies deep shows the player the same fight
        // three or four times in eight floors, and nothing else here notices:
        // the pools still resolve, they are still disjoint, they still climb.
        foreach (var act in ActDatabase.All)
        {
            int distinct = act.NormalEncounters.SelectMany(g => g).Distinct().Count();
            Check($"{act.Id}_offers_enough_normal_enemies", distinct >= 6, $"distinct normals={distinct}");
        }
    }

    private void TestNewRunStartsInFirstAct()
    {
        RngStreams.Init(1234);
        RunState.InitNewRun();

        Check("new_run_starts_in_act_one", RunState.ActIndex == 0, $"actIndex={RunState.ActIndex}");
        Check("new_run_is_not_final_act", !RunState.IsFinalAct, "a 3-act game must not start on its last act");
        Check("new_run_map_matches_act_one_length",
            RunState.MapNodes.Max(n => n.Floor) + 1 == ActDatabase.At(0).FloorCount,
            $"floors={RunState.MapNodes.Max(n => n.Floor) + 1}");
    }

    private void TestAdvanceActKeepsProgressAndReplacesMap()
    {
        RngStreams.Init(99);
        RunState.InitNewRun();

        // Stand in for a played act: a grown deck, a picked-up relic, spent HP,
        // and a walked path. None of it may be lost crossing into act 2.
        RunState.Deck.Add(CardDatabase.Get("cleave"));
        RunState.Gold = 240;
        RunState.PlayerCurrentHp = 20;
        var deckBefore = RunState.Deck.Count;
        var relicsBefore = RunState.Relics.Count;
        var firstAct = RunState.CurrentAct;
        var mapBefore = RunState.MapNodes;
        RunState.CurrentNodeId = RunState.MapNodes.First(n => n.Floor == 0).Id;
        RunState.VisitedNodeIds.Add(RunState.CurrentNodeId);

        var cleared = RunState.AdvanceAct();

        Check("advance_act_moves_to_next_act", RunState.ActIndex == 1, $"actIndex={RunState.ActIndex}");
        Check("advance_act_keeps_deck", RunState.Deck.Count == deckBefore, $"deck={RunState.Deck.Count}");
        Check("advance_act_keeps_relics", RunState.Relics.Count == relicsBefore, $"relics={RunState.Relics.Count}");
        Check("advance_act_keeps_gold", RunState.Gold == 240, $"gold={RunState.Gold}");
        Check("advance_act_replaces_map", !ReferenceEquals(RunState.MapNodes, mapBefore)
                                          && RunState.MapNodes.Max(n => n.Floor) + 1 == ActDatabase.At(1).FloorCount,
            $"floors={RunState.MapNodes.Max(n => n.Floor) + 1}, expected={ActDatabase.At(1).FloorCount}");
        Check("advance_act_resets_position", RunState.CurrentNodeId == "" && RunState.VisitedNodeIds.Count == 0,
            $"currentNode='{RunState.CurrentNodeId}', visited={RunState.VisitedNodeIds.Count}");

        // Node ids restart at f0_0 each act, so a stale visited set would grey
        // out the new act's opening nodes.
        Check("advance_act_map_is_the_new_acts_enemies",
            RunState.MapNodes.Where(n => n.Type == MapNodeType.Combat)
                .All(n => n.EnemyIds.All(id => ActDatabase.At(1).NormalEncounters.Any(g => g.Contains(id)))),
            "a combat node holds an enemy not in act 2's pools");

        int expectedMax = 50 + firstAct.ClearMaxHpBonus;
        Check("advance_act_raises_max_hp", RunState.PlayerMaxHp == expectedMax,
            $"maxHp={RunState.PlayerMaxHp}, expected={expectedMax}");
        // Derived from the act's own percentage rather than typed here, so this
        // keeps holding if the dial moves - but pinned to an exact HP rather
        // than a range, because "healed something, didn't overheal" passed at
        // 30% and at 100% alike and so said nothing about either. The heal is
        // computed off the *raised* max, which is what makes the current 100
        // mean a genuinely full bar and not the old ceiling.
        int expectedHp = Mathf.Min(expectedMax, 20 + firstAct.ClearHealPercent * expectedMax / 100);
        Check("advance_act_heals_by_the_acts_percentage_of_the_new_max",
            RunState.PlayerCurrentHp == expectedHp,
            $"hp={RunState.PlayerCurrentHp}/{RunState.PlayerMaxHp}, expected={expectedHp} " +
            $"({firstAct.ClearHealPercent}% of {expectedMax} from 20)");

        // What the reward banner prints. It has to be the HP actually restored,
        // not the nominal percentage, or the screen lies to a player who was
        // already near full.
        Check("act_clear_reports_the_hp_actually_restored",
            cleared?.Healed == expectedHp - 20,
            $"reported={cleared?.Healed}, restored={expectedHp - 20}");
    }

    private void TestFinalActDoesNotAdvance()
    {
        RngStreams.Init(7);
        RunState.InitNewRun();
        RunState.ActIndex = ActDatabase.Count - 1;

        Check("last_act_reports_final", RunState.IsFinalAct, $"actIndex={RunState.ActIndex}");

        var mapBefore = RunState.MapNodes;
        RunState.AdvanceAct();
        Check("advance_act_on_final_act_is_a_no_op",
            RunState.ActIndex == ActDatabase.Count - 1 && ReferenceEquals(RunState.MapNodes, mapBefore),
            $"actIndex={RunState.ActIndex}");

        RunState.ActIndex = 0;
    }

    // RunScore's Floors Climbed reads MaxFloorReached, and each act renumbers
    // its floors from 0 - so without the FloorsInPreviousActs offset, entering
    // act 2 would score *less* than finishing act 1.
    private void TestFloorsAccumulateAcrossActs()
    {
        RngStreams.Init(3);
        RunState.InitNewRun();

        int act1Floors = RunState.CurrentAct.FloorCount;
        RunState.Stats.MaxFloorReached = act1Floors;

        RunState.AdvanceAct();
        Check("floors_in_previous_acts_tracks_cleared_act", RunState.Stats.FloorsInPreviousActs == act1Floors,
            $"floorsInPreviousActs={RunState.Stats.FloorsInPreviousActs}, act1={act1Floors}");

        // What MapScreen.OnNodeChosen computes when the first node of act 2 is
        // entered (floor 0 there).
        int reported = Mathf.Max(RunState.Stats.MaxFloorReached, RunState.Stats.FloorsInPreviousActs + 0 + 1);
        Check("first_floor_of_act_two_outranks_last_floor_of_act_one", reported == act1Floors + 1,
            $"reported={reported}, act1={act1Floors}");

        RunState.ActIndex = 0;
    }

    // The tests above call AdvanceAct directly. This one goes through the path a
    // player actually takes - win a boss fight in CombatScreen, click Continue -
    // because that branch (final act -> Victory, otherwise advance) is where a
    // three-act run either works or silently ends after act 1.
    //
    // Same shape as Phase4ContentSmokeTest's elite-reward test, including the
    // one harmless "parent node is busy" engine error from OnContinuePressed
    // calling ChangeSceneToFile inside this test's own call stack.
    private async Task TestBossWinAdvancesTheActThroughCombatScreen(SceneTree tree)
    {
        // OnContinuePressed lands on Reward, which is an auto-save screen, so
        // this test would otherwise overwrite the developer's real in-progress
        // run save just by running.
        using var saveGuard = RunSaveGuard.Protect();
        // And pin the screen change to a hard cut, so this test does not
        // depend on whether the machine running it has Reduce Motion set.
        using var cutGuard = HardCutGuard.Protect();
        try
        {
            RngStreams.Init(555);
            RunState.InitNewRun();
            RunState.Deck = new List<CardDefinition> { CardDatabase.Get("strike"), CardDatabase.Get("strike") };

            var bossId = RunState.CurrentAct.BossIds[0];
            CombatContext.EnemyDefinitionIds = new List<string> { bossId };
            CombatContext.IsElite = false;
            CombatContext.IsBoss = true;
            CombatContext.GoldReward = RunState.CurrentAct.BossGold;

            var instance = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
            AddChild(instance);
            var combat = instance.GetNode<CombatManager>("CombatManager");

            // A 150 HP boss would take dozens of paced turns to chew through with
            // two Strikes; the win *branch* is what's under test, not the damage
            // math, so the fight is started for real and then shortened.
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
            Check("boss_fight_reaches_combat_end", combat.State == CombatState.CombatEnd, $"state={combat.State}");

            int floorsBefore = RunState.Stats.FloorsInPreviousActs;
            instance.GetNode<Button>("CombatEndPanel/ContinueButton").EmitSignal(Button.SignalName.Pressed);

            Check("act_one_boss_win_advances_to_act_two", RunState.ActIndex == 1, $"actIndex={RunState.ActIndex}");
            Check("act_one_boss_win_does_not_end_the_run",
                RunManager.Instance.CurrentScreen == RunManager.ScreenState.Reward,
                $"screen={RunManager.Instance.CurrentScreen}");
            Check("act_one_boss_win_banks_its_floors", RunState.Stats.FloorsInPreviousActs > floorsBefore,
                $"floorsInPreviousActs={RunState.Stats.FloorsInPreviousActs}");
            Check("boss_win_grants_a_relic", RewardContext.GuaranteedRelic is not null,
                "bosses should guarantee a relic like elites do");
            Check("boss_win_counted_in_stats", RunState.Stats.BossesSlain == 1,
                $"bossesSlain={RunState.Stats.BossesSlain}");

            instance.QueueFree();
        }
        finally
        {
            RunState.ActIndex = 0;
        }
    }

    // A definition nothing can roll is dead content: it would never appear in a
    // run, and no test above would notice.
    private void TestEveryEnemyIsReachableFromSomeAct()
    {
        var reachable = ActDatabase.All
            .SelectMany(a => a.NormalEncounters.Concat(a.EliteEncounters).SelectMany(g => g).Concat(a.BossIds))
            .ToHashSet();
        var orphans = EnemyDatabase.All.Select(e => e.Id).Where(id => !reachable.Contains(id)).ToList();
        Check("no_enemy_is_unreachable", orphans.Count == 0, $"unreferenced: {string.Join(", ", orphans)}");
    }

    // summon_enemy is a *second* way an enemy id reaches a fight, and it does
    // not go through an act's encounter pools - so it is the one route that can
    // put act 3 content into an act 1 room without any of the assertions above
    // seeing it. Per-act distinctness is the half this file owns, and it stops
    // being true the moment a summon crosses an act.
    //
    // Enforced as "the summoner and its summon share every act they appear in",
    // which is the strongest form available: an escort like rot_hound is in one
    // act's normal *and* elite pools, so membership is a set rather than an
    // index.
    private void TestNoSummonCrossesAnAct()
    {
        var actsOf = new Dictionary<string, HashSet<string>>();
        foreach (var act in ActDatabase.All)
        {
            var ids = act.NormalEncounters.Concat(act.EliteEncounters).SelectMany(g => g).Concat(act.BossIds);
            foreach (var id in ids)
            {
                if (!actsOf.TryGetValue(id, out var set)) actsOf[id] = set = new HashSet<string>();
                set.Add(act.Id);
            }
        }

        var problems = new List<string>();
        foreach (var def in EnemyDatabase.All)
        {
            var summoned = def.Moves.Concat(def.EnrageMoves).SelectMany(m => m.Effects)
                .Concat(def.OnDeath)
                .Where(e => e.Action == "summon_enemy" && e.EnemyId is { Length: > 0 })
                .Select(e => e.EnemyId!)
                .Distinct();

            foreach (var id in summoned)
            {
                var summonerActs = actsOf.GetValueOrDefault(def.Id, new HashSet<string>());
                var summonedActs = actsOf.GetValueOrDefault(id, new HashSet<string>());
                if (!summonerActs.SetEquals(summonedActs))
                {
                    problems.Add($"{def.Id} ({string.Join("/", summonerActs)}) summons "
                        + $"{id} ({string.Join("/", summonedActs)})");
                }
            }
        }

        Check("no_summon_crosses_an_act", problems.Count == 0, string.Join("; ", problems));
    }
}
