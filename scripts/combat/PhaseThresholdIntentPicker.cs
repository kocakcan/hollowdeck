using Hollowdeck.Data;

namespace Hollowdeck.Combat;

// Loops Definition.Moves sequentially like SequentialLoopingIntentPicker,
// until CurrentHp/MaxHp drops to or below Definition.EnrageHpPercent - then
// permanently switches to looping Definition.EnrageMoves instead. Used for
// the act boss's enrage phase; any enemy can opt in via aiType
// "phase_threshold" without touching the combat loop itself.
public class PhaseThresholdIntentPicker : IIntentPicker
{
    private int _index;
    private bool _enraged;

    public EnemyMove PickNext(EnemyCombatant self)
    {
        if (!_enraged && self.MaxHp > 0 &&
            self.CurrentHp * 100 <= self.MaxHp * self.Definition.EnrageHpPercent)
        {
            _enraged = true;
            _index = 0;
        }

        var moves = _enraged ? self.Definition.EnrageMoves : self.Definition.Moves;
        var move = moves[_index];
        _index = _index + 1 >= moves.Count ? 0 : _index + 1;
        return move;
    }
}
