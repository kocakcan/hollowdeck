using System.Collections.Generic;

namespace Hollowdeck.Data;

public class RelicDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BehaviorId { get; set; } = "";
    public string? Hook { get; set; }
    public EffectSpec? Effect { get; set; }
    public Dictionary<string, int> Params { get; set; } = new();
}
