using System.Collections.Generic;

namespace Hollowdeck.Data;

public class PotionDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public CardTargetType Target { get; set; }
    public List<EffectSpec> Effects { get; set; } = new();
}
