using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Scores a finished run, modelled on Slay the Spire's score categories
// (https://slaythespire.wiki.gg/wiki/Score). Only categories Hollowdeck
// actually has a mechanic for are implemented - there's no ascension level,
// no curse cards, no run timer and no Heart fight, so Ascension/Curses!/
// Speedster/Heartbreaker have nothing to read. Thresholds are scaled to
// Hollowdeck's much smaller numbers (50 max HP, ~44g per fight, an 84-card
// pool, 33 relics) - the StS value each was scaled from is noted per-category
// below.
//
// The threshold categories are now set against measurement rather than
// estimate: tools/balance-report.sh walks 500 seeded three-act maps and
// reports, per category, the best a player *routing for that category* can
// reach and the share of seeds where that clears the bar. Re-run it after
// touching acts.json or the shop prices - a threshold nobody can reach is not
// a hard category, it is dead points, and this file shipped two of them.
public static class RunScore
{
    // Where the numbers below came from, so the next pass starts from the
    // measurement rather than re-deriving it:
    //
    //   category         reachable in   note
    //   Money Money           100%      gold tiers are comfortable at every rung
    //   Raining Money         100%
    //   I Like Gold           100%      typical best-path purse is ~1040g
    //   I Like Shiny          100%      typical best path collects 15 relics
    //   Librarian              98%
    //   Encyclopedian          23%      was 50 cards, reachable in 0% of seeds
    //   Mystery Machine        83%      was 5 event rooms, reachable in 42%
    //
    // Those percentages assume the whole purse goes to one category and every
    // detour is taken, so a real run sits well under them. They are a ceiling,
    // not a forecast.

    public const int PointsPerFloor = 5;
    public const int PointsPerEnemy = 2;
    public const int PointsPerElite = 10;
    public const int PointsPerBoss = 50;
    public const int PointsPerChampion = 25;
    public const int PointsPerPerfectBoss = 50;

    // StS: 99 damage in one attack. Hollowdeck's biggest printed hit is
    // Cataclysm's 22, so 30 is the equivalent "you built something silly" bar -
    // it has to come from Strength or Vulnerable on top of a printed number.
    public const int OverkillDamage = 30;
    public const int OverkillPoints = 25;

    // StS: 20 cards in one turn, against a 3-energy baseline. Hollowdeck's
    // player has less energy and a smaller draw, so 10 is the equivalent.
    public const int ComboCards = 10;
    public const int ComboPoints = 25;

    // StS: 1,000 / 2,000 / 3,000 gold. Hollowdeck pays ~44g per fight, and a
    // best-effort path banks ~1040 across three acts, so all three rungs are
    // comfortable - they measure whether the player *spent* rather than earned.
    private static readonly (int Gold, int Points, string Label)[] GoldTiers =
    {
        (750, 75, "I Like Gold"),
        (500, 50, "Raining Money"),
        (250, 25, "Money Money"),
    };

    // StS: 25 relics. Hollowdeck has 33 in total, and a best-effort path
    // collects around 15, so 8 stays a comfortable bar. The pool grew 27 -> 33
    // with relic tiers and the collected figure did not move: what a route can
    // pick up is capped by how many Elite/Boss/Treasure/Shop nodes it can
    // string together, not by how many relics exist.
    public const int ShinyRelics = 8;
    public const int ShinyPoints = 50;

    // StS: 35 / 50 cards. Deck sizes looked comparable and are not: a
    // Hollowdeck run is ~21 fights (one card each) plus what ~1000 gold buys
    // at 50g a card, so the ceiling with every detour taken and the whole
    // purse spent on cards is 47, median 41. Encyclopedian at 50 was therefore
    // unreachable on *every* one of 500 seeds - not a hard category, dead
    // points. 43 puts it at the top quartile of deck-building runs and leaves
    // Librarian where it was.
    private static readonly (int Size, int Points, string Label)[] DeckSizeTiers =
    {
        (43, 50, "Encyclopedian"),
        (35, 25, "Librarian"),
    };

    public const int HighlanderPoints = 100;
    public const int CollectorPoints = 25;

    // StS: 100 points for finishing with no rare cards. Kept as-is - it is a
    // self-imposed restriction rather than a threshold scaled to pool size,
    // so Hollowdeck's smaller numbers do not change what it is worth.
    public const int PauperPoints = 100;

    // StS: 15 unknown rooms across three acts. Hollowdeck generates only a
    // handful of Event nodes per act - a uniformly-routed run visits 1.6, and
    // even a player taking every event the map offers has a median ceiling of
    // 4. At 5 the category was a lottery on the map roll rather than a choice:
    // only 42% of seeds could produce it at all, so on the majority of maps no
    // amount of routing earned it. 3 is reachable on 83% of maps and still
    // demands most of the detours, which is what the category is about.
    public const int MysteryRooms = 3;
    public const int MysteryPoints = 25;

    public record Entry(string Label, int Points);

    public static List<Entry> EvaluateCurrentRun() =>
        Evaluate(RunState.Stats, RunState.Deck, RunState.Gold, RunState.Relics.Count);

    // Returns the itemized breakdown, in the order it should be displayed;
    // Total() sums it. Categories that scored nothing are omitted entirely
    // rather than listed as 0, so the run-end screen only shows what was
    // actually earned.
    public static List<Entry> Evaluate(RunStats stats, List<CardDefinition> deck, int gold, int relicCount)
    {
        var entries = new List<Entry>();

        void Add(string label, int points)
        {
            if (points > 0) entries.Add(new Entry(label, points));
        }

        Add("Floors Climbed", stats.MaxFloorReached * PointsPerFloor);
        Add("Enemies Slain", stats.EnemiesSlain * PointsPerEnemy);
        Add("Elites Killed", stats.ElitesSlain * PointsPerElite);
        Add("Bosses Slain", stats.BossesSlain * PointsPerBoss);
        Add("Champion", stats.PerfectElites * PointsPerChampion);
        Add("Perfect", stats.PerfectBosses * PointsPerPerfectBoss);

        if (stats.OverkillEarned) Add("Overkill", OverkillPoints);
        if (stats.ComboEarned) Add("C-c-c-combo", ComboPoints);

        // Tiers are listed highest-first and only the first match is taken -
        // StS's "overrides" wording (I Like Gold overrides Raining Money,
        // Encyclopedian overrides Librarian), not a cumulative sum.
        foreach (var (threshold, points, label) in GoldTiers)
        {
            if (gold < threshold) continue;
            Add(label, points);
            break;
        }

        if (relicCount >= ShinyRelics) Add("I Like Shiny", ShinyPoints);

        foreach (var (size, points, label) in DeckSizeTiers)
        {
            if (deck.Count < size) continue;
            Add(label, points);
            break;
        }

        // Starter cards are excluded from both deck-composition bonuses -
        // 5 Strikes and 4 Defends would otherwise deny Highlander to every
        // possible deck and hand out Collector for free.
        var nonStarters = deck.Where(c => !RunState.StarterCardIds.Contains(c.Id)).ToList();
        if (nonStarters.Count > 0 && nonStarters.Select(c => c.Id).Distinct().Count() == nonStarters.Count)
        {
            Add("Highlander", HighlanderPoints);
        }

        int collectorSets = nonStarters.GroupBy(c => c.Id).Count(g => g.Count() >= 4);
        Add("Collector", collectorSets * CollectorPoints);

        // Pauper: cleared the run without picking up a single Rare. Measured
        // over the whole deck rather than nonStarters, because a starter card
        // being Rare would make the category unearnable by construction and
        // that should show up as a content bug, not as silently dead points.
        //
        // This is the category the file used to carry a comment apologising
        // for: while every entry in cards.json was Rarity.Common it would have
        // awarded unconditionally, so it was left out. Rarities are assigned
        // now (12/13/6 across the pool), so it means something.
        if (deck.Count > 0 && deck.All(c => c.Rarity != Rarity.Rare)) Add("Pauper", PauperPoints);

        if (stats.EventRoomsVisited >= MysteryRooms) Add("Mystery Machine", MysteryPoints);

        return entries;
    }

    public static int Total(IEnumerable<Entry> entries) => entries.Sum(e => e.Points);
}
