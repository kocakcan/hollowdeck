using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Hollowdeck.Run;

// Autoload singleton for cross-run meta-progression (unlocks, currency).
// Deliberately separate from RunManager/RunState - different lifetime and
// versioning needs. Persists to user:// (writable app-data dir), not
// res:// (packaged read-only project tree).
public partial class MetaProgressionManager : Node
{
    public static MetaProgressionManager Instance { get; private set; } = null!;

    // Relics that start locked; everything else (all cards, all potions,
    // and every other relic) is available from the start - see the Phase 3
    // plan for why only these two are worth gating at this content scale.
    public static readonly HashSet<string> LockedRelicIds = new() { "vampire_fang", "momentum_token" };

    private const string SavePath = "user://meta_progression.json";
    private const int MaxRecentSeeds = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private MetaSaveData _data = new();

    public int Shards => _data.Shards;
    public IReadOnlyList<SeedLogEntry> RecentSeeds => _data.RecentSeeds;

    public override void _Ready()
    {
        Instance = this;
        LoadFrom(SavePath);
    }

    // Only ever check membership against ids from RelicDatabase.All, never
    // call RelicDatabase.Get(idFromSave) - a stale id from an old save (a
    // relic later removed/renamed) must stay inert, not throw.
    public bool IsRelicUnlocked(string relicId) =>
        !LockedRelicIds.Contains(relicId) || _data.UnlockedRelicIds.Contains(relicId);

    // path defaults to the real save; tests pass a scratch path explicitly
    // so they can never write into the developer's/player's actual save.
    public void GrantShards(int amount, string? path = null)
    {
        _data.Shards += amount;
        SaveTo(path ?? SavePath);
    }

    public bool TryUnlockRelic(string relicId, int cost, string? path = null)
    {
        if (_data.UnlockedRelicIds.Contains(relicId)) return true;
        if (_data.Shards < cost) return false;

        _data.Shards -= cost;
        _data.UnlockedRelicIds.Add(relicId);
        SaveTo(path ?? SavePath);
        return true;
    }

    public void LogSeed(int seed, string outcome, string? path = null)
    {
        _data.RecentSeeds.Insert(0, new SeedLogEntry
        {
            Seed = seed,
            Outcome = outcome,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
        });
        if (_data.RecentSeeds.Count > MaxRecentSeeds)
        {
            _data.RecentSeeds.RemoveRange(MaxRecentSeeds, _data.RecentSeeds.Count - MaxRecentSeeds);
        }
        SaveTo(path ?? SavePath);
    }

    // Path-parameterized so tests can point at a scratch file instead of
    // the real save. LoadFrom/SaveTo(SavePath) via _Ready and the mutators
    // above are the normal-use surface.
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
        }
    }

    public void SaveTo(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(_data, Options));
    }
}
