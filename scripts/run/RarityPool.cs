using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// The tier-first weighted draw, shared by CardPool and PotionPool.
//
// This is deliberately generic rather than copied per item type, which is the
// opposite call to BlockMath-vs-DamageMath and for the opposite reason. That
// split exists to make a *wrong reuse* unrepresentable: nothing applies
// Strength to Block, so two files stop a later edit reaching for the wrong
// multiplier. Here there is no wrong reuse to prevent - what would be
// duplicated is the algorithm, and every part of it is a correctness property
// rather than a taste: draw a tier before an item, roulette only over tiers
// that still have stock so an exhausted tier renormalises instead of biasing,
// pick uniformly inside the tier, and remove on pick so a draw is without
// replacement. OrderBy below is the clearest case - it is there so the
// roulette does not depend on Dictionary key order, and nothing in a
// hand-written second copy would remind anyone of it. Two copies of that is
// the shape CombatManager.DecayAtTurnEnd was collapsed out of.
//
// What is *not* shared is the weight table. That one genuinely is the
// BlockMath hazard - cards are 60/37/3 and potions are 65/25/10, and a potion
// draw quietly running on the card weights is exactly the bug worth making
// impossible. So weightOf is a required parameter with no default and this
// class holds no weights of its own.
public static class RarityPool
{
    /// `count` distinct items drawn without replacement, weighted by rarity.
    /// Returns fewer only if the pool itself is smaller.
    ///
    /// Draws a *tier* first and then a uniform item within it, rather than
    /// weighting each item by its own rarity. The difference matters: a tier
    /// with more rows in it would otherwise collect more total probability
    /// purely because there are more of them, and every row authored later
    /// would silently re-tune the odds of every tier.
    public static List<T> Sample<T>(
        IEnumerable<T> pool, int count, Random rng, Func<T, Rarity> rarityOf, Func<Rarity, int> weightOf)
    {
        var remaining = pool.GroupBy(rarityOf).ToDictionary(g => g.Key, g => g.ToList());
        var picked = new List<T>();

        while (picked.Count < count)
        {
            var tier = PickTier(remaining, rng, weightOf);
            if (tier is null) break; // pool exhausted
            var items = remaining[tier.Value];
            int index = rng.Next(items.Count);
            picked.Add(items[index]);
            items.RemoveAt(index);
            if (items.Count == 0) remaining.Remove(tier.Value);
        }

        return picked;
    }

    /// One item, weighted the same way.
    public static T? SampleOne<T>(
        IEnumerable<T> pool, Random rng, Func<T, Rarity> rarityOf, Func<Rarity, int> weightOf)
        where T : class =>
        Sample(pool, 1, rng, rarityOf, weightOf).FirstOrDefault();

    // Roulette over the tiers that still have stock in them.
    private static Rarity? PickTier<T>(
        Dictionary<Rarity, List<T>> remaining, Random rng, Func<Rarity, int> weightOf)
    {
        int total = remaining.Keys.Sum(weightOf);
        if (total <= 0) return null;

        int roll = rng.Next(total);
        foreach (var tier in remaining.Keys.OrderBy(t => t))
        {
            roll -= weightOf(tier);
            if (roll < 0) return tier;
        }
        // Unreachable while the weights are positive, but a tier must come
        // back rather than the caller silently getting a short list.
        return remaining.Keys.First();
    }
}
