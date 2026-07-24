using Godot;

namespace Hollowdeck.UI;

// Phase 8 chrome: replaces the flat-theme-ProgressBar-plus-Modulate-tint
// placeholder (Phase 5) with a bronze-bordered bezel for HP bars, and wraps
// a sourced CC0 ornate frame (see CREDITS.md) for the End Turn button.
public static class ChromeStyles
{
    public static StyleBoxFlat HpBarBackground()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.05f),
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
}
