using System;
using System.Collections.Generic;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Rarity-weighted potion sampling - the one place "which potion does the
// player get offered" is decided, for all four grant sites: the combat drop,
// the shop's two-potion stock, and the gain_potion event outcome.
//
// The gap this closes is the one CardPool closed for cards. Until now a potion
// had no rarity at all and every site shuffled the whole database uniformly,
// so greater_block_potion (18 Block) was exactly as likely as block_potion
// (12), and elixir_of_vigor was as likely as either. A tier that governs
// nothing is decoration - which is what ROADMAP's diagnosis table meant by
// "no rarity field".
public static class PotionPool
{
    // Relative weights, not percentages - TierPool normalises them over
    // whatever tiers still have stock.
    //
    // Flatter than CardPool's 60/37/3 on purpose. A card is a permanent deck
    // slot, so a Rare card should be an event; a potion is one-shot and the
    // belt holds three, so a Rare potion should be a good find rather than a
    // story. Against a 12-row pool, CardPool's weights would put a *named*
    // Rare potion at well under 1% - a tier authored and never seen.
    public const int CommonWeight = 65;
    public const int UncommonWeight = 25;
    public const int RareWeight = 10;

    // The number worth watching as content grows is per *row*, not per tier,
    // because the tier's weight is divided among its members. At 6/4/2 rows
    // that is Common 10.8%, Uncommon 6.3%, Rare 5.0% - monotone, which is what
    // makes the tiers mean anything. Authoring two more Uncommons alone would
    // drop Uncommon to 4.2% and put it *below* Rare, silently. That is what
    // EffectSmokeTest.potion_tiers_stay_monotone_by_row is watching for.
    public static int WeightOf(Rarity rarity) => rarity switch
    {
        Rarity.Rare => RareWeight,
        Rarity.Uncommon => UncommonWeight,
        _ => CommonWeight,
    };

    /// `count` distinct potions drawn without replacement, weighted by rarity.
    /// Returns fewer only if the pool itself is smaller.
    ///
    /// There is no IsPlayable-style exclusion here, and no unlock filter:
    /// unlike cards, every potion in the database is offerable and nothing on
    /// the unlock track gates one (see LibraryScreen, where UnlockKind has no
    /// Potion case). If that ever changes, this is the single place it goes.
    public static List<PotionDefinition> Sample(IEnumerable<PotionDefinition> pool, int count, Random rng) =>
        TierPool.Sample(pool, count, rng, p => p.Rarity, WeightOf);

    /// One potion, weighted the same way - the combat drop and the event
    /// outcome, both of which grant exactly one.
    public static PotionDefinition? SampleOne(IEnumerable<PotionDefinition> pool, Random rng) =>
        TierPool.SampleOne(pool, rng, p => p.Rarity, WeightOf);
}
