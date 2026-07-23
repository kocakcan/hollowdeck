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
}
