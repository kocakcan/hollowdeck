using System.Collections.Generic;
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
    // previous is the caller's last-seen Statuses snapshot (same before/
    // after diffing idiom CombatScreen's _lastStats already uses) - null on
    // a combatant's very first Populate call, which intentionally skips the
    // apply pop-in (nothing should "pop in" the instant combat starts).
    public static void Populate(HBoxContainer row, Combatant combatant, int iconSize,
        IReadOnlyDictionary<StatusType, int>? previous = null)
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
            bool isNew = previous is not null &&
                         (!previous.TryGetValue(status, out var prevAmount) || prevAmount <= 0);

            if (ArtAssets.StatusIcon(status) is { } icon)
            {
                var iconRect = new TextureRect
                {
                    Texture = icon,
                    CustomMinimumSize = new Vector2(iconSize, iconSize),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TooltipText = tooltip,
                    MouseFilter = Control.MouseFilterEnum.Stop,
                    PivotOffset = new Vector2(iconSize, iconSize) / 2f,
                };
                row.AddChild(iconRect);
                row.AddChild(new Label { Text = amount.ToString(), TooltipText = tooltip });
                if (isNew) PlayApplyPop(iconRect, IsDebuff(status));
            }
            else
            {
                row.AddChild(new Label { Text = $"{status} {amount}", TooltipText = tooltip });
            }
        }

        // Statuses that were present last call but aren't anymore (expired,
        // or cured) - the rebuild above already dropped their real icon, so
        // this plays a short-lived fading stand-in among the current row
        // rather than trying to preserve/animate the exact original pip
        // across a full clear-and-rebuild (a much larger structural change
        // for a purely cosmetic difference).
        if (previous is not null)
        {
            foreach (var (status, prevAmount) in previous)
            {
                if (prevAmount <= 0) continue;
                if (combatant.Statuses.TryGetValue(status, out var curAmount) && curAmount > 0) continue;
                PlayExpireGhost(row, status, iconSize);
            }
        }
    }

    private static void PlayApplyPop(TextureRect iconRect, bool isDebuff)
    {
        iconRect.Scale = Vector2.One * 0.4f;
        var flashColor = isDebuff ? UiTheme.Palette.StatusDebuff : UiTheme.Palette.StatusBuff;
        var original = iconRect.Modulate;
        iconRect.Modulate = flashColor;
        var tween = iconRect.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(iconRect, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(iconRect, "modulate", original, 0.3).SetTrans(Tween.TransitionType.Sine);
    }

    private static void PlayExpireGhost(HBoxContainer row, StatusType status, int iconSize)
    {
        if (ArtAssets.StatusIcon(status) is not { } icon) return;
        var ghost = new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0.8f),
        };
        row.AddChild(ghost);
        var tween = ghost.CreateTween();
        tween.TweenProperty(ghost, "modulate:a", 0f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(ghost.QueueFree));
    }

    private static bool IsDebuff(StatusType status) => status is StatusType.Weak or StatusType.Vulnerable or StatusType.Poison;

    private static string Describe(StatusType status, int amount) => status switch
    {
        StatusType.Strength => $"Strength {amount}: attacks deal +{amount} damage.",
        StatusType.Weak => $"Weak {amount}: attacks deal {(int)((1 - DamageMath.WeakMultiplier) * 100)}% less damage. Wears off by 1 each turn.",
        StatusType.Vulnerable => $"Vulnerable {amount}: takes {(int)((DamageMath.VulnerableMultiplier - 1) * 100)}% more damage. Wears off by 1 each turn.",
        StatusType.Poison => $"Poison {amount}: loses {amount} HP each turn (ignores Block), then Poison drops by 1.",
        _ => $"{status} {amount}",
    };
}
