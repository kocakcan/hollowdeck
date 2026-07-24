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

    public static void ApplyHpBarStyle(ProgressBar bar)
    {
        bar.Modulate = Colors.White; // clear the old placeholder tint hack
        bar.AddThemeStyleboxOverride("background", HpBarBackground());
        bar.AddThemeStyleboxOverride("fill", HpBarFill());
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

    // Attack/Skill get distinct frame colors so card type reads at a glance,
    // matching the genre convention of color-coded card frames - kept as the
    // dominant signal so a second color dimension (rarity) never fights it.
    // Hovered adds a thicker, brighter border plus a native StyleBoxFlat
    // drop-shadow for the "glow outline" - no shader needed. Rare cards get
    // an additional gold glow layered on top even at rest; Common/Uncommon
    // get no extra treatment yet - a real bordered-ring-per-rarity frame is
    // Phase 2 work, this just wires the parameter through.
    public static StyleBoxFlat CardFrameStyle(CardType type, Rarity rarity, bool hovered)
    {
        var isAttack = type == CardType.Attack;
        var style = new StyleBoxFlat
        {
            BgColor = isAttack ? UiTheme.Palette.AttackFill : UiTheme.Palette.SkillFill,
            BorderColor = hovered
                ? (isAttack ? UiTheme.Palette.AttackBorderHover : UiTheme.Palette.SkillBorderHover)
                : (isAttack ? UiTheme.Palette.AttackBorder : UiTheme.Palette.SkillBorder),
        };
        style.SetBorderWidthAll(hovered ? UiTheme.BorderWidth.Thick : UiTheme.BorderWidth.Normal);
        style.SetCornerRadiusAll((int)UiTheme.Radius.Card);

        if (hovered)
        {
            style.ShadowColor = isAttack
                ? new Color(0.9f, 0.4f, 0.25f, 0.65f)
                : new Color(0.35f, 0.8f, 0.6f, 0.65f);
            style.ShadowSize = 10;
        }
        else if (rarity == Rarity.Rare)
        {
            style.ShadowColor = UiTheme.Palette.RarityRareGlow;
            style.ShadowSize = 6;
        }

        return style;
    }
}
