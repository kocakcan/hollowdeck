using System.Collections.Generic;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public abstract class Combatant
{
    public string Name = "";
    public int MaxHp;
    public int CurrentHp;
    public int Block;
    public Dictionary<StatusType, int> Statuses = new();
    public bool IsDead => CurrentHp <= 0;

    public int GetStatus(StatusType status) => Statuses.GetValueOrDefault(status, 0);

    public void AddStatus(StatusType status, int amount)
    {
        Statuses[status] = GetStatus(status) + amount;
    }

    public void DecayStatus(StatusType status)
    {
        var current = GetStatus(status);
        if (current <= 0) return;
        Statuses[status] = current - 1;
    }
}

public class PlayerCombatant : Combatant
{
    public int MaxEnergy = 3;
    public int CurrentEnergy;
    public PileManager Piles = null!;
}

public class EnemyCombatant : Combatant
{
    public EnemyDefinition Definition = null!;
    public IIntentPicker IntentPicker = null!;
    public EnemyMove? CurrentMove;
    public EnemyMove? LastMove;

    // Left the fight alive, via an escape move. Set by EscapeEffect and acted
    // on one pass later in CombatManager.ResolveDeathsAndSettle, so an enemy
    // cannot vanish part-way through the remaining specs of its own move.
    public bool HasEscaped;

    // Recursion guard for Definition.OnDeath: an onDeath can kill another
    // enemy (or summon one that immediately dies), so ResolveDeaths loops
    // until nothing new is dying, and this is what terminates it.
    public bool OnDeathFired;

    // The one predicate every "is this still in the fight" site reads - the
    // combat loop's skips, the drag hit test, the keyboard target. There are
    // two ways out of a fight now and a third would otherwise be four call
    // sites to remember rather than one.
    public bool IsGone => IsDead || HasEscaped;
}
