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
    //
    // v4 added MapNode.Concealed - the "?" node's fog. Nothing changed on this
    // DTO for it: MapNodes serializes the domain class and RunSaveManager sets
    // IncludeFields, so the flag round-trips on its own. A v3 save's nodes
    // carry no `concealed` key and deserialize as false, which is again exactly
    // right - every map drawn before this feature was fully visible, and that
    // is what resuming one should show. No migration code needed.
    // v5 added CardSkipStreak - how many card rewards have been declined in a
    // row. A v4 save carries no `cardSkipStreak` key and deserializes it as 0,
    // which is once again exactly right: a run saved before the streak existed
    // has skipped nothing, and its next reward should be drawn on the flat
    // table it always was. No migration code needed.
    //
    // v6 added AscensionLevel - which rung of the ladder the run is being
    // played on. A v5 save carries no `ascensionLevel` key and deserializes it
    // as 0, which is once again exactly right: every run saved before the
    // ladder existed was played with it switched off. No migration code needed.
    //
    // Worth knowing about what this field does *not* do: the rung's effects are
    // not persisted, they are recomputed. Enemy HP comes off EnemyFactory at
    // the start of each fight and the map was already generated at the rung it
    // was generated on, so resuming a rung-12 run needs only the number back.
    // What is already in the save - the deck with its imposed Curses, the max
    // HP the rung lowered - is state the rung produced once and does not
    // reproduce.
    public int SaveVersion { get; set; } = 6;
    public int RunSeed { get; set; }
    public int Gold { get; set; }
    public int PlayerMaxHp { get; set; }
    public int PlayerCurrentHp { get; set; }
    public List<string> DeckCardIds { get; set; } = new();
    public List<string> RelicIds { get; set; } = new();
    public List<PotionSaveEntry> Potions { get; set; } = new();
    public int ActIndex { get; set; }
    public int CardSkipStreak { get; set; }
    public int AscensionLevel { get; set; }
    public List<MapNode> MapNodes { get; set; } = new();
    public string CurrentNodeId { get; set; } = "";
    public List<string> VisitedNodeIds { get; set; } = new();
    public RunStats Stats { get; set; } = new();
}
