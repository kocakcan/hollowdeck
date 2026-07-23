using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class DrawCardsEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (ctx.Source is not PlayerCombatant player)
        {
            GD.PushWarning("DrawCardsEffect: source is not a PlayerCombatant, ignoring.");
            return;
        }

        player.Piles.DrawHand(spec.Amount);
    }
}
