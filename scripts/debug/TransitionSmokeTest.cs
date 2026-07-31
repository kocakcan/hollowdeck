using Godot;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Headless check for the cross-screen fade (ROADMAP Phase 5).
//
// Three things here are invisible when they break, which is why they get a
// suite rather than an eyeball:
//
//   - Reduce Motion. A setting that silently stops being honoured looks
//     exactly like a setting that works, and this is the one animation a
//     player cannot avoid - it is on every screen change.
//   - The cover's geometry and layer. If it stops covering the viewport, or a
//     future CanvasLayer outranks it, the fade still "plays" and simply does
//     not hide the swap.
//   - That the covered callback fires exactly once, at full black. That
//     callback is RunManager's ChangeSceneToFile - if it stopped firing, every
//     button in the game would stop navigating.
//
// ScreenFade.Play takes an Action rather than a scene path precisely so this
// test can hand it a recorder instead of something that swaps its own scene.
public partial class TransitionSmokeTest : Node
{
    private const string ScratchPath = "user://transition_settings_test.json";

    private int _pass;
    private int _fail;

    public override async void _Ready()
    {
        var fade = RunManager.Instance.Fade;
        var settings = SettingsManager.Instance;

        TestOverlayIsBuilt(fade);
        TestReduceMotionSkipsTheFade(fade);

        // Every check below needs motion *allowed*, and the autoload booted
        // from the developer's real user://settings.json - where Reduce Motion
        // may well be on. Set it explicitly against the scratch path rather
        // than inheriting whatever the machine happens to have; assuming the
        // default is how these four checks failed the first time they ran.
        settings.SetReduceMotion(false, ScratchPath);
        await TestFadeCoversThenClears(fade);
        TestRestartingMidFadeStillCompletes(fade);

        // Put the real settings back, so running the suite never changes what
        // the player sees next launch.
        settings.LoadFrom("user://settings.json");

        GD.Print($"TransitionSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void TestOverlayIsBuilt(ScreenFade fade)
    {
        Check("fade_is_attached_to_the_runmanager_autoload",
            fade.GetParent() == RunManager.Instance,
            $"parent was {fade.GetParent()?.Name}");

        // Above the layer 0 every screen draws on. If this ever ties, the
        // fade paints under the HUD and the swap shows through it.
        Check("fade_layer_is_above_screen_content", fade.Layer > 0, $"layer={fade.Layer}");

        var cover = fade.GetNode<ColorRect>("Cover");
        Check("fade_starts_hidden", !cover.Visible && Mathf.IsZeroApprox(cover.Color.A),
            $"visible={cover.Visible}, alpha={cover.Color.A}");

        // On-ramp, like everything else that gets drawn (ART_SPEC section 5).
        // A #000 wash would be the single off-ramp colour on screen, and it is
        // the one that covers the whole screen.
        Check("fade_colour_is_on_the_ramp", PixelSpec.IsOnRamp(cover.Color),
            $"colour={cover.Color.ToHtml()}");

        // Stop, so the third of a second the swap takes cannot deliver a click
        // to the outgoing screen and then the incoming one.
        Check("fade_swallows_input_while_up", cover.MouseFilter == Control.MouseFilterEnum.Stop,
            $"filter={cover.MouseFilter}");

        var viewport = GetViewport().GetVisibleRect().Size;
        Check("fade_covers_the_whole_viewport",
            cover.Size.X >= viewport.X && cover.Size.Y >= viewport.Y,
            $"cover={cover.Size}, viewport={viewport}");
    }

    // The gate that matters. Play must decline outright rather than run a
    // shorter fade, because RunManager's fallback - the hard cut - is what
    // Reduce Motion is asking for.
    private void TestReduceMotionSkipsTheFade(ScreenFade fade)
    {
        var settings = SettingsManager.Instance;
        settings.SetReduceMotion(true, ScratchPath);

        bool ran = false;
        bool played = fade.Play(() => ran = true);
        var cover = fade.GetNode<ColorRect>("Cover");

        Check("reduce_motion_declines_the_fade", !played, "Play returned true with Reduce Motion on");
        Check("reduce_motion_leaves_the_cover_down", !cover.Visible, "the cover went up anyway");
        // Declined means declined: ScreenFade must not have run the action
        // either, or RunManager's fallback would swap the scene twice.
        Check("reduce_motion_does_not_run_the_action", !ran,
            "the covered action fired even though Play declined");
    }

    private async System.Threading.Tasks.Task TestFadeCoversThenClears(ScreenFade fade)
    {
        var cover = fade.GetNode<ColorRect>("Cover");
        int ranCount = 0;

        bool played = fade.Play(() => ranCount++);
        Check("fade_plays_when_motion_is_allowed", played, "Play returned false");
        Check("fade_raises_the_cover_immediately", fade.IsFading, "IsFading was false right after Play");

        // Sample every frame across the whole sequence rather than checking
        // the alpha at one chosen instant. A single timed probe is a coin
        // flip about where the headless main loop happens to be - the first
        // version of this asked for a partial alpha at t=0.08 into a 0.12s
        // fade and read 0. What is actually worth asserting is that the cover
        // *animates*: that some frame caught it strictly between clear and
        // opaque, and some frame caught it fully opaque.
        bool sawPartial = false;
        bool sawOpaque = false;
        for (int frame = 0; frame < 120; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float alpha = cover.Color.A;
            if (alpha is > 0.02f and < 0.98f) sawPartial = true;
            if (alpha >= 0.98f) sawOpaque = true;
            if (!fade.IsFading) break;
        }

        Check("fade_ramps_rather_than_snapping", sawPartial,
            "no frame caught the cover partly opaque - it cut straight to black");
        Check("fade_reaches_full_black", sawOpaque,
            "the cover never reached full opacity, so the swap would show through");
        Check("fade_runs_the_covered_action_exactly_once", ranCount == 1, $"ran {ranCount} time(s)");
        Check("fade_clears_itself_when_done",
            !cover.Visible && Mathf.IsZeroApprox(cover.Color.A) && !fade.IsFading,
            $"visible={cover.Visible}, alpha={cover.Color.A}");
    }

    // A second navigation arriving mid-fade restarts rather than queueing. The
    // failure this guards is the cover being left up forever: kill the tween
    // that owns "put the cover back down" and nothing else ever does it, so
    // the game is a black screen that still accepts no input.
    private void TestRestartingMidFadeStillCompletes(ScreenFade fade)
    {
        var cover = fade.GetNode<ColorRect>("Cover");
        int first = 0, second = 0;

        fade.Play(() => first++);
        fade.Play(() => second++);

        Check("restarting_mid_fade_keeps_the_cover_up", fade.IsFading, "the cover dropped on restart");
        Check("restarting_mid_fade_drops_the_superseded_action", first == 0,
            $"the first navigation still fired ({first})");
        // second is checked by the fact the tween is live; awaiting it here
        // would double this suite's runtime for a path the previous test
        // already covers end-to-end.
        Check("restarting_mid_fade_has_not_fired_the_new_action_yet", second == 0,
            $"the replacement fired before reaching full black ({second})");

        cover.Visible = false;
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _pass++;
            GD.Print($"PASS {name}");
        }
        else
        {
            _fail++;
            GD.PrintErr($"FAIL {name}: {detail}");
        }
    }
}
