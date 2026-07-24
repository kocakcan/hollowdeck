using Godot;

namespace Hollowdeck.UI;

// Small self-contained popup: punches in, rises and fades over ~0.6s, then
// frees itself. Spawned as a sibling in CombatScreen (or a child of a
// freshly-rebuilt EnemyView), never something with a lifetime CombatScreen
// needs to track.
public partial class FloatingText : Label
{
    public void Play(string text, Color color, Vector2 position, bool bigHit = false)
    {
        ThemeTypeVariation = "CombatDisplayLabel";
        Text = text;
        Modulate = bigHit ? color.Lerp(UiTheme.Palette.AccentGoldBright, 0.5f) : color;
        Position = position;
        if (bigHit) AddThemeFontSizeOverride("font_size", 26);
        PivotOffset = Size / 2f;

        Scale = Vector2.One * (bigHit ? 2.2f : 1.6f);
        var punch = GetTree().CreateTween();
        punch.TweenProperty(this, "scale", Vector2.One, bigHit ? 0.22 : 0.15).SetTrans(Tween.TransitionType.Back);

        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "position", position + new Vector2(0, -40), 0.6)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "modulate:a", 0f, 0.6).SetTrans(Tween.TransitionType.Sine);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
