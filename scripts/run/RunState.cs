using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Map;

namespace Hollowdeck.Run;

// What clearing an act just did, handed back by RunState.AdvanceAct so the
// reward screen after a boss can say it. All of it - the chapter that ended,
// the one starting, and the max-HP bonus and heal that came with the change -
// used to happen silently, which is a large part of why a run's shape was
// invisible: a player who has just beaten a boss and been dropped on a fresh
// map has no way to tell "act 2 of 3 begins" from "the game is over".
public readonly record struct ActClear(
    int ClearedNumber,
    string ClearedName,
    int NextNumber,
    string NextName,
    int TotalActs,
    int MaxHpBonus,
    int Healed);

// Run-persistent state: survives scene changes within a run the same way
// CombatContext/RunEndContext do (only the node tree is torn down, not the
// CLR), just with run-length lifetime instead of single-transition lifetime.
public static class RunState
{
    public const int MaxPotionSlots = 3;

    // Where every run starts, before a blessing moves it. Named rather than
    // written into InitNewRun as literals because BalanceModel needs the same
    // two numbers and had its own copies of both - PlayerMaxHpByAct defaulted
    // to 50 and Reachable to 99, with nothing asserting either mirror. That is
    // the hazard BalanceModel's own comment names one file over, where a flat
    // ShopRelicPrice = 150 sat under a "mirrors ShopScreen" note until the
    // shop's prices moved and it did not.
    public const int StartingMaxHp = 50;
    public const int StartingGold = 99;

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

    // How many card rewards have been declined back to back. Spent by the next
    // fight's reward draw (CombatScreen.SampleCardChoices -> CardPool.Sample),
    // which shifts the rarity weights by it; reset to 0 the moment a card is
    // taken. See CardPool.MaxSkipStreak for the ladder.
    //
    // Deliberately here rather than on RunStats: that type's own header says it
    // is what a finished run needs to be *scored*, and this is a live dial the
    // next draw reads. It would have cost three fewer lines there (Stats
    // round-trips as an object), which is exactly the reason to be careful
    // about it.
    //
    // AdvanceAct does not clear it. The deck, relics and gold all carry across
    // an act boundary and a streak is the same kind of fact - a player who
    // skipped two rewards to set up the act-2 opener should get what they paid
    // for.
    public static int CardSkipStreak;

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
        Gold = StartingGold;
        PlayerMaxHp = StartingMaxHp;
        PlayerCurrentHp = StartingMaxHp;
        Deck = StartingDeck();
        // Every run starts with one guaranteed relic (Second Wind: heal 6 HP
        // on winning a fight) rather than an empty relic bar - Shop/
        // Treasure/Elite reward pools already dedupe against RunState
        // .Relics, so this can't also be rolled as a "new" pick later.
        Relics = new List<RelicInstance> { new(RelicDatabase.Get("second_wind")) };
        Potions = new List<PotionInstance>();

        ActIndex = 0;
        CardSkipStreak = 0;
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
    // Returns what it just did (null on the final act, where it does nothing) -
    // the reward screen shown immediately afterwards is the only place the
    // player can be told any of it. Callers that only care about the state
    // change can ignore the value.
    public static ActClear? AdvanceAct()
    {
        if (IsFinalAct) return null;

        var cleared = CurrentAct;
        int clearedNumber = ActIndex + 1;
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
        int before = PlayerCurrentHp;
        PlayerCurrentHp = Math.Min(PlayerMaxHp, PlayerCurrentHp + heal);

        // The HP actually restored, not the nominal percentage: a player at
        // full health is healed 0, and saying otherwise on the reward screen
        // would be the same kind of lie a drifted intent telegraph is.
        return new ActClear(
            clearedNumber, cleared.Name, ActIndex + 1, CurrentAct.Name,
            ActDatabase.Count, cleared.ClearMaxHpBonus, PlayerCurrentHp - before);
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
