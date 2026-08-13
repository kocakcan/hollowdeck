using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for MetaProgressionManager's save/load/migration logic and
// the unlock-filter wiring into Shop/MetaProgressionScreen, plus RunScore's
// arithmetic. Always operates against a scratch file (ScratchPath), NEVER
// the real save path, so a test run can never clobber a developer's/player's
// actual progress.
// Run via `godot --headless scenes/debug/MetaProgressionSmokeTest.tscn`.
public partial class MetaProgressionSmokeTest : Node
{
    private const string ScratchPath = "user://meta_progression_test.json";

    // First and second rungs of the track - referenced by threshold below,
    // so retuning UnlockTrack's numbers doesn't silently invalidate a test.
    private static readonly UnlockEntry FirstEntry = MetaProgressionManager.UnlockTrack[0];
    private static readonly UnlockEntry SecondEntry = MetaProgressionManager.UnlockTrack[1];

    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestFreshInstallDefaults();
        TestSaveThenLoadRoundTrip();
        TestCorruptedFileFallsBackToDefaults();
        TestUnknownFieldTolerance();
        TestStaleUnlockedRelicIdTolerance();
        TestProgressGatesContentEndToEnd();
        TestShardMigrationFromV1();
        TestAscensionLimitMigrationToV3();
        TestTheAscensionLimitOnlyRisesOnAWin();
        TestRunScoreArithmetic();
        TestMetaProgressionScreenLoads();
        TestShopExcludesLockedContent();

        GD.Print($"MetaProgressionSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void ResetScratch()
    {
        if (FileAccess.FileExists(ScratchPath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ScratchPath));
        MetaProgressionManager.Instance.LoadFrom(ScratchPath);
    }

    private void WriteScratchRaw(string json)
    {
        using var file = FileAccess.Open(ScratchPath, FileAccess.ModeFlags.Write);
        file.StoreString(json);
    }

    private static bool IsEntryUnlocked(UnlockEntry entry) => entry.Kind == UnlockKind.Card
        ? MetaProgressionManager.Instance.IsCardUnlocked(entry.Id)
        : MetaProgressionManager.Instance.IsRelicUnlocked(entry.Id);

    private void TestFreshInstallDefaults()
    {
        ResetScratch();
        var meta = MetaProgressionManager.Instance;

        Check("fresh_install_zero_progress", meta.TotalProgress == 0, $"progress={meta.TotalProgress}");
        Check("fresh_install_no_seed_history", meta.RecentSeeds.Count == 0, $"count={meta.RecentSeeds.Count}");
        Check("fresh_install_first_entry_locked", !IsEntryUnlocked(FirstEntry), "expected locked");
        Check("fresh_install_ungated_content_available",
            meta.IsCardUnlocked("strike") && meta.IsRelicUnlocked("second_wind"),
            "starter content must never be gated");
        Check("fresh_install_next_unlock_is_first_entry",
            meta.NextUnlock() == FirstEntry, $"next={meta.NextUnlock()}");
    }

    private void TestSaveThenLoadRoundTrip()
    {
        ResetScratch();
        var meta = MetaProgressionManager.Instance;

        meta.AddRunResult(FirstEntry.Threshold, 12345, "Win", path: ScratchPath);

        meta.LoadFrom(ScratchPath); // reload from disk, discard in-memory state
        Check("round_trip_progress", meta.TotalProgress == FirstEntry.Threshold, $"progress={meta.TotalProgress}");
        Check("round_trip_best_score", meta.BestRunScore == FirstEntry.Threshold, $"best={meta.BestRunScore}");
        Check("round_trip_runs_completed", meta.RunsCompleted == 1, $"runs={meta.RunsCompleted}");
        Check("round_trip_unlock", IsEntryUnlocked(FirstEntry), "expected unlocked after reload");
        Check("round_trip_seed_history",
            meta.RecentSeeds.Count == 1 && meta.RecentSeeds[0].Seed == 12345
                && meta.RecentSeeds[0].Score == FirstEntry.Threshold,
            $"count={meta.RecentSeeds.Count}");
    }

    private void TestCorruptedFileFallsBackToDefaults()
    {
        ResetScratch();
        WriteScratchRaw("{ not valid json [[[");

        var meta = MetaProgressionManager.Instance;
        meta.LoadFrom(ScratchPath);
        Check("corrupted_file_falls_back_to_defaults", meta.TotalProgress == 0 && meta.RecentSeeds.Count == 0,
            $"progress={meta.TotalProgress} seeds={meta.RecentSeeds.Count}");
    }

    private void TestUnknownFieldTolerance()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 2, "totalProgress": 42, "unlockedRelicIds": [], "recentSeeds": [],
              "bogusFutureField": { "nested": "whatever" } }
            """);

        var meta = MetaProgressionManager.Instance;
        meta.LoadFrom(ScratchPath);
        Check("unknown_field_ignored_known_fields_parse", meta.TotalProgress == 42, $"progress={meta.TotalProgress}");
    }

    private void TestStaleUnlockedRelicIdTolerance()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 2, "totalProgress": 0, "unlockedRelicIds": ["this_relic_id_does_not_exist"],
              "recentSeeds": [] }
            """);

        var meta = MetaProgressionManager.Instance;
        meta.LoadFrom(ScratchPath);

        List<RelicDefinition> unlocked;
        try
        {
            unlocked = RelicDatabase.All.Where(r => meta.IsRelicUnlocked(r.Id)).ToList();
        }
        catch (System.Exception e)
        {
            Check("stale_id_does_not_throw", false, e.Message);
            return;
        }
        Check("stale_id_does_not_throw", true, "");

        var gatedRelicIds = MetaProgressionManager.UnlockTrack
            .Where(e => e.Kind == UnlockKind.Relic).Select(e => e.Id).ToHashSet();
        Check("stale_id_does_not_falsely_unlock_real_relics",
            !unlocked.Any(r => gatedRelicIds.Contains(r.Id)),
            "a gated relic was incorrectly reported unlocked");
    }

    private void TestProgressGatesContentEndToEnd()
    {
        ResetScratch();
        var meta = MetaProgressionManager.Instance;

        Check("initially_locked", !IsEntryUnlocked(FirstEntry), "expected locked at 0 progress");
        Check("locked_card_excluded_from_pool",
            MetaProgressionTrackCards().Any() &&
            !meta.UnlockedCards().Any(c => c.Id == FirstCardEntry().Id),
            "a locked card appeared in UnlockedCards()");

        meta.AddRunResult(FirstEntry.Threshold - 1, 1, "Lose", path: ScratchPath);
        Check("still_locked_one_point_short", !IsEntryUnlocked(FirstEntry), "expected still locked");

        var unlocked = meta.AddRunResult(1, 2, "Lose", path: ScratchPath);
        Check("unlocks_on_crossing_threshold", IsEntryUnlocked(FirstEntry), "expected unlocked");
        Check("crossing_reports_newly_unlocked",
            unlocked.Count == 1 && unlocked[0] == FirstEntry, $"reported={unlocked.Count}");
        Check("unlocked_card_enters_pool",
            FirstEntry.Kind != UnlockKind.Card || meta.UnlockedCards().Any(c => c.Id == FirstEntry.Id),
            "unlocked card missing from UnlockedCards()");
        Check("next_entry_still_locked", !IsEntryUnlocked(SecondEntry), "expected still locked");

        // Already-announced entries must not be reported a second time.
        var again = meta.AddRunResult(0, 3, "Lose", path: ScratchPath);
        Check("unlock_announced_only_once", again.Count == 0, $"re-reported={again.Count}");
    }

    private static IEnumerable<UnlockEntry> MetaProgressionTrackCards() =>
        MetaProgressionManager.UnlockTrack.Where(e => e.Kind == UnlockKind.Card);

    private static UnlockEntry FirstCardEntry() => MetaProgressionTrackCards().First();

    // The whole point of the v1 -> v2 conversion: an existing player's Shard
    // balance becomes progress, and relics they already bought stay unlocked
    // even though their thresholds are far above that balance.
    private void TestShardMigrationFromV1()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 1, "shards": 170, "unlockedRelicIds": ["vengeful_spirit"],
              "recentSeeds": [ { "seed": 99, "outcome": "Win", "timestampUtc": "2026-07-26T07:20:16Z" } ] }
            """);

        var meta = MetaProgressionManager.Instance;
        meta.LoadFrom(ScratchPath);

        Check("migration_shards_become_progress", meta.TotalProgress == 170, $"progress={meta.TotalProgress}");
        Check("migration_grandfathers_bought_relic", meta.IsRelicUnlocked("vengeful_spirit"),
            "a relic bought under v1 was taken away");
        Check("migration_keeps_seed_history", meta.RecentSeeds.Count == 1, $"count={meta.RecentSeeds.Count}");
        Check("migration_legacy_seed_has_no_score", meta.RecentSeeds[0].Score == 0, "expected unscored legacy entry");

        // 170 progress clears the first rung (150) but nothing above it.
        Check("migration_applies_thresholds", IsEntryUnlocked(FirstEntry), "expected first rung unlocked at 170");
        Check("migration_does_not_over_unlock", !IsEntryUnlocked(SecondEntry), "expected second rung still locked");

        // Reloading the migrated file must be a no-op, not a second +170.
        meta.LoadFrom(ScratchPath);
        Check("migration_is_idempotent", meta.TotalProgress == 170, $"progress={meta.TotalProgress}");

        // Retroactive unlocks are pre-announced, so a returning player isn't
        // shown a wall of "NEW!" on their next run-end screen.
        var announced = meta.AddRunResult(0, 4, "Lose", path: ScratchPath);
        Check("migration_pre_seeds_announcements", announced.Count == 0, $"announced={announced.Count}");
    }

    // v2 -> v3, the ascension limit. The step converts nothing, which is
    // exactly why it is worth a test: an absent field and a field the migration
    // forgot look identical from the outside, and both read as 0.
    //
    // The interesting half is the v1 file, because Migrate stopped being a
    // single gate and became a chain when this landed. A v1 save has to run
    // *both* steps in one pass and come out stamped at the current version, and
    // the shape it replaced would have run the shard fold, written v2, and left
    // the file one version short with nothing complaining.
    private void TestAscensionLimitMigrationToV3()
    {
        var meta = MetaProgressionManager.Instance;

        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 2, "totalProgress": 400, "runsCompleted": 3,
              "recentSeeds": [ { "seed": 5, "outcome": "Lose", "score": 120,
                                 "timestampUtc": "2026-07-26T07:20:16Z" } ] }
            """);
        meta.LoadFrom(ScratchPath);
        Check("v2_without_a_win_starts_the_ladder_locked", meta.AscensionLimit == 0,
            $"limit={meta.AscensionLimit}");
        Check("v2_migration_keeps_progress", meta.TotalProgress == 400, $"progress={meta.TotalProgress}");

        // A player who has already finished the game has, by definition, won at
        // rung 0 - so the migration owes them rung 1 rather than making them
        // beat it again to unlock what they have already done.
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 2, "totalProgress": 400,
              "recentSeeds": [ { "seed": 5, "outcome": "Win", "score": 300,
                                 "timestampUtc": "2026-07-26T07:20:16Z" } ] }
            """);
        meta.LoadFrom(ScratchPath);
        Check("v2_with_a_win_unlocks_the_first_rung", meta.AscensionLimit == 1,
            $"limit={meta.AscensionLimit}");

        meta.LoadFrom(ScratchPath);
        Check("v3_migration_is_idempotent", meta.AscensionLimit == 1, $"limit={meta.AscensionLimit}");

        // The chain, driven from the bottom. Both steps in one pass.
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 1, "shards": 170, "unlockedRelicIds": [],
              "recentSeeds": [ { "seed": 99, "outcome": "Win", "timestampUtc": "2026-07-26T07:20:16Z" } ] }
            """);
        meta.LoadFrom(ScratchPath);
        Check("v1_reaches_v3_in_one_pass",
            meta.TotalProgress == 170 && meta.AscensionLimit == 1,
            $"progress={meta.TotalProgress}, limit={meta.AscensionLimit}");
        meta.LoadFrom(ScratchPath);
        Check("v1_to_v3_is_idempotent", meta.TotalProgress == 170 && meta.AscensionLimit == 1,
            $"progress={meta.TotalProgress}, limit={meta.AscensionLimit}");
    }

    // What the ladder is: winning at your limit raises it, and nothing else
    // does. Three cases, and the two that must *not* move it are the content of
    // the test - a limit that crept up on losses would make the whole thing a
    // difficulty menu with extra steps.
    private void TestTheAscensionLimitOnlyRisesOnAWin()
    {
        var meta = MetaProgressionManager.Instance;

        ResetScratch();
        meta.LoadFrom(ScratchPath);
        Check("a_fresh_save_has_the_ladder_locked", meta.AscensionLimit == 0, $"limit={meta.AscensionLimit}");

        meta.AddRunResult(200, 1, "Lose", 0, path: ScratchPath);
        Check("a_loss_does_not_raise_the_limit", meta.AscensionLimit == 0, $"limit={meta.AscensionLimit}");

        meta.AddRunResult(200, 2, "Win", 0, path: ScratchPath);
        Check("a_win_at_the_limit_raises_it", meta.AscensionLimit == 1, $"limit={meta.AscensionLimit}");

        // Winning with the toggle off once the ladder has been climbed. The
        // player has already proved rung 1; rung 0 proves nothing new.
        meta.AddRunResult(200, 3, "Win", 0, path: ScratchPath);
        Check("a_win_below_the_limit_does_not_raise_it", meta.AscensionLimit == 1,
            $"limit={meta.AscensionLimit}");

        meta.AddRunResult(200, 4, "Win", 1, path: ScratchPath);
        Check("a_win_at_the_new_limit_raises_it_again", meta.AscensionLimit == 2,
            $"limit={meta.AscensionLimit}");

        // The rung goes into the seed log beside the score, which is the only
        // place a finished run's difficulty is recorded at all.
        Check("the_seed_log_records_the_rung", meta.RecentSeeds[0].Ascension == 1,
            $"logged {meta.RecentSeeds[0].Ascension}");

        // At the top of the ladder a win is still a win; it has nothing left to
        // unlock, and must not index past the content file.
        meta.AddRunResult(200, 5, "Win", AscensionDatabase.MaxLevel, path: ScratchPath);
        Check("the_limit_stops_at_the_top_of_the_ladder",
            meta.AscensionLimit == AscensionDatabase.MaxLevel,
            $"limit={meta.AscensionLimit} against a ladder of {AscensionDatabase.MaxLevel}");

        // Round-trip: the limit is persisted, not merely held.
        meta.LoadFrom(ScratchPath);
        Check("the_limit_survives_a_reload", meta.AscensionLimit == AscensionDatabase.MaxLevel,
            $"limit={meta.AscensionLimit}");
    }

    private void TestRunScoreArithmetic()
    {
        var stats = new RunStats
        {
            MaxFloorReached = 10,     // 50
            EnemiesSlain = 12,        // 24
            ElitesSlain = 2,          // 20
            BossesSlain = 1,          // 50
            PerfectElites = 1,        // 25
            PerfectBosses = 1,        // 50
            EventRoomsVisited = RunScore.MysteryRooms, // 25
            OverkillEarned = true,    // 25
            ComboEarned = true,       // 25
        };
        var deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("strike"), // starters: ignored
            CardDatabase.Get("cleave"), CardDatabase.Get("flex"),   // distinct non-starters
        };

        var breakdown = RunScore.Evaluate(stats, deck, gold: 250, relicCount: 2);
        int total = RunScore.Total(breakdown);

        // 294 above + Money Money (25 at 250 gold) + Highlander (100)
        // + Pauper (100 - the deck is Strike/Strike/Cleave/Flex, all Common).
        Check("run_score_total", total == 519, $"total={total} breakdown=[{string.Join(", ", breakdown)}]");
        Check("run_score_omits_zero_categories", breakdown.All(e => e.Points > 0), "a zero-point row was listed");
        Check("run_score_gold_tier_not_cumulative",
            breakdown.Count(e => e.Label is "Money Money" or "Raining Money" or "I Like Gold") == 1,
            "more than one gold tier awarded");

        // The ascension row. Rung 0 must leave every number above untouched -
        // that is what the 519 one line up is now also asserting - and a real
        // rung must add exactly its percentage of the rest, listed rather than
        // folded into the total, so the printed column still sums to the
        // printed Total. A multiplier applied to Total() instead would pass a
        // check on the total alone and fail this one.
        Check("run_score_rung_zero_adds_no_row",
            breakdown.All(e => !e.Label.StartsWith("Ascension")), "a rung-0 run listed an Ascension row");

        var climbed = RunScore.Evaluate(stats, deck, gold: 250, relicCount: 2, ascension: 6);
        var ascRow = climbed.FirstOrDefault(e => e.Label == "Ascension 6");
        int expected = 519 * RunScore.AscensionBonusPercent * 6 / 100;
        Check("run_score_ascension_row_is_a_share_of_the_rest",
            ascRow is not null && ascRow.Points == expected,
            $"got {ascRow?.Points.ToString() ?? "no row"}, expected {expected}");
        Check("run_score_rows_still_sum_to_the_total",
            RunScore.Total(climbed) == climbed.Sum(e => e.Points),
            "the printed rows no longer add up to the printed total");
        Check("run_score_ascension_is_the_last_row",
            climbed[^1].Label == "Ascension 6", $"last row is {climbed[^1].Label}");

        // A run that scored nothing gets nothing for the rung. Otherwise the
        // ladder would pay for reaching the run-end screen, which is a thing a
        // player does by dying on floor 1.
        var nothing = RunScore.Evaluate(new RunStats(), new List<CardDefinition>(), 0, 0, ascension: 20);
        Check("run_score_ascension_pays_nothing_on_a_zero_run", RunScore.Total(nothing) == 0,
            $"total={RunScore.Total(nothing)}");

        // Starter duplicates must not deny Highlander, real duplicates must.
        var dupeDeck = new List<CardDefinition> { CardDatabase.Get("cleave"), CardDatabase.Get("cleave") };
        var dupeBreakdown = RunScore.Evaluate(new RunStats(), dupeDeck, gold: 0, relicCount: 0);
        Check("run_score_highlander_denied_by_duplicates",
            dupeBreakdown.All(e => e.Label != "Highlander"), "Highlander awarded to a deck with duplicates");

        var emptyBreakdown = RunScore.Evaluate(new RunStats(), new List<CardDefinition>(), gold: 0, relicCount: 0);
        Check("run_score_empty_run_scores_zero", RunScore.Total(emptyBreakdown) == 0,
            $"total={RunScore.Total(emptyBreakdown)}");

        // Pauper, the category that could not exist while every card was
        // Common. It has to be denied by a single Rare anywhere in the deck.
        Check("run_score_pauper_awarded_for_a_rare_free_deck",
            breakdown.Any(e => e.Label == "Pauper"), "no Pauper row for an all-Common deck");

        var rare = CardDatabase.All.First(c => c.Rarity == Rarity.Rare);
        var rareDeck = new List<CardDefinition> { CardDatabase.Get("cleave"), rare };
        var rareBreakdown = RunScore.Evaluate(new RunStats(), rareDeck, gold: 0, relicCount: 0);
        Check("run_score_pauper_denied_by_one_rare",
            rareBreakdown.All(e => e.Label != "Pauper"), $"Pauper awarded to a deck holding {rare.Id}");

        // And it must not award to a run that never had a deck at all - which
        // "no card is Rare" is vacuously true for.
        Check("run_score_pauper_not_awarded_to_an_empty_deck",
            emptyBreakdown.All(e => e.Label != "Pauper"), "Pauper awarded for an empty deck");
    }

    private void TestMetaProgressionScreenLoads()
    {
        ResetScratch();
        MetaProgressionManager.Instance.AddRunResult(99, 7, "Lose", path: ScratchPath);

        var packed = GD.Load<PackedScene>("res://scenes/MetaProgressionScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var progressLabel = instance.GetNode<Label>("Margin/Root/ProgressLabel");
        var trackList = instance.GetNode<VBoxContainer>("Margin/Root/Columns/TrackColumn/TrackScroll/UnlockTrackList");
        var historyList = instance.GetNode<VBoxContainer>(
            "Margin/Root/Columns/HistoryColumn/HistoryScroll/SeedHistoryList");

        Check("meta_screen_shows_progress", progressLabel.Text.Contains("99"), $"text='{progressLabel.Text}'");
        Check("meta_screen_lists_whole_track",
            trackList.GetChildCount() == MetaProgressionManager.UnlockTrack.Length,
            $"rows={trackList.GetChildCount()}");
        Check("meta_screen_lists_run_history", historyList.GetChildCount() == 1,
            $"rows={historyList.GetChildCount()}");
        instance.QueueFree();
    }

    private void TestShopExcludesLockedContent()
    {
        ResetScratch(); // nothing unlocked
        RunState.Gold = 200;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        var packed = GD.Load<PackedScene>("res://scenes/ShopScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var gatedRelicNames = MetaProgressionManager.UnlockTrack
            .Where(e => e.Kind == UnlockKind.Relic)
            .Select(e => RelicDatabase.Get(e.Id).Name)
            .ToList();
        // The relic/potion stock is a row of framed tiles now, not a scrolling
        // list of buttons - so the offer's name is a Label inside the tile
        // rather than the Button's own text (the button just says "Buy (150g)").
        var offers = instance.GetNode<HBoxContainer>("OffersRow");
        bool anyLockedRelicOffered = offers.GetChildren()
            .SelectMany(tile => tile.GetChild(0).GetChildren())
            .OfType<Label>()
            .Any(label => gatedRelicNames.Any(label.Text.Contains));
        Check("shop_never_offers_locked_relics", !anyLockedRelicOffered, "a locked relic appeared as a shop offer");

        var gatedCardNames = MetaProgressionManager.UnlockTrack
            .Where(e => e.Kind == UnlockKind.Card)
            .Select(e => CardDatabase.Get(e.Id).Name)
            .ToList();
        var cardRow = instance.GetNode<HBoxContainer>("CardOffersRow");
        bool anyLockedCardOffered = cardRow.GetChildren()
            .SelectMany(column => column.GetChildren())
            .OfType<UI.CardView>()
            .Any(view => gatedCardNames.Contains(view.CardInstance!.Definition.Name));
        Check("shop_never_offers_locked_cards", !anyLockedCardOffered, "a locked card appeared as a shop offer");

        instance.QueueFree();
    }
}
