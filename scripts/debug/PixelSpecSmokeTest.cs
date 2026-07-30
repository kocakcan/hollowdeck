using System.IO;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Enforces docs/ART_SPEC.md so the pixel-art rules cannot quietly drift back.
// This exists because the project previously accumulated three competing
// palettes with nothing asserting against a fourth - the whole argument for
// committing to pixel art was that it turns consistency from taste into a
// spec, and a spec nothing checks is still taste.
//
// Covers the parts that are checkable from inside the engine:
//   - every authored asset sits on a legal grid (section 1)
//   - creature sprites render at an integer scale (section 2)
//   - the project default texture filter is Nearest (section 3)
//   - no SVG survives under assets/ (section 8)
//   - the fonts in use are the bitmap pair, not the retired serif pair
//   - every content definition resolves to an icon file
//   - tools/artgen's ramp still matches PixelSpec.Ramp
//
// Palette conformance itself (section 5) is checked by `artgen validate`,
// which tools/run-smoke-tests.sh runs before these suites - it reads the raw
// PNG bytes, whereas GD.Load hands back an imported texture. What is asserted
// here instead is that the two implementations share one ramp, since a
// validator clamping to a different palette than the game draws with would
// pass assets that look wrong.
public partial class PixelSpecSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        TestSpritesAreOnLegalGrid();
        TestBackgroundTilesAreOnLegalGrid();
        TestIconsAreOnLegalGrid();
        TestCreatureSpritesRenderAtIntegerScale();
        TestDefaultTextureFilterIsNearest();
        TestNoSvgRemainsUnderAssets();
        TestFontsAreTheBitmapPair();
        TestRampIsSelfConsistent();
        TestEveryDefinitionHasAnIcon();
        TestArtgenRampMatchesPixelSpec();

        GD.Print($"PixelSpecSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
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

    private void TestSpritesAreOnLegalGrid()
    {
        foreach (var path in PngsUnder("res://assets/sprites"))
        {
            var texture = GD.Load<Texture2D>(path);
            int w = texture.GetWidth(), h = texture.GetHeight();
            Check($"sprite_grid_{path.GetFile()}", PixelSpec.IsLegalGrid(w, h),
                $"{path} is {w}x{h}, not a legal grid (ART_SPEC section 1)");
        }
    }

    private void TestBackgroundTilesAreOnLegalGrid()
    {
        foreach (var path in PngsUnder("res://assets/backgrounds"))
        {
            var texture = GD.Load<Texture2D>(path);
            int w = texture.GetWidth(), h = texture.GetHeight();
            Check($"tile_grid_{path.GetFile()}",
                w == PixelSpec.TileGrid && h == PixelSpec.TileGrid,
                $"{path} is {w}x{h}, expected {PixelSpec.TileGrid}x{PixelSpec.TileGrid}");
        }
    }

    // Icons are the one category the game draws at two different scales (1x in
    // the HUD, 3x in a card's art window), so an off-grid icon is not just
    // wrong once - it is resampled in the card frame as well.
    private void TestIconsAreOnLegalGrid()
    {
        foreach (var path in PngsUnder("res://assets/icons"))
        {
            var texture = GD.Load<Texture2D>(path);
            int w = texture.GetWidth(), h = texture.GetHeight();
            Check($"icon_grid_{path.GetFile()}",
                w == PixelSpec.IconGrid && h == PixelSpec.IconGrid,
                $"{path} is {w}x{h}, expected {PixelSpec.IconGrid}x{PixelSpec.IconGrid}");
        }
    }

    // The bug this catches for real: the player sprite was rendered into a
    // 180x180 box from a 32x32 source (5.625x), so it was resampled and read
    // as subtly softer than the enemies it was standing next to.
    private void TestCreatureSpritesRenderAtIntegerScale()
    {
        AssertNodeScaleIsIntegral("res://scenes/CombatScreen.tscn", "PlayerSprite");
        AssertNodeScaleIsIntegral("res://scenes/EnemyView.tscn", "VBox/Sprite");
    }

    private void AssertNodeScaleIsIntegral(string scenePath, string nodePath)
    {
        var scene = GD.Load<PackedScene>(scenePath);
        var root = scene.Instantiate();
        var rect = root.GetNodeOrNull<TextureRect>(nodePath);
        if (rect is null)
        {
            Check($"integer_scale_{nodePath}", false, $"{nodePath} not found in {scenePath}");
            root.QueueFree();
            return;
        }

        // Height is the governing axis: both sprite rects use
        // StretchMode.KeepAspectCentered, so a 32x32 source fills the box
        // vertically and the width follows.
        float boxHeight = rect.CustomMinimumSize.Y > 0 ? rect.CustomMinimumSize.Y : rect.Size.Y;
        float scale = boxHeight / PixelSpec.CreatureGrid;
        Check($"integer_scale_{nodePath}",
            Mathf.Abs(scale - Mathf.Round(scale)) < 0.001f && scale >= 1f,
            $"{scenePath}:{nodePath} renders {PixelSpec.CreatureGrid}px source into " +
            $"{boxHeight}px = {scale}x, which is not an integer scale (ART_SPEC section 2)");

        root.QueueFree();
    }

    private void TestDefaultTextureFilterIsNearest()
    {
        // 0 == Nearest. Without this, every icon built in code (card art,
        // relics, potions, statuses, intents, map nodes) inherits Linear and
        // is bilinearly blurred - the .tscn sprites set it individually and
        // so hid the problem.
        var setting = ProjectSettings.GetSetting("rendering/textures/canvas_textures/default_texture_filter");
        Check("default_texture_filter_is_nearest", setting.AsInt32() == 0,
            $"expected 0 (Nearest), got {setting} (ART_SPEC section 3)");
    }

    // ART_SPEC section 8 wants zero SVGs under assets/. This was a ratchet
    // ("may not grow past 78") for as long as the vector icons were still
    // there; Phase 3 converted all 78, so it is now a plain equality check -
    // any SVG appearing under assets/ reopens the mixed-media problem the
    // pixel-art commitment closed.
    private void TestNoSvgRemainsUnderAssets()
    {
        var found = FilesUnder("res://assets", ".svg").ToList();
        Check("no_svg_under_assets", found.Count == 0,
            $"{found.Count} SVG(s) under assets/: {string.Join(", ", found.Take(5))} - " +
            "icons are generated by tools/artgen as 32x32 PNGs (ART_SPEC section 8)");
    }

    // The convention in ArtAssets.cs is that art resolves by definition id, so
    // a renamed or newly-added card silently loses its icon rather than
    // failing - it just draws text-only. This closes that: every id in the
    // content JSON must have a file, and (the other direction) every file must
    // belong to a real id, which catches an artgen entry whose name no longer
    // matches anything.
    private void TestEveryDefinitionHasAnIcon()
    {
        CardDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        AssertIconsMatch("cards", CardDatabase.All.Select(c => c.Id));
        AssertIconsMatch("relics", RelicDatabase.All.Select(r => r.Id));
        AssertIconsMatch("potions", PotionDatabase.All.Select(p => p.Id));
    }

    private void AssertIconsMatch(string category, System.Collections.Generic.IEnumerable<string> ids)
    {
        var expected = ids.Select(id => id.TrimEnd('+')).Distinct().ToHashSet();
        var present = FilesUnder($"res://assets/icons/{category}", ".png")
            .Select(path => path.GetFile().GetBaseName())
            .ToHashSet();

        var missing = expected.Except(present).OrderBy(x => x).ToList();
        Check($"{category}_icons_cover_every_definition", missing.Count == 0,
            $"no icon for: {string.Join(", ", missing)} - add it to tools/artgen/src/icons/");

        var orphaned = present.Except(expected).OrderBy(x => x).ToList();
        Check($"{category}_icons_have_no_orphans", orphaned.Count == 0,
            $"icon(s) with no matching definition: {string.Join(", ", orphaned)} - " +
            "an id was renamed in the JSON but not in artgen, or the reverse");
    }

    // tools/artgen/src/palette.rs is a hand-maintained mirror of Ramp.All,
    // because the validator has to clamp offline to exactly what the game
    // clamps to at runtime. Nothing stops the two drifting except this - so
    // parse the Rust source and compare.
    private void TestArtgenRampMatchesPixelSpec()
    {
        const string source = "res://tools/artgen/src/palette.rs";
        if (!Godot.FileAccess.FileExists(source))
        {
            Check("artgen_ramp_matches_pixelspec", false, $"missing {source}");
            return;
        }

        var text = Godot.FileAccess.GetFileAsString(source);
        // Anchored to line start: palette.rs's own module doc quotes the
        // declaration shape as an example, and an unanchored pattern happily
        // matched the comment as a 44th entry.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text,
            @"^pub const [A-Z]\d: Rgb = Rgb\(0x([0-9a-f]{2}), 0x([0-9a-f]{2}), 0x([0-9a-f]{2})\);",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        var rust = matches.Select(m =>
            $"#{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}").ToList();
        var sharp = PixelSpec.Ramp.All.Select(c => "#" + c.ToHtml(false)).ToList();

        Check("artgen_ramp_matches_pixelspec",
            rust.Count == sharp.Count && !rust.Where((c, i) => c != sharp[i]).Any(),
            $"artgen has {rust.Count} entries, PixelSpec has {sharp.Count}; " +
            $"first divergence at index {rust.Zip(sharp).ToList().FindIndex(p => p.First != p.Second)}");
    }

    private void TestFontsAreTheBitmapPair()
    {
        Check("display_font_exists", ResourceLoader.Exists(UiTheme.Fonts.DisplayPath),
            $"missing {UiTheme.Fonts.DisplayPath}");
        Check("body_font_exists", ResourceLoader.Exists(UiTheme.Fonts.BodyPath),
            $"missing {UiTheme.Fonts.BodyPath}");
        Check("retired_serif_fonts_removed",
            !ResourceLoader.Exists("res://assets/fonts/Cinzel-Regular.ttf") &&
            !ResourceLoader.Exists("res://assets/fonts/IMFellEnglish-Regular.ttf"),
            "a retired serif face is still present in assets/fonts/");
    }

    // Guards the ramp itself: Clamp must be idempotent on ramp colours, and
    // every entry must be distinct, or palette-clamping silently collapses
    // two semantics onto one colour.
    private void TestRampIsSelfConsistent()
    {
        foreach (var colour in PixelSpec.Ramp.All)
        {
            if (!PixelSpec.IsOnRamp(colour))
            {
                Check("ramp_entry_on_ramp", false, $"{colour.ToHtml()} fails its own IsOnRamp check");
                return;
            }
        }
        Check("ramp_entry_on_ramp", true, "");

        int distinct = PixelSpec.Ramp.All.Select(c => c.ToHtml(false)).Distinct().Count();
        Check("ramp_entries_distinct", distinct == PixelSpec.Ramp.All.Length,
            $"{PixelSpec.Ramp.All.Length} ramp entries collapse to {distinct} distinct colours");
    }

    private static System.Collections.Generic.IEnumerable<string> PngsUnder(string root) =>
        FilesUnder(root, ".png");

    // Walks res:// via DirAccess rather than System.IO so it resolves the same
    // way inside a packaged export, where assets live in the .pck.
    private static System.Collections.Generic.IEnumerable<string> FilesUnder(string root, string extension)
    {
        using var dir = DirAccess.Open(root);
        if (dir is null) yield break;

        dir.ListDirBegin();
        for (var name = dir.GetNext(); name != ""; name = dir.GetNext())
        {
            if (name is "." or "..") continue;
            string full = root.PathJoin(name);
            if (dir.CurrentIsDir())
            {
                foreach (var nested in FilesUnder(full, extension)) yield return nested;
            }
            else if (name.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase))
            {
                yield return full;
            }
        }
        dir.ListDirEnd();
    }
}
