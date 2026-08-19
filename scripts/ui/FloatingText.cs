using Godot;

namespace Hollowdeck.UI;

// Small self-contained popup: opens a rung large, drops to its resting rung,
// rises and fades over ~0.6s, then frees itself. Spawned as a sibling in
// CombatScreen (or a child of a freshly-rebuilt EnemyView), never something
// with a lifetime CombatScreen needs to track.
public partial class FloatingText : Label
{
    // How long the number stays legible on its way up. Longer than any curve in
    // the vocabulary because this one is a *readout* rather than a movement -
    // the rise is just what keeps it from covering the next one.
    private const float RiseSeconds = 0.6f;
    private const int RisePx = 40;

    // The punch-in is a step between two rungs of ART_SPEC section 7's ladder,
    // not a scale tween.
    //
    // What was here was `Scale = 1.6` (or 2.2 for a big hit) easing back to 1,
    // which put a Silkscreen Bold glyph at 25.6px and 52.8px on the way through
    // - neither a multiple of the 8px design em, so every stem in the number
    // resampled for the whole of the punch. That is the same violation
    // CardView's hover bump was, arriving through section 7's door instead of
    // section 2's: a bitmap face rendered away from its em grows uneven stems
    // exactly the way a texture drawn at a fractional scale does.
    //
    // A big hit opens two rungs above an ordinary one and rests one above it,
    // so the two are still told apart at rest as well as at the peak.
    private const int OrdinaryOpenSize = 24;
    private const int OrdinaryRestSize = 16;
    private const int BigHitOpenSize = 32;
    private const int BigHitRestSize = 24;

    // A big hit holds its open rung longer than an ordinary one - the extra
    // time is what makes the larger number readable at its peak, so the period
    // is content and no curve is involved at all. There is nothing to ease: two
    // legal sizes with nothing between them is the whole point, the way
    // GlowRing steps its ramp rather than tweening through the 43 colours that
    // are not on it.
    private const float OrdinaryHoldSeconds = 0.15f;
    private const float BigHitHoldSeconds = 0.22f;

    public void Play(string text, Color color, Vector2 position, bool bigHit = false)
    {
        ThemeTypeVariation = "CombatDisplayLabel";
        Text = text;
        Modulate = bigHit ? color.Lerp(UiTheme.Palette.AccentGoldBright, 0.5f) : color;

        int openSize = bigHit ? BigHitOpenSize : OrdinaryOpenSize;
        int restSize = bigHit ? BigHitRestSize : OrdinaryRestSize;

        // The box is fixed at the open rung's footprint and the text is centred
        // inside it (FloatingText.tscn sets both alignments), so dropping a rung
        // re-lays the glyphs *within* the box instead of moving it.
        //
        // That is not tidiness. `position` is already being tweened by the rise
        // below, so a second writer on the same property would be fighting it -
        // and it would be fighting it at exactly the moment the number is meant
        // to be read. Fixing the box means the step touches one property that
        // nothing else touches.
        var box = new Vector2(WidthAt(openSize), HeightAt(openSize));
        CustomMinimumSize = box;
        Size = box;

        // Centred on the point the old punch-in grew about - the resting rung's
        // own centre - so the readout still lands where it always did, and on a
        // whole pixel, since a bitmap glyph on a half pixel is the thing this
        // whole change is about.
        var rest = new Vector2(WidthAt(restSize), HeightAt(restSize));
        Position = (position - (box - rest) / 2f).Round();

        StepTo(openSize);
        var step = GetTree().CreateTween();
        step.Wait(bigHit ? BigHitHoldSeconds : OrdinaryHoldSeconds);
        step.TweenCallback(Callable.From(() => StepTo(restSize)));

        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenTo(this, "position", Position + new Vector2(0, -RisePx), Motion.Fade.Over(RiseSeconds));
        tween.TweenTo(this, "modulate:a", 0f, Motion.Fade.Over(RiseSeconds));
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    // PixelSpecSmokeTest's font-size scan reads *literal*
    // AddThemeFontSizeOverride calls out of the source and cannot see a size
    // that arrives in a variable, which is the same hole TextFit.Apply closes
    // by checking the rung itself. This is that check, for the same reason: the
    // four rungs above are constants in this file and invisible to the scan.
    private void StepTo(int fontSize)
    {
        if (!PixelSpec.IsLegalFontSize(fontSize))
        {
            GD.PushError($"FloatingText: {fontSize} is not a multiple of the " +
                         $"{PixelSpec.FontDesignEm}px design em (ART_SPEC section 7)");
            return;
        }

        AddThemeFontSizeOverride("font_size", fontSize);
    }

    private float WidthAt(int fontSize) => TextFit.Width(this, fontSize);

    // TextFit measures width and not height, because its two callers fit a
    // string into a box whose height a container decides. This one owns both.
    private float HeightAt(int fontSize)
    {
        var font = GetThemeFont("font");
        if (font is null) return fontSize;
        int outline = HasThemeConstant("outline_size") ? GetThemeConstant("outline_size") : 0;
        return font.GetHeight(fontSize) + outline * 2;
    }
}
