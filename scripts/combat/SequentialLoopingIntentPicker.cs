using Hollowdeck.Data;

namespace Hollowdeck.Combat;

public class SequentialLoopingIntentPicker : IIntentPicker
{
    private int _index;

    public EnemyMove PickNext(EnemyCombatant self)
    {
        var move = self.Definition.Moves[_index];
        _index = _index + 1 >= self.Definition.Moves.Count
            ? self.Definition.LoopFromIndex
            : _index + 1;
        return move;
    }
}
