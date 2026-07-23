using Hollowdeck.Data;

namespace Hollowdeck.Combat;

public interface IIntentPicker
{
    EnemyMove PickNext(EnemyCombatant self);
}
