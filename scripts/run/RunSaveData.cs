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
    public int SaveVersion { get; set; } = 1;
    public int RunSeed { get; set; }
    public int Gold { get; set; }
    public int PlayerMaxHp { get; set; }
    public int PlayerCurrentHp { get; set; }
    public List<string> DeckCardIds { get; set; } = new();
    public List<string> RelicIds { get; set; } = new();
    public List<PotionSaveEntry> Potions { get; set; } = new();
    public List<MapNode> MapNodes { get; set; } = new();
    public string CurrentNodeId { get; set; } = "";
    public List<string> VisitedNodeIds { get; set; } = new();
}
