using Godot;

namespace Hollowdeck.UI;

// Small self-contained popup: rises and fades over ~0.6s, then frees itself.
// Spawned as a sibling in CombatScreen (or a child of a freshly-rebuilt
// EnemyView), never something with a lifetime CombatScreen needs to track.
public partial class FloatingText : Label
{
    public void Play(string text, Color color, Vector2 position)
    {
        Text = text;
        Modulate = color;
        Position = position;

        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "position", position + new Vector2(0, -40), 0.6)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "modulate:a", 0f, 0.6).SetTrans(Tween.TransitionType.Sine);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
