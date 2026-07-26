using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Scores a finished run, modelled on Slay the Spire's score categories
// (https://slaythespire.wiki.gg/wiki/Score). Only categories Hollowdeck
// actually has a mechanic for are implemented - there's no ascension level,
// no curse cards, no run timer and no Heart fight, so Ascension/Curses!/
// Speedster/Heartbreaker have nothing to read. Thresholds were scaled to
// Hollowdeck's much smaller numbers (50 max HP, ~45g per fight, a 30-card
// pool, 22 relics) - the StS value each was scaled from is noted per-category
// below. They were set when a run was a single act; a full three-act run now
// banks three bosses' worth of points and climbs further up the unlock track,
// so they want a pass alongside the act balance pass (see ROADMAP.md).
//
// Pauper (no rare cards) is deliberately absent: every entry in cards.json
// is currently Rarity.Common, so it would award unconditionally. Add it once
// rarities are actually assigned across the pool.
public static class RunScore
{
    public const int PointsPerFloor = 5;
    public const int PointsPerEnemy = 2;
    public const int PointsPerElite = 10;
    public const int PointsPerBoss = 50;
    public const int PointsPerChampion = 25;
    public const int PointsPerPerfectBoss = 50;

    // StS: 99 damage in one attack. Hollowdeck's biggest printed hit is Last
    // Stand's 20, so 30 is the equivalent "you built something silly" bar.
    public const int OverkillDamage = 30;
    public const int OverkillPoints = 25;

    // StS: 20 cards in one turn, against a 3-energy baseline. Hollowdeck's
    // player has less energy and a smaller draw, so 10 is the equivalent.
    public const int ComboCards = 10;
    public const int ComboPoints = 25;

    // StS: 1,000 / 2,000 / 3,000 gold. Hollowdeck pays ~45g per fight.
    private static readonly (int Gold, int Points, string Label)[] GoldTiers =
    {
        (750, 75, "I Like Gold"),
        (500, 50, "Raining Money"),
        (250, 25, "Money Money"),
    };

    // StS: 25 relics. Hollowdeck only has 22 in total.
    public const int ShinyRelics = 8;
    public const int ShinyPoints = 50;

    // StS: 35 / 50 cards - kept as-is, deck sizes are comparable.
    private static readonly (int Size, int Points, string Label)[] DeckSizeTiers =
    {
        (50, 50, "Encyclopedian"),
        (35, 25, "Librarian"),
    };

    public const int HighlanderPoints = 100;
    public const int CollectorPoints = 25;

    // StS: 15 unknown rooms across three acts. Hollowdeck generates only a
    // handful of Event nodes per act.
    public const int MysteryRooms = 5;
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

        if (stats.EventRoomsVisited >= MysteryRooms) Add("Mystery Machine", MysteryPoints);

        return entries;
    }

    public static int Total(IEnumerable<Entry> entries) => entries.Sum(e => e.Points);
}
