using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.UI;

// Phase 8 chrome: replaces the flat-theme-ProgressBar-plus-Modulate-tint
// placeholder (Phase 5) with a bronze-bordered bezel for HP bars, and wraps
// a sourced CC0 ornate frame (see CREDITS.md) for the End Turn button.
// Also owns card frame styling (moved here from CardView's private
// FrameStyle, which duplicated color literals with no sharing) so all
// procedural StyleBox construction reads from the same UiTheme tokens.
public static class ChromeStyles
{
    public static StyleBoxFlat HpBarBackground()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgPanel,
            BorderColor = new Color(0.5f, 0.4f, 0.22f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        return style;
    }

    public static StyleBoxFlat HpBarFill()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.66f, 0.13f, 0.13f),
            BorderColor = new Color(0.85f, 0.35f, 0.25f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        return style;
    }

    // Amber, distinct from the main bar's red, so the lagging "damage you
    // just took" zone reads as a separate layer rather than a paler version
    // of the same bar.
    public static StyleBoxFlat HpGhostBarFill()
    {
        var style = new StyleBoxFlat { BgColor = new Color(0.9f, 0.5f, 0.25f, 0.85f) };
        style.SetCornerRadiusAll(3);
        return style;
    }

    // GhostHpBar (behind) draws the bezel background + a lagging fill; the
    // real HpBar (in front) draws its normal fill but with an EMPTY
    // background, so the ghost's background/fill actually shows through in
    // the gap between the two values instead of being fully occluded by an
    // opaque background HpBar would otherwise paint every frame regardless
    // of its own fill percentage.
    public static void ApplyHpBarStyle(ProgressBar bar, ProgressBar ghostBar)
    {
        bar.Modulate = Colors.White; // clear the old placeholder tint hack
        ghostBar.Modulate = Colors.White;

        ghostBar.AddThemeStyleboxOverride("background", HpBarBackground());
        ghostBar.AddThemeStyleboxOverride("fill", HpGhostBarFill());

        bar.AddThemeStyleboxOverride("background", new StyleBoxEmpty());
        bar.AddThemeStyleboxOverride("fill", HpBarFill());
    }

    // Main bar always tweens to the new value quickly; the ghost bar only
    // lags behind on a HP loss (a heal or no-op change just snaps the ghost
    // along so it never looks like an inverted "future healing" preview).
    // ghostTween is a ref to the caller's stored Tween field so a rapid
    // second hit can kill the still-draining ghost tween instead of two
    // tweens fighting over the same Value property.
    public static void TweenHpBar(ProgressBar bar, ProgressBar ghostBar, ref Tween? ghostTween, double newValue)
    {
        double oldValue = bar.Value;
        bar.CreateTween().TweenProperty(bar, "value", newValue, 0.25).SetTrans(Tween.TransitionType.Sine);

        ghostTween?.Kill();
        if (newValue >= oldValue)
        {
            ghostBar.Value = newValue;
            return;
        }

        ghostBar.Value = oldValue;
        var tween = ghostBar.CreateTween();
        ghostTween = tween;
        tween.TweenInterval(0.15);
        tween.TweenProperty(ghostBar, "value", newValue, 0.4).SetTrans(Tween.TransitionType.Sine);
    }

    // NinePatch-style stretch: TextureMargin preserves the ~22px ornate
    // border at any button size instead of squashing it.
    public static StyleBoxTexture EndTurnButtonStyle(string texturePath)
    {
        var style = new StyleBoxTexture { Texture = GD.Load<Texture2D>(texturePath) };
        style.TextureMarginLeft = 22;
        style.TextureMarginTop = 22;
        style.TextureMarginRight = 22;
        style.TextureMarginBottom = 22;
        style.ContentMarginLeft = 16;
        style.ContentMarginTop = 10;
        style.ContentMarginRight = 16;
        style.ContentMarginBottom = 10;
        return style;
    }

    // Two independent color channels rather than one fighting a second
    // painted on top of it: fill reads CardType (Attack/Skill), border reads
    // Rarity (Common/Uncommon/Rare). Hover brightens whichever border color
    // is already showing (a pure brightness signal, not a hue swap), Rare
    // keeps its gold glow at rest, and an upgraded card blends its border
    // toward green independent of rarity - an upgraded Rare card reads as
    // gold-with-a-green-cast rather than one signal replacing the other.
    public static StyleBoxFlat CardFrameStyle(CardType type, Rarity rarity, bool hovered, bool isUpgraded = false)
    {
        var borderColor = rarity switch
        {
            Rarity.Uncommon => UiTheme.Palette.RarityUncommon,
            Rarity.Rare => UiTheme.Palette.RarityRare,
            _ => UiTheme.Palette.RarityCommon,
        };
        if (isUpgraded) borderColor = borderColor.Lerp(UiTheme.Palette.UpgradeAccent, 0.35f);
        if (hovered) borderColor = borderColor.Lerp(Colors.White, 0.4f);

        var style = new StyleBoxFlat
        {
            BgColor = type == CardType.Attack ? UiTheme.Palette.AttackFill : UiTheme.Palette.SkillFill,
            BorderColor = borderColor,
        };
        style.SetBorderWidthAll(hovered ? UiTheme.BorderWidth.Thick : UiTheme.BorderWidth.Normal);
        style.SetCornerRadiusAll((int)UiTheme.Radius.Card);

        if (hovered)
        {
            style.ShadowColor = new Color(borderColor.R, borderColor.G, borderColor.B, 0.65f);
            style.ShadowSize = 10;
        }
        else if (rarity == Rarity.Rare)
        {
            style.ShadowColor = UiTheme.Palette.RarityRareGlow;
            style.ShadowSize = 6;
        }

        return style;
    }

    // Circular badge (cost pip, Exhaust pip) - a large corner radius always
    // rounds to a full circle regardless of the node's actual size, so no
    // per-size radius math is needed at the call site.
    public static StyleBoxFlat BadgeStyle(Color fill, Color ring)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = ring };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        style.SetCornerRadiusAll(999);
        return style;
    }

    // Subtle darker overlay shared by the card's name banner, art window,
    // and description box - semi-transparent black alpha-blends over
    // whatever's already painted beneath it (the card's type-colored fill),
    // so all three zones read as distinct insets without introducing a
    // third hue that would compete with type (fill) and rarity (border).
    public static StyleBoxFlat InsetPanelStyle()
    {
        var style = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.28f) };
        style.SetCornerRadiusAll((int)UiTheme.Radius.Panel);
        style.ContentMarginLeft = 4;
        style.ContentMarginRight = 4;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        return style;
    }

    // Generic bronze-bordered HUD row panel - same visual language as the
    // HP bar bezel (HpBarBackground), reused for any row that currently
    // paints straight onto the fog/vignette backdrop with no framing at all
    // (relic bar, potion belt, gold display).
    public static StyleBoxFlat PanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgPanel,
            BorderColor = new Color(0.5f, 0.4f, 0.22f),
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        style.SetCornerRadiusAll((int)UiTheme.Radius.Panel);
        style.ContentMarginLeft = UiTheme.Spacing.Sm;
        style.ContentMarginRight = UiTheme.Spacing.Sm;
        style.ContentMarginTop = UiTheme.Spacing.Xs;
        style.ContentMarginBottom = UiTheme.Spacing.Xs;
        return style;
    }

    // Always-on glow for the boss map node - same drop-shadow trick
    // CardFrameStyle uses for a Rare card's glow, keyed to the existing
    // "Damage" semantic red token (danger/ominous) rather than Rarity's
    // gold, since this isn't a rarity signal.
    public static StyleBoxFlat BossNodeGlowStyle()
    {
        var accent = UiTheme.Palette.Damage;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.05f, 0.05f, 0.9f),
            BorderColor = accent,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thick);
        style.SetCornerRadiusAll(999);
        style.ShadowColor = new Color(accent.R, accent.G, accent.B, 0.75f);
        style.ShadowSize = 12;
        return style;
    }
}
