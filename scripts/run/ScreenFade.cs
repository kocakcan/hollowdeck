using Godot;
using Hollowdeck.UI;

namespace Hollowdeck.Run;

// The cross-screen fade RunManager.ChangeScreen drives. Every other state
// change in the game is tweened - card draws, HP bars, intent reveals, the map
// node ring - and the screen swap was the one remaining hard cut
// (ROADMAP Phase 5).
//
// Lives on its own CanvasLayer parented to the RunManager autoload rather than
// to a screen, which is the whole trick: ChangeSceneToFile frees the *current
// scene*, so anything that has to be visible on both sides of the swap has to
// sit outside it. One instance covers all 13 screens; there is nothing to add
// to a .tscn and nothing for a new screen to remember to do.
public partial class ScreenFade : CanvasLayer
{
    // Above everything. Nothing else in the project creates a CanvasLayer, so
    // this only has to beat the default layer 0 that every screen draws on -
    // but it is set high rather than to 1 so a future HUD layer does not
    // quietly end up painting over the transition.
    private const int OverlayLayer = 128;

    // Out is quicker than in: leaving reads as a cut you barely notice,
    // arriving reads as the new screen resolving. Both are UiTheme.Motion
    // constants rather than new numbers.
    private const float FadeOutTime = UiTheme.Motion.Fast;
    private const float FadeInTime = UiTheme.Motion.Normal;

    // ChangeSceneToFile is itself deferred to the end of the frame, so the
    // swap does not happen the instant the callback runs. Holding full black
    // for a beat means the new scene is definitely up before the fade-in
    // starts - without it the first frames of "arriving" still show the screen
    // being left.
    private const float HoldTime = 0.05f;

    private ColorRect _cover = null!;
    private Tween? _tween;

    public override void _Ready()
    {
        Layer = OverlayLayer;

        _cover = new ColorRect
        {
            Name = "Cover",
            // N0, not pure black: the ramp's darkest entry is what every
            // backdrop and outline in the game already fades toward, and a
            // #000 wash would be the one off-ramp colour on screen
            // (docs/ART_SPEC.md section 5).
            Color = new Color(PixelSpec.Ramp.N0, 0f),
            // Stop, not Ignore. Swallowing clicks for the third of a second
            // the swap takes is a feature: it closes the window where a
            // double-click lands on the outgoing screen's button and then the
            // incoming screen's, which is the input-during-animation class of
            // bug the combat state machine exists to prevent (risk 4).
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_cover);
        _cover.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    /// True while a transition is on screen - the cover is up and eating input.
    public bool IsFading => _cover.Visible;

    /// Fades to black, runs `whileCovered` at full black, fades back in.
    /// Returns false if it declined (Reduce Motion), in which case the caller
    /// should just do the thing itself - so the hard cut lives on exactly one
    /// path rather than being duplicated at the call site.
    ///
    /// Takes an action rather than a scene path on purpose: a fade has no
    /// business knowing what a scene is, and a test can hand it a lambda that
    /// records it fired instead of one that swaps the test's own scene out
    /// from under it.
    public bool Play(System.Action whileCovered)
    {
        // Same gating ScreenBackground.AddDustMotes uses. A player who has
        // turned motion off has asked for cuts, and a fade is the single most
        // unavoidable animation in the game - it is on every screen change.
        if (SettingsManager.Instance?.ReduceMotion ?? false) return false;

        // A second transition arriving mid-fade restarts rather than queues.
        // Queueing would play the first fade's remainder against the second
        // fade's destination, and dropping it would lose the navigation.
        _tween?.Kill();
        _cover.Visible = true;

        var tween = _cover.CreateTween();
        _tween = tween;
        tween.SetTrans(UiTheme.Motion.EaseStandard);
        tween.TweenProperty(_cover, "color:a", 1f, FadeOutTime);
        tween.TweenCallback(Callable.From(whileCovered));
        tween.TweenInterval(HoldTime);
        tween.TweenProperty(_cover, "color:a", 0f, FadeInTime);
        tween.TweenCallback(Callable.From(ClearCover));
        return true;
    }

    // Bound to the cover rather than to the tree (CreateTween on the node, not
    // GetTree().CreateTween), so it is scoped to something that outlives the
    // scene swap happening in the middle of it.
    private void ClearCover()
    {
        _cover.Visible = false;
        _cover.Color = new Color(PixelSpec.Ramp.N0, 0f);
    }
}
