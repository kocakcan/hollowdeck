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

    private TextureRect _target = null!;
    private readonly Dictionary<string, Texture2D[]> _clips = new();

    private string _clip = IdleClip;
    private int _frame;
    private double _elapsed;
    private EndBehavior _onEnd = EndBehavior.Loop;
    private Action? _onComplete;

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
    public void PlayIdle() => Play(IdleClip, EndBehavior.Loop);

    /// <summary>
    /// Plays <paramref name="clip"/> once and returns to idle, the shape every
    /// call site already had (`tween.Chain().TweenCallback(StartIdleBob)`).
    /// An unknown clip falls back to idle rather than freezing the sprite on
    /// whatever frame it was holding.
    /// </summary>
    public void Play(string clip, Action? onComplete = null)
    {
        if (!_clips.ContainsKey(clip))
        {
            PlayIdle();
            onComplete?.Invoke();
            return;
        }
        Play(clip, EndBehavior.ReturnToIdle, onComplete);
    }

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

    private void Play(string clip, EndBehavior onEnd, Action? onComplete = null)
    {
        _clip = clip;
        _frame = 0;
        _elapsed = 0;
        _onEnd = onEnd;
        _onComplete = onComplete;
        ApplyFrame();
    }

    public override void _Process(double delta)
    {
        if (!_clips.TryGetValue(_clip, out var frames)) return;
        if (_onEnd == EndBehavior.Hold && _frame >= frames.Length - 1) return;

        // ReduceMotion holds the idle loop on its rest frame but leaves every
        // other clip alone. Idle is decoration; hit, windup, death and escape
        // are feedback about what just happened in the fight, and ScreenFade's
        // rule is that ReduceMotion declines decorative motion rather than
        // shortening the informative kind.
        if (_clip == IdleClip && (SettingsManager.Instance?.ReduceMotion ?? false)) return;

        _elapsed += delta;
        double perFrame = FrameTime.TryGetValue(_clip, out double t) ? t : DefaultFrameTime;
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
                // The last frame is already applied; the guard at the top of
                // _Process is what stops this being re-decided every tick.
                var finished = _onComplete;
                _onComplete = null;
                _frame = frames.Length - 1;
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
