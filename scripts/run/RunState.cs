using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Map;

namespace Hollowdeck.Run;

// Run-persistent state: survives scene changes within a run the same way
// CombatContext/RunEndContext do (only the node tree is torn down, not the
// CLR), just with run-length lifetime instead of single-transition lifetime.
public static class RunState
{
    public const int MaxPotionSlots = 3;

    public static int Gold;
    public static int PlayerMaxHp;
    public static int PlayerCurrentHp;
    public static List<CardDefinition> Deck = new();
    public static List<RelicInstance> Relics = new();
    public static List<PotionInstance> Potions = new();

    public static List<MapNode> MapNodes = new();
    public static string CurrentNodeId = "";
    public static HashSet<string> VisitedNodeIds = new();

    // Which act (chapter) the run is in, 0-based into ActDatabase.All. MapNodes
    // above only ever holds the *current* act's graph - clearing an act
    // regenerates it (see AdvanceAct), so node ids repeat across acts and
    // VisitedNodeIds is reset with them.
    public static int ActIndex;

    public static ActDefinition CurrentAct => ActDatabase.At(ActIndex);

    public static bool IsFinalAct => ActIndex >= ActDatabase.Count - 1;

    // Scoring tallies for this run - see RunScore/MetaProgressionManager.
    public static RunStats Stats = new();

    // The cards every run opens with. Shared by StartingDeck below and
    // RunScore's Highlander/Collector bonuses (which must ignore them), and
    // deliberately never gated by unlocks.
    public static readonly HashSet<string> StarterCardIds = new() { "strike", "defend", "bash" };

    public static void InitNewRun()
    {
        Gold = 99;
        PlayerMaxHp = 50;
        PlayerCurrentHp = 50;
        Deck = StartingDeck();
        // Every run starts with one guaranteed relic (Second Wind: heal 6 HP
        // on winning a fight) rather than an empty relic bar - Shop/
        // Treasure/Elite reward pools already dedupe against RunState
        // .Relics, so this can't also be rolled as a "new" pick later.
        Relics = new List<RelicInstance> { new(RelicDatabase.Get("second_wind")) };
        Potions = new List<PotionInstance>();

        ActIndex = 0;
        MapNodes = MapGenerator.Generate(RngStreams.Map, CurrentAct);
        CurrentNodeId = "";
        VisitedNodeIds = new HashSet<string>();
        Stats = new RunStats();
    }

    // Called when an act's boss dies and there's another act to go. The deck,
    // relics, potions and gold all carry over untouched - only the map is
    // replaced, so the run continues rather than restarting.
    //
    // Floors are counted cumulatively for scoring (RunScore's Floors Climbed)
    // because the new act's floor numbering restarts at 0; without the offset,
    // reaching act 2 would *lower* the recorded progress.
    public static void AdvanceAct()
    {
        if (IsFinalAct) return;

        var cleared = CurrentAct;
        Stats.FloorsInPreviousActs += cleared.FloorCount;

        ActIndex++;
        MapNodes = MapGenerator.Generate(RngStreams.Map, CurrentAct);
        CurrentNodeId = "";
        VisitedNodeIds = new HashSet<string>();

        // A run tuned around one act's 50 HP would not survive three, so
        // clearing an act raises the ceiling and heals part of the damage. Both
        // amounts come from the cleared act's data - see ActDefinition.
        PlayerMaxHp += cleared.ClearMaxHpBonus;
        int heal = cleared.ClearHealPercent * PlayerMaxHp / 100;
        PlayerCurrentHp = Math.Min(PlayerMaxHp, PlayerCurrentHp + heal);
    }

    public static MapNode GetMapNode(string id) => MapNodes.First(n => n.Id == id);

    private static List<CardDefinition> StartingDeck()
    {
        // Never filtered by unlocks: starters are not on the unlock track
        // (see MetaProgressionManager.UnlockTrack), so a brand-new save can
        // always build this deck.
        var counts = new (string id, int count)[]
        {
            ("strike", 5),
            ("defend", 4),
            ("bash", 1),
        };

        var deck = new List<CardDefinition>();
        foreach (var (id, count) in counts)
        {
            var def = CardDatabase.Get(id);
            for (int i = 0; i < count; i++) deck.Add(def);
        }
        return deck;
    }
}
