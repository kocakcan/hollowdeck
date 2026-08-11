using System;
using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Which relic the player gets offered, and from which pool - the one place
// that is decided, for all five grant sites: the elite reward, the boss reward,
// the treasure room, the shop's two-relic stock, and the gain_relic event
// outcome.
//
// The gap this closes is the one CardPool closed for cards and PotionPool for
// potions, and it is the last of the three. Until now RelicDefinition carried
// no tier at all and every site ran its own copy of the same three-line LINQ
// over the whole database, picked uniformly, and stopped - so a boss handed
// over exactly what 150 gold would have, and the strongest relic in the game
// was as likely out of a chest as out of the act's final fight.
//
// What is different here, and the reason this is not simply PotionPool with a
// third weight table: a relic tier is two axes wearing one enum. Common,
// Uncommon and Rare are a power ladder and behave like a rarity - every site
// that draws them draws them at the weights below. Boss, Shop and Event are
// *sources*: they name where a relic may come from rather than how strong it
// is, and the whole point of the feature is that a Boss relic is one no shop
// can ever stock. So a site is expressed as which tiers it may see, and
// TierPool renormalises over whatever is actually in the pool it is handed.
public static class RelicPool
{
    // Relative weights, not percentages - TierPool normalises them over
    // whatever tiers still have stock.
    //
    // Steeper than PotionPool's 65/25/10 and much flatter than CardPool's
    // 60/37/3, which is the right place between them: a run collects on the
    // order of six relics against sixteen card rewards, so a Rare relic at
    // CardPool's 3 would be a tier most runs never see, and relics have no
    // one-shot tier where a generous rate is harmless.
    public const int CommonWeight = 50;
    public const int UncommonWeight = 33;
    public const int RareWeight = 17;

    // Boss is never mixed with another tier - a boss draw sees the Boss tier
    // alone - so this number is renormalised to the whole draw every time and
    // says nothing about how a Boss relic compares to a Rare one. It has to be
    // *positive* rather than accurate: at 0, TierPool.PickTier finds a total
    // weight of 0, returns null, and the boss reward silently vanishes.
    public const int BossWeight = 100;

    // These two do mix, with the ladder, at their own site only. 25 against a
    // ladder totalling 100 puts a shop-exclusive relic in one shop offer in
    // five, which is enough for the tier to be a thing the player notices
    // without crowding out the ladder the rest of the game is drawn from.
    public const int ShopWeight = 25;
    public const int EventWeight = 25;

    // The number worth watching as content grows is per *row*, not per tier,
    // because a tier's weight is divided among its members - the same hazard
    // PotionPool documents, and a sharper one here because there are six tiers
    // to unbalance rather than three. At 7/9/6 ladder rows that is Common 7.1,
    // Uncommon 3.7, Rare 2.8 - monotone, which is what makes the ladder mean
    // anything. Authoring three more Uncommons alone would put an Uncommon
    // below a Rare, silently. EffectSmokeTest.relic_tiers_stay_monotone_by_row
    // is watching for it.
    public static int WeightOf(RelicTier tier) => tier switch
    {
        RelicTier.Uncommon => UncommonWeight,
        RelicTier.Rare => RareWeight,
        RelicTier.Boss => BossWeight,
        RelicTier.Shop => ShopWeight,
        RelicTier.Event => EventWeight,
        _ => CommonWeight,
    };

    // The power ladder, which every site except the boss draws from.
    private static readonly RelicTier[] Ladder =
        { RelicTier.Common, RelicTier.Uncommon, RelicTier.Rare };

    /// Which tiers a given grant site may draw from. This is the whole feature:
    /// everything else here is the machinery that enforces it.
    ///
    /// Boss is the only site that leaves the ladder entirely, and that is the
    /// asymmetry worth stating rather than inferring. A shop or an event adds
    /// its own exclusive tier *on top of* the ladder, because a shop that only
    /// ever stocked its three exclusives would be empty by act II. A boss
    /// replaces the ladder, because "the relic you get for killing a boss" is
    /// the one grant in the game that should never be an Anchor Stone.
    public static RelicTier[] TiersFor(RelicSite site) => site switch
    {
        RelicSite.Boss => new[] { RelicTier.Boss },
        RelicSite.Shop => Ladder.Append(RelicTier.Shop).ToArray(),
        RelicSite.Event => Ladder.Append(RelicTier.Event).ToArray(),
        _ => Ladder,
    };

    /// `count` distinct relics for a site, drawn without replacement and
    /// weighted by tier. Returns fewer only if the site's pool is smaller even
    /// after the ladder top-up below.
    ///
    /// The owned-and-unlocked filter lives here rather than at the five call
    /// sites that used to each carry a copy, for exactly the reason CardPool
    /// holds IsPlayable: it is a relic rule, a sixth grant site added later
    /// inherits it for free, and a generic sampler is not where a reader would
    /// look for it. It stays out of TierPool for the same reason.
    public static List<RelicDefinition> Sample(RelicSite site, int count, Random rng)
    {
        var picked = TierPool.Sample(Available(TiersFor(site)), count, rng, r => r.Tier, WeightOf);

        // A site whose own tiers cannot fill the draw tops up from the ladder
        // before giving up. Only Boss can actually reach this - the ladder is 22
        // rows against about six relics a run - and it is the one where
        // returning nothing is worst: a player doing well enough to own every
        // Boss relic would be paid nothing for killing a boss. A Rare instead is
        // a worse reward, which is the correct shape for a pool running dry;
        // silence is not.
        //
        // The shortfall is filled by a *second* draw rather than by widening the
        // first one's pool, and that is the whole subtlety. Concatenating the
        // ladder into the pool up front would put it in the same tier roulette
        // as Boss, where BossWeight is only half the total - so a boss with two
        // of its own relics left would hand back Commons about one tile in two
        // while both Boss relics sat unoffered. That also turns BossWeight into
        // a live mixing ratio, which the comment above it says it is not. Drawn
        // this way "Boss is never mixed with another tier" stays true: its own
        // tier is exhausted before the ladder is asked for anything.
        //
        // Measured against `count` rather than zero, which at count 1 is the
        // same condition - so every single-draw site behaves exactly as it did.
        // A boss offering a choice of three has to actually offer three, and a
        // Boss tier with two rows left would otherwise hand over two tiles with
        // nothing thrown anywhere.
        if (picked.Count < count && site != RelicSite.Reward)
        {
            var already = picked.Select(r => r.Id).ToHashSet();
            picked.AddRange(TierPool.Sample(
                Available(Ladder).Where(r => !already.Contains(r.Id)),
                count - picked.Count, rng, r => r.Tier, WeightOf));
        }

        return picked;
    }

    /// One relic, weighted the same way - the treasure room, the elite reward
    /// and the gain_relic event. The shop stocks two and a boss offers three.
    public static RelicDefinition? SampleOne(RelicSite site, Random rng) =>
        Sample(site, 1, rng).FirstOrDefault();

    private static List<RelicDefinition> Available(IReadOnlyCollection<RelicTier> tiers)
    {
        var owned = RunState.Relics.Select(r => r.Definition.Id).ToHashSet();
        return RelicDatabase.All
            .Where(r => tiers.Contains(r.Tier)
                && !owned.Contains(r.Id)
                && MetaProgressionManager.Instance.IsRelicUnlocked(r.Id))
            .ToList();
    }
}

// The five places a relic is handed over. A site rather than a tier at the call
// sites, because what a caller knows is where it is standing, not which tiers
// that implies - and keeping the mapping in TiersFor above is what stops a new
// grant site inventing its own tier set.
public enum RelicSite { Reward, Boss, Treasure, Shop, Event }
