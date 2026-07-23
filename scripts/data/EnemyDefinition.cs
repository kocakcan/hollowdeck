using System.Collections.Generic;

namespace Hollowdeck.Data;

public class EnemyDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxHp { get; set; }
    public string AiType { get; set; } = "sequential";
    public int LoopFromIndex { get; set; } = 0;
    public List<EnemyMove> Moves { get; set; } = new();

    // Only used by aiType "phase_threshold": once CurrentHp/MaxHp drops to
    // or below this percent (0-100), the enemy permanently switches from
    // looping Moves to looping EnrageMoves instead. 0 (default) means no
    // enrage phase - every enemy stays plain sequential unless authored
    // otherwise.
    public int EnrageHpPercent { get; set; } = 0;
    public List<EnemyMove> EnrageMoves { get; set; } = new();
}
