using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public class WeightedRandomIntentPicker : IIntentPicker
{
    public EnemyMove PickNext(EnemyCombatant self)
    {
        // Excluding the last-used move adds variety for 3+-move enemies,
        // but for exactly 2 moves it collapses into strict alternation -
        // not "weighted random" at all, since the excluded move's weight
        // gets ignored every other turn. That's what made Acid Slime's
        // Weak-applying "corrode" (nominally 40% weight) actually fire on
        // a deterministic every-other-turn cadence instead. Only enemies
        // with 3+ moves get the anti-repeat treatment.
        var candidates = self.LastMove is null || self.Definition.Moves.Count <= 2
            ? self.Definition.Moves
            : self.Definition.Moves.Where(m => m.MoveId != self.LastMove.MoveId).ToList();

        if (candidates.Count == 0) candidates = self.Definition.Moves;

        int total = candidates.Sum(m => m.Weight);
        int roll = RngStreams.EnemyAI.Next(total);
        foreach (var move in candidates)
        {
            if (roll < move.Weight) return move;
            roll -= move.Weight;
        }

        return candidates[^1];
    }
}
