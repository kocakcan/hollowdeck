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

    // How far a skip streak may climb. Past this the ladder stops rather than
    // continuing: at rung 4 the Common weight below reaches 0, which PickTier
    // would leave present-in-the-pool but unreachable - a tier that exists and
    // can never be drawn, with nothing thrown. The clamp in WeightOf is what
    // makes that unrepresentable rather than merely unauthored.
    public const int MaxSkipStreak = 3;

    // What one rung moves. The three steps sum to *zero* on purpose: the total
    // stays 100 at every rung, so a weight still reads directly as its tier's
    // percentage share and the ladder can be checked by eye.
    //
    //   streak   Common  Uncommon  Rare
    //     0        60       37       3
    //     1        45       46       9
    //     2        30       55      15
    //     3        15       64      21
    //
    // Uncommon dominates at every rung, which is the property that keeps the
    // top of the ladder a richer pool rather than a Rare dispenser. Rare does
    // pass Common at the cap, and that is intended - three consecutive skips is
    // three cards given up.
    public const int SkipCommonStep = -15;
    public const int SkipUncommonStep = 9;
    public const int SkipRareStep = 6;

    // Deliberately one table shared by rewards, the shop and events rather
    // than three. Making the shop stock richer than a fight reward is a real
    // design lever, but it is a balance decision that belongs with the
    // three-act curve pass, not a number invented here - and a single table is
    // the thing that makes "how likely is a Rare" answerable at all.
    //
    // The skip streak below is the one departure from that, and it is a
    // departure in a shape the sentence above still holds for: it is not a
    // second authored table, it is this one plus a per-draw offset the *player*
    // moves, and it applies to exactly one site (the post-fight reward). A shop
    // draw and an event grant call this overload and are unchanged.
    public static int WeightOf(Rarity rarity) => WeightOf(rarity, 0);

    /// The same table with a skip streak folded in - `skipStreak` consecutive
    /// card rewards declined, which shifts weight out of Common and into
    /// Uncommon and Rare for this draw only.
    ///
    /// Clamped rather than trusted: a caller reading a saved counter has no
    /// upper bound on it, and the ladder has to terminate somewhere the Common
    /// weight is still positive (see MaxSkipStreak).
    public static int WeightOf(Rarity rarity, int skipStreak)
    {
        int rung = Math.Clamp(skipStreak, 0, MaxSkipStreak);
        return rarity switch
        {
            Rarity.Rare => RareWeight + rung * SkipRareStep,
            Rarity.Uncommon => UncommonWeight + rung * SkipUncommonStep,
            _ => CommonWeight + rung * SkipCommonStep,
        };
    }

    /// `count` distinct cards drawn without replacement, weighted by rarity.
    /// Returns fewer only if the pool itself is smaller.
    ///
    /// The tier-first draw itself lives in TierPool, shared with PotionPool and
    /// RelicPool. What stays here is the pair of things that are about *cards*:
    /// the weights above, and the IsPlayable filter below.
    public static List<CardDefinition> Sample(IEnumerable<CardDefinition> pool, int count, Random rng) =>
        Sample(pool, count, rng, 0);

    /// The same draw with a skip streak applied to the weights. Only the
    /// post-fight reward passes a non-zero streak (CombatScreen); the shop and
    /// the random-card event outcome call the overload above and are unaffected.
    ///
    /// Note the streak changes *which* cards come back and not how many rng
    /// values the draw spends - TierPool costs exactly two Next() calls per card
    /// at every rung - so a boosted reward leaves RngStreams.Shop in the same
    /// position an unboosted one would, and a seed's later shop stock is still
    /// reproducible. That is risk 2, and it would have been easy to break here.
    public static List<CardDefinition> Sample(
        IEnumerable<CardDefinition> pool, int count, Random rng, int skipStreak) =>
        // Curses and Status cards live in CardDatabase like any other row, and
        // nothing on the unlock track gates them (MetaProgressionManager
        // treats an ungated id as unlocked), so without this they would be
        // offered as rewards and stocked in the shop. Filtering here rather
        // than at each caller is the whole reason this class exists: it is the
        // single place "which cards does the player get offered" is decided,
        // and a fourth grant site added later inherits the rule for free. It
        // stays *here* rather than moving down into TierPool for the same
        // reason - it is a card rule, and a generic sampler is not where a
        // reader would look for it.
        TierPool.Sample(pool.Where(c => c.IsPlayable), count, rng,
            c => c.Rarity, r => WeightOf(r, skipStreak));

    /// One card, weighted the same way. For the event outcome, which grants a
    /// single card and previously did its own uniform pick.
    public static CardDefinition? SampleOne(IEnumerable<CardDefinition> pool, Random rng) =>
        Sample(pool, 1, rng).FirstOrDefault();
}
