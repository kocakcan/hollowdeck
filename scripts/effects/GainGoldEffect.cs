using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Effects;

// Adds gold to the run purse. The one effect in the registry that ignores
// ctx.Targets entirely: gold is run state, not a property of a combatant, so
// there is nothing per-target to apply it to and EffectSpec.Scope is
// meaningless here. It exists because Scavenger's Charm ("winning a fight
// above half HP grants gold") is otherwise the single relic in the game that
// no EffectSpec can express - it used to reach into RunState from a bespoke
// RelicBehavior subclass.
//
// No SfxCues entry: AudioCues has no coin/gold cue and the ten it does have
// are one-per-category, so borrowing an unrelated one would read as a bug.
public class GainGoldEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        // Clamped at zero because a *negative* amount is now authored content:
        // an escaping thief's move steals gold with this action rather than
        // with a second effect that would only differ by a sign. Without the
        // clamp a purse that cannot pay goes negative and then swallows the
        // next reward silently.
        RunState.Gold = System.Math.Max(0, RunState.Gold + ctx.AmountFor(spec));
    }
}
