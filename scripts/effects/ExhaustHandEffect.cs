using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Effects;

// Exhausts every card left in hand. The payoff half of the pair with
// DiscardCardsEffect: Wildfire trades the hand for cards, All In trades it for
// damage.
//
// The card being played is already out of hand by the time this runs -
// CombatManager removes it from Hand before resolving its EffectSpecs - so a
// card carrying exhaust_hand does not exhaust *itself* here. Its own
// `"exhaust": true` is what sends it to the Exhaust pile afterwards, and both
// of the cards that use this set it, so the distinction is invisible in play
// but matters if one ever doesn't.
public class ExhaustHandEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (ctx.Source is not PlayerCombatant player)
        {
            GD.PushWarning("ExhaustHandEffect: source is not a PlayerCombatant, ignoring.");
            return;
        }

        // Copy first: ExhaustCard removes from Hand, so iterating Hand
        // directly would skip every other card.
        foreach (var card in new List<CardInstance>(player.Piles.Hand))
        {
            player.Piles.ExhaustCard(card);
        }
    }
}
