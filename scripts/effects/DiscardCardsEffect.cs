using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Effects;

// Discards N random cards from hand - a *cost*, paid by cards that overshoot
// their energy value (Wild Swing, Gambit). Self-only and mutates PileManager
// directly, exactly as DrawCardsEffect does.
//
// It does not signal the UI, and must not: ResolveCard fires HandChanged
// *before* the effect loop (that one is for the played card leaving hand) and
// CombatantsChanged *after* it, which CombatScreen also binds to Refresh. So
// the pile change here is already picked up one call later - an effect that
// signalled for itself would rebuild the hand twice mid-resolution.
public class DiscardCardsEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (ctx.Source is not PlayerCombatant player)
        {
            GD.PushWarning("DiscardCardsEffect: source is not a PlayerCombatant, ignoring.");
            return;
        }

        // RngStreams.Combat, never a fresh Random (genre risk 2) - which card
        // gets discarded is a draw-order decision and belongs on the same
        // stream as the shuffle, so it can't shift what the shop stocks.
        var hand = player.Piles.Hand;
        for (int i = 0; i < ctx.AmountFor(spec) && hand.Count > 0; i++)
        {
            int index = RngStreams.Combat.Next(hand.Count);
            player.Piles.Discard.Add(hand[index]);
            hand.RemoveAt(index);
        }
    }
}
