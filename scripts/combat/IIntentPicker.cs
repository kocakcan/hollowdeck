using Hollowdeck.Data;

namespace Hollowdeck.Combat;

public interface IIntentPicker
{
    EnemyMove PickNext(EnemyCombatant self);

    // Has this picker just entered a new phase, making the intent already on
    // screen stale? Called by CombatManager.ResolveDeathsAndSettle on the
    // *player's* turn only, and answered true at most once per transition, so a
    // true here costs one AdvanceEnemyIntent and re-telegraphs the enemy before
    // the player commits to ending their turn.
    //
    // Defaulted to false because a phase change is normally allowed to wait for
    // the enemy's own turn boundary - the intent it is holding is a real move
    // that still resolves truthfully. PhaseThresholdIntentPicker deliberately
    // leaves it that way: a boss crossing its enrage threshold mid-card has a
    // genuine attack telegraphed, and flipping it early would change every boss
    // fight in the game and the measured curve underneath them. Only a picker
    // whose current telegraph is *about* the phase itself has anything to
    // correct, which today is WakeOnDamageIntentPicker alone.
    bool TryAdvancePhase(EnemyCombatant self) => false;
}
