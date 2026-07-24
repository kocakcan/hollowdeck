using Godot;

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
        var background = new TextureRect
        {
            Texture = tile,
            StretchMode = TextureRect.StretchModeEnum.Tile,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
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
        var vignette = new TextureRect
        {
            Texture = BuildVignette(0.68f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };

        screen.AddChild(background);
        screen.AddChild(fog);
        screen.AddChild(vignette);
        screen.MoveChild(background, 0);
        screen.MoveChild(fog, 1);
        screen.MoveChild(vignette, 2);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Oversize + offset the fog layer relative to full-rect so it has
        // room to drift without exposing an edge, then let it wander slowly.
        fog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fog.OffsetLeft -= 40;
        fog.OffsetTop -= 40;
        fog.OffsetRight += 40;
        fog.OffsetBottom += 40;
        var basePos = fog.Position;
        var tween = fog.GetTree().CreateTween();
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(fog, "position", basePos + new Vector2(24, 14), 14.0);
        tween.TweenProperty(fog, "position", basePos + new Vector2(-18, 10), 16.0);
        tween.TweenProperty(fog, "position", basePos, 12.0);
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
