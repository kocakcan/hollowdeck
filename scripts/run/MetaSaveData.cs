using System.Collections.Generic;

namespace Hollowdeck.Run;

public class SeedLogEntry
{
    public int Seed { get; set; }
    public string Outcome { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
}

// Plain DTO for MetaProgressionManager's save file. SaveVersion exists so a
// future format change has somewhere to branch on, even though nothing
// reads it yet - tolerant deserialization (missing/unknown fields) covers
// the common case; SaveVersion is the escape hatch for changes too large
// for that (e.g. a field meaning changing, not just being added/removed).
public class MetaSaveData
{
    public int SaveVersion { get; set; } = 1;
    public int Shards { get; set; }
    public List<string> UnlockedRelicIds { get; set; } = new();
    public List<SeedLogEntry> RecentSeeds { get; set; } = new();
}
