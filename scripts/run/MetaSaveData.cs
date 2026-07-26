using System.Collections.Generic;

namespace Hollowdeck.Run;

public class SeedLogEntry
{
    public int Seed { get; set; }
    public string Outcome { get; set; } = "";
    public string TimestampUtc { get; set; } = "";

    // Added in save v2. Runs logged before that deserialize with Score 0,
    // which the unlock screen renders as "-" rather than a fake zero.
    public int Score { get; set; }
}

// Plain DTO for MetaProgressionManager's save file. SaveVersion exists so a
// format change has somewhere to branch on - v1 (the Shard currency) -> v2
// (the score-driven progress track) is the first change too large for plain
// tolerant deserialization to handle, since it repurposes what a player's
// accumulated number *means*. See MetaProgressionManager.Migrate.
public class MetaSaveData
{
    public const int CurrentVersion = 2;

    public int SaveVersion { get; set; } = CurrentVersion;

    // Total progress accumulated across every run ever played - the single
    // number the whole unlock track is keyed off. Only ever grows.
    public int TotalProgress { get; set; }
    public int BestRunScore { get; set; }
    public int RunsCompleted { get; set; }

    // v1 only: the old purchasable-unlock currency. Retained on the DTO
    // purely so Migrate can read it out of an existing save and fold it
    // into TotalProgress; nothing writes it any more.
    public int Shards { get; set; }

    // v1 relics bought with Shards. Kept as a permanent grandfather list:
    // anything in here stays unlocked regardless of the progress threshold
    // it would otherwise sit behind, so a migrating player never has content
    // taken back off them.
    public List<string> UnlockedRelicIds { get; set; } = new();

    // Track entry ids the player has already been shown as newly unlocked,
    // so the run-end screen announces each unlock exactly once.
    public List<string> SeenUnlockIds { get; set; } = new();

    public List<SeedLogEntry> RecentSeeds { get; set; } = new();
}
