using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.UI;

// Procedural chrome: HP-bar bezels, card frames, badges and the emphasis
// button treatment. Owns all StyleBox construction so it reads from one set
// of UiTheme tokens rather than duplicated color literals.
//
// Rebuilt for pixel art (docs/ART_SPEC.md section 6). Two things are now
// illegal here and were removed throughout:
//   - corner radii. StyleBoxFlat rounds corners by anti-aliasing them, which
//     cannot exist in pixels. Every box is hard-edged; UiTheme.Radius is gone.
//   - ShadowSize glows. Same problem - a blur has no pixel representation.
//     Emphasis is carried by border weight and ramp brightness instead.
public static class ChromeStyles
{
    public static StyleBoxFlat HpBarBackground()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgPanel,
            BorderColor = PixelSpec.Ramp.G1,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }

    public static StyleBoxFlat HpBarFill()
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.R3,
            BorderColor = PixelSpec.Ramp.R4,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thin);
        return style;
    }

    // Amber, distinct from the main bar's red, so the lagging "damage you
    // just took" zone reads as a separate layer rather than a paler version
    // of the same bar. Opaque now rather than alpha 0.85: a blended fill
    // lands on off-ramp colors, and the ghost sits over the bezel background
    // anyway so it never needed the transparency.
    public static StyleBoxFlat HpGhostBarFill() => new() { BgColor = PixelSpec.Ramp.E2 };

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

    // The "this is the important button" treatment - End Turn, and the main
    // menu's entries. Replaces the sourced CC0 ornate wooden frame this used
    // to wrap (StumpyStrust's Fantasy UI Box): that texture is smooth,
    // anti-aliased fantasy art, and sitting it beside Silkscreen glyphs and
    // 32x32 sprites was exactly the mixed-media seam the pixel-art commitment
    // exists to close.
    //
    // Distinguished from the ordinary theme button by weight, not ornament: a
    // double-thickness gold bezel on a darker face, which survives the medium
    // where a carved frame does not.
    //
    // Applies all four states in one call because the four were always set
    // together at every call site.
    public static void ApplyEmphasisButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", EmphasisState(PixelSpec.Ramp.N2, PixelSpec.Ramp.G2));
        button.AddThemeStyleboxOverride("hover", EmphasisState(PixelSpec.Ramp.N3, PixelSpec.Ramp.G4));
        button.AddThemeStyleboxOverride("pressed", EmphasisState(PixelSpec.Ramp.N1, PixelSpec.Ramp.G3));
        button.AddThemeStyleboxOverride("disabled", EmphasisState(PixelSpec.Ramp.N1, PixelSpec.Ramp.N3));
    }

    private static StyleBoxFlat EmphasisState(Color fill, Color border)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thick);
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

        // Rare's rest-state emphasis used to be a 6px blur glow. With blurs
        // gone it reads through the ramp instead - the border steps up to the
        // brightest gold - plus the extra border weight below. An animated
        // pixel border (G3->G4->G5 cycling) is the eventual treatment; that
        // belongs in CardView with the other tweens, not in this pure style
        // function.
        if (!hovered && rarity == Rarity.Rare) borderColor = UiTheme.Palette.RarityRareGlow;

        var style = new StyleBoxFlat
        {
            // PowerFill was claimed in UiTheme when the type/rarity channel
            // split landed, precisely so the third fill would not have to be
            // picked under deadline when Power arrived. This is that.
            BgColor = type switch
            {
                CardType.Attack => UiTheme.Palette.AttackFill,
                CardType.Power => UiTheme.Palette.PowerFill,
                _ => UiTheme.Palette.SkillFill,
            },
            BorderColor = borderColor,
        };

        // Border weight is now the only emphasis channel, so it carries both
        // signals: hover is thickest, an un-hovered Rare still reads heavier
        // than a Common.
        style.SetBorderWidthAll(hovered || rarity == Rarity.Rare
            ? UiTheme.BorderWidth.Thick
            : UiTheme.BorderWidth.Normal);

        return style;
    }

    // Badge (cost pip, Exhaust pip). Square, not circular: the old version
    // used a 999 corner radius to force a circle at any size, and a
    // radius-rounded box is an anti-aliased curve. A hard 2px-ringed square
    // reads the same at a glance and is native to the medium.
    public static StyleBoxFlat BadgeStyle(Color fill, Color ring)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = ring };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }

    // Keyboard-hotkey badge - deliberately a muted slate rounded-square
    // "keycap" rather than another BadgeStyle circle, so it can never be
    // mistaken for the gold cost badge or bronze exhaust badge sharing the
    // same card frame (both of those are circular and gold/bronze).
    public static StyleBoxFlat HotkeyBadgeStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.18f, 0.22f),
            BorderColor = new Color(0.55f, 0.6f, 0.68f),
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
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
        style.ContentMarginLeft = UiTheme.Spacing.Sm;
        style.ContentMarginRight = UiTheme.Spacing.Sm;
        style.ContentMarginTop = UiTheme.Spacing.Xs;
        style.ContentMarginBottom = UiTheme.Spacing.Xs;
        return style;
    }

    // A single HUD slot - one relic, one potion - as opposed to PanelStyle's
    // frame around a whole row. The relic bar and potion belt used to be one
    // wide PanelStyle box each holding a couple of small items, which read as
    // mostly empty; framing per item instead makes the row exactly as wide as
    // its contents (ROADMAP Phase 2).
    //
    // An empty slot is drawn, not omitted, so the potion belt shows capacity
    // rather than shrinking - it recedes via a dimmer bezel (G0 over the deep
    // background) instead of disappearing, which is the difference between
    // "you have room for two more" and "there is nothing here".
    public static StyleBoxFlat SlotStyle(bool filled)
    {
        var style = new StyleBoxFlat
        {
            BgColor = filled ? UiTheme.Palette.BgPanel : UiTheme.Palette.BgDeep,
            BorderColor = filled ? PixelSpec.Ramp.G1 : PixelSpec.Ramp.G0,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        style.ContentMarginLeft = UiTheme.Spacing.Xs;
        style.ContentMarginRight = UiTheme.Spacing.Xs;
        style.ContentMarginTop = UiTheme.Spacing.Xs;
        style.ContentMarginBottom = UiTheme.Spacing.Xs;
        return style;
    }

    // Always-on emphasis for the boss map node, keyed to the "Damage"
    // semantic red (danger/ominous) rather than Rarity's gold, since this is
    // not a rarity signal.
    //
    // Was a 999-radius circle with a 12px red bloom; both are anti-aliased
    // effects. Now a hard box with the heaviest border weight in the system
    // over a deep oxblood face - it still reads as the one node on the map
    // you are not supposed to walk into casually, and the skull icon it
    // frames was always doing most of that work anyway.
    public static StyleBoxFlat BossNodeGlowStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.R0,
            BorderColor = UiTheme.Palette.Damage,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thick);
        return style;
    }
}
