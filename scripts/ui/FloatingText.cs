using Godot;

namespace Hollowdeck.UI;

// Small self-contained popup: punches in, rises and fades over ~0.6s, then
// frees itself. Spawned as a sibling in CombatScreen (or a child of a
// freshly-rebuilt EnemyView), never something with a lifetime CombatScreen
// needs to track.
public partial class FloatingText : Label
{
    // How long the number stays legible on its way up. Longer than any curve in
    // the vocabulary because this one is a *readout* rather than a movement -
    // the rise is just what keeps it from covering the next one.
    private const float RiseSeconds = 0.6f;

    public void Play(string text, Color color, Vector2 position, bool bigHit = false)
    {
        ThemeTypeVariation = "CombatDisplayLabel";
        Text = text;
        Modulate = bigHit ? color.Lerp(UiTheme.Palette.AccentGoldBright, 0.5f) : color;
        Position = position;
        if (bigHit) AddThemeFontSizeOverride("font_size", 24);
        PivotOffset = Size / 2f;

        Scale = Vector2.One * (bigHit ? 2.2f : 1.6f);
        // A big hit punches in over a longer beat than an ordinary one - the
        // extra time is what makes the larger number readable at its peak, so
        // the period is content and the curve is not.
        var punch = GetTree().CreateTween();
        punch.TweenTo(this, "scale", Vector2.One, Motion.Pop.Over(bigHit ? 0.22f : 0.15f));

        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenTo(this, "position", position + new Vector2(0, -40), Motion.Fade.Over(RiseSeconds));
        tween.TweenTo(this, "modulate:a", 0f, Motion.Fade.Over(RiseSeconds));
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
