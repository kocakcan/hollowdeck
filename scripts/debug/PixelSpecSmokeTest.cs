using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;
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
//   - the canvas letterboxes rather than expanding (section 4)
//   - no SVG survives under assets/ (section 8)
//   - the fonts in use are the bitmap pair, not the retired serif pair
//   - every rendered font size is a multiple of the faces' 8px design em
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
        TestChromeSlicesAreOnLegalGrid();
        TestEveryChromeSliceIsDrawnBySomething();
        TestChromeCornersFitTheSlice();
        TestChromeEdgesTileRatherThanStretch();
        TestCreatureSpritesRenderAtIntegerScale();
        TestDefaultTextureFilterIsNearest();
        TestCanvasIsLetterboxedNotExpanded();
        TestNoSvgRemainsUnderAssets();
        TestFontsAreTheBitmapPair();
        TestEveryRenderedFontSizeIsOnTheGrid();
        TestRampIsSelfConsistent();
        TestEveryDefinitionHasAnIcon();
        TestArtgenRampMatchesPixelSpec();
        TestEveryCreatureHasItsFrames();
        TestEveryCombatEffectHasItsFrames();
        TestNoTweenTransformsAPixelSprite();
        TestTheTransformScanSeesEveryPixelSurface();
        TestEveryTweenUsesTheMotionVocabulary();
        TestEveryMotionCurveIsUsed();
        TestTheMotionRegistryIsComplete();
        TestTheIdleClipActuallyAdvances();
        TestACombatEffectAdvancesAndFrees();
        TestTheGlowRingActuallyAdvances();
        TestEveryGlowFrameIsOnTheRamp();
        TestTheGlowRingOpensOnWhatItReplaced();
        TestOnlyARareCardCarriesAGlowRing();
        TestNoScreenAuthorsItsOwnBackdrop();
        TestTheBackdropIsBuiltToSpec();
        TestDustMotesAreOnTheRampAtAnIntegerScale();

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
            // IsLegalGrid rather than a hardcoded 64x64, because this
            // directory holds two asset classes now: the tiles that repeat and
            // the focal feature that is placed once, which is 256x128.
            Check($"tile_grid_{path.GetFile()}",
                PixelSpec.IsLegalGrid(w, h) &&
                (w == PixelSpec.TileGrid || w == PixelSpec.FocalWidth),
                $"{path} is {w}x{h}, which is neither a background tile nor a focal feature");
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

    // assets/theme was unasserted on this side entirely until the chrome
    // slices landed - the three sweeps above walk sprites, backgrounds and
    // icons, and a 9-slice is on neither of those grids. `artgen validate`
    // covers the same ground from the raw bytes; this is the engine half, and
    // it is the one that fails if a slice is on disk but never imported.
    private void TestChromeSlicesAreOnLegalGrid()
    {
        foreach (var path in PngsUnder("res://assets/theme"))
        {
            var texture = GD.Load<Texture2D>(path);
            int w = texture.GetWidth(), h = texture.GetHeight();
            bool legal = (w == PixelSpec.ChromeSlice && h == PixelSpec.ChromeSlice)
                || (w == PixelSpec.ChromeSlice + 8 && h == PixelSpec.ChromeSlice + 8);
            Check($"chrome_grid_{path.GetFile()}", legal,
                $"{path} is {w}x{h}, expected 16x16 or 24x24 (ART_SPEC section 1)");
        }
    }

    // Every chrome StyleBoxTexture the game actually builds, from *both*
    // consumers: the ChromeStyles producers and the theme resource. Collected
    // by driving the real functions rather than by listing textures, for the
    // reason TestNoTweenTransformsAPixelSprite had to be rewritten - a check
    // that knows only the names someone already thought of is green over the
    // ones they did not. The emphasis states in particular are reachable only
    // by applying the style to a real Button and reading the overrides back.
    private List<(string Label, StyleBoxTexture Style)> CollectChromeStyles()
    {
        var found = new List<(string, StyleBoxTexture)>();

        void Add(string label, StyleBox? style)
        {
            if (style is StyleBoxTexture textured) found.Add((label, textured));
        }

        Add("ChromeStyles.PanelStyle", ChromeStyles.PanelStyle(focused: false));
        Add("ChromeStyles.PanelStyle(focused)", ChromeStyles.PanelStyle(focused: true));
        Add("ChromeStyles.SlotStyle(filled)", ChromeStyles.SlotStyle(filled: true));
        Add("ChromeStyles.SlotStyle(empty)", ChromeStyles.SlotStyle(filled: false));
        Add("ChromeStyles.PlinthStyle", ChromeStyles.PlinthStyle());

        var button = new Button();
        ChromeStyles.ApplyEmphasisButtonStyle(button);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            Add($"ApplyEmphasisButtonStyle/{state}", button.GetThemeStylebox(state));
        button.QueueFree();

        var theme = GD.Load<Theme>("res://assets/theme/hollowdeck_theme.tres");
        if (theme is not null)
        {
            foreach (var type in theme.GetStyleboxTypeList())
                foreach (var name in theme.GetStyleboxList(type))
                    Add($"theme {type}/{name}", theme.GetStylebox(name, type));
        }

        return found;
    }

    // The data/code seam this codebase actually produces, one content type
    // over: a slice renamed in artgen and not in ChromeStyles (or the reverse)
    // makes ArtAssets.ChromeSlice return null, and a StyleBoxTexture with no
    // texture draws *nothing*. So the panel does not degrade to an unstyled
    // panel - it leaves the interface, silently, with nothing thrown.
    //
    // Three sets have to agree, and each pair catches a different mistake:
    // the PNGs on disk, the names in ChromeStyles.Slices, and the textures
    // something actually draws. A slice generated and never wired up is as
    // much a bug as a name with no file behind it.
    //
    // What this deliberately does NOT catch, so the next reader does not
    // assume it does: the comparison is by *membership*, so two producers
    // swapping slices with each other - PanelStyle drawing the plinth and
    // PlinthStyle drawing the panel - leaves all three sets identical and
    // passes. Pinning which producer draws which name would close it, and it
    // would be a mirror of the code in a second file with nothing asserting
    // the mirror, which is the shape this codebase has shipped three times.
    // The trade is deliberate and rests on the failure modes being different
    // kinds: a missing slice is *invisible* (a StyleBoxTexture with no texture
    // draws nothing, which is why this check exists at all), while a swapped
    // one is a panel wearing the plinth's frame - wrong in a way the first
    // screenshot shows.
    private void TestEveryChromeSliceIsDrawnBySomething()
    {
        var declared = ChromeStyles.Slices.All.ToHashSet();

        var onDisk = FilesUnder("res://assets/theme", ".png")
            .Select(path => path.GetFile().GetBaseName())
            .ToHashSet();

        var drawn = CollectChromeStyles()
            .Where(entry => entry.Style.Texture is not null)
            .Select(entry => entry.Style.Texture.ResourcePath.GetFile().GetBaseName())
            .ToHashSet();

        var missingArt = declared.Except(onDisk).OrderBy(x => x).ToList();
        Check("chrome_slices_have_art", missingArt.Count == 0,
            $"ChromeStyles.Slices names with no PNG: {string.Join(", ", missingArt)} - " +
            "run `artgen generate chrome` and re-import");

        var orphanArt = onDisk.Except(declared).OrderBy(x => x).ToList();
        Check("chrome_slices_have_no_orphans", orphanArt.Count == 0,
            $"PNG(s) under assets/theme with no name in ChromeStyles.Slices: " +
            $"{string.Join(", ", orphanArt)} - a slice was generated and never wired up");

        var undrawn = declared.Except(drawn).OrderBy(x => x).ToList();
        Check("chrome_slices_are_drawn", undrawn.Count == 0,
            $"slice(s) nothing draws: {string.Join(", ", undrawn)} - " +
            "declared in ChromeStyles.Slices but reached by no producer and no theme entry");
    }

    // ART_SPEC section 1: "corners must be <= 1/3 of the slice". Stated since
    // the spec was written and checked by nothing, because it is a property of
    // the StyleBox rather than of the PNG - `artgen validate` reads pixels and
    // cannot see a texture margin.
    //
    // Over-large corners are not a cosmetic problem: Godot draws the corner
    // patches at 1:1 and gives whatever is left to the tiled edges, so a margin
    // past a third leaves a box that is nearly all corner, and one past half
    // makes the corners overlap each other.
    private void TestChromeCornersFitTheSlice()
    {
        foreach (var (label, style) in CollectChromeStyles())
        {
            if (style.Texture is null) continue;

            float budget = Mathf.Min(style.Texture.GetWidth(), style.Texture.GetHeight()) / 3f;
            var margins = new[]
            {
                ("left", style.TextureMarginLeft), ("top", style.TextureMarginTop),
                ("right", style.TextureMarginRight), ("bottom", style.TextureMarginBottom),
            };

            foreach (var (side, margin) in margins)
            {
                Check($"chrome_corner_{label}_{side}", margin <= budget,
                    $"{label} has a {margin}px {side} texture margin on a " +
                    $"{style.Texture.GetWidth()}x{style.Texture.GetHeight()} slice; " +
                    $"ART_SPEC section 1 allows {budget}");
            }
        }
    }

    // The rule that is wrong by default, which is what makes it worth an
    // assertion rather than a comment: AxisStretchMode.Stretch is what a
    // StyleBoxTexture is born with, and it resamples the edge strip to whatever
    // fractional width the box happens to be. That is a non-integer scale -
    // section 2's "a bug, not a judgement call" - reached by doing nothing at
    // all. TileFit is the same violation from the other side, since it rescales
    // the tiles to fit a whole number of them.
    private void TestChromeEdgesTileRatherThanStretch()
    {
        foreach (var (label, style) in CollectChromeStyles())
        {
            Check($"chrome_tiles_{label}",
                style.AxisStretchHorizontal == StyleBoxTexture.AxisStretchMode.Tile &&
                style.AxisStretchVertical == StyleBoxTexture.AxisStretchMode.Tile,
                $"{label} stretches its edges ({style.AxisStretchHorizontal} horizontal, " +
                $"{style.AxisStretchVertical} vertical) - only Tile keeps every edge pixel " +
                "at 1:1 (ART_SPEC section 2)");
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

    // ART_SPEC section 4: the UI layer works in 1152x648 space. "keep" is what
    // makes that a fact rather than a hope - it letterboxes, so the canvas is
    // exactly 1152x648 at every window size.
    //
    // "expand" was the setting here for a long time and looks like the more
    // generous choice, which is exactly why this needs an assertion rather than
    // a comment. It grows the canvas along the window's long axis (a 1470x956
    // window gives 1152x749), and every screen in this codebase positions
    // against the design size: MapScreen's DesignHeight, CombatScreen.tscn's
    // HandArea/EnemyRow offsets, ScreenChrome's DesignWidth, PileViewPopup's
    // box. So the extra 101px was dead space under the map that nothing laid
    // out into, and it was invisible on a 16:9 display - the shape of bug that
    // ships. Making these screens genuinely responsive is the alternative and a
    // much larger change; if that is ever done, this check is the thing to
    // delete, deliberately.
    private void TestCanvasIsLetterboxedNotExpanded()
    {
        var mode = ProjectSettings.GetSetting("display/window/stretch/mode").AsString();
        Check("stretch_mode_is_canvas_items", mode == "canvas_items",
            $"expected canvas_items, got '{mode}' (ART_SPEC section 4)");

        var aspect = ProjectSettings.GetSetting("display/window/stretch/aspect").AsString();
        Check("stretch_aspect_is_keep", aspect == "keep",
            $"expected keep, got '{aspect}' - a non-16:9 window would grow the canvas past " +
            "1152x648 and every fixed-offset layout would be wrong (ART_SPEC section 4)");
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
        EventDatabase.LoadAll();

        AssertIconsMatch("cards", CardDatabase.All.Select(c => c.Id));
        AssertIconsMatch("relics", RelicDatabase.All.Select(r => r.Id));
        AssertIconsMatch("potions", PotionDatabase.All.Select(p => p.Id));
        // Events are the one category where a missing icon is survivable -
        // ArtAssets.EventIcon falls back to the map's scroll - so this is
        // guarding the *orphan* direction more than the missing one: a
        // renamed event id would silently drop back to the generic art
        // forever, with a stale file sitting next to it.
        AssertIconsMatch("events", EventDatabase.All.Select(e => e.Id));

        // Statuses are an enum rather than a JSON pool, but the failure is the
        // same and quieter: StatusRow falls back to a bare "Metallicize 3"
        // Label when ArtAssets.StatusIcon returns null, so a new StatusType
        // ships looking broken rather than not at all. ArtAssets lowercases
        // the enum name, so that is the filename it must match.
        AssertIconsMatch("status",
            System.Enum.GetValues<Combat.StatusType>().Select(s => s.ToString().ToLowerInvariant()));

        // Intents and map nodes are the two categories that were missing here
        // entirely, and they are the two that cannot be checked by filename:
        // both go through a switch in ArtAssets that maps an enum member to a
        // name it does not equal (MapNodeType.Combat -> "fight"), so an
        // enumerate-and-lowercase sweep like the status one above would just
        // be wrong. Drive the real lookup instead - that covers the arm and
        // the file in one assertion, which is what a missing arm needs: the
        // `_ =>` default swallows it, Load returns null, EnemyView hides the
        // TextureRect, and the telegraph renders as a bare label with every
        // suite green. This branch added two IntentTypes by hand and got away
        // with it; the next two roadmap items (wake_on_damage, and Phase 9's
        // unknown map node) both walk straight into it.
        AssertEveryEnumMemberResolvesArt("intents",
            System.Enum.GetValues<IntentType>(), ArtAssets.IntentIcon);
        AssertEveryEnumMemberResolvesArt("map",
            System.Enum.GetValues<Map.MapNodeType>(), ArtAssets.MapIcon);

        // Sprites were the hole in all of the above: every category here is
        // under assets/icons, so an enemy shipped with no PNG passed the whole
        // suite and rendered as an empty TextureRect mid-fight. Unlike icons
        // these are sourced rather than generated, so the fix it points at is
        // the CREDITS pipeline rather than artgen.
        EnemyDatabase.LoadAll();
        AssertArtCovers("enemy_sprites", "res://assets/sprites/enemies",
            EnemyDatabase.All.Select(e => e.Id),
            "source a 32x32 CC0 tile, clamp it with tools/artgen, and record it in CREDITS.md");
    }

    // Both directions like AssertArtCovers, but driven through the ArtAssets
    // lookup rather than through a list of ids, because these two categories
    // do not name their files after their enum members.
    private void AssertEveryEnumMemberResolvesArt<T>(string category, T[] members,
        System.Func<T, Texture2D?> lookup) where T : System.Enum
    {
        string directory = $"res://assets/icons/{category}";
        string fallback = $"{directory}/unknown.png";

        var reached = new HashSet<string>();
        var unresolved = new List<string>();
        foreach (var member in members)
        {
            var texture = lookup(member);
            // Landing on unknown.png counts as unresolved, not as covered.
            // The fallback is there so a missed arm still draws *something*
            // in a build nobody ran this suite against - it is not a licence
            // to skip the arm. Without this comparison the fallback would
            // hide the exact bug the check was added for, which is a worse
            // state than having no check at all.
            if (texture is null || texture.ResourcePath == fallback)
                unresolved.Add(member.ToString());
            else
                reached.Add(texture.ResourcePath.GetFile().GetBaseName());
        }

        Check($"{category}_icons_cover_every_definition", unresolved.Count == 0,
            $"ArtAssets resolves no icon of its own for: {string.Join(", ", unresolved)} - " +
            "add the switch arm in ArtAssets.cs and the entry in tools/artgen/src/icons/");

        var orphaned = FilesUnder(directory, ".png")
            .Select(path => path.GetFile().GetBaseName())
            .Where(name => name != "unknown" && !reached.Contains(name))
            .OrderBy(x => x)
            .ToList();
        Check($"{category}_icons_have_no_orphans", orphaned.Count == 0,
            $"file(s) no enum member resolves to: {string.Join(", ", orphaned)} - " +
            "a member was renamed or removed but its icon was not");
    }

    private void AssertIconsMatch(string category, System.Collections.Generic.IEnumerable<string> ids) =>
        AssertArtCovers($"{category}_icons", $"res://assets/icons/{category}", ids,
            "add it to tools/artgen/src/icons/");

    private void AssertArtCovers(string label, string directory,
        System.Collections.Generic.IEnumerable<string> ids, string fixHint)
    {
        var expected = ids.Select(id => id.TrimEnd('+')).Distinct().ToHashSet();
        var present = FilesUnder(directory, ".png")
            .Select(path => path.GetFile().GetBaseName())
            .ToHashSet();

        var missing = expected.Except(present).OrderBy(x => x).ToList();
        Check($"{label}_cover_every_definition", missing.Count == 0,
            $"no art for: {string.Join(", ", missing)} - {fixHint}");

        var orphaned = present.Except(expected).OrderBy(x => x).ToList();
        Check($"{label}_have_no_orphans", orphaned.Count == 0,
            $"file(s) with no matching definition: {string.Join(", ", orphaned)} - " +
            "an id was renamed in the JSON but not in the art, or the reverse");
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
        Check("retired_offgrid_body_font_removed",
            !ResourceLoader.Exists("res://assets/fonts/Jersey15-Regular.ttf"),
            "Jersey 15 is back; its 27px design em cannot render crisply at any UI size");

        foreach (int size in new[] { UiTheme.Fonts.Small, UiTheme.Fonts.Body, UiTheme.Fonts.Heading, UiTheme.Fonts.Title })
        {
            Check($"ui_theme_size_{size}_is_on_the_font_grid", PixelSpec.IsLegalFontSize(size),
                $"{size} is not a multiple of the {PixelSpec.FontDesignEm}px design em");
        }
    }

    // The check that would have caught Jersey 15. Godot's font_size is the em
    // in pixels, so a size that isn't a multiple of the face's design em puts
    // the glyph grid on fractional device pixels and the rasterizer drops
    // stems - which is how the body face spent three phases rendering "Deal 6
    // damage" as "Deal 8 damage" with every smoke test green.
    //
    // Scans the theme and every scene/script for a rendered size rather than
    // trusting a constants list, because the sizes that drifted off-grid were
    // all local AddThemeFontSizeOverride calls and .tscn overrides, none of
    // which went anywhere near PixelSpec.
    private void TestEveryRenderedFontSizeIsOnTheGrid()
    {
        var theme = GD.Load<Theme>("res://assets/theme/hollowdeck_theme.tres");
        Check("theme_loads", theme is not null, "hollowdeck_theme.tres failed to load");
        if (theme is null) return;

        Check("theme_default_font_size_is_on_the_grid",
            PixelSpec.IsLegalFontSize(theme.DefaultFontSize),
            $"default_font_size {theme.DefaultFontSize} is not a multiple of {PixelSpec.FontDesignEm}");

        foreach (var type in theme.GetFontSizeTypeList())
        {
            foreach (var name in theme.GetFontSizeList(type))
            {
                int size = theme.GetFontSize(name, type);
                Check($"theme_{type}_{name}_is_on_the_grid", PixelSpec.IsLegalFontSize(size),
                    $"{type}/{name} = {size}, not a multiple of {PixelSpec.FontDesignEm}");
            }
        }

        foreach (var (path, line, size) in ScanSourceForFontSizes())
        {
            Check($"{path.GetFile()}_line_{line}_is_on_the_grid", PixelSpec.IsLegalFontSize(size),
                $"{path}:{line} renders text at {size}, not a multiple of {PixelSpec.FontDesignEm}");
        }
    }

    // Every literal font size in scenes/ and scripts/ui/, as (path, line,
    // size). Deliberately a text scan: the alternative is instantiating every
    // screen, and a size only reachable behind a branch would still be missed.
    private static IEnumerable<(string Path, int Line, int Size)> ScanSourceForFontSizes()
    {
        var pattern = new Regex(
            """AddThemeFontSizeOverride\("[a-z_]+", (\d+)\)|theme_override_font_sizes/[a-z_]+ = (\d+)""");

        foreach (string path in FilesUnder("res://scenes", ".tscn").Concat(FilesUnder("res://scripts/ui", ".cs")))
        {
            // Qualified: this file already has System.IO in scope for Path.
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file is null) continue;
            int line = 0;
            while (!file.EofReached())
            {
                line++;
                var match = pattern.Match(file.GetLine());
                if (!match.Success) continue;
                string digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                yield return (path, line, int.Parse(digits));
            }
        }
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
    // The clip set tools/artgen/src/anim.rs writes, restated rather than
    // imported because there is no import available across the Rust/C# seam -
    // same argument MapSmokeTest makes for restating the map's width band. A
    // clip added on one side of that seam and not the other has to be argued
    // for here rather than inherited.
    private static readonly (string Clip, int Frames)[] EnemyClips =
    {
        ("idle", 2), ("windup", 2), ("hit", 2), ("death", 3), ("escape", 3),
    };

    private static readonly (string Clip, int Frames)[] PlayerClips =
    {
        ("idle", 2), ("windup", 2), ("hit", 2),
    };

    // Bidirectional, exactly like TestEveryDefinitionHasAnIcon: every creature
    // has its frames, and every frame directory is a live creature.
    //
    // The forward half catches the workflow failure - frames are generated, so
    // a new enemy has none until `artgen animate` is re-run, and SpriteAnimator
    // deliberately degrades to the static texture rather than crashing. Without
    // this check that enemy would simply stand still forever with every suite
    // green. The orphan half catches a renamed id leaving a stale frame set
    // behind, which is the same failure the icon sweep exists for.
    private void TestEveryCreatureHasItsFrames()
    {
        EnemyDatabase.LoadAll();
        var expected = new HashSet<string> { "player" };

        // The clip *names* the game asks for, against the table this file
        // restates from anim.rs. Two copies is the floor - nothing imports
        // across the Rust/C# seam - so this is what holds them level, and
        // SpriteAnimator's own constants are what stop the call sites being a
        // third. A name only the animator knows loads nothing and throws
        // nothing.
        Check("creature_clip_names_match",
            SpriteAnimator.CreatureClips.OrderBy(c => c)
                .SequenceEqual(EnemyClips.Select(c => c.Clip).OrderBy(c => c)),
            $"SpriteAnimator.CreatureClips [{string.Join(", ", SpriteAnimator.CreatureClips)}] does " +
            $"not match anim.rs's ENEMY_CLIPS [{string.Join(", ", EnemyClips.Select(c => c.Clip))}]");

        Check("player_clip_names_match",
            SpriteAnimator.PlayerClips.OrderBy(c => c)
                .SequenceEqual(PlayerClips.Select(c => c.Clip).OrderBy(c => c)),
            $"SpriteAnimator.PlayerClips [{string.Join(", ", SpriteAnimator.PlayerClips)}] does " +
            $"not match anim.rs's PLAYER_CLIPS [{string.Join(", ", PlayerClips.Select(c => c.Clip))}]");

        foreach (var enemy in EnemyDatabase.All)
        {
            expected.Add(enemy.Id);
            AssertClips(enemy.Id, EnemyClips);
        }
        AssertClips("player", PlayerClips);

        using var dir = DirAccess.Open("res://assets/sprites/anim");
        if (dir is null)
        {
            Check("anim_directory_exists", false,
                "res://assets/sprites/anim is missing - run 'artgen animate'");
            return;
        }

        foreach (string name in dir.GetDirectories())
        {
            Check($"anim_orphan_{name}", expected.Contains(name),
                $"assets/sprites/anim/{name}/ has no matching creature - a renamed id " +
                "leaves its old frames behind and nothing else would say so");
        }
    }

    private void AssertClips(string spriteId, (string Clip, int Frames)[] clips)
    {
        foreach (var (clip, frames) in clips)
        {
            var loaded = ArtAssets.AnimFrames(spriteId, clip);
            Check($"frames_{spriteId}_{clip}", loaded.Length == frames,
                $"assets/sprites/anim/{spriteId}/{clip}_*.png has {loaded.Length} frame(s), " +
                $"expected {frames} - re-run 'artgen animate'");
        }
    }

    // Frames per combat effect, restated from tools/artgen/src/icons/fx.rs for
    // the reason EnemyClips above is restated from anim.rs: nothing imports
    // across the Rust/C# seam, so two copies is the floor and this is what holds
    // them level.
    private const int FxFrameCount = 4;

    // A real frame at 60fps, and how many of them a burst gets to finish in.
    // Half a second against an authored run of 4 x CombatFx.FrameSeconds =
    // 0.24s, so the budget is roughly double what the effect asks for - loose
    // enough that a cadence tweak does not fail this, tight enough that a
    // one-shot outliving the beat it annotates does.
    private const double FrameSeconds60Fps = 1.0 / 60.0;
    private const int BurstBudgetTicks = 30;

    // Three-way, and it needs to be for the same reason the chrome slices do:
    // the sets that have to agree are PNGs on disk, names in CombatFx, and
    // effects something actually spawns - and each pair of those can agree while
    // the third disagrees.
    //
    // The third set is the one worth having. Every check on the driver stays
    // green while nothing calls it, which is precisely what the GlowRing pass
    // had to add three separate seam assertions for; an effect authored, drawn,
    // generated and never spawned is 4 files of dead art that `artgen validate`
    // reports as conforming.
    private void TestEveryCombatEffectHasItsFrames()
    {
        // Reflected rather than listed, so this cannot go stale against the
        // class it is checking. It also gives the constant *names*, which is
        // what a call site spells - `CombatFx.Impact`, never "impact".
        var constants = typeof(CombatFx)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Check("combat_fx_all_lists_every_constant",
            constants.Values.OrderBy(v => v).SequenceEqual(CombatFx.All.OrderBy(v => v)),
            $"CombatFx.All [{string.Join(", ", CombatFx.All)}] does not cover the declared " +
            $"constants [{string.Join(", ", constants.Values)}] - All is what every sweep here " +
            "drives, so a constant missing from it is an effect nothing checks");

        // On disk: the prefix of every fx_*.png, i.e. the run each frame is in.
        var onDisk = PngsUnder("res://assets/icons/fx")
            .Select(path => path.GetFile().GetBaseName())
            .Select(name => name.Contains('_') ? name[..name.LastIndexOf('_')] : name)
            .ToHashSet();

        var missing = CombatFx.All.Except(onDisk).OrderBy(x => x).ToList();
        Check("combat_fx_have_art", missing.Count == 0,
            $"no frames for: {string.Join(", ", missing)} - run `artgen generate fx` and re-import");

        var orphaned = onDisk.Except(CombatFx.All).OrderBy(x => x).ToList();
        Check("combat_fx_have_no_orphans", orphaned.Count == 0,
            $"frame run(s) under assets/icons/fx with no CombatFx name: " +
            $"{string.Join(", ", orphaned)} - an effect was generated and never wired up");

        foreach (string effect in CombatFx.All)
        {
            var frames = ArtAssets.FxFrames(effect);
            Check($"combat_fx_frames_{effect}", frames.Length == FxFrameCount,
                $"assets/icons/fx/{effect}_*.png has {frames.Length} frame(s), expected " +
                $"{FxFrameCount} - re-run `artgen generate fx`");
        }

        var spawned = ScanSourceForNames("res://scripts/ui", "CombatFx", "CombatFx.cs");
        var unspawned = constants
            .Where(entry => !spawned.Contains(entry.Key))
            .Select(entry => entry.Value)
            .OrderBy(x => x)
            .ToList();
        Check("combat_fx_are_spawned", unspawned.Count == 0,
            $"effect(s) nothing plays: {string.Join(", ", unspawned)} - declared in CombatFx and " +
            "reached by no call site, so every other check here passes over dead art");
    }

    // Collects the member names used as `<type>.<Member>` under a directory,
    // skipping the file that declares them. A source scan rather than a runtime
    // observation because these fire off a combat HP diff, which instantiating a
    // screen would not produce - the same argument the transform and font-size
    // scans in this file already make.
    //
    // **Comment lines are skipped, and that is what makes the check able to
    // fail.** The first version did not skip them, so a burst deleted from
    // PopupDelta still counted as spawned: the paragraph above it explaining
    // when Venom fires names `CombatFx.Venom`, and prose about a call site
    // reads to a regex exactly like the call site. Measured - repointing that
    // one spawn at another effect left all 1113 checks green. The same skip is
    // in ScanSourceForSpriteTransforms below, for the same reason.
    private static HashSet<string> ScanSourceForNames(string root, string type, string declaringFile)
    {
        var pattern = new Regex($@"\b{Regex.Escape(type)}\.(?<member>\w+)");
        var found = new HashSet<string>();
        foreach (string path in FilesUnder(root, ".cs"))
        {
            if (path.GetFile() == declaringFile) continue;
            using var reader = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (reader is null) continue;
            while (!reader.EofReached())
            {
                string line = reader.GetLine();
                if (line.TrimStart().StartsWith("//")) continue;
                foreach (Match match in pattern.Matches(line))
                {
                    found.Add(match.Groups["member"].Value);
                }
            }
        }
        return found;
    }

    // The driver half, and the half that matters: four PNGs on disk plus a
    // TextureRect that never swaps them look identical to every check above.
    // Same shape as TestTheIdleClipActuallyAdvances, and driven by calling
    // _Process directly for the same reason - a suite asserts inside _Ready and
    // cannot yield.
    //
    // ReduceMotion is asserted in both positions, again because only one is the
    // bug: every burst opens on a solid disc with an N8 core, which is the
    // photosensitive frame and must be declined, and the three frames after it
    // are the effect and must not be.
    private void TestACombatEffectAdvancesAndFrees()
    {
        bool original = SettingsManager.Instance.ReduceMotion;
        const string scratch = "user://settings_combatfx_test.json";

        foreach (bool reduceMotion in new[] { false, true })
        {
            SettingsManager.Instance.SetReduceMotion(reduceMotion, scratch);

            var host = new Control();
            AddChild(host);
            CombatFx.Play(host, new Vector2(200, 120), CombatFx.Impact);

            var rect = host.GetChildren().OfType<TextureRect>().FirstOrDefault();
            if (rect is null)
            {
                Check($"combat_fx_spawns_a_rect_reduce_motion_{reduceMotion}", false,
                    "CombatFx.Play added no TextureRect - the effect is silently doing nothing");
                host.QueueFree();
                continue;
            }

            var animator = rect.GetChildren().OfType<SpriteAnimator>().FirstOrDefault();
            if (animator is null)
            {
                Check($"combat_fx_attaches_a_driver_reduce_motion_{reduceMotion}", false,
                    "the effect rect carries no SpriteAnimator - frames on disk with nothing " +
                    "ticking them render as one held pose");
                host.QueueFree();
                continue;
            }

            string expected = reduceMotion ? "impact_1" : "impact_0";
            Check($"combat_fx_opens_on_{expected}",
                rect.Texture?.ResourcePath.Contains(expected) ?? false,
                $"ReduceMotion={reduceMotion} should open the burst on {expected}, got " +
                $"{rect.Texture?.ResourcePath ?? "<null>"}");

            // Ticked at a real frame time for a fixed budget, deliberately not
            // at a multiple of CombatFx.FrameSeconds. Deriving the tick from
            // the constant under test makes the check unable to observe that
            // constant at all: the first version stepped by FrameSeconds * 2
            // and stayed green with FrameSeconds set to 600, which is a burst
            // that never finishes. So the assertion is a *budget* - a one-shot
            // that outlives the beat it annotates is the failure, whether it
            // stalled on one frame or is merely half an hour long.
            var seen = new HashSet<string>();
            for (int tick = 0; tick < BurstBudgetTicks; tick++)
            {
                seen.Add(rect.Texture?.ResourcePath ?? "<null>");
                animator._Process(FrameSeconds60Fps);
            }

            Check($"combat_fx_advances_reduce_motion_{reduceMotion}", seen.Count > 1,
                $"the burst held one frame ({string.Join(", ", seen)}) across " +
                $"{BurstBudgetTicks} frames with ReduceMotion={reduceMotion}");

            // Freed at the end of the run, not left behind. A burst that never
            // frees is one leaked 160x160 Control per hit, which is invisible
            // for a fight and then is not.
            Check($"combat_fx_frees_itself_reduce_motion_{reduceMotion}",
                rect.IsQueuedForDeletion(),
                "the effect rect is still alive after its run - AttachOneShot's onComplete " +
                "is what frees it, and nothing else will");

            host.QueueFree();
        }

        SettingsManager.Instance.SetReduceMotion(original, scratch);
        if (Godot.FileAccess.FileExists(scratch)) DirAccess.RemoveAbsolute(scratch);

        TestACombatEffectRendersAtIntegerScale();
    }

    // Section 2, for the one pixel TextureRect in the game that no .tscn
    // declares - so TestCreatureSpritesRenderAtIntegerScale, which reads
    // CustomMinimumSize out of a scene file, cannot see it at all.
    private void TestACombatEffectRendersAtIntegerScale()
    {
        var host = new Control();
        AddChild(host);
        CombatFx.Play(host, new Vector2(201, 123), CombatFx.Ward);

        var rect = host.GetChildren().OfType<TextureRect>().FirstOrDefault();
        if (rect is null)
        {
            Check("combat_fx_integer_scale_setup", false, "CombatFx.Play added no TextureRect");
            host.QueueFree();
            return;
        }

        // Pinned to the exact size, not merely to *an* integer scale. "Integer
        // and square" is satisfied by CardArtScale's 96x96 too, so a burst
        // shipped at 60% of its intended size would leave this green - and
        // combat_fx_lands_on_a_whole_source_pixel would not catch it either,
        // since changing ApplyPixelRect and SnapTranslation together in
        // CombatFx.Play keeps them agreeing with each other.
        var expected = PixelSpec.SizeFor(PixelSpec.CreatureGrid, PixelSpec.SpriteScale);
        Check("combat_fx_renders_at_an_integer_scale", rect.Size.IsEqualApprox(expected),
            $"a burst measures {rect.Size}, expected {expected} - a {PixelSpec.CreatureGrid}px " +
            $"source at SpriteScale {PixelSpec.SpriteScale} (ART_SPEC section 2)");

        Check("combat_fx_is_nearest_filtered",
            rect.TextureFilter == CanvasItem.TextureFilterEnum.Nearest,
            $"a burst filters as {rect.TextureFilter}, not Nearest (ART_SPEC section 3)");

        // Section 9's translation rule. The centre a call site hands over comes
        // from a Control's GlobalPosition plus half its Size and is fractional
        // far more often than not; the spawner has to round it, and (201, 123)
        // is deliberately on neither axis's grid.
        Check("combat_fx_lands_on_a_whole_source_pixel",
            rect.Position.X % PixelSpec.SpriteScale == 0 && rect.Position.Y % PixelSpec.SpriteScale == 0,
            $"a burst sits at {rect.Position}, which is not a multiple of " +
            $"{PixelSpec.SpriteScale} - a pixel asset placed off the grid renders uneven " +
            "pixel widths exactly as a fractional scale does (ART_SPEC section 9)");

        // Section 5, indirectly but where it actually bites: the burst is a
        // Control laid over the enemy row, and the CpuParticles2D it replaced
        // was a Node2D that could never have taken a click.
        Check("combat_fx_ignores_the_mouse",
            rect.MouseFilter == Control.MouseFilterEnum.Ignore,
            "a burst is hit-testable - a 160x160 Control over the enemy row swallows the " +
            "click that targets the enemy under it");

        host.QueueFree();
    }

    // Frames on disk and a driver that never advances them look identical to
    // every other check in this file - and that is exactly how this shipped
    // broken: the idle clip was gated on ReduceMotion, so a player with that
    // setting on saw the whole feature as sprites standing still. Reported from
    // a playthrough, which is the second time this project has learned that a
    // green suite says nothing about what moves.
    //
    // Driven by calling _Process directly rather than by waiting frames: a
    // smoke test asserts inside _Ready and cannot yield, and the state machine
    // is what is under test, not Godot's main loop.
    //
    // ReduceMotion is asserted in *both* positions because only one of them is
    // the bug. The setting must decline the hit clip's opening flash frame - the
    // one genuinely photosensitive thing here - and must not touch the idle
    // breathe, which is a 1px squash and was ungated for the three phases before
    // this file existed.
    private void TestTheIdleClipActuallyAdvances()
    {
        bool original = SettingsManager.Instance.ReduceMotion;
        const string scratch = "user://settings_pixelspec_test.json";

        foreach (bool reduceMotion in new[] { false, true })
        {
            SettingsManager.Instance.SetReduceMotion(reduceMotion, scratch);

            var host = new TextureRect();
            AddChild(host);
            var animator = SpriteAnimator.Attach(host, "player", "idle", "windup", "hit");
            if (animator is null)
            {
                Check($"idle_advances_reduce_motion_{reduceMotion}", false,
                    "SpriteAnimator.Attach returned null - the player has no generated frames");
                host.QueueFree();
                continue;
            }

            var seen = new HashSet<string>();
            for (int tick = 0; tick < 8; tick++)
            {
                seen.Add(host.Texture?.ResourcePath ?? "<null>");
                animator._Process(0.5);
            }

            Check($"idle_advances_reduce_motion_{reduceMotion}", seen.Count > 1,
                $"the idle clip held one frame ({string.Join(", ", seen)}) across 8 ticks with " +
                $"ReduceMotion={reduceMotion} - a breathing loop that does not breathe reads as " +
                "no animation at all");

            // The flash frame is idle_0's sibling, so compare against the clip
            // rather than a literal: with ReduceMotion on, playing `hit` must
            // land on frame 1, not the full-silhouette N8 frame 0.
            animator.Play("hit");
            string expected = reduceMotion ? "hit_1" : "hit_0";
            Check($"hit_opens_on_{expected}",
                host.Texture?.ResourcePath.Contains(expected) ?? false,
                $"ReduceMotion={reduceMotion} should open the hit clip on {expected}, got " +
                $"{host.Texture?.ResourcePath ?? "<null>"}");

            host.QueueFree();
        }

        SettingsManager.Instance.SetReduceMotion(original, scratch);
        if (Godot.FileAccess.FileExists(scratch)) DirAccess.RemoveAbsolute(scratch);

        TestATerminalClipIsTerminal();
    }

    // Two properties of PlayTerminal that its own doc comment asserts and the
    // state machine did not.
    //
    // The callback: reaching the arm that fires it needs the frame counter to
    // pass frames.Length, and the "stop ticking" guard used to return on exactly
    // the value below it - so the arm was unreachable and the callback silently
    // never ran. Latent, because nothing passes one today; a trap, because the
    // *other* path through PlayTerminal (unknown clip) does fire it, so the two
    // disagree. The concrete cost is whoever later moves EnemyView's removal
    // onto the clip to stop freeing the view mid-dissolve: the view is never
    // removed and RefreshEnemies's diff never settles.
    //
    // The stickiness: nothing stopped a later Play("hit") overwriting a death
    // clip and dropping the corpse back onto the breathing loop one frame time
    // later. The old code was immune by accident - the death tween drove the
    // view's Scale and the recoil drove the sprite's, two channels - and frame
    // clips collapse them into one.
    private void TestATerminalClipIsTerminal()
    {
        var host = new TextureRect();
        AddChild(host);
        var animator = SpriteAnimator.Attach(host, "player", SpriteAnimator.PlayerClips);
        if (animator is null)
        {
            Check("terminal_clip_setup", false, "SpriteAnimator.Attach returned null for the player");
            host.QueueFree();
            return;
        }

        bool fired = false;
        animator.PlayTerminal("windup", () => fired = true);
        for (int tick = 0; tick < 8; tick++) animator._Process(0.5);

        Check("terminal_clip_fires_its_callback", fired,
            "PlayTerminal's onComplete never ran - the arm that invokes it is unreachable when " +
            "the hold guard returns on the frame index instead of a latch");

        string held = host.Texture?.ResourcePath ?? "<null>";
        animator.Play("hit");
        for (int tick = 0; tick < 4; tick++) animator._Process(0.5);

        Check("terminal_clip_cannot_be_interrupted",
            (host.Texture?.ResourcePath ?? "<null>") == held,
            $"a Play() during a terminal clip moved the sprite off {held} to " +
            $"{host.Texture?.ResourcePath ?? "<null>"} - a dead enemy must not return to idle");

        host.QueueFree();
    }

    // The three glow-ring checks below (ART_SPEC section 6, PIXEL_ART_ROADMAP
    // section 3) share this: attach a ring to a throwaway Control, tick its
    // _Process directly, and read the border colour back off the stylebox the
    // ring actually installed.
    //
    // Reading it *back* rather than off GlowRing.Gold is the whole point. The
    // cycle array is trivially on-ramp and trivially varied; what can go wrong
    // is the builder in between - CardFrameStyle already lerps for upgraded and
    // hovered two lines from where the glow lands - and a check on the array
    // would be green through any of it.
    //
    // Driving _Process directly is the TestTheIdleClipActuallyAdvances lesson
    // one surface over: a cycle that exists and a driver that never advances it
    // look identical to every static check in this file.
    private static List<Color> TickRing(string[] styleboxes, System.Func<Color, StyleBox> build, Color[] cycle,
        int ticks, out Color opening)
    {
        var host = new Control();
        var ring = GlowRing.Attach(host, styleboxes, build, cycle);

        opening = ((StyleBoxFlat)host.GetThemeStylebox(styleboxes[0])).BorderColor;

        var seen = new List<Color>();
        for (int tick = 0; tick < ticks; tick++)
        {
            foreach (string name in styleboxes)
            {
                seen.Add(((StyleBoxFlat)host.GetThemeStylebox(name)).BorderColor);
            }
            ring._Process(GlowRing.FrameSeconds);
        }

        ring.Stop();
        host.QueueFree();
        return seen;
    }

    private void TestTheGlowRingActuallyAdvances()
    {
        var seen = TickRing(new[] { "panel" }, c => ChromeStyles.TargetLockStyle(c), GlowRing.Gold, 8, out _);

        Check("glow_ring_advances", seen.Distinct().Count() > 1,
            $"the ring held one colour ({string.Join(", ", seen.Distinct().Select(c => c.ToHtml(false)))}) " +
            "across 8 ticks - a glow that does not cycle is the static border it was supposed to replace");

        // A ping-pong, not a sawtooth: the cycle must come back down through its
        // middle entry rather than dropping two ramp steps in one frame, which
        // reads as a flicker. Over a full cycle every entry is visited, and the
        // peak and trough are each visited once.
        var cycle = GlowRing.Gold;
        var full = TickRing(new[] { "panel" }, c => ChromeStyles.TargetLockStyle(c), cycle, cycle.Length, out _);
        Check("glow_ring_visits_every_entry", full.Distinct().Count() == cycle.Distinct().Count(),
            $"one cycle painted {full.Distinct().Count()} distinct colours, expected " +
            $"{cycle.Distinct().Count()} - the ring is skipping an entry");
    }

    // The reason this ring steps rather than tweening. A TweenProperty on
    // "border_color" passes through every colour between G3 and G4, and section
    // 5 admits exactly 43 - so the obvious implementation is off-ramp on almost
    // every frame it draws, and nothing else in this file would notice.
    //
    // Scoped to the two producers that pass the colour straight through. A Rare
    // *upgraded* card's border is a lerp toward UpgradeAccent before the glow is
    // ever consulted (CardFrameStyle), so it is off-ramp today, independently of
    // this feature and out of its scope.
    private void TestEveryGlowFrameIsOnTheRamp()
    {
        var surfaces = new (string Label, System.Func<Color, StyleBox> Build, Color[] Cycle)[]
        {
            ("TargetLockStyle", c => ChromeStyles.TargetLockStyle(c), GlowRing.Gold),
            ("BossNodeGlowStyle", c => ChromeStyles.BossNodeGlowStyle(c), GlowRing.Danger),
        };

        foreach (var (label, build, cycle) in surfaces)
        {
            var offRamp = TickRing(new[] { "panel" }, build, cycle, cycle.Length * 2, out _)
                .Where(c => !PixelSpec.IsOnRamp(c))
                .Distinct()
                .ToList();

            Check($"glow_frames_on_ramp_{label}", offRamp.Count == 0,
                $"{label} painted {offRamp.Count} colour(s) off the section 5 ramp: " +
                $"{string.Join(", ", offRamp.Select(c => c.ToHtml(false)))} - a glow must step " +
                "between authored ramp entries, never interpolate between them");
        }
    }

    // Attach paints the peak before the first tick, and that is what makes this
    // feature's rest state identity rather than a change.
    //
    // Each of these three colours is exactly what the static border it replaced
    // painted - so a screenshot taken the moment a screen builds is unchanged,
    // which six committed ScreenShot fixtures depend on, and the ring never
    // opens dimmer than the still it took over from.
    private void TestTheGlowRingOpensOnWhatItReplaced()
    {
        var expected = new (string Label, System.Func<Color, StyleBox> Build, Color[] Cycle, Color Was)[]
        {
            ("target_lock", c => ChromeStyles.TargetLockStyle(c), GlowRing.Gold, PixelSpec.Ramp.G5),
            ("boss_node", c => ChromeStyles.BossNodeGlowStyle(c), GlowRing.Danger, UiTheme.Palette.Damage),
            ("rare_card",
                c => ChromeStyles.CardFrameStyle(CardType.Skill, Rarity.Rare, hovered: false, rareGlow: c),
                GlowRing.Gold, UiTheme.Palette.RarityRareGlow),
        };

        foreach (var (label, build, cycle, was) in expected)
        {
            TickRing(new[] { "panel" }, build, cycle, 1, out var opening);
            Check($"glow_opens_on_the_still_it_replaced_{label}", opening == was,
                $"{label} opened on {opening.ToHtml(false)}, expected {was.ToHtml(false)} - " +
                "the rest state of this feature is supposed to be byte-identical to what shipped before it");
        }
    }

    // The third surface, asserted where it is wired rather than where it is
    // built. Everything above drives GlowRing directly, and every one of those
    // checks stays green if no view ever attaches one - a feature at a seam
    // fails by being connected to nothing, which is the shape this project
    // keeps finding at the data/code boundary.
    //
    // Both directions, and the second is the one that needs a test. CardViews
    // are pooled and reused across refreshes (CombatScreen's _cardViews, the
    // pile popups, every picker grid), so the same node is a Rare on one
    // SetCardInstance and a Common on the next; a ring that attaches and never
    // detaches puts a gold pulse on a Common and reads as a bug in the rarity
    // colours rather than in the ring.
    private void TestOnlyARareCardCarriesAGlowRing()
    {
        var rare = CardDatabase.All.FirstOrDefault(c => c.Rarity == Rarity.Rare);
        var common = CardDatabase.All.FirstOrDefault(c => c.Rarity == Rarity.Common);
        if (rare is null || common is null)
        {
            Check("card_glow_ring_fixtures_exist", false,
                "cards.json has no Rare or no Common to test the ring against");
            return;
        }

        var view = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(view);

        view.SetCardInstance(new CardInstance(rare));
        Check("a_rare_card_carries_a_glow_ring",
            view.GetChildren().OfType<GlowRing>().Count() == 1,
            $"a Rare CardView carries {view.GetChildren().OfType<GlowRing>().Count()} GlowRing(s), " +
            "expected exactly one");

        view.SetCardInstance(new CardInstance(common));
        Check("a_common_card_carries_no_glow_ring",
            view.GetChildren().OfType<GlowRing>().Count() == 0,
            "a reused CardView kept its Rare glow ring after becoming a Common - " +
            "RefreshRareRing has to detach, not just attach");

        // Back to Rare, and still exactly one: the early return in
        // RefreshRareRing exists so a repeated refresh does not restart the
        // cycle, and getting that wrong in the other direction stacks drivers.
        view.SetCardInstance(new CardInstance(rare));
        view.SetCardInstance(new CardInstance(rare));
        Check("refreshing_a_rare_card_does_not_stack_rings",
            view.GetChildren().OfType<GlowRing>().Count() == 1,
            $"two refreshes left {view.GetChildren().OfType<GlowRing>().Count()} GlowRing(s) on one card");

        view.QueueFree();
    }

    // ART_SPEC section 9. The bug this exists for shipped and survived three
    // phases: EnemyView tweened its sprite's Scale to 1.04/1.08/1.15 and its
    // rotation to 6 degrees, CombatScreen slid the player 6px at a 5x render
    // scale, and section 2 called all of that a bug the whole time. Nothing
    // caught it because TestCreatureSpritesRenderAtIntegerScale reads the
    // *static* CustomMinimumSize out of the .tscn - the rule was enforced at
    // rest and broken in motion.
    //
    // A source scan for the same reason ScanSourceForFontSizes is one:
    // instantiating the screens would miss anything behind a branch, and these
    // are all behind combat events.
    //
    // Alpha is deliberately not in the pattern. Modulate resamples nothing, so
    // it stays the one property a pixel asset may be tweened on, and both death
    // and escape still use it.
    // The scan above is only as wide as ThisIsAPixelSurface is, and a scan that
    // finds nothing looks exactly like a codebase with nothing to find. That is
    // not a hypothetical here: this same guard was keyed to the literal
    // `TweenProperty(` for three phases, ART_SPEC section 11 renamed all
    // thirty-four call sites to `TweenTo(` in one commit, and the scan went from
    // nine findings in CardView.cs to zero with every call site still present
    // and all 23 suites green.
    //
    // So the predicate is asserted rather than trusted. These three files are
    // the ones it is *for* - a creature view, a card, a damage number - and each
    // reaches it through a different half of the rule. If a regex edit stops
    // resolving any of them, this fails here rather than going quiet over there.
    private void TestTheTransformScanSeesEveryPixelSurface()
    {
        foreach (string fileName in new[]
                 { "EnemyView.cs", "CardView.cs", "FloatingText.cs", "PotionView.cs" })
        {
            Check($"{fileName}_is_scanned_as_a_pixel_surface", ThisIsAPixelSurfaceIn(fileName),
                $"scripts/ui/{fileName} no longer resolves as a pixel surface, so a transform on " +
                "`this` in it would go unseen (ART_SPEC section 9)");
        }

        // Four probes, one per door, so a regex edit cannot take out an arm
        // unnoticed: EnemyView and CardView through the TextureRect they
        // declare, FloatingText through Label, PotionView through its
        // ArtAssets-backed Icon.
        //
        // And the other direction, which is the one that actually went wrong.
        // Nothing in this scan reads a comment, because everything it reads is
        // matched out of source text and this codebase's comments are prose
        // *about* pixel art. HoverTooltip is the probe for it: it names
        // TextureRect nowhere and is not a surface, while its neighbours
        // discuss them at length - and the polluted version of this predicate
        // called almost every file in the directory a surface off comment text
        // alone.
        foreach (string fileName in new[] { "HoverTooltip.cs", "Motion.cs", "ChromeStyles.cs" })
        {
            Check($"{fileName}_is_not_a_pixel_surface", !ThisIsAPixelSurfaceIn(fileName),
                $"scripts/ui/{fileName} resolves as a pixel surface, which it is not - the " +
                "predicate is matching prose rather than declarations");
        }
    }

    private static bool ThisIsAPixelSurfaceIn(string fileName)
    {
        string path = $"res://scripts/ui/{fileName}";
        using var reader = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (reader is null) return false;

        var lines = new List<string>();
        while (!reader.EofReached()) lines.Add(reader.GetLine());

        // The scan's own predicate, called rather than re-implemented. It was
        // re-implemented, which is two copies of the holder rule and two
        // chances for this probe to keep passing after the scan stops working.
        return ThisIsAPixelSurface(CodeLines(lines));
    }

    private void TestNoTweenTransformsAPixelSprite()
    {
        var findings = ScanSourceForSpriteTransforms().ToList();
        foreach (var (path, line, detail) in findings)
        {
            Check($"{path.GetFile()}_line_{line}_transforms_a_pixel_sprite", false,
                $"{path}:{line} tweens {detail} - a pixel sprite animates by frame swap " +
                "(SpriteAnimator), never by transform (ART_SPEC section 9)");
        }

        Check("no_tween_transforms_a_pixel_sprite", findings.Count == 0,
            $"{findings.Count} site(s) found");
    }

    // Which identifiers hold a pixel texture is *discovered per file* from
    // their declarations, not hand-listed.
    //
    // It was hand-listed - `_sprite`, `_playerSprite`, `_intentIcon` - and that
    // version was narrower than three documents claimed it was. Two of the five
    // violations the frame-animation change removed were tweens on `this`
    // rather than on the sprite (EnemyView's death and escape squeezed the whole
    // view), so restoring either would have gone straight past it. And it was
    // green over two *live* ones the whole time: StatusRow's 0.4-to-1.0 pop on a
    // 32x32 status icon, and CombatScreen's zero-to-one pop on the 16x16 energy
    // gem. Both are now fixed; neither would have been found by a list that only
    // knew the names someone had already thought of.
    //
    // A `TextureRect` is what holds a pixel texture in this codebase, so the
    // type is the honest predicate. `this` is added for views that *wrap* one,
    // because scaling the container resamples the child just the same.
    private static readonly Regex PixelHolderDeclaration = new(
        @"\bTextureRect\s*\??\s+(?<name>\w+)\b|\bvar\s+(?<name>\w+)\s*=\s*new\s+TextureRect\b|\b(?<name>\w+)\s*=\s*GetNode<TextureRect>");

    // When `this` is itself a pixel surface, so that transforming the whole
    // Control resamples what it carries just as surely as transforming a child
    // would. **Derived, not listed.**
    //
    // It was a hand-written set - `{ "EnemyView.cs" }` - and that is the same
    // shape as the hand-written holder list above it, which was green over two
    // live violations while three documents claimed full coverage. A list of
    // one is worse than a list of three, not better: it looks settled.
    //
    // Two doors into the same rule, which is why the predicate has two arms:
    //
    //   a TextureRect in the file   - the Control wraps pixel art, and scaling
    //                                 or rotating it resamples that art
    //                                 (ART_SPEC section 2). EnemyView and
    //                                 CardView both arrive this way.
    //   the class derives from Label - the Control *is* bitmap type, and
    //                                 scaling it renders the face away from its
    //                                 8px design em, which grows uneven stems
    //                                 exactly the way a fractional texture scale
    //                                 does (ART_SPEC section 7). FloatingText
    //                                 arrives this way, and no list would have
    //                                 predicted it - it holds no texture at all.
    //
    // Measured across scripts/ui: **thirteen** files are pixel surfaces -
    // CardView, CombatFx, CombatScreen, EnemyView, FloatingText,
    // MetaProgressionScreen, PileCounterBar, PixelSpec, PotionView,
    // ScreenBackground, ScreenChrome, SpriteAnimator, StatusRow. Three of those
    // are the ones this replaced an exception list for; the rest were always in
    // scope and simply had nothing to catch.
    //
    // That distinction is written out because the first version of this comment
    // got it wrong in the other direction, claiming the predicate resolved
    // "exactly three files and nothing else" - which was a measurement of how
    // many files *transform* `this`, not of how many are surfaces. A coverage
    // claim that is off by ten is the same species as the exception list it
    // replaced: a reader deciding whether a new view needs the predicate
    // widened would have trusted it.
    private static readonly Regex BitmapTypeDeclaration = new(@"\bclass\s+\w+\s*:\s*Label\b");

    // A Control that paints a pixel texture through a property rather than by
    // holding a TextureRect. PotionView is the one today - a Button whose Icon
    // comes from ArtAssets - and it is here because the two doors above do not
    // describe it: it declares no TextureRect and is not a Label, so scaling it
    // would have gone unseen. Keyed to ArtAssets rather than to the property
    // alone, because an Icon is only a pixel surface when what it holds is.
    private static readonly Regex PixelIconAssignment = new(@"\b(?:Icon|TextureNormal)\s*=\s*ArtAssets\.");

    // **Every one of these reads code lines only, and that is not tidiness.**
    // This scan discovers what to look at by matching source text, and this
    // codebase's comments are dense prose *about* pixel art - so a comment
    // saying "a `TextureRect` is what holds a pixel texture" declares a holder
    // called `is`, and one saying "one asset class over" declares a type called
    // `over`. Measured before this filter existed: the derived type set held
    // `of`, `over`, `and`, `rather` and `supported`, each of which was then
    // interpolated into a holder pattern that matched ordinary prose in almost
    // every file. Coverage therefore moved when a comment was *reworded* -
    // demonstrated by a live `Scale =` over a pixel icon going from caught to
    // invisible across a six-comment edit that changed no code at all.
    //
    // That is the same failure this scan has now had four times, arriving
    // through a fourth door: a guard keyed to a spelling dies when the spelling
    // moves, and it dies silently. Comment text is the worst thing yet to have
    // keyed it to, because nothing else in the codebase treats prose as
    // load-bearing and no reviewer would think to check.
    private static List<string> CodeLines(IEnumerable<string> lines) =>
        lines.Where(line => !line.TrimStart().StartsWith("//")).ToList();

    private static bool ThisIsAPixelSurface(IEnumerable<string> codeLines)
    {
        var lines = codeLines as IList<string> ?? codeLines.ToList();
        return TextureRectHolders(lines).Count > 0
            || lines.Any(line => BitmapTypeDeclaration.IsMatch(line))
            || lines.Any(line => PixelIconAssignment.IsMatch(line));
    }

    // Anchored to the start of a line and to real modifiers, so it matches a
    // declaration and not the word "class" inside a sentence.
    private static readonly Regex ClassDeclaration = new(
        @"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|new)\s+)*class\s+(?<name>\w+)\b");

    private static HashSet<string> TextureRectHolders(IEnumerable<string> codeLines)
    {
        var holders = new HashSet<string>();
        foreach (string text in codeLines)
        {
            foreach (Match declaration in PixelHolderDeclaration.Matches(text))
            {
                holders.Add(declaration.Groups["name"].Value);
            }
        }

        return holders;
    }

    private static IEnumerable<(string Path, int Line, string Detail)> ScanSourceForSpriteTransforms()
    {
        // Both the tween form and the direct assignment, since setting
        // _sprite.Scale outright is the same violation held still.
        //
        // **Both spellings of the tween form, and that alternation is the whole
        // reason this comment is here.** This pattern was keyed to the literal
        // `TweenProperty(` for three phases, which was every tween in the
        // project - and then ART_SPEC section 11 renamed all thirty-four call
        // sites to `TweenTo(` in one commit. Measured on that commit: this scan
        // went from 9 matching lines in CardView.cs alone to 0, with the same 9
        // call sites still there and all 23 suites green. A guard keyed to a
        // spelling dies the day the spelling changes, and it dies silently,
        // because a scan that finds nothing looks exactly like a codebase with
        // nothing to find. The name is in a regex rather than in the type
        // system, so nothing else could have caught it.
        //
        // **Three alternatives, and the third one is not decoration.** The
        // qualified form `foo.Scale =` was the only assignment shape the
        // pattern knew, and C# does not require the qualifier: `Scale = ` in a
        // Node's own method means `this.Scale =` and is the shape that was
        // actually written. Both deferred files used it - FloatingText opened
        // its punch on a bare `Scale = Vector2.One * 2.2f` and CardView started
        // its draw tween on `Scale = Vector2.One * 0.6f` - so those two lines
        // were invisible to this scan the whole time, and the exemption list
        // that named them was covering something already uncovered. Mutation
        // tested: re-adding a bare `Scale =` to FloatingText passed a full
        // suite before this arm existed.
        //
        // The `=(?!=|>)` on the assignment arms keeps `==` and an
        // expression-bodied `=>` out; the lookbehind is what stops `CardArtScale
        // = 3` and `TileScale = 2` reading as a transform.
        //
        // **TweenPingPong is in the alternation for the same reason TweenTo is**,
        // and it was missing for as long as it has existed. It is the vocabulary's
        // third builder (ART_SPEC section 11) and takes its property in the same
        // argument position as the other two, so nothing about it is harder to
        // scan - it simply was not thought of when the pattern was written
        // against the two that existed then. What it was hiding is the worst
        // instance of section 9 in the codebase: RewardScreen looping a +/-2.5
        // degree rotation on every card in the reward fan, forever, which is
        // precisely the "slow idle loop" that section calls out as never
        // permitted even when a fast lunge is. Third time this scan has gone
        // quiet because a builder's spelling moved out from under it.
        var transform = new Regex(
            """(?:TweenProperty|TweenTo|TweenPingPong)\(\s*(?<node>\w+|this)\s*, "(?<prop>scale|rotation|rotation_degrees|skew)"|(?<![\w.])(?<node>\w+|this)\.(?<prop>Scale|RotationDegrees|Rotation|Skew)\s*=(?!=|>)|(?<![\w.])(?<prop>Scale|RotationDegrees|Rotation|Skew)\s*=(?!=|>)""");

        // Read every file once first, because which identifiers are pixel
        // holders in *one* file depends on what the others turned out to be.
        var sources = new Dictionary<string, List<string>>();
        foreach (string path in FilesUnder("res://scripts/ui", ".cs"))
        {
            using var reader = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (reader is null) continue;
            var lines = new List<string>();
            while (!reader.EofReached()) lines.Add(reader.GetLine());
            sources[path] = lines;
        }

        // The types whose *instances* are pixel surfaces, discovered rather
        // than named: a class that wraps a TextureRect or is a Label carries
        // pixel art or bitmap type, so transforming one of its instances
        // resamples what it holds no matter which file does the transforming.
        //
        // This is the arm that catches a violation from the outside. The two
        // arms above only ever look at a class's own methods, so RewardScreen
        // tilting and endlessly swaying the CardViews in its reward fan was
        // invisible to all of them - a *continuous* rotation of pixel art,
        // which is the one case section 9 singles out as never allowed even
        // for fast motion, sitting in a file that holds no pixel asset itself.
        var pixelSurfaceTypes = new HashSet<string>();
        foreach (var (_, lines) in sources)
        {
            var code = CodeLines(lines);
            if (!ThisIsAPixelSurface(code)) continue;
            foreach (string text in code)
            {
                var declared = ClassDeclaration.Match(text);
                if (declared.Success) pixelSurfaceTypes.Add(declared.Groups["name"].Value);
            }
        }

        // An empty alternation is `(?:)`, which matches the empty string
        // everywhere and would make every identifier in the project a holder.
        // It cannot happen while any pixel surface exists, which is exactly why
        // it would be a silent catastrophe if one ever did not.
        if (pixelSurfaceTypes.Count == 0)
        {
            throw new System.InvalidOperationException(
                "no pixel surface types were derived - the transform scan would match everything");
        }

        string types = string.Join("|", pixelSurfaceTypes);
        var pixelSurfaceHolder = new Regex(
            $@"\b(?:{types})\s*\??\s+(?<name>\w+)\b" +
            $@"|\bvar\s+(?<name>\w+)\s*=\s*[\w.]+\.Instantiate<(?:{types})>");

        foreach (var (path, lines) in sources)
        {
            var code = CodeLines(lines);
            var holders = TextureRectHolders(code);

            // Asked before the typed identifiers are folded in, and that order
            // is load-bearing. `this` is a pixel surface because of what the
            // class *holds*, not because of what it happens to reference -
            // RewardScreen declares a CardView and is not itself pixel art, so
            // reading the merged set here would make `this.Scale` a violation
            // in every file that so much as names one.
            if (ThisIsAPixelSurface(code)) holders.Add("this");

            foreach (string text in code)
            {
                foreach (Match declaration in pixelSurfaceHolder.Matches(text))
                {
                    holders.Add(declaration.Groups["name"].Value);
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("//")) continue;

                // Matched against this line *and the next*, because a tween
                // call is routinely wrapped: RewardScreen's card sway is
                // `tween.TweenPingPong(` on one line and `view,
                // "rotation_degrees", ...` on the next, and a line-at-a-time
                // scan sees neither half. Only matches that *start* on this
                // line are reported, so the next line is not read twice.
                string probe = i + 1 < lines.Count ? lines[i] + " " + lines[i + 1].TrimStart() : lines[i];
                foreach (Match match in transform.Matches(probe))
                {
                    if (match.Index >= lines[i].Length) continue;
                    // The unqualified arm captures no node, because the node
                    // it means is not written down: an unqualified property
                    // assignment inside a Node's own method is `this`.
                    string node = match.Groups["node"].Success ? match.Groups["node"].Value : "this";
                    if (!holders.Contains(node)) continue;
                    yield return (path, i + 1, $"{node}.{match.Groups["prop"].Value}");
                }
            }
        }
    }

    // ART_SPEC section 11. The vocabulary only means anything if a call site
    // cannot go around it, and going around it is one keystroke: TweenProperty
    // is a public method on Tween that takes a raw duration and defaults to
    // Linear/InOut, and SetTrans/SetEase are public on both Tween and
    // PropertyTweener.
    //
    // So the check is structural rather than about the numbers. A site may pick
    // its own *period* (through MotionCurve.Over, which is what the ambient
    // loops and the content-branched durations use) and may not pick its own
    // shape, because a shape is the thing the vocabulary exists to hold at
    // eight rather than at thirty-four.
    //
    // A source scan for the same reason ScanSourceForFontSizes and
    // ScanSourceForSpriteTransforms are: these are almost all behind combat
    // events or ReduceMotion branches, so instantiating the screens reaches
    // roughly none of them. Comment lines are skipped - Motion.cs's own prose
    // names TweenProperty, and so does GlowRing's comment explaining why it is
    // a frame timer instead of one.
    private void TestEveryTweenUsesTheMotionVocabulary()
    {
        var findings = ScanSourceForRawTweens().ToList();
        foreach (var (path, line, detail) in findings)
        {
            // The call name is in the key because one line can carry two
            // violations - `TweenProperty(...).SetTrans(...)` is the exact shape
            // this replaced - and two findings under one key print as one
            // repeated failure rather than as two.
            Check($"{path.GetFile()}_line_{line}_calls_{detail.TrimEnd('(', ')')}_by_hand", false,
                $"{path}:{line} calls {detail} directly - a tween takes its duration, transition " +
                "and ease from a Motion curve (Tween.TweenTo / TweenPingPong, ART_SPEC section 11)");
        }

        Check("every_tween_uses_the_motion_vocabulary", findings.Count == 0,
            $"{findings.Count} site(s) found");
    }

    // Motion.cs is the only *file* exemption, because it is the thing being
    // defined - every builder in it is a raw call by construction.
    private static readonly HashSet<string> RawTweenExemptFiles = new() { "Motion.cs" };

    // The audio exemption is a *property*, not a file, and the difference is
    // the whole argument. What ART_SPEC section 11 actually says is that
    // `volume_db` is not motion: nothing on screen moves, and the
    // animation-speed setting Motion.Seconds exists to host must not cut a
    // music crossfade short. Exempting AudioManager.cs wholesale would have
    // covered a mute-indicator flash or a bus-meter nudge added to that file
    // later - visible motion, silently outside the vocabulary and outside the
    // future setting, with the exemption's own stated reason not applying to
    // it. The predicate should be the reason.
    private const string ExemptTweenProperty = "volume_db";

    private static IEnumerable<(string Path, int Line, string Detail)> ScanSourceForRawTweens()
    {
        var raw = new Regex(@"\.(?<call>TweenProperty|TweenInterval|SetTrans|SetEase)\s*\(");

        // Every directory under scripts/, not only ui and run. Section 11 opens
        // with the unqualified claim that TweenTo and TweenPingPong are the only
        // two ways a property is animated, and a scan rooted at two of the seven
        // source directories does not back that sentence. There are no raw
        // tweens outside ui/run today; this is what keeps that true rather than
        // true-so-far.
        foreach (string path in FilesUnder("res://scripts", ".cs"))
        {
            if (RawTweenExemptFiles.Contains(path.GetFile())) continue;

            using var reader = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (reader is null) continue;

            for (int line = 1; !reader.EofReached(); line++)
            {
                string text = reader.GetLine();
                if (text.TrimStart().StartsWith("//")) continue;
                if (text.Contains(ExemptTweenProperty)) continue;
                foreach (Match match in raw.Matches(text))
                {
                    yield return (path, line, $"{match.Groups["call"].Value}()");
                }
            }
        }
    }

    // The orphan direction, and the one that earns its keep - the scan above
    // stays green over a vocabulary of thirty unused curves. This is the third
    // set TestEveryCombatEffectHasItsFrames already carries for the same
    // reason: a curve authored, documented and never used is a name the next
    // reader has to rule out, and the whole point of eight is that eight is
    // small enough to hold.
    //
    // Reflection over the field names rather than a hand-written list, because
    // a hand-written list only knows the curves somebody already thought of -
    // the lesson TestNoTweenTransformsAPixelSprite was rewritten over.
    private void TestEveryMotionCurveIsUsed()
    {
        var declared = typeof(Motion)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MotionCurve))
            .Select(f => f.Name)
            .ToList();

        var used = ScanSourceForNames("res://scripts/ui", "Motion", "Motion.cs");
        foreach (string name in ScanSourceForNames("res://scripts/run", "Motion", "Motion.cs"))
        {
            used.Add(name);
        }

        Check("motion_declares_curves", declared.Count > 0, "reflection found no MotionCurve fields");
        foreach (string name in declared)
        {
            Check($"motion_curve_{name.ToLowerInvariant()}_is_used", used.Contains(name),
                $"Motion.{name} is declared but nothing under scripts/ui or scripts/run uses it");
        }
    }

    // Motion.All is what the two sweeps above and any future one drive, so a
    // curve left out of it is invisible to all of them - the same seam
    // CombatFx.All has, one asset class over. Reflection is the honest side of
    // that comparison for the same reason it is above.
    private void TestTheMotionRegistryIsComplete()
    {
        var declared = typeof(Motion)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MotionCurve))
            .Select(f => (Name: f.Name, Curve: (MotionCurve)f.GetValue(null)!))
            .ToList();

        foreach (var (name, curve) in declared)
        {
            Check($"motion_all_contains_{name.ToLowerInvariant()}", Motion.All.Contains(curve),
                $"Motion.{name} is not in Motion.All, so every sweep driven by All is blind to it");
        }

        Check("motion_all_has_no_strays", Motion.All.Count == declared.Count,
            $"Motion.All holds {Motion.All.Count} curves against {declared.Count} declared fields");

        // Two curves with the same three values are one curve with two names,
        // which is a vocabulary that does not distinguish anything. Compared as
        // values rather than by name, since the record struct's equality is
        // exactly the question being asked.
        Check("motion_curves_are_distinct", Motion.All.Distinct().Count() == Motion.All.Count,
            "two Motion curves share a duration, transition and ease");

        foreach (var (name, curve) in declared)
        {
            Check($"motion_curve_{name.ToLowerInvariant()}_has_a_period", curve.Seconds > 0f,
                $"Motion.{name} runs for {curve.Seconds}s - a zero-length tween is an assignment");
        }
    }

    // A call-site sweep, and the reason it is the assertion this whole item
    // turns on. ActDefinition has carried MapBackground/MapTint since acts
    // landed, and for six phases *two* of the thirteen screens read them: the
    // other eleven each passed a hardcoded tile name and a float Color literal,
    // so a shop in the Hollow Throne was the same room as a shop in the Sunken
    // Ward. Nothing could tell that from ScreenBackground's own source, which
    // is the general lesson section 6 of PIXEL_ART_ROADMAP records - shipping
    // the abstraction is not the same as landing it, and only a sweep over the
    // call sites can say which happened.
    //
    // The entry points take no art, so the violation is expressible as a
    // literal on the call line: either someone widened the API or someone went
    // around it. `AttachRoom`'s `BackdropRoom` argument is not one - a screen
    // naming the kind of room it *is* is a fact about that screen, where a
    // tile name is a fact about the art, and only the second can drift. Comment lines are skipped because prose about a call site
    // reads to a regex exactly like one - the first version of the CombatFx
    // spawn check counted the paragraph above the call and could not fail.
    private void TestNoScreenAuthorsItsOwnBackdrop()
    {
        var call = new Regex(@"ScreenBackground\.\w+\s*\(");
        var findings = new System.Collections.Generic.List<string>();

        foreach (string path in FilesUnder("res://scripts", ".cs"))
        {
            if (path.GetFile() == "ScreenBackground.cs") continue;

            using var reader = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (reader is null) continue;

            for (int line = 1; !reader.EofReached(); line++)
            {
                string text = reader.GetLine();
                if (text.TrimStart().StartsWith("//")) continue;
                if (!call.IsMatch(text)) continue;
                if (text.Contains('"') || text.Contains("new Color("))
                {
                    findings.Add($"{path.GetFile()}:{line} {text.Trim()}");
                }
            }
        }

        Check("no_screen_authors_its_own_backdrop", findings.Count == 0, string.Join(" | ", findings));
    }

    // Driven against a real backdrop rather than against the builders, because
    // what can go wrong is the assembly in between: a 128x128 tile texture
    // under StretchMode.Scale is stretched rather than tiled, and the texture
    // check alone stays green through it. Same argument TestEveryGlowFrameIsOnTheRamp
    // makes for reading the installed StyleBox instead of the cycle array.
    private void TestTheBackdropIsBuiltToSpec()
    {
        ActDatabase.LoadAll();

        var scratch = new Control();
        AddChild(scratch);
        ScreenBackground.AttachMap(scratch);

        var rects = new System.Collections.Generic.List<TextureRect>();
        foreach (var child in scratch.GetChildren())
        {
            if (child is TextureRect rect) rects.Add(rect);
        }

        Check("backdrop_builds_its_layers", rects.Count >= 4, $"{rects.Count} TextureRects");
        if (rects.Count < 4) { scratch.QueueFree(); return; }

        // Section 2's row says a background tile renders at 2x, and
        // StretchMode.Tile repeats a texture at its *native* size - so tiling
        // the 64x64 file draws it at 1x. PixelSpec.TileScale sat at 2 with no
        // readers for six phases while three documents said 128x128.
        var floor = rects[0];
        Check("backdrop_floor_tiles_at_the_declared_scale",
            floor.StretchMode == TextureRect.StretchModeEnum.Tile &&
            floor.Texture is not null &&
            floor.Texture.GetWidth() == PixelSpec.TileGrid * PixelSpec.TileScale &&
            floor.Texture.GetHeight() == PixelSpec.TileGrid * PixelSpec.TileScale,
            $"stretch={floor.StretchMode} size={floor.Texture?.GetWidth()}x{floor.Texture?.GetHeight()}");

        Check("backdrop_floor_is_nearest_filtered",
            floor.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, $"{floor.TextureFilter}");

        // Two haze layers, and the pair is the depth: one alone is weather.
        // They are the tiled-and-Linear layers, since the floor is tiled and
        // Nearest and the gradients are neither.
        var haze = new System.Collections.Generic.List<TextureRect>();
        foreach (var rect in rects)
        {
            if (rect.StretchMode == TextureRect.StretchModeEnum.Tile &&
                rect.TextureFilter == CanvasItem.TextureFilterEnum.Linear)
            {
                haze.Add(rect);
            }
        }
        Check("backdrop_carries_two_haze_layers", haze.Count == 2, $"{haze.Count}");

        // The invariant a drifting layer needs and the one that is silent when
        // missed: a layer that wanders inside its own rect exposes the screen
        // edge on the leg that travels furthest, and it does so seconds after
        // the screen opens rather than on the frame a screenshot is taken.
        foreach (var layer in haze)
        {
            Check($"haze_layer_has_room_to_drift_{haze.IndexOf(layer)}",
                layer.OffsetLeft < 0 && layer.OffsetTop < 0 &&
                layer.OffsetRight > 0 && layer.OffsetBottom > 0,
                $"offsets {layer.OffsetLeft}/{layer.OffsetTop}/{layer.OffsetRight}/{layer.OffsetBottom}");
        }

        // The vignette is last so nothing the backdrop draws sits over it, and
        // every layer stays behind the screen's own _Draw output - MapScreen's
        // connecting Beziers are drawn by the parent Control, so a layer that
        // forgot ShowBehindParent would paint over the map's own edges.
        var behind = 0;
        foreach (var rect in rects) if (rect.ShowBehindParent) behind++;
        Check("every_backdrop_layer_draws_behind_its_screen", behind == rects.Count, $"{behind}/{rects.Count}");

        scratch.QueueFree();
    }

    // The dust motes, which were a CpuParticles2D drawing a smooth 8x8 radial
    // GradientTexture2D at ScaleAmountMin 0.4 / Max 1.1 - an off-ramp gradient
    // (section 5), a soft alpha edge (section 3) and a fractional scale
    // (section 2) in one node. That is the same node type and the same three
    // violations as CombatScreen.SpawnHitSpark, which PIXEL_ART_ROADMAP section
    // 5 retired while calling it "the last smooth-gradient art on the combat
    // screen"; it was not, and it survived for the same two reasons - a
    // CpuParticles2D is not a TextureRect so ScanSourceForSpriteTransforms
    // skips it, and its texture is not a file under assets/ so artgen validate
    // never reads it. This is the check that covers the gap those two leave.
    private void TestDustMotesAreOnTheRampAtAnIntegerScale()
    {
        var image = ScreenBackground.BuildMoteImage();

        var offRamp = 0;
        var softAlpha = 0;
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A is not (0f or 1f)) softAlpha++;
                else if (pixel.A > 0f && !PixelSpec.IsOnRamp(pixel)) offRamp++;
            }
        }
        Check("mote_texture_is_hard_alpha", softAlpha == 0, $"{softAlpha} partially transparent pixels");
        Check("mote_texture_is_on_the_ramp", offRamp == 0, $"{offRamp} off-ramp pixels");

        // Godot samples continuously between min and max, so any min != max is
        // a fractional-scale generator rather than a range of sizes - which is
        // how 0.4/1.1 read as a taste decision for six phases.
        var motes = ScreenBackground.BuildMotes();
        Check("mote_scale_is_a_single_integer",
            Mathf.IsEqualApprox(motes.ScaleAmountMin, motes.ScaleAmountMax) &&
            motes.ScaleAmountMin >= 1f &&
            Mathf.IsEqualApprox(motes.ScaleAmountMin, Mathf.Round(motes.ScaleAmountMin)),
            $"min={motes.ScaleAmountMin} max={motes.ScaleAmountMax}");
        Check("mote_texture_is_nearest_filtered",
            motes.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, $"{motes.TextureFilter}");
        motes.QueueFree();
    }

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
