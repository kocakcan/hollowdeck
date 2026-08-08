using Godot;

namespace Hollowdeck.UI;

// Picking the largest legal font size a string fits in, for the two labels
// whose text is content-authored and whose box is decided by layout: an
// enemy's name (EnemyView, width handed down by CombatScreen.FitEnemiesToTheRow)
// and a screen's title (ScreenChrome.AddTitle, width is whatever the run-status
// block and the deck strip leave).
//
// Both used to render at one size and let TextServer trim, which is how a
// playtest produced a boss called "CROWN REA" and an act title painted over the
// gold chip. Trimming is still the last resort underneath this - a 20-character
// name in a four-enemy group has nowhere to go - but it should be the last
// resort rather than the first thing that happens.
//
// The ladder is the caller's, because "what may this shrink to" is a design
// question per label. What is *not* the caller's is whether a rung is legal:
// ART_SPEC section 6 wants every rendered size on the 8px design em, and
// PixelSpecSmokeTest's source scan can only see literal
// AddThemeFontSizeOverride calls - it cannot see a size that arrives here in a
// variable. So this checks the rung itself, which is the only place that hole
// closes.
public static class TextFit
{
    /// Applies the largest size in `sizesLargestFirst` whose rendered width
    /// fits `maxWidth`, falling back to the smallest rung when none does.
    /// Measures through the label's own theme (so a ThemeTypeVariation is
    /// honoured) and counts the outline, which paints outside the glyph box.
    /// Returns the size applied.
    public static int Apply(Label label, float maxWidth, params int[] sizesLargestFirst)
    {
        int chosen = sizesLargestFirst[^1];
        foreach (int size in sizesLargestFirst)
        {
            if (!PixelSpec.IsLegalFontSize(size))
            {
                GD.PushError($"TextFit: {size} is not a multiple of the {PixelSpec.FontDesignEm}px " +
                             "design em (ART_SPEC section 6)");
                continue;
            }
            chosen = size;
            if (Width(label, size) <= maxWidth) break;
        }

        label.AddThemeFontSizeOverride("font_size", chosen);
        return chosen;
    }

    /// What `label.Text` paints at `fontSize`, outline included.
    public static float Width(Label label, int fontSize)
    {
        var font = label.GetThemeFont("font");
        if (font is null) return 0f;
        int outline = label.HasThemeConstant("outline_size") ? label.GetThemeConstant("outline_size") : 0;
        return font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, fontSize).X + outline * 2;
    }
}
