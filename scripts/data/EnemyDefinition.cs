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
}
