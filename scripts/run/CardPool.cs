using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Rarity-weighted card sampling - the one place "which cards does the player
// get offered" is decided.
//
// Rarity has existed on CardDefinition since Phase 1 and drove exactly one
// thing: a card's border colour. Every site that handed the player a card -
// reward picks, shop stock, the random-card event outcome - shuffled the whole
// unlocked pool uniformly and took the first N, so a Rare was exactly as
// likely as a Strike and the rarity tier was pure decoration. That is the gap
// ROADMAP Phase 6 called out (see also RunScore's Pauper category, which could
// not exist while every card was Common).
//
// Weights are Slay the Spire's reward proportions, which are well-tuned for a
// pool this shape: Rare is rare enough that seeing one is an event, and
// Uncommon carries most of the deck-building.
public static class CardPool
{
    // Relative weights, not percentages - they are normalised over whatever
    // tiers are actually available, so a pool with no Rares left still works
    // rather than silently biasing toward Common.
    public const int CommonWeight = 60;
    public const int UncommonWeight = 37;
    public const int RareWeight = 3;

    // Deliberately one table shared by rewards, the shop and events rather
    // than three. Making the shop stock richer than a fight reward is a real
    // design lever, but it is a balance decision that belongs with the
    // three-act curve pass, not a number invented here - and a single table is
    // the thing that makes "how likely is a Rare" answerable at all.
    public static int WeightOf(Rarity rarity) => rarity switch
    {
        Rarity.Rare => RareWeight,
        Rarity.Uncommon => UncommonWeight,
        _ => CommonWeight,
    };

    /// `count` distinct cards drawn without replacement, weighted by rarity.
    /// Returns fewer only if the pool itself is smaller.
    ///
    /// Draws a *tier* first and then a uniform card within it, rather than
    /// weighting each card by its own rarity. The difference matters: the
    /// pool is 12 Common / 13 Uncommon / 6 Rare, so per-card weighting would
    /// hand Uncommon more total probability than Common purely because there
    /// happen to be more of them, and every card authored later would silently
    /// re-tune the odds of every tier.
    public static List<CardDefinition> Sample(IEnumerable<CardDefinition> pool, int count, Random rng)
    {
        var remaining = pool.GroupBy(c => c.Rarity)
            .ToDictionary(g => g.Key, g => g.ToList());
        var picked = new List<CardDefinition>();

        while (picked.Count < count)
        {
            var tier = PickTier(remaining, rng);
            if (tier is null) break; // pool exhausted
            var cards = remaining[tier.Value];
            int index = rng.Next(cards.Count);
            picked.Add(cards[index]);
            cards.RemoveAt(index);
            if (cards.Count == 0) remaining.Remove(tier.Value);
        }

        return picked;
    }

    /// One card, weighted the same way. For the event outcome, which grants a
    /// single card and previously did its own uniform pick.
    public static CardDefinition? SampleOne(IEnumerable<CardDefinition> pool, Random rng) =>
        Sample(pool, 1, rng).FirstOrDefault();

    // Roulette over the tiers that still have cards in them.
    private static Rarity? PickTier(Dictionary<Rarity, List<CardDefinition>> remaining, Random rng)
    {
        int total = remaining.Keys.Sum(WeightOf);
        if (total <= 0) return null;

        int roll = rng.Next(total);
        foreach (var tier in remaining.Keys.OrderBy(t => t))
        {
            roll -= WeightOf(tier);
            if (roll < 0) return tier;
        }
        // Unreachable while the weights are positive, but a tier must come
        // back rather than the caller silently getting a short list.
        return remaining.Keys.First();
    }
}
