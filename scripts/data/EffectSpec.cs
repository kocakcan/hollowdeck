namespace Hollowdeck.Data;

public enum EffectScope { Target, Self }

public class EffectSpec
{
    public string Action { get; set; } = "";
    public int Amount { get; set; }
    public string? Status { get; set; }
    public EffectScope Scope { get; set; } = EffectScope.Target;
}
