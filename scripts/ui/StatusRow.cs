using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Effects;

namespace Hollowdeck.UI;

// Fills an HBoxContainer with icon+count pairs for a combatant's active
// statuses (shared by EnemyView and CombatScreen's player info). Tooltips
// state the actual mechanics, with numbers sourced from DamageMath so the
// text can't drift from the resolution code.
public static class StatusRow
{
    public static void Populate(HBoxContainer row, Combatant combatant, int iconSize)
    {
        foreach (var child in row.GetChildren())
        {
            row.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var (status, amount) in combatant.Statuses)
        {
            if (amount <= 0) continue;
            var tooltip = Describe(status, amount);
            if (ArtAssets.StatusIcon(status) is { } icon)
            {
                row.AddChild(new TextureRect
                {
                    Texture = icon,
                    CustomMinimumSize = new Vector2(iconSize, iconSize),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TooltipText = tooltip,
                    MouseFilter = Control.MouseFilterEnum.Stop,
                });
                row.AddChild(new Label { Text = amount.ToString(), TooltipText = tooltip });
            }
            else
            {
                row.AddChild(new Label { Text = $"{status} {amount}", TooltipText = tooltip });
            }
        }
    }

    private static string Describe(StatusType status, int amount) => status switch
    {
        StatusType.Strength => $"Strength {amount}: attacks deal +{amount} damage.",
        StatusType.Weak => $"Weak {amount}: attacks deal {(int)((1 - DamageMath.WeakMultiplier) * 100)}% less damage. Wears off by 1 each turn.",
        StatusType.Vulnerable => $"Vulnerable {amount}: takes {(int)((DamageMath.VulnerableMultiplier - 1) * 100)}% more damage. Wears off by 1 each turn.",
        StatusType.Poison => $"Poison {amount}: loses {amount} HP each turn (ignores Block), then Poison drops by 1.",
        _ => $"{status} {amount}",
    };
}
