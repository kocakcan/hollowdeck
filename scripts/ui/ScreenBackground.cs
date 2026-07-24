using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Attaches a tiled dungeon-floor backdrop (DCSS tiles, see CREDITS.md) plus a
// radial vignette behind a screen's existing content. Done in code rather than
// per-.tscn nodes so every screen stays a two-line change and the look is
// adjusted in one place. Call from _Ready; safe no-op if the tile is missing.
public static class ScreenBackground
{
    public static void Attach(Control screen, string tileName, Color tint)
    {
        var tile = ArtAssets.BackgroundTile(tileName);
        if (tile is null) return;

        // ShowBehindParent keeps the screen's own _Draw output (e.g.
        // MapScreen's connecting lines) rendering above the backdrop.
        // TextureFilter=Nearest keeps the pixel-art tile crisp at any tiled
        // scale - the one place in the project this was still missing (the
        // enemy/player sprites already set it); the vignette below stays on
        // the default Linear filter since it's a smooth procedural gradient,
        // not pixel art, and Nearest would band it.
        var background = new TextureRect
        {
            Texture = tile,
            StretchMode = TextureRect.StretchModeEnum.Tile,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Modulate = tint,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        var vignette = new TextureRect
        {
            Texture = BuildVignette(0.5f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };

        // Insert at the top of the tree so everything else draws over them.
        screen.AddChild(background);
        screen.AddChild(vignette);
        screen.MoveChild(background, 0);
        screen.MoveChild(vignette, 1);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    // Combat-only variant: adds a drifting fog layer between the tile and a
    // stronger vignette, for more atmospheric depth than the flat Attach()
    // treatment. Kept separate from Attach() so the other 9 screens using it
    // are entirely unaffected.
    public static void AttachCombat(Control screen, string tileName, Color tint)
    {
        var tile = ArtAssets.BackgroundTile(tileName);
        if (tile is null) return;

        var background = new TextureRect
        {
            Texture = tile,
            StretchMode = TextureRect.StretchModeEnum.Tile,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Modulate = tint,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        var fog = new TextureRect
        {
            Texture = BuildFogNoise(),
            StretchMode = TextureRect.StretchModeEnum.Tile,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
            Modulate = new Color(0.85f, 0.85f, 0.9f, 0.16f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        // Dark gradient band anchored to the bottom third - gives combatants
        // something to visually "stand on" instead of floating on an
        // undifferentiated tiled floor, without needing a real 3D ground
        // plane or matching player/enemy positioning (which use two
        // different layout mechanisms - see Phase 4 plan's scope notes).
        var groundPlane = new TextureRect
        {
            Texture = BuildGroundPlane(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        var vignette = new TextureRect
        {
            Texture = BuildVignette(0.68f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };

        screen.AddChild(background);
        screen.AddChild(fog);
        screen.AddChild(groundPlane);
        screen.AddChild(vignette);
        screen.MoveChild(background, 0);
        screen.MoveChild(fog, 1);
        screen.MoveChild(groundPlane, 2);
        screen.MoveChild(vignette, 3);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        groundPlane.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        AddDustMotes(screen);

        // Oversize + offset the fog layer relative to full-rect so it has
        // room to drift without exposing an edge, then let it wander slowly.
        fog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fog.OffsetLeft -= 40;
        fog.OffsetTop -= 40;
        fog.OffsetRight += 40;
        fog.OffsetBottom += 40;
        var basePos = fog.Position;
        // fog.CreateTween() (not GetTree().CreateTween()) so this infinite,
        // long-lived loop auto-kills when fog leaves the tree - unlike the
        // codebase's other short one-shot tweens, this one is virtually
        // guaranteed to still be running whenever the screen is torn down.
        var tween = fog.CreateTween();
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(fog, "position", basePos + new Vector2(24, 14), 14.0);
        tween.TweenProperty(fog, "position", basePos + new Vector2(-18, 10), 16.0);
        tween.TweenProperty(fog, "position", basePos, 12.0);
    }

    private static Texture2D BuildGroundPlane()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0f, 0.6f, 1f },
            Colors = new Color[] { new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0.45f) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0.5f, 0f),
            FillTo = new Vector2(0.5f, 1f),
            Width = 4,
            Height = 512,
        };
    }

    // Slow-drifting motes for ambient depth - continuous (not one-shot like
    // CombatScreen's hit sparks), low-opacity, and skipped entirely under
    // Settings > Reduce Motion, same gating the hit-spark particle count
    // already respects.
    private static void AddDustMotes(Control screen)
    {
        if (SettingsManager.Instance.ReduceMotion) return;

        var particles = new CpuParticles2D
        {
            Position = new Vector2(576, 300),
            Emitting = true,
            Amount = 22,
            Lifetime = 9.0,
            Texture = BuildDustTexture(),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(560, 300),
            Direction = Vector2.Up,
            Spread = 25f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 10f,
            ScaleAmountMin = 0.4f,
            ScaleAmountMax = 1.1f,
            Color = new Color(1f, 1f, 1f, 0.22f),
            Gravity = Vector2.Zero,
        };
        screen.AddChild(particles);
        screen.MoveChild(particles, 3);
    }

    private static Texture2D BuildDustTexture()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0f, 1f },
            Colors = new Color[] { Colors.White, new Color(1, 1, 1, 0) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 8,
            Height = 8,
        };
    }

    private static Texture2D BuildVignette(float edgeAlpha)
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0.55f, 1f },
            Colors = new Color[] { new(0, 0, 0, 0), new(0, 0, 0, edgeAlpha) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.05f, 0.5f),
            Width = 512,
            Height = 512,
        };
    }

    // Procedural, seamlessly-tiling cloud noise used as a soft fog overlay -
    // avoids sourcing/attributing an external texture for something this
    // simple, and NoiseTexture2D's seamless mode guarantees no tile seams.
    private static Texture2D BuildFogNoise()
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3,
            Frequency = 0.01f,
        };
        return new NoiseTexture2D
        {
            Noise = noise,
            Seamless = true,
            Width = 256,
            Height = 256,
            AsNormalMap = false,
        };
    }
}
