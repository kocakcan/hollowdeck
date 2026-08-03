namespace Hollowdeck.Data;

public class RelicDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BehaviorId { get; set; } = "";

    // Which RelicBehavior hook fires the effect below. See RelicTrigger.cs
    // for the target/condition/limit vocabulary that turns "fires an effect
    // on a hook" into something every relic in the game can be authored as.
    public string? Hook { get; set; }
    public EffectSpec? Effect { get; set; }
    public RelicTarget Target { get; set; } = RelicTarget.Self;
    public RelicCondition? Condition { get; set; }
    public RelicLimit? Limit { get; set; }
}
