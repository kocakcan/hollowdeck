using System;
using System.Collections.Generic;
using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Plays the derived frame clips (tools/artgen/src/anim.rs) on a creature
// sprite by swapping its Texture.
//
// This replaces the scale/rotation tweens EnemyView and CombatScreen used to
// drive, and the reason is ART_SPEC section 2: `scale -> 1.04` on a
// nearest-filtered 32px sprite renders 5.2 device pixels per source pixel, so
// some source pixels came out 5px wide and some 6px, and `rotation_degrees ->
// 6` staircased the silhouette outright. The spec called that a bug rather
// than a judgement call for three phases while the code did it every frame -
// PixelSpecSmokeTest only ever checked the *static* CustomMinimumSize, so the
// rule was enforced at rest and broken in motion.
//
// Frame animation cannot break it: the motion lives inside the 32x32 frame and
// the node transform is never touched.
//
// **Deliberately not AnimatedSprite2D.** The nodes this drives are TextureRects
// whose CustomMinimumSize is the exact thing
// PixelSpecSmokeTest.TestCreatureSpritesRenderAtIntegerScale asserts on, and
// EnemyView's sits inside a VBoxContainer. A Node2D there would break Control
// layout and that assertion at once. Swapping Texture on the node that already
// exists keeps every layout fact intact.
//
// It also dissolves the problem EnemyView.cs documented and worked around: a
// Container owns its children's position and size, which is why the old code
// reached for Scale as "the one transform a container does not touch". A frame
// swap is not a transform, so the container has no opinion about it.
public sealed partial class SpriteAnimator : Node
{
    // Seconds per frame, per clip. Idle is slow because a 32x32 breathe is a
    // held pose rather than an interpolation - the tween version's smoothness
    // is exactly what required a fractional scale to exist.
    private static readonly Dictionary<string, double> FrameTime = new()
    {
        ["idle"] = 0.45,
        ["windup"] = 0.09,
        ["hit"] = 0.05,
        ["death"] = 0.12,
        ["escape"] = 0.09,
    };

    private const string IdleClip = "idle";
    private const double DefaultFrameTime = 0.1;

    // The synthetic clip name AttachOneShot registers its frames under. Not in
    // FrameTime above, because a one-shot's cadence comes from its caller - see
    // _frameTime.
    private const string OneShotClip = "one_shot";

    // The clip sets, named here rather than typed out at each Attach call.
    //
    // They were string literals at the two call sites, which made the clip names
    // a *third* copy of a table that already exists twice - anim.rs writes it
    // and PixelSpecSmokeTest restates it across the Rust/C# seam. A typo in that
    // third copy is completely silent: Attach never loads the clip, PlayTerminal
    // takes its missing-clip branch, the escaping enemy fades in place instead
    // of walking out, all 438 frames are still on disk and all 23 suites stay
    // green. That is the CardUpgrade.ShouldScale shape one asset class over.
    //
    // Two copies is the irreducible minimum, because nothing imports across a
    // language boundary. TestEveryCreatureHasItsFrames pins those two together,
    // and these constants are what stop there being a third.
    public static readonly string[] CreatureClips = { IdleClip, "windup", "hit", "death", "escape" };

    // No death or escape: a player death is RunEndScreen, and the player never
    // flees a fight. Authoring them would put two clips on disk nothing plays.
    public static readonly string[] PlayerClips = { IdleClip, "windup", "hit" };

    private TextureRect _target = null!;
    private readonly Dictionary<string, Texture2D[]> _clips = new();

    private string _clip = IdleClip;
    private int _frame;
    private double _elapsed;
    private EndBehavior _onEnd = EndBehavior.Loop;
    private bool _held;
    private Action? _onComplete;
    private double? _frameTime;

    /// <summary>
    /// Attaches an animator to <paramref name="target"/>, preloading every
    /// clip for <paramref name="spriteId"/>. Returns null when the sprite has
    /// no generated frames, so a run in progress keeps rendering the static
    /// texture rather than crashing.
    /// </summary>
    /// <remarks>
    /// That null is not a silent fallback in the sense EventIcon's is. These
    /// frames are generated for every id, so missing ones mean `artgen animate`
    /// was not re-run - and PixelSpecSmokeTest.TestEveryCreatureHasItsFrames
    /// fails on exactly that, in both directions. The graceful runtime path
    /// exists so the failure is a red suite rather than a crashed playtest.
    /// </remarks>
    public static SpriteAnimator? Attach(TextureRect target, string spriteId, params string[] clips)
    {
        var animator = new SpriteAnimator { Name = "SpriteAnimator", _target = target };
        foreach (string clip in clips)
        {
            var frames = ArtAssets.AnimFrames(spriteId, clip);
            if (frames.Length > 0) animator._clips[clip] = frames;
        }

        if (!animator._clips.ContainsKey(IdleClip))
        {
            animator.QueueFree();
            return null;
        }

        target.AddChild(animator);
        animator.PlayIdle();
        animator.ScatterIdlePhase();
        return animator;
    }

    /// <summary>
    /// Attaches an animator that plays one run of <paramref name="frames"/> and
    /// stops on the last one, invoking <paramref name="onComplete"/> — which is
    /// where a transient effect node frees itself. Returns null on an empty run.
    /// </summary>
    /// <remarks>
    /// Deliberately not routed through <see cref="Attach"/>: that one resolves
    /// frames from a sprite id and requires an idle clip, and a combat burst
    /// (tools/artgen/src/icons/fx.rs) has neither a sprite nor a rest state. It
    /// is the same driver underneath — _Process, ApplyFrame and the Hold end
    /// behaviour are untouched — which is the point. A burst is a pixel asset
    /// animating by frame swap, so it belongs to this class rather than to a
    /// second one that would have to re-learn ART_SPEC section 9.
    ///
    /// The clip is registered under OneShotClip and that name is in
    /// FlashOpeningClips, so the Reduce Motion rule reaches these for free: an
    /// effect's frame 0 is its brightest, exactly as the hit clip's is, and it
    /// gets declined rather than softened by the mechanism already written for
    /// that. A per-effect gate would have been a second answer to one question.
    /// </remarks>
    public static SpriteAnimator? AttachOneShot(
        TextureRect target, Texture2D[] frames, double frameTime, Action? onComplete = null)
    {
        if (frames.Length == 0) return null;

        var animator = new SpriteAnimator { Name = "SpriteAnimator", _target = target };
        animator._clips[OneShotClip] = frames;
        animator._frameTime = frameTime;
        target.AddChild(animator);
        animator.Play(OneShotClip, EndBehavior.Hold, onComplete);
        return animator;
    }

    // Start the breathing loop at a random point in its cycle, so a fight
    // against two of the same enemy does not show them inhaling in perfect
    // unison - which reads as one creature drawn twice rather than two.
    //
    // The tween version carried this as `TweenInterval(GD.Randf() * 1.0)` and
    // it was worth keeping; a frame clip needs both halves of the phase (which
    // frame, and how far into it) rather than just a delay, because two frames
    // held for 0.45s each spread over a 0.9s cycle that a sub-frame offset
    // alone cannot cover.
    //
    // GD.Randf and deliberately not RngStreams: this is cosmetic jitter, and
    // risk 2 is that cosmetic jitter must never draw from the deterministic run
    // streams. Two identical seeds still produce identical fights.
    private void ScatterIdlePhase()
    {
        if (!_clips.TryGetValue(IdleClip, out var frames)) return;
        _frame = (int)(GD.Randf() * frames.Length) % frames.Length;
        _elapsed = GD.Randf() * (FrameTime.TryGetValue(IdleClip, out double t) ? t : DefaultFrameTime);
        ApplyFrame();
    }

    /// <summary>The idle loop, which is where every one-shot clip lands.</summary>
    public void PlayIdle()
    {
        if (IsTerminal) return;
        Play(IdleClip, EndBehavior.Loop);
    }

    /// <summary>
    /// Plays <paramref name="clip"/> once and returns to idle, the shape every
    /// call site already had (`tween.Chain().TweenCallback(StartIdleBob)`).
    /// An unknown clip falls back to idle rather than freezing the sprite on
    /// whatever frame it was holding.
    /// </summary>
    public void Play(string clip, Action? onComplete = null)
    {
        if (IsTerminal) return;
        if (!_clips.ContainsKey(clip))
        {
            PlayIdle();
            onComplete?.Invoke();
            return;
        }
        Play(clip, EndBehavior.ReturnToIdle, onComplete);
    }

    // A terminal clip is terminal, and that has to be enforced here rather than
    // left to call-site timing.
    //
    // PlayTerminal's whole contract is that a dead enemy does not go back to
    // breathing, but nothing made Hold sticky: any Play("hit") landing inside
    // the 0.35s death fade would overwrite the clip and, one frame time later,
    // drop the corpse onto the idle loop. The old code could not hit this
    // because the death tween drove the *view's* Scale while the recoil drove
    // the sprite's, so they were different channels; frame clips make them one.
    //
    // The obvious route is already closed - ResolveDeathsAndSettle strips the
    // enemy from Enemies before CombatantsChanged fires, so no reaction is
    // popped for it - but PlayHitReaction defers by HitStopSeconds on a big hit,
    // and "no reachable 60ms window" is a property of two call sites rather than
    // an invariant. This makes it one.
    private bool IsTerminal => _onEnd == EndBehavior.Hold;

    /// <summary>
    /// Plays a clip the creature does not come back from - death, escape - and
    /// holds its last frame. Returning to idle here would put a dead enemy back
    /// on its breathing loop underneath the view's fade.
    /// </summary>
    public void PlayTerminal(string clip, Action? onComplete = null)
    {
        if (!_clips.ContainsKey(clip))
        {
            onComplete?.Invoke();
            return;
        }
        Play(clip, EndBehavior.Hold, onComplete);
    }

    // What happens when a clip runs out of frames. An enum rather than a bool
    // because there are genuinely three answers and the third one bit: `idle`
    // written as "does not return to idle" holds its last frame, so the
    // breathing loop froze on the squashed pose after one cycle.
    private enum EndBehavior
    {
        Loop,
        ReturnToIdle,
        Hold,
    }

    // The clips whose frame 0 is a full-silhouette brightness flash, and the
    // one place ReduceMotion touches this class.
    //
    // The gate was on `idle` first and that was backwards twice over. The idle
    // breathe is a 1px squash held half a second - the gentlest motion in the
    // game, and *ungated* before this file existed, since the scale tween it
    // replaced never asked. Freezing it made a player with ReduceMotion on see
    // no sprite animation at all, which is what it reads as: a bug. Meanwhile
    // the hit clip opens by painting the whole creature N8 for one frame, which
    // is the single most photosensitive thing the feature added, and it was the
    // one going ungated.
    //
    // So the flash is *declined* - the clip starts on its recoil frame instead,
    // keeping the hit legible - which is ScreenFade's own rule for this setting:
    // decline the effect rather than soften it.
    //
    // OneShotClip is here for the same reason and it is not a widening of the
    // rule: every combat effect burst opens on a solid disc with an N8 core,
    // which is the same full-brightness frame the hit clip opens on. It is
    // listed by clip name rather than per effect so a fifth burst inherits the
    // gate instead of needing one - the CardUpgrade.ShouldScale failure shape,
    // and this is the side of it that stays silent.
    private static readonly HashSet<string> FlashOpeningClips = new() { "hit", OneShotClip };

    private void Play(string clip, EndBehavior onEnd, Action? onComplete = null)
    {
        _clip = clip;
        _frame = SkipsFlashFrame(clip) ? 1 : 0;
        _elapsed = 0;
        _onEnd = onEnd;
        _held = false;
        _onComplete = onComplete;
        ApplyFrame();
    }

    private bool SkipsFlashFrame(string clip) =>
        FlashOpeningClips.Contains(clip)
        && (SettingsManager.Instance?.ReduceMotion ?? false)
        && _clips.TryGetValue(clip, out var frames) && frames.Length > 1;

    public override void _Process(double delta)
    {
        if (!_clips.TryGetValue(_clip, out var frames)) return;

        // Stop on a flag set *after* the terminal arm has run, not on the frame
        // index. Guarding on `_frame >= frames.Length - 1` returned on exactly
        // the value the increment below had to pass through, so the arm that
        // fires onComplete was unreachable and PlayTerminal's callback silently
        // never ran. Nothing passes one today, which is what made it latent
        // rather than broken - and what would have made it a puzzle for whoever
        // first moved EnemyView's removal onto the clip.
        if (_held) return;

        _elapsed += delta;
        double perFrame = _frameTime ?? (FrameTime.TryGetValue(_clip, out double t) ? t : DefaultFrameTime);
        if (_elapsed < perFrame) return;
        _elapsed -= perFrame;

        _frame++;
        if (_frame < frames.Length)
        {
            ApplyFrame();
            return;
        }

        switch (_onEnd)
        {
            case EndBehavior.Loop:
                _frame = 0;
                ApplyFrame();
                return;

            case EndBehavior.ReturnToIdle:
            {
                var finished = _onComplete;
                _onComplete = null;
                PlayIdle();
                finished?.Invoke();
                return;
            }

            default:
            {
                // Hold: the last frame is already applied, so step back onto it
                // and latch. _held is what stops this being re-decided every
                // tick, and it is set here rather than inferred from _frame so
                // that this arm runs exactly once - which is what makes the
                // callback reachable at all.
                _frame = frames.Length - 1;
                _held = true;
                var finished = _onComplete;
                _onComplete = null;
                finished?.Invoke();
                return;
            }
        }
    }

    private void ApplyFrame()
    {
        if (_clips.TryGetValue(_clip, out var frames) && _frame < frames.Length)
        {
            _target.Texture = frames[_frame];
        }
    }
}
