using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public class WeightedRandomIntentPicker : IIntentPicker
{
    // How many times running one move may be picked. Two rules shipped before
    // this one and each was honest at exactly one move count: excluding the
    // last-played move outright is a cap of 1, and it collapsed a 2-move enemy
    // into strict alternation - Acid Slime's nominally-40% "corrode" fired on a
    // deterministic every-other-turn cadence, its weight ignored. Disabling the
    // exclusion below three moves fixed that and opened the other end of the
    // trade: a 2-move enemy became unbounded i.i.d. sampling and could play the
    // same move four times running. A run cap is one rule that means something
    // at every move count.
    //
    // BalanceModel reads this constant rather than keeping a copy - it solves
    // the resulting chain to report what an enemy does on an average turn, and
    // a model capping at a different number than the picker is a silently wrong
    // curve. Same reason BalanceModel reads CombatManager.MaxEnemies.
    public const int MaxRun = 2;

    private string _lastMoveId = "";
    private int _run;

    public EnemyMove PickNext(EnemyCombatant self)
    {
        // The cap is the *only* thing that ever excludes a move, so below it
        // this is plain weighted sampling and every weight is live every turn.
        IReadOnlyList<EnemyMove> candidates = _run < MaxRun
            ? self.Definition.Moves
            : self.Definition.Moves.Where(m => m.MoveId != _lastMoveId).ToList();

        // A one-move enemy, or a list whose MoveIds all collide, would exclude
        // itself into nothing. The cap cannot be honoured there, so it yields.
        if (candidates.Count == 0) candidates = self.Definition.Moves;

        var picked = Roll(candidates);
        _run = picked.MoveId == _lastMoveId ? _run + 1 : 1;
        _lastMoveId = picked.MoveId;
        return picked;
    }

    private static EnemyMove Roll(IReadOnlyList<EnemyMove> candidates)
    {
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
