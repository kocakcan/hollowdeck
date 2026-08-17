using System.Collections.Generic;
using Godot;

namespace Hollowdeck.UI;

// A curve is a duration, a transition and an ease, kept together because the
// three are one decision. Splitting them is what UiTheme.Motion used to do -
// Fast/Normal/Slow beside EaseStandard/EaseOvershoot - and in three phases only
// two of thirty-four tween sites ever picked a pair out of it. The other
// thirty-two wrote their own literals, including four separate re-spellings of
// Fast as `0.12` and three of Slow as `0.35`.
//
// `Over` is the escape hatch, and it is deliberately narrow: it changes the
// *period* and keeps the shape. An ambient loop's half-period is a per-site
// fact (a fog bank drifts for 14 seconds, a map ring pulses for 0.7) while the
// curve it drifts on is not, so the site that needs its own number still cannot
// pick its own easing.
public readonly record struct MotionCurve(
    float Seconds,
    Tween.TransitionType Trans,
    Tween.EaseType Ease)
{
    /// The same curve over a different period, for the sites whose duration is
    /// a fact about that site rather than a choice - loop half-periods, a shake
    /// step, a duration that branches on content.
    public MotionCurve Over(float seconds) => this with { Seconds = seconds };
}

/// The named easing vocabulary every tween in the game is built from.
///
/// docs/ART_SPEC.md section 11 is the rule; PIXEL_ART_ROADMAP.md item 6 is
/// where it came from (saint11's Easings card). The point is not that eight
/// curves are enough shapes - it is that a call site picking a ninth has to
/// argue for it here, where the other eight are visible, rather than in a
/// literal nobody else reads.
public static class Motion
{
    // Everything below is Out except Drift. That is the single largest change
    // this vocabulary made: *no* SetEase call existed anywhere in the codebase
    // before it, so all thirty-four sites ran Godot's default InOut - which
    // means every card that sprang home first pulled away from home, and every
    // Back tween overshot on both ends. A thing arriving decelerates into its
    // rest; only a loop is symmetric.

    /// One step of a shake. Uniform by design: a jitter that eases is a wobble.
    public static readonly MotionCurve Jolt =
        new(0.03f, Tween.TransitionType.Linear, Tween.EaseType.Out);

    /// The in-half of an impact tint - the fastest thing on screen.
    public static readonly MotionCurve Flash =
        new(0.06f, Tween.TransitionType.Sine, Tween.EaseType.Out);

    /// An immediate answer to input: hover, the out-half of a flash, the fade
    /// to black that starts a screen change.
    public static readonly MotionCurve Snap =
        new(0.12f, Tween.TransitionType.Sine, Tween.EaseType.Out);

    /// A small overshoot - something leaving with a kick, or a number punching
    /// in. Back rather than Sine, so it reads as force rather than as travel.
    public static readonly MotionCurve Pop =
        new(0.18f, Tween.TransitionType.Back, Tween.EaseType.Out);

    /// A value arriving at a new resting place: an HP bar, a banner, a trail
    /// going out. The default when nothing more specific applies.
    public static readonly MotionCurve Settle =
        new(0.20f, Tween.TransitionType.Sine, Tween.EaseType.Out);

    /// Something arriving with weight - a dealt card, a relic rising onto its
    /// plinth. Settle plus the overshoot that says the thing has mass.
    public static readonly MotionCurve Land =
        new(0.28f, Tween.TransitionType.Back, Tween.EaseType.Out);

    /// An alpha ramp long enough to read, in either direction: a dying enemy, an
    /// expiring status, art resolving onto a screen.
    public static readonly MotionCurve Fade =
        new(0.35f, Tween.TransitionType.Sine, Tween.EaseType.Out);

    /// The half-period of an ambient loop, and the one symmetric curve here -
    /// an Out ease on a ping-pong loop snaps at both turns, which reads as a
    /// stutter rather than as breathing. Nearly every caller passes its own
    /// period through `Over`; the 1.4 is the win-plinth breath it is taken from.
    public static readonly MotionCurve Drift =
        new(1.40f, Tween.TransitionType.Sine, Tween.EaseType.InOut);

    /// Every curve above. PixelSpecSmokeTest drives this rather than a list of
    /// names - the same reason CombatFx.All exists - and asserts both that it
    /// holds every MotionCurve field on this class and that something actually
    /// uses each one. A curve added and left out of All would be invisible to
    /// the second sweep, which is the half that catches dead vocabulary.
    public static readonly IReadOnlyList<MotionCurve> All = new[]
    {
        Jolt, Flash, Snap, Pop, Settle, Land, Fade, Drift,
    };

    /// The one place a curve's period becomes a number of seconds, and
    /// therefore the one place ROADMAP Phase 11's animation-speed setting has
    /// to multiply. It is deliberately a function with one caller rather than a
    /// field nothing writes: a `Scale` field sitting at 1f forever is dead
    /// state no assertion can see, where a funnel is a place to put the
    /// multiply when the setting lands.
    ///
    /// Note that a speed setting cannot be Engine.TimeScale - CombatScreen's
    /// hit-stop comment already argued that out, since the same dial would
    /// rescale CombatManager's turn pacing.
    public static double Seconds(MotionCurve curve) => curve.Seconds;

    /// The only way a tween in this project animates a property. Applies the
    /// curve's duration, transition and ease together, which is what makes
    /// "this site picked its own easing" unrepresentable rather than
    /// discouraged.
    public static PropertyTweener TweenTo(
        this Tween tween, GodotObject target, NodePath property, Variant finalValue, MotionCurve curve)
        => tween
            .TweenProperty(target, property, finalValue, Seconds(curve))
            .SetTrans(curve.Trans)
            .SetEase(curve.Ease);

    /// A ping-pong step pair, which is what every ambient loop in the game is:
    /// out to `away`, back to `home`, on one curve. Six sites hand-wrote this
    /// as two TweenProperty lines that had to agree about the period, and the
    /// campfire's four-step flicker is the only loop that is not this shape.
    public static void TweenPingPong(
        this Tween tween, GodotObject target, NodePath property,
        Variant away, Variant home, MotionCurve curve)
    {
        tween.TweenTo(target, property, away, curve);
        tween.TweenTo(target, property, home, curve);
    }
}
