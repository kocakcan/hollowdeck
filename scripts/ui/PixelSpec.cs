using System;
using Godot;

namespace Hollowdeck.UI;

// The pixel-art rule set, in code. Prose version with the reasoning lives in
// docs/ART_SPEC.md; tools/artgen validates authored assets against the same
// numbers. Exists because the project previously carried three competing
// palettes with nothing to stop a fourth - consistency held as taste drifts,
// consistency held as constants can be asserted in a smoke test.
//
// Distinct from UiTheme: UiTheme holds values that vary by rarity/type/state
// at runtime, PixelSpec holds the invariants no asset or view is allowed to
// break. UiTheme.Palette's colors are drawn from Ramp below rather than
// duplicating literals.
public static class PixelSpec
{
    // Authoring grids (docs/ART_SPEC.md section 1). Anything not on one of
    // these is a bad asset, not a special case.
    public const int CreatureGrid = 32;
    public const int TallCreatureHeight = 48; // bosses whose silhouette needs it
    public const int TileGrid = 64;

    // A backdrop *feature* - the gate, the furnace, the throne - is placed once
    // rather than tiled, so it is a different asset class from the floors and
    // walls beside it in assets/backgrounds/ and gets its own grid. 256x128
    // renders to 512x256 at TileScale, a bit under half the canvas.
    public const int FocalWidth = 256;
    public const int FocalHeight = 128;
    public const int IconGrid = 32;
    public const int ChromeSlice = 16;

    // Integer scale factors per context (section 2). A pixel asset at a
    // fractional scale renders uneven pixel widths - the single most obvious
    // way pixel art reads as broken - so these are the only legal values.
    public const int SpriteScale = 5;      // 32 -> 160, what EnemyView already used
    public const int CardArtScale = 3;     // 32 -> 96, CardView's art window
    public const int HudIconScale = 1;     // 32 -> 32
    public const int TileScale = 2;        // 64 -> 128

    // The design em, in pixels, of both faces (section 7). Godot's font_size
    // *is* the em, so a legal size is an exact integer multiple of this -
    // anything else lands the glyph grid on fractional device pixels and the
    // rasterizer has to drop stems, which is what makes a pixel face look
    // mangled rather than merely small.
    //
    // Both faces are 8 on purpose. The body face used to be Jersey 15, whose
    // em is 27px, rendered at 12-24 - i.e. 0.59 device pixels per design
    // pixel, dropping ~40% of every stem. "Deal 6 damage" rendered as "Deal 8
    // damage". Nothing caught it because the "15" in Jersey 15 is the cap
    // height, not the em, and ART_SPEC recorded that 15 as the design size.
    // Run tools/font-grid.py against any candidate face before adopting it.
    public const int FontDesignEm = 8;

    // Legal bitmap font sizes (section 7). Anything else resamples the glyph
    // bitmap and defeats the point of a bitmap face.
    public static readonly int[] FontSizes = { 8, 16, 24, 32 };

    public static bool IsLegalFontSize(int size) => size > 0 && size % FontDesignEm == 0;

    // Rounds an arbitrary desired scale to the nearest legal integer, floored
    // at 1 so a layout can never scale a sprite out of existence. Call this
    // instead of writing a float scale anywhere - the point is that there is
    // one place a fractional scale can be introduced, and it rounds.
    public static int SnapScale(float desired) => Math.Max(1, (int)MathF.Round(desired));

    // Explicit pixel size for a grid at a scale. Views should set
    // CustomMinimumSize from this rather than letting anchors or
    // SizeFlags.Expand decide, which lands on whatever fractional scale the
    // container happens to produce.
    public static Vector2 SizeFor(int grid, int scale) => new(grid * scale, grid * scale);

    public static Vector2 SizeFor(int gridWidth, int gridHeight, int scale) =>
        new(gridWidth * scale, gridHeight * scale);

    // Section 9's other half: a translation must land on a whole source pixel,
    // i.e. a multiple of the asset's render factor. Rounds each axis
    // independently, which is what "whole source pixel" means on a square grid.
    //
    // Callers differ in what they measure the offset *from* - PlayerSprite
    // rounds relative to its rest position, because that position is not itself
    // a multiple of SpriteScale, while a combat effect rounds its absolute
    // placement. That choice stays theirs; the rounding does not, because a
    // second copy of it is a second chance to write `(int)` instead of Round and
    // bias every offset toward zero.
    public static Vector2 SnapTranslation(Vector2 offset, int scale) => new(
        Mathf.Round(offset.X / scale) * scale,
        Mathf.Round(offset.Y / scale) * scale);

    // The one place a Control carrying pixel art may be scaled, and the reason
    // it exists is that no source scan can tell a legal scale from an illegal
    // one. PixelSpecSmokeTest's transform scan reads `x.Scale =` and cannot
    // evaluate the right-hand side, so it has to treat every such write as a
    // violation - which is correct, because `Vector2.One * 2f` and
    // `Vector2.One * 1.15f` are the same line to a regex.
    //
    // Taking an `int` is what closes that: the scale being whole is a fact the
    // type system holds rather than one a reader has to check. Same shape as
    // TextFit re-checking a font rung the font-size scan cannot see.
    //
    // For a card or an icon shown *bigger* on purpose - the library popup and
    // the combat inspect peek, both at 2x. Not for animation: section 9 permits
    // no scale tween at all, whatever the endpoints.
    public static void ApplyIntegerScale(Control node, int scale)
    {
        node.PivotOffset = node.CustomMinimumSize / 2f;
        node.Scale = Vector2.One * scale;
    }

    // Nearest filtering on every pixel asset (section 3). The engine default
    // is Linear, which blurs pixel art at any scale.
    //
    // Deliberately NOT applied to the vignette / ground-plane gradients in
    // ScreenBackground: those are smooth procedural lighting, not pixel art,
    // and Nearest would band them.
    public static void ApplyPixelFilter(CanvasItem item) =>
        item.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

    // Applies filter + an exact integer size in one call, which is the
    // combination every pixel TextureRect needs and the pair most likely to
    // drift apart if set separately.
    public static void ApplyPixelRect(TextureRect rect, int grid, int scale)
    {
        ApplyPixelFilter(rect);
        rect.CustomMinimumSize = SizeFor(grid, scale);
        rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
    }

    // The shared 43-color ramp (section 5). Seven hue families, each
    // hue-shifted rather than a straight lightness ramp - shadows toward
    // blue/violet, highlights toward yellow - which is what stops pixel art
    // from reading flat.
    //
    // Anchored on the pre-existing UiTheme.Palette values (marked) so the
    // game's warm bronze/oxblood identity survives the change of medium.
    public static class Ramp
    {
        // Neutral - stone, bone, parchment
        public static readonly Color N0 = Color.FromHtml("#07060a");
        public static readonly Color N1 = Color.FromHtml("#12100f");
        public static readonly Color N2 = Color.FromHtml("#1e1a18");
        public static readonly Color N3 = Color.FromHtml("#2e2724");
        public static readonly Color N4 = Color.FromHtml("#453b35");
        public static readonly Color N5 = Color.FromHtml("#665950");
        public static readonly Color N6 = Color.FromHtml("#8c7d70"); // was RarityCommon
        public static readonly Color N7 = Color.FromHtml("#c4b5a4");
        public static readonly Color N8 = Color.FromHtml("#ede4d4");

        // Bronze / gold - chrome, rare, currency
        public static readonly Color G0 = Color.FromHtml("#3d2a12");
        public static readonly Color G1 = Color.FromHtml("#6b4a1f");
        public static readonly Color G2 = Color.FromHtml("#9c6f2e");
        public static readonly Color G3 = Color.FromHtml("#c79e47"); // was AccentGold
        public static readonly Color G4 = Color.FromHtml("#ebc766"); // was AccentGoldBright
        public static readonly Color G5 = Color.FromHtml("#fbeaa8");

        // Oxblood / red - attack, damage, danger
        public static readonly Color R0 = Color.FromHtml("#240d10");
        public static readonly Color R1 = Color.FromHtml("#3f1418");
        public static readonly Color R2 = Color.FromHtml("#522121"); // was AttackFill
        public static readonly Color R3 = Color.FromHtml("#8c2b2b");
        public static readonly Color R4 = Color.FromHtml("#c93f3f");
        public static readonly Color R5 = Color.FromHtml("#ff5959"); // was Damage

        // Verdigris / green - skill, heal, upgrade
        public static readonly Color V0 = Color.FromHtml("#0d1f1a");
        public static readonly Color V1 = Color.FromHtml("#1f4238"); // was SkillFill
        public static readonly Color V2 = Color.FromHtml("#2f6b52");
        public static readonly Color V3 = Color.FromHtml("#4a9c68");
        public static readonly Color V4 = Color.FromHtml("#73d973"); // was UpgradeAccent
        public static readonly Color V5 = Color.FromHtml("#a8f0a0");

        // Steel / blue - block, uncommon
        public static readonly Color B0 = Color.FromHtml("#0f1626");
        public static readonly Color B1 = Color.FromHtml("#1e2c47");
        public static readonly Color B2 = Color.FromHtml("#35496e");
        public static readonly Color B3 = Color.FromHtml("#598cd9"); // was RarityUncommon
        public static readonly Color B4 = Color.FromHtml("#8cbfff"); // was Block
        public static readonly Color B5 = Color.FromHtml("#c4dfff");

        // Ember / orange - exhaust, act II
        public static readonly Color E0 = Color.FromHtml("#3d1a08");
        public static readonly Color E1 = Color.FromHtml("#6b2f0f");
        public static readonly Color E2 = Color.FromHtml("#a85420");
        public static readonly Color E3 = Color.FromHtml("#f2a64d"); // was ExhaustAccent
        public static readonly Color E4 = Color.FromHtml("#ffd9a0");

        // Void / violet - act III, arcane, the Power card type when it lands
        public static readonly Color P0 = Color.FromHtml("#180d24");
        public static readonly Color P1 = Color.FromHtml("#2e1a42");
        public static readonly Color P2 = Color.FromHtml("#4d2e6b");
        public static readonly Color P3 = Color.FromHtml("#7a52a3");
        public static readonly Color P4 = Color.FromHtml("#b08cd9");

        // Flat list for validation and clamping. Order is irrelevant to
        // Nearest() but kept family-grouped so a human reading a failure
        // message can tell which ramp a color landed in.
        public static readonly Color[] All =
        {
            N0, N1, N2, N3, N4, N5, N6, N7, N8,
            G0, G1, G2, G3, G4, G5,
            R0, R1, R2, R3, R4, R5,
            V0, V1, V2, V3, V4, V5,
            B0, B1, B2, B3, B4, B5,
            E0, E1, E2, E3, E4,
            P0, P1, P2, P3, P4,
        };
    }

    // Nearest ramp color to an arbitrary color, by squared RGB distance.
    // Alpha is carried through untouched: a sprite's transparency is not a
    // palette decision, and clamping it would eat soft mask edges.
    //
    // Used at runtime for tinting that must stay on-palette, and mirrored by
    // tools/artgen for the offline asset clamp - the two must agree, so this
    // stays plain squared-RGB rather than anything perceptual that would be
    // fiddly to reimplement identically in Rust.
    public static Color Clamp(Color color)
    {
        var best = Ramp.All[0];
        float bestDistance = float.MaxValue;
        foreach (var candidate in Ramp.All)
        {
            float dr = candidate.R - color.R;
            float dg = candidate.G - color.G;
            float db = candidate.B - color.B;
            float distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = candidate;
        }
        return new Color(best.R, best.G, best.B, color.A);
    }

    // True if a color already sits on the ramp. The tolerance absorbs the
    // float round-trip through Color.FromHtml's 8-bit conversion; it is not
    // meant to admit near-misses, so it stays well under one 8-bit step
    // (1/255 = 0.0039).
    public static bool IsOnRamp(Color color)
    {
        foreach (var candidate in Ramp.All)
        {
            if (Mathf.Abs(candidate.R - color.R) < 0.002f &&
                Mathf.Abs(candidate.G - color.G) < 0.002f &&
                Mathf.Abs(candidate.B - color.B) < 0.002f)
            {
                return true;
            }
        }
        return false;
    }

    // Legal grid dimensions (section 1), for the validator and the smoke test.
    public static bool IsLegalGrid(int width, int height) =>
        (width == CreatureGrid && height == CreatureGrid) ||
        (width == CreatureGrid && height == TallCreatureHeight) ||
        (width == TileGrid && height == TileGrid) ||
        (width == FocalWidth && height == FocalHeight) ||
        (width == ChromeSlice && height == ChromeSlice) ||
        (width == ChromeSlice + 8 && height == ChromeSlice + 8); // 24x24 chrome
}
