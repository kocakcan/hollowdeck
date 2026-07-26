using System.Collections.Generic;
using Hollowdeck.Map;

namespace Hollowdeck.Run;

public class PotionSaveEntry
{
    public string DefinitionId { get; set; } = "";
}

// Plain DTO for RunSaveManager - ids/definition-ids only, never embedded
// definitions, same reasoning as MetaSaveData: a balance tweak to
// cards.json/relics.json mid-dev must not corrupt an existing run save.
public class RunSaveData
{
    // v2 added Stats. Older saves simply deserialize it as a fresh zeroed
    // RunStats (tolerant deserialization), so a run resumed across the
    // upgrade just scores from where it was reloaded rather than failing.
    //
    // v3 added ActIndex. A v2 save deserializes it as 0, which is exactly
    // right: every save written before acts existed is an act-1 run, and its
    // MapNodes are already act 1's graph. No migration code needed.
    public int SaveVersion { get; set; } = 3;
    public int RunSeed { get; set; }
    public int Gold { get; set; }
    public int PlayerMaxHp { get; set; }
    public int PlayerCurrentHp { get; set; }
    public List<string> DeckCardIds { get; set; } = new();
    public List<string> RelicIds { get; set; } = new();
    public List<PotionSaveEntry> Potions { get; set; } = new();
    public int ActIndex { get; set; }
    public List<MapNode> MapNodes { get; set; } = new();
    public string CurrentNodeId { get; set; } = "";
    public List<string> VisitedNodeIds { get; set; } = new();
    public RunStats Stats { get; set; } = new();
}
