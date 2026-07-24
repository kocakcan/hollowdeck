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

        MapNodes = MapGenerator.Generate(RngStreams.Map);
        CurrentNodeId = "";
        VisitedNodeIds = new HashSet<string>();
    }

    public static MapNode GetMapNode(string id) => MapNodes.First(n => n.Id == id);

    private static List<CardDefinition> StartingDeck()
    {
        // Not affected by unlocks - all cards are available from the start.
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
