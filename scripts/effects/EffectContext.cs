using System.Collections.Generic;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

public class EffectContext
{
    public required Combatant Source { get; init; }
    public required IReadOnlyList<Combatant> Targets { get; init; }
    public required CombatManager Combat { get; init; }

    // How much energy an X-cost card spent, or null for everything else -
    // which is every effect resolution in the game except a card whose Cost is
    // the -1 sentinel.
    public int? XAmount { get; init; }

    // The one place spec.Amount is turned into the amount that actually
    // resolves. Every IEffect reads this rather than spec.Amount directly, so
    // an effect cannot invent its own fallback and a new effect cannot forget
    // X exists.
    //
    // Opt-in per spec (EffectSpec.PerX) rather than a blanket override, so a
    // mixed card - "Deal X damage. Gain 3 Block." - scales one and not the
    // other. The consequence worth knowing: this multiplies the *amount*, not
    // a repeat count, so "deal 6 damage X times" is not expressible. Nothing
    // needs it; a card that wants it needs a different primitive, not a
    // widening of this one.
    public int AmountFor(EffectSpec spec) =>
        spec.PerX && XAmount is { } x ? spec.Amount * x : spec.Amount;
}
