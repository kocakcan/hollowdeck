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
            Texture = BuildVignette(),
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

    private static Texture2D BuildVignette()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0.55f, 1f },
            Colors = new Color[] { new(0, 0, 0, 0), new(0, 0, 0, 0.5f) },
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
}
