using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Debug;

// The ascension ladder's data layer: the authored rows, the fold that turns
// them into a rung, and the two ends of that fold.
//
// The balance half of the ladder lives in BalanceSmokeTest, which is where the
// curve is. What is here is everything a twenty-row prose-and-numbers table can
// get wrong on its own: a row that moves nothing, a level that skips or
// repeats, a curse id naming a card that does not exist or - worse - one that
// is playable, and the fold silently failing to accumulate.
//
// Run via `godot --headless scenes/debug/AscensionSmokeTest.tscn`.
public partial class AscensionSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        AscensionDatabase.LoadAll();

        TestTheLadderIsAuthored();
        TestRungZeroIsIdentity();
        TestEveryRungChangesSomething();
        TestTheFoldAccumulates();
        TestOutOfRangeClamps();
        TestCursesAreRealAndUnplayable();
        TestTheModifierMethods();

        GD.Print($"AscensionSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void TestTheLadderIsAuthored()
    {
        var rungs = AscensionDatabase.All;

        Check("ladder_is_twenty_rungs", rungs.Count == 20, $"got {rungs.Count}");
        Check("max_level_matches_the_pool", AscensionDatabase.MaxLevel == rungs.Count,
            $"MaxLevel {AscensionDatabase.MaxLevel} against {rungs.Count} rows");

        // Contiguous from 1. A gap or a repeat would leave Effective folding a
        // different number of rows than the level says, and every rung above it
        // silently off by one.
        var levels = rungs.Select(r => r.Level).ToList();
        bool contiguous = levels.Count > 0 && levels.SequenceEqual(Enumerable.Range(1, levels.Count));
        Check("levels_are_contiguous_from_one", contiguous, $"got [{string.Join(", ", levels)}]");

        var unlabelled = rungs.Where(r => string.IsNullOrWhiteSpace(r.Label)).Select(r => r.Level);
        Check("every_rung_has_a_label", !unlabelled.Any(), $"unlabelled: {Join(unlabelled.Select(l => l.ToString()))}");

        // The setup screen and the balance report both print these, and the
        // display font is a pixel face with no glyph outside ASCII.
        var nonAscii = rungs.Where(r => r.Label.Any(c => c < 32 || c > 126)).Select(r => r.Level.ToString());
        Check("labels_are_ascii", !nonAscii.Any(), $"non-ascii on rungs: {Join(nonAscii)}");

        // At(level) is 1-based and At(1) must be the first row, not the second.
        Check("at_is_one_based", rungs.Count == 0 || AscensionDatabase.At(1).Level == 1,
            $"At(1) is level {(rungs.Count == 0 ? -1 : AscensionDatabase.At(1).Level)}");
    }

    private void TestRungZeroIsIdentity()
    {
        var zero = AscensionDatabase.Effective(0);

        // The single most important check in this file. A modifier struct that
        // nobody threads through compiles, prints a perfect balance report and
        // changes nothing the player sees; the mirror failure is a rung 0 that
        // is *not* identity, which would quietly move every band, threshold and
        // encounter cost the last four phases measured. Both are silent, and
        // this is the assertion that is not.
        Check("rung_zero_is_identity", zero.IsIdentity, $"got {zero}");
        Check("rung_zero_has_level_zero", zero.Level == 0, $"got {zero.Level}");
        Check("none_is_identity", AscensionModifiers.None.IsIdentity, "AscensionModifiers.None moved");

        // Identity is a property of the *methods*, not only of the fields - a
        // field left at 100 with a method that scales anyway is the same bug
        // wearing a green check.
        Check("rung_zero_leaves_enemy_hp_alone", zero.EnemyHp(37, false) == 37 && zero.EnemyHp(37, true) == 37,
            $"got {zero.EnemyHp(37, false)}/{zero.EnemyHp(37, true)}");
        Check("rung_zero_leaves_damage_alone", zero.EnemyDamage(11) == 11, $"got {zero.EnemyDamage(11)}");
        Check("rung_zero_leaves_prices_alone", zero.ShopPrice(150) == 150, $"got {zero.ShopPrice(150)}");
        Check("rung_zero_leaves_potions_alone", zero.PotionPercent(40) == 40, $"got {zero.PotionPercent(40)}");
        Check("rung_zero_leaves_the_heal_alone", zero.ClearHeal(100) == 100, $"got {zero.ClearHeal(100)}");
        Check("rung_zero_leaves_starting_hp_alone", zero.StartingMaxHp(50) == 50, $"got {zero.StartingMaxHp(50)}");
        Check("rung_zero_leaves_map_weights_alone",
            zero.EliteWeight(15) == 15 && zero.CombatWeight(50) == 50,
            $"got elite {zero.EliteWeight(15)}, combat {zero.CombatWeight(50)}");
    }

    private void TestEveryRungChangesSomething()
    {
        // A row that moves no field is a rung the player pays for and receives
        // nothing from - the failure BlessingSmokeTest's own
        // "every row measurably changes RunState" check exists to catch, one
        // content type over. Driven off the *fold* rather than off the row's
        // fields, so a field added to AscensionDefinition and forgotten in
        // AscensionDatabase.Resolve fails here rather than shipping inert.
        var inert = new List<string>();
        for (int level = 1; level <= AscensionDatabase.MaxLevel; level++)
        {
            if (AscensionDatabase.Effective(level) with { Level = level - 1 }
                == (AscensionDatabase.Effective(level - 1) with { Level = level - 1 }))
            {
                inert.Add(level.ToString());
            }
        }

        Check("every_rung_changes_something", inert.Count == 0, $"inert rungs: {Join(inert)}");
    }

    private void TestTheFoldAccumulates()
    {
        // Two rungs turn each knob at least twice in the authored ladder, so a
        // fold that overwrote instead of summing would come back with the last
        // rung's value rather than the total. Checked against the sum of the
        // rows rather than against a literal, so retuning the ladder does not
        // break this.
        int expectedDamage = 100 + AscensionDatabase.All.Sum(r => r.EnemyDamagePercent);
        int expectedHp = 100 + AscensionDatabase.All.Sum(r => r.EnemyHpPercent);
        int expectedCurses = AscensionDatabase.All.Sum(r => r.StartingCurseIds.Count);

        var top = AscensionDatabase.Effective(AscensionDatabase.MaxLevel);

        Check("the_fold_sums_damage", top.EnemyDamagePercent == expectedDamage,
            $"got {top.EnemyDamagePercent}, expected {expectedDamage}");
        Check("the_fold_sums_hp", top.EnemyHpPercent == expectedHp,
            $"got {top.EnemyHpPercent}, expected {expectedHp}");
        Check("the_fold_concatenates_curses", top.StartingCurseIds.Count == expectedCurses,
            $"got {top.StartingCurseIds.Count}, expected {expectedCurses}");
        Check("the_top_rung_knows_its_level", top.Level == AscensionDatabase.MaxLevel,
            $"got {top.Level}");

        // The ladder only ever takes. A rung that handed something back would
        // make the ladder non-monotone in a way BalanceSmokeTest's composite
        // measure cannot see, because six of the nine knobs are invisible to it.
        Check("the_ladder_never_gives", top.EnemyDamagePercent >= 100 && top.EnemyHpPercent >= 100
            && top.ShopPricePercent >= 100 && top.BossHpBonusPercent >= 0
            && top.StartingMaxHpDelta <= 0 && top.ClearHealPercentDelta >= 0
            && top.EliteWeightDelta >= 0 && top.PotionDropPercentDelta >= 0,
            $"got {top}");
    }

    private void TestOutOfRangeClamps()
    {
        // Both of these numbers arrive from a save file - the run save's
        // AscensionLevel and the meta save's limit - so the clamp is the guard,
        // not the caller. Same argument CardPool.WeightOf makes about the skip
        // streak, and for the same reason: the caller is a JSON file.
        int max = AscensionDatabase.MaxLevel;

        Check("a_negative_level_clamps_to_zero", AscensionDatabase.Effective(-3).Level == 0,
            $"got {AscensionDatabase.Effective(-3).Level}");
        Check("a_level_past_the_ladder_clamps_to_the_top", AscensionDatabase.Effective(max + 50).Level == max,
            $"got {AscensionDatabase.Effective(max + 50).Level}");
        Check("at_clamps_at_both_ends",
            AscensionDatabase.At(-1).Level == 1 && AscensionDatabase.At(max + 9).Level == max,
            $"got {AscensionDatabase.At(-1).Level} and {AscensionDatabase.At(max + 9).Level}");
    }

    private void TestCursesAreRealAndUnplayable()
    {
        var missing = new List<string>();
        var playable = new List<string>();

        foreach (var id in AscensionDatabase.All.SelectMany(r => r.StartingCurseIds))
        {
            var card = CardDatabase.All.FirstOrDefault(c => c.Id == id);
            if (card is null) missing.Add(id);
            else if (card.IsPlayable) playable.Add(id);
        }

        Check("every_starting_curse_is_a_real_card", missing.Count == 0, $"unknown ids: {Join(missing)}");

        // The one that is not merely a typo guard. IsPlayable is derived from
        // CardType, so this is really "the ladder only ever adds a Status or a
        // Curse" - a rung that opened the deck with a real card would be a
        // difficulty rung that makes the run easier, and nothing else in the
        // repo would say so.
        Check("every_starting_curse_is_unplayable", playable.Count == 0, $"playable: {Join(playable)}");

        Check("the_ladder_imposes_at_least_one_card",
            AscensionDatabase.All.Sum(r => r.StartingCurseIds.Count) > 0,
            "no rung starts the player with anything");
    }

    private void TestTheModifierMethods()
    {
        // Rounding is half-up and stated once, because the game and BalanceModel
        // both read these methods: a rule that differed between them would put
        // every encounter cost in the report a point off the fight the player
        // actually gets.
        var m = AscensionModifiers.None with { EnemyHpPercent = 105, EnemyDamagePercent = 105 };
        Check("scaling_rounds_half_up", m.EnemyHp(10, false) == 11, $"10 at 105% gave {m.EnemyHp(10, false)}");
        Check("scaling_rounds_down_below_half", m.EnemyHp(9, false) == 9, $"9 at 105% gave {m.EnemyHp(9, false)}");

        // A zero-damage move is not an attack, and floor-to-1 would give every
        // enemy in the game a scratch it was never authored with.
        Check("a_zero_damage_move_stays_zero", m.EnemyDamage(0) == 0, $"got {m.EnemyDamage(0)}");
        Check("a_scaled_attack_never_vanishes", m.EnemyDamage(1) >= 1, $"got {m.EnemyDamage(1)}");

        // The boss bonus stacks on top of the general HP knob rather than
        // replacing it, and applies to nothing else.
        var boss = AscensionModifiers.None with { EnemyHpPercent = 110, BossHpBonusPercent = 20 };
        Check("boss_hp_stacks_on_the_general_knob", boss.EnemyHp(100, true) == 130,
            $"got {boss.EnemyHp(100, true)}");
        Check("boss_hp_leaves_normals_alone", boss.EnemyHp(100, false) == 110,
            $"got {boss.EnemyHp(100, false)}");

        // The elite weight is *moved*, not added: what Elite gains, Combat loses,
        // so the node table's total is unchanged and no other node type is
        // resilenced. MapGenerator's own comment records what an added weight
        // cost the last time - the `?` node took 6.8% of Elite's frequency by
        // growing the denominator, and nothing in the repo noticed.
        var map = AscensionModifiers.None with { EliteWeightDelta = 6 };
        Check("the_elite_weight_is_conserved",
            map.EliteWeight(15) + map.CombatWeight(50) == 15 + 50,
            $"got {map.EliteWeight(15)} + {map.CombatWeight(50)}");
        Check("the_elite_weight_actually_rises", map.EliteWeight(15) > 15, $"got {map.EliteWeight(15)}");

        // Floors. Every one of these reaches a system that divides by it or
        // indexes with it.
        var extreme = AscensionModifiers.None with
        {
            PotionDropPercentDelta = 500,
            ClearHealPercentDelta = 500,
            StartingMaxHpDelta = -500,
            EliteWeightDelta = 500,
        };
        Check("potion_percent_floors_at_zero", extreme.PotionPercent(40) == 0, $"got {extreme.PotionPercent(40)}");
        Check("the_heal_floors_at_zero", extreme.ClearHeal(100) == 0, $"got {extreme.ClearHeal(100)}");
        Check("starting_hp_floors_above_zero", extreme.StartingMaxHp(50) >= 1, $"got {extreme.StartingMaxHp(50)}");
        Check("the_combat_weight_floors_at_zero", extreme.CombatWeight(50) == 0, $"got {extreme.CombatWeight(50)}");
    }

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
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
