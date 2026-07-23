using System.Collections.Generic;
using Hollowdeck.Data;

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
    public static bool TreasureClaimed;

    public static void InitNewRun()
    {
        Gold = 99;
        PlayerMaxHp = 50;
        PlayerCurrentHp = 50;
        Deck = StartingDeck();
        Relics = new List<RelicInstance>();
        Potions = new List<PotionInstance>();
        TreasureClaimed = false;
    }

    private static List<CardDefinition> StartingDeck()
    {
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
