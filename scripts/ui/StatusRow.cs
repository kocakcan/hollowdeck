using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;

namespace Hollowdeck.UI;

// Fills an HBoxContainer with icon+count pairs for a combatant's active
// statuses (shared by EnemyView and CombatScreen's player info). The tooltip
// prose lives in Keywords, which the card and intent hover panels read from
// too - the wording of what Weak does is one sentence in one place, whether
// the player meets it on an icon, a card or an enemy's telegraph.
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
            var tooltip = Keywords.StatusTooltip(status, amount);
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

    // The pop used to scale from 0.4 to 1.0 with a Back overshoot, which swept
    // a 32x32 generated icon through every fractional scale between - and past
    // 1.0 on the way out. ART_SPEC section 9: a pixel asset animates by frame
    // swap or by alpha, never by transform.
    //
    // Nothing was lost by deleting it. The colour flash below was already
    // carrying this beat and is the legal channel; the scale was a second
    // signal for the same event, on the axis that happens to be illegal. It
    // starts brighter now so the flash alone reads as firmly as the pair did.
    private static void PlayApplyPop(TextureRect iconRect, bool isDebuff)
    {
        var flashColor = isDebuff ? UiTheme.Palette.StatusDebuff : UiTheme.Palette.StatusBuff;
        var original = iconRect.Modulate;
        iconRect.Modulate = flashColor * 1.6f;
        var tween = iconRect.CreateTween();
        tween.TweenTo(iconRect, "modulate", flashColor, Motion.Snap);
        tween.TweenTo(iconRect, "modulate", original, Motion.Fade);
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
        tween.TweenTo(ghost, "modulate:a", 0f, Motion.Fade);
        tween.TweenCallback(Callable.From(ghost.QueueFree));
    }

    // Public because EnemyView's incoming-debuff badge asks the same question:
    // one list, so a tenth status can't be a debuff here and not there.
    //
    // Since Phase 8 this is no longer only a rendering question. ApplyStatusEffect
    // reads it to decide what Artifact refuses, so a debuff added to StatusType
    // and forgotten here does not merely render with the wrong tint - it walks
    // straight past Artifact, and nothing throws. That is why the list stayed
    // here rather than being duplicated into the effect layer, and why
    // EffectSmokeTest.TestArtifactRefusesExactlyTheDebuffs drives it over the
    // whole enum instead of over a hand-picked few.
    public static bool IsDebuff(StatusType status) =>
        status is StatusType.Weak or StatusType.Vulnerable or StatusType.Poison or StatusType.Frail;

}
