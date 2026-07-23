using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public class WeightedRandomIntentPicker : IIntentPicker
{
    public EnemyMove PickNext(EnemyCombatant self)
    {
        var candidates = self.LastMove is null
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
