using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

public enum UnlockKind { Card, Relic }

// One rung of the unlock track. Id is a CardDatabase/RelicDatabase id;
// Threshold is the TotalProgress at which it opens.
public record UnlockEntry(int Threshold, UnlockKind Kind, string Id);

// Autoload singleton for cross-run meta-progression. Deliberately separate
// from RunManager/RunState - different lifetime and versioning needs.
// Persists to user:// (writable app-data dir), not res:// (packaged
// read-only project tree).
//
// Progression is a *track*, not a shop: every finished run scores points
// (RunScore), those points accumulate into TotalProgress, and content opens
// automatically as thresholds are crossed. There is nothing to buy and
// nothing to spend, so progress only ever goes up.
public partial class MetaProgressionManager : Node
{
    public static MetaProgressionManager Instance { get; private set; } = null!;

    // The entire balance surface of the progression system. Everything NOT
    // listed here is available from the first run - "light gating": 74 of
    // the 84 cards (including the strike/defend/bash starters) plus 29 of
    // the 33 relics need no unlocking at all. Cards authored after this list
    // was written are unlocked by default, which is the intended default:
    // gating is opt-in, one line at a time, and a balance decision.
    public static readonly UnlockEntry[] UnlockTrack =
    {
        new(150, UnlockKind.Card, "whirlwind"),
        new(300, UnlockKind.Relic, "vampire_fang"),
        new(500, UnlockKind.Card, "riposte"),
        new(750, UnlockKind.Card, "venom_strike"),
        new(1000, UnlockKind.Relic, "momentum_token"),
        new(1300, UnlockKind.Card, "battle_trance"),
        new(1600, UnlockKind.Card, "crippling_blow"),
        new(2000, UnlockKind.Relic, "toxic_fang"),
        new(2400, UnlockKind.Card, "bloodletting"),
        new(2900, UnlockKind.Card, "toxic_cloud"),
        new(3400, UnlockKind.Relic, "vengeful_spirit"),
        new(4000, UnlockKind.Card, "meditate"),
        new(4700, UnlockKind.Card, "impale"),
        new(5500, UnlockKind.Card, "last_stand"),
    };

    private const string SavePath = "user://meta_progression.json";
    private const int MaxRecentSeeds = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private MetaSaveData _data = new();

    public int TotalProgress => _data.TotalProgress;
    public int BestRunScore => _data.BestRunScore;
    public int RunsCompleted => _data.RunsCompleted;
    public IReadOnlyList<SeedLogEntry> RecentSeeds => _data.RecentSeeds;

    // The highest rung RunSetupScreen may switch on. Clamped against the
    // content file on the way out rather than on the way in, so a save from a
    // build with a longer ladder degrades to the top of this one instead of
    // offering a rung AscensionDatabase cannot resolve.
    public int AscensionLimit => Math.Min(_data.AscensionLimit, AscensionDatabase.MaxLevel);

    public override void _Ready()
    {
        Instance = this;
        LoadFrom(SavePath);
    }

    // Only ever check membership against ids from the track/database, never
    // call RelicDatabase.Get(idFromSave) - a stale id from an old save (a
    // relic later removed/renamed) must stay inert, not throw.
    public bool IsRelicUnlocked(string relicId) => IsUnlocked(UnlockKind.Relic, relicId);

    public bool IsCardUnlocked(string cardId) => IsUnlocked(UnlockKind.Card, cardId);

    private bool IsUnlocked(UnlockKind kind, string id)
    {
        var entry = UnlockTrack.FirstOrDefault(e => e.Kind == kind && e.Id == id);
        if (entry is null) return true; // Not gated at all.
        if (kind == UnlockKind.Relic && _data.UnlockedRelicIds.Contains(id)) return true; // Grandfathered from v1.
        return _data.TotalProgress >= entry.Threshold;
    }

    // The card pool every "give the player a card" site should draw from -
    // reward picks, shop stock, and the random-card event outcome. Upgraded
    // variants aren't in CardDatabase, so this only ever sees base cards.
    public IEnumerable<CardDefinition> UnlockedCards() =>
        CardDatabase.All.Where(c => IsCardUnlocked(c.Id));

    // The next rung the player is working toward, or null once the whole
    // track is complete.
    public UnlockEntry? NextUnlock() =>
        UnlockTrack.FirstOrDefault(e => _data.TotalProgress < e.Threshold);

    // Records a finished run: banks its score, logs the seed, and returns
    // whatever the added progress just opened up (so the run-end screen can
    // announce it). Each entry is announced exactly once - SeenUnlockIds
    // makes that survive a restart, which a simple before/after threshold
    // comparison would not if the player quit on the run-end screen.
    // path defaults to the real save; tests pass a scratch path explicitly
    // so they can never write into the developer's/player's actual save.
    //
    // The rung arrives as an argument rather than being read off RunState,
    // which would be one line shorter and would break the property this whole
    // suite of tests rests on: this type is a pure function of what it is
    // handed plus what is on disk, which is why it can be driven against a
    // scratch file with no run in progress at all.
    public List<UnlockEntry> AddRunResult(int score, int seed, string outcome, int ascension = 0,
        string? path = null)
    {
        _data.TotalProgress += score;
        _data.BestRunScore = Math.Max(_data.BestRunScore, score);
        _data.RunsCompleted++;

        // The ladder advances on a win at the limit, and on nothing else. Not
        // on a loss, and not on a win *below* the limit - the second is what
        // makes `>=` rather than `==` the wrong comparison to reach for if the
        // player has climbed past this rung already, and it is why the max is
        // taken rather than incremented blind.
        //
        // Capped at the content file: at the top of the ladder a win is still a
        // win, it just has nothing left to unlock.
        if (outcome == "Win" && ascension >= _data.AscensionLimit)
        {
            _data.AscensionLimit = Math.Min(ascension + 1, AscensionDatabase.MaxLevel);
        }

        _data.RecentSeeds.Insert(0, new SeedLogEntry
        {
            Seed = seed,
            Outcome = outcome,
            Score = score,
            Ascension = ascension,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
        });
        if (_data.RecentSeeds.Count > MaxRecentSeeds)
        {
            _data.RecentSeeds.RemoveRange(MaxRecentSeeds, _data.RecentSeeds.Count - MaxRecentSeeds);
        }

        var newlyUnlocked = UnlockTrack
            .Where(e => _data.TotalProgress >= e.Threshold && !_data.SeenUnlockIds.Contains(e.Id))
            .ToList();
        _data.SeenUnlockIds.AddRange(newlyUnlocked.Select(e => e.Id));

        SaveTo(path ?? SavePath);
        return newlyUnlocked;
    }

    // Path-parameterized so tests can point at a scratch file instead of the
    // real save. LoadFrom/SaveTo(SavePath) via _Ready and AddRunResult are
    // the normal-use surface.
    public void LoadFrom(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            _data = new MetaSaveData();
            return;
        }

        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            _data = JsonSerializer.Deserialize<MetaSaveData>(file.GetAsText(), Options) ?? new MetaSaveData();
        }
        catch (Exception e)
        {
            GD.PushWarning($"MetaProgressionManager: '{path}' unreadable ({e.Message}); using defaults.");
            _data = new MetaSaveData();
            return;
        }

        if (Migrate()) SaveTo(path);
    }

    // v1 -> v2: Shards were a currency you spent; TotalProgress is a total
    // you accumulate. Converting 1:1 is the honest reading of "how much has
    // this player played" - it deliberately does NOT try to add back what
    // was already spent on relic purchases, because those relics stay
    // unlocked for free via UnlockedRelicIds. Returns true if anything
    // changed and the file should be rewritten.
    // v2 -> v3 adds the ascension limit and converts nothing: a save written
    // before the ladder existed did not have a rung unlocked, and 0 is what it
    // deserializes to. The step is written out anyway, and the single `>=` gate
    // this used to be became a chain of `< n` steps to hold it, because a gate
    // is only correct while there is exactly one hop. With two, a v1 file
    // reaching v3 has to run the shard fold *and* every later step in order,
    // and the version it lands on must be the current one rather than the one
    // the first step happened to write.
    private bool Migrate()
    {
        int from = _data.SaveVersion;
        if (from >= MetaSaveData.CurrentVersion) return false;

        if (from < 2)
        {
            _data.TotalProgress += _data.Shards;
            _data.Shards = 0;

            // Anything already past its threshold at migration time is treated
            // as already announced, so a returning player isn't handed a wall of
            // "NEW!" for content the conversion just granted retroactively.
            _data.SeenUnlockIds = UnlockTrack
                .Where(e => _data.TotalProgress >= e.Threshold)
                .Select(e => e.Id)
                .ToList();
        }

        if (from < 3)
        {
            // Nothing to convert. A v2 player has won runs but never at a rung,
            // so their limit is 1 - rung 1 is unlocked for anyone who has
            // finished the game, which is exactly what winning at rung 0 means.
            if (_data.RecentSeeds.Any(s => s.Outcome == "Win")) _data.AscensionLimit = 1;
        }

        _data.SaveVersion = MetaSaveData.CurrentVersion;
        GD.Print($"MetaProgressionManager: migrated save v{from} -> v{MetaSaveData.CurrentVersion} "
                 + $"({_data.TotalProgress} progress, ascension limit {_data.AscensionLimit}).");
        return true;
    }

    public void SaveTo(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(_data, Options));
    }
}
