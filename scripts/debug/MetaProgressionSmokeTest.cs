using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for MetaProgressionManager's save/load logic and the
// unlock-filter wiring into Shop/MetaProgressionScreen. Always operates
// against a scratch file (ScratchPath), NEVER the real save path, so a
// test run can never clobber a developer's/player's actual Shards/unlocks.
// Run via `godot --headless scenes/debug/MetaProgressionSmokeTest.tscn`.
public partial class MetaProgressionSmokeTest : Node
{
    private const string ScratchPath = "user://meta_progression_test.json";
    private const string LockedRelic = "vampire_fang";
    private const string OtherLockedRelic = "momentum_token";

    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestFreshInstallDefaults();
        TestSaveThenLoadRoundTrip();
        TestCorruptedFileFallsBackToDefaults();
        TestUnknownFieldTolerance();
        TestStaleUnlockedRelicIdTolerance();
        TestUnlockGatesContentEndToEnd();
        TestMetaProgressionScreenLoads();
        TestShopExcludesLockedRelics();

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

    private void TestFreshInstallDefaults()
    {
        ResetScratch();
        var meta = MetaProgressionManager.Instance;

        Check("fresh_install_zero_shards", meta.Shards == 0, $"shards={meta.Shards}");
        Check("fresh_install_no_seed_history", meta.RecentSeeds.Count == 0, $"count={meta.RecentSeeds.Count}");
        Check("fresh_install_relic_still_locked", !meta.IsRelicUnlocked(LockedRelic), "expected locked");
    }

    private void TestSaveThenLoadRoundTrip()
    {
        ResetScratch();
        var meta = MetaProgressionManager.Instance;

        meta.GrantShards(25, ScratchPath);
        meta.TryUnlockRelic(LockedRelic, 0, ScratchPath);
        meta.LogSeed(12345, "Win", ScratchPath);

        meta.LoadFrom(ScratchPath); // reload from disk, discard in-memory state
        Check("round_trip_shards", meta.Shards == 25, $"shards={meta.Shards}");
        Check("round_trip_unlock", meta.IsRelicUnlocked(LockedRelic), "expected unlocked after reload");
        Check("round_trip_seed_history", meta.RecentSeeds.Count == 1 && meta.RecentSeeds[0].Seed == 12345,
            $"count={meta.RecentSeeds.Count}");
    }

    private void TestCorruptedFileFallsBackToDefaults()
    {
        ResetScratch();
        WriteScratchRaw("{ not valid json [[[");

        var meta = MetaProgressionManager.Instance;
        meta.LoadFrom(ScratchPath);
        Check("corrupted_file_falls_back_to_defaults", meta.Shards == 0 && meta.RecentSeeds.Count == 0,
            $"shards={meta.Shards} seeds={meta.RecentSeeds.Count}");
    }

    private void TestUnknownFieldTolerance()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 1, "shards": 42, "unlockedRelicIds": [], "recentSeeds": [],
              "bogusFutureField": { "nested": "whatever" } }
            """);

        var meta = MetaProgressionManager.Instance;
        meta.LoadFrom(ScratchPath);
        Check("unknown_field_ignored_known_fields_parse", meta.Shards == 42, $"shards={meta.Shards}");
    }

    private void TestStaleUnlockedRelicIdTolerance()
    {
        ResetScratch();
        WriteScratchRaw("""
            { "saveVersion": 1, "shards": 0, "unlockedRelicIds": ["this_relic_id_does_not_exist"], "recentSeeds": [] }
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
            unlocked = new List<RelicDefinition>();
            Check("stale_id_does_not_throw", false, e.Message);
            return;
        }
        Check("stale_id_does_not_throw", true, "");
        Check("stale_id_does_not_falsely_unlock_real_relics",
            !unlocked.Any(r => r.Id == LockedRelic) && !unlocked.Any(r => r.Id == OtherLockedRelic),
            "a locked relic was incorrectly reported unlocked");
    }

    private void TestUnlockGatesContentEndToEnd()
    {
        ResetScratch();
        var meta = MetaProgressionManager.Instance;

        Check("initially_locked", !meta.IsRelicUnlocked(LockedRelic), "expected locked at 0 shards");
        Check("underfunded_purchase_fails", !meta.TryUnlockRelic(LockedRelic, 60, ScratchPath), "expected purchase to fail");
        Check("still_locked_after_failed_purchase", !meta.IsRelicUnlocked(LockedRelic), "expected still locked");

        meta.GrantShards(60, ScratchPath);
        bool bought = meta.TryUnlockRelic(LockedRelic, 60, ScratchPath);
        Check("funded_purchase_succeeds", bought, "expected purchase to succeed");
        Check("shards_deducted", meta.Shards == 0, $"shards={meta.Shards}");
        Check("now_unlocked", meta.IsRelicUnlocked(LockedRelic), "expected unlocked");
        Check("other_locked_relic_unaffected", !meta.IsRelicUnlocked(OtherLockedRelic), "expected still locked");
    }

    private void TestMetaProgressionScreenLoads()
    {
        ResetScratch();
        MetaProgressionManager.Instance.GrantShards(99, ScratchPath);

        var packed = GD.Load<PackedScene>("res://scenes/MetaProgressionScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var shardsLabel = instance.GetNode<Label>("ShardsLabel");
        var relicList = instance.GetNode<VBoxContainer>("RelicUnlocksList");

        Check("meta_screen_shows_shards", shardsLabel.Text.Contains("99"), $"text='{shardsLabel.Text}'");
        Check("meta_screen_lists_locked_relics", relicList.GetChildCount() == 2, $"rows={relicList.GetChildCount()}");
        instance.QueueFree();
    }

    private void TestShopExcludesLockedRelics()
    {
        ResetScratch(); // nothing unlocked
        RunState.Gold = 200;
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        var packed = GD.Load<PackedScene>("res://scenes/ShopScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var offers = instance.GetNode<VBoxContainer>("OffersList");
        bool anyLockedRelicOffered = false;
        foreach (var row in offers.GetChildren())
        {
            var button = row.GetChild<Button>(0);
            if (button.Text.Contains("Vampire Fang") || button.Text.Contains("Momentum Token"))
            {
                anyLockedRelicOffered = true;
            }
        }
        Check("shop_never_offers_locked_relics", !anyLockedRelicOffered, "a locked relic appeared as a shop offer");
        instance.QueueFree();
    }
}
