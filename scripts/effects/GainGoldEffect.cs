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
        RunState.Gold += spec.Amount;
    }
}
