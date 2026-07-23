using System.Collections.Generic;

namespace Hollowdeck.Data;

// No Description field - same reasoning as CardDefinition: generated from
// Effects by EffectDescriptionFormatter, never hand-authored/stale.
public class PotionDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public CardTargetType Target { get; set; }
    public List<EffectSpec> Effects { get; set; } = new();
}
