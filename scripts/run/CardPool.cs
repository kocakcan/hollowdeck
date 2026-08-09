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
    /// The tier-first draw itself lives in RarityPool, shared with PotionPool.
    /// What stays here is the pair of things that are about *cards*: the
    /// weights above, and the IsPlayable filter below.
    public static List<CardDefinition> Sample(IEnumerable<CardDefinition> pool, int count, Random rng) =>
        // Curses and Status cards live in CardDatabase like any other row, and
        // nothing on the unlock track gates them (MetaProgressionManager
        // treats an ungated id as unlocked), so without this they would be
        // offered as rewards and stocked in the shop. Filtering here rather
        // than at each caller is the whole reason this class exists: it is the
        // single place "which cards does the player get offered" is decided,
        // and a fourth grant site added later inherits the rule for free. It
        // stays *here* rather than moving down into RarityPool for the same
        // reason - it is a card rule, and a generic sampler is not where a
        // reader would look for it.
        RarityPool.Sample(pool.Where(c => c.IsPlayable), count, rng, c => c.Rarity, WeightOf);

    /// One card, weighted the same way. For the event outcome, which grants a
    /// single card and previously did its own uniform pick.
    public static CardDefinition? SampleOne(IEnumerable<CardDefinition> pool, Random rng) =>
        Sample(pool, 1, rng).FirstOrDefault();
}
