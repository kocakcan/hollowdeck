using System;
using Godot;

namespace Hollowdeck.UI;

// The animated glow ring - docs/ART_SPEC.md section 6, docs/PIXEL_ART_ROADMAP.md
// section 3.
//
// Section 6 has specified "glow" (rare cards, boss nodes, the target lock) as an
// animated pixel border rather than a blur since the spec was written, and the
// code painted a static bright border instead: CardFrameStyle's own comment
// called the ring "the eventual treatment" and ChromeStyles' header said the two
// other surfaces stayed flat because a glow "wants a driver, not a still". This
// is the driver.
//
// **It steps, it never interpolates.** That is the whole reason this is a
// _Process frame timer rather than the obvious TweenProperty on "border_color":
// a tween passes through every colour between G3 and G4, and section 5 admits
// exactly 43. PixelSpec.IsOnRamp would reject essentially all of them. Stepping
// between authored ramp entries is on-ramp by construction, and it is the same
// argument SpriteAnimator makes one asset class over - a frame swap cannot break
// the integer-scale rule the way a scale tween did.
//
// So this is deliberately shaped like SpriteAnimator: a Node parented to the
// thing it drives, a static Attach factory, a frame index advanced in _Process.
// It is a separate class rather than a generalisation of that one because the
// target types have nothing in common - SpriteAnimator sets TextureRect.Texture,
// this one installs a StyleBox on a Control - and every field of both is the
// frame cursor either way.
//
// It is also its own *file* for a reason worth keeping: CardView.cs is a named
// entry in PixelSpecSmokeTest's DeferredTransformExceptions, so code written
// there escapes the transform scan entirely. The ring's third surface is a card,
// and putting the driver in that file would have put it in the one place nothing
// is watching.
public sealed partial class GlowRing : Node
{
    // The cycles, authored low -> high -> back down.
    //
    // Ping-pong rather than a sawtooth (G3, G4, G5, G3): a sawtooth drops two
    // ramp steps in one frame every cycle, which reads as a flicker. Stepping
    // back down through the middle entry reads as a pulse, which is what a glow
    // is supposed to be.
    //
    // Two triples rather than one, because a glow ring is a mechanism and the
    // colour is the caller's. Section 6 names G3 -> G4 -> G5, and that is the
    // gold instance - rare cards and the target lock. The boss node takes the
    // red one: ChromeStyles.BossNodeGlowStyle is keyed to the Damage semantic on
    // purpose, since gold there would read as a rarity signal on a node that is
    // not one.
    public static readonly Color[] Gold =
    {
        PixelSpec.Ramp.G3, PixelSpec.Ramp.G4, PixelSpec.Ramp.G5, PixelSpec.Ramp.G4,
    };

    public static readonly Color[] Danger =
    {
        PixelSpec.Ramp.R3, PixelSpec.Ramp.R4, PixelSpec.Ramp.R5, PixelSpec.Ramp.R4,
    };

    // Which entry a cycle peaks on. Both cycles above are the same shape, so
    // this is derived rather than authored per cycle - an array with a peak
    // somewhere else is not a ping-pong.
    public static int PeakIndex(Color[] cycle) => cycle.Length / 2;

    public static Color Peak(Color[] cycle) => cycle[PeakIndex(cycle)];

    // Slower than any sprite clip. A glow is ambient furniture the player reads
    // past, not a beat they react to; at SpriteAnimator's 0.09s windup rate the
    // same two ramp steps read as a strobe.
    public const double FrameSeconds = 0.22;

    private Control _target = null!;
    private string[] _styleboxes = Array.Empty<string>();
    private Func<Color, StyleBox> _build = null!;
    private Color[] _cycle = Array.Empty<Color>();
    private int _frame;
    private double _elapsed;

    /// <summary>
    /// Attaches a ring to <paramref name="target"/>, repainting each name in
    /// <paramref name="styleboxes"/> every frame with
    /// <paramref name="build"/> applied to the current entry of
    /// <paramref name="cycle"/>.
    /// </summary>
    /// <remarks>
    /// The node parents itself to the target, so freeing the target frees the
    /// ring. A caller whose surface outlives the glow (EnemyView's target lock
    /// is the only one) has to free it itself - see the note on Stop.
    /// </remarks>
    public static GlowRing Attach(Control target, string[] styleboxes, Func<Color, StyleBox> build, Color[] cycle)
    {
        var ring = new GlowRing
        {
            Name = "GlowRing",
            _target = target,
            _styleboxes = styleboxes,
            _build = build,
            _cycle = cycle,
        };

        target.AddChild(ring);

        // Open on the peak, not on frame 0. Three things depend on it, and only
        // the first is cosmetic:
        //
        //   - The brightest entry is exactly the static colour this ring
        //     replaces on all three surfaces (G5 for a Rare card and the target
        //     lock, R5 = Damage for the boss node), so the rest state is
        //     unchanged and the ring never opens *dimmer* than the still it took
        //     over from.
        //   - ScreenShot's fixtures render these screens and capture
        //     immediately. Opening on a fixed frame is what keeps six committed
        //     screenshots from moving every time the suite runs.
        //   - There is deliberately no ScatterIdlePhase analogue for the same
        //     reason. SpriteAnimator scatters because two of the same enemy
        //     breathing in unison read as one creature drawn twice; two Rare
        //     cards in a hand pulsing in unison read as one *rule*, which is
        //     what they are.
        ring._frame = PeakIndex(cycle);
        ring.Apply();
        return ring;
    }

    /// <summary>
    /// Frees the ring without touching what it last painted, for a caller that
    /// installs its own stylebox immediately afterwards.
    /// </summary>
    /// <remarks>
    /// QueueFree alone is not enough, and the reason is worth stating precisely
    /// because it is what makes the bug intermittent rather than absent. Godot
    /// frees a queued node at the end of the frame and runs a parent before its
    /// children, so a ring released that way still gets one more _Process tick
    /// *after* the caller has installed its own box - and if that tick is the
    /// one that crosses FrameSeconds, it repaints over it. At 60fps that is
    /// roughly one unlock in fourteen. EnemyView.SetTargetLocked(false) puts a
    /// StyleBoxEmpty back and IsTargetLocked is defined as "not StyleBoxEmpty",
    /// so the visible result is an enemy that reports itself targeted with
    /// nothing aimed at it, sometimes.
    ///
    /// SetProcess(false) is what actually closes that; RemoveChild is what makes
    /// it observable, and CombatTargetingSmokeTest asserts on the child rather
    /// than on the stylebox for exactly that reason.
    /// </remarks>
    public void Stop()
    {
        SetProcess(false);
        GetParent()?.RemoveChild(this);
        QueueFree();
    }

    public override void _Process(double delta)
    {
        // Deliberately not gated on SettingsManager.ReduceMotion.
        //
        // The house rule is SpriteAnimator's: gate the photosensitive thing (its
        // hit clip opens by painting the whole creature N8), not the gentle
        // ambient loop - gating the idle breathe instead was reported from a
        // playthrough as "sprites don't animate". A two-step walk up a hue-
        // shifted ramp is the gentle end of that distinction, and the closest
        // thing already on screen agrees: MapScreen.BuildCurrentNodeRing loops
        // modulate:a between 1.0 and 0.35 with no gate, on the same screen the
        // boss ring lives on. Gating one of those two and not the other is what
        // would actually read as broken.
        if (!GodotObject.IsInstanceValid(_target))
        {
            SetProcess(false);
            return;
        }

        _elapsed += delta;
        if (_elapsed < FrameSeconds) return;
        _elapsed -= FrameSeconds;

        _frame = (_frame + 1) % _cycle.Length;
        Apply();
    }

    private void Apply()
    {
        foreach (string name in _styleboxes)
        {
            _target.AddThemeStyleboxOverride(name, _build(_cycle[_frame]));
        }
    }
}
