using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public interface IEffect
{
    void Execute(EffectContext ctx, EffectSpec spec);
}

// Escape hatch for the rare card that doesn't decompose into EffectSpecs
// (hollowdeck.md's "small IScriptedEffect escape hatch"). No Phase 1 card
// needs an implementation yet - this just proves the seam exists.
public interface IScriptedEffect
{
    void Execute(EffectContext ctx);
}
