using System.Collections.Generic;

namespace Hollowdeck.Data;

public enum IntentType { Attack, Defend, Buff }

public class EnemyIntent
{
    public IntentType Type { get; set; }
    public int DisplayAmount { get; set; }
}

public class EnemyMove
{
    public string MoveId { get; set; } = "";
    public EnemyIntent Intent { get; set; } = new();
    public List<EffectSpec> Effects { get; set; } = new();
    public int Weight { get; set; } = 1;
}
