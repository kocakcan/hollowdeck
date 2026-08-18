using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The backdrop every screen stands on: a tiled dungeon floor (DCSS tiles, see
// CREDITS.md), two drifting haze layers, and a vignette. Done in code rather
// than per-.tscn nodes so every screen stays a one-line change and the look is
// adjusted in one place. Call from _Ready; safe no-op if the tile is missing.
//
// The four entry points below are the whole public surface, and none of them
// takes a tile name or a colour. That is the point of the class rather than a
// convenience: ActDefinition has carried MapBackground/MapTint since acts
// landed and *two* of the thirteen screens read them - the other eleven each
// hardcoded a tile name and a float Color literal, so a shop in the Hollow
// Throne was the same room as a shop in the Sunken Ward. A vocabulary two call
// sites out of thirteen use looks exactly like one nobody needs, which is the
// same failure UiTheme.Motion shipped with for three phases (docs/
// PIXEL_ART_ROADMAP.md section 6). With the arguments gone it cannot recur, and
// PixelSpecSmokeTest.TestNoScreenAuthorsItsOwnBackdrop sweeps the call sites so
// a thirteenth screen cannot quietly reintroduce one.
public static class ScreenBackground
{
    // What a place looks like: the floor, its tint, the colour of the air, and
    // the colour the edges fall off to. Four values because that is what an act
    // authors - see ActDefinition. Kept as one struct so a new surface is a new
    // reader rather than four more parameters at every call site.
    private readonly record struct Atmosphere(string Set, string Floor, Color Tint, Color Haze, Color Edge);

    // The menu look, and deliberately act-blind. MainMenu, Settings, Library
    // and MetaProgression are all reachable with no run in play, and
    // RunState.CurrentAct clamps rather than throwing - so an act-driven menu
    // would silently be act I's rather than nobody's. Four of the five screens
    // it replaces already used this tile; the fifth was black_cobalt.
    private static readonly Atmosphere Menu = new(
        "throne", "throne_inlay", Color.FromString("e0e0e0", Colors.White),
        Color.FromString("b08cd9", Colors.White), Color.FromString("180d24", Colors.Black));

    // ------------------------------------------------------------------
    // Entry points
    // ------------------------------------------------------------------

    public static void AttachMap(Control screen) => Build(screen, MapAir(), combat: false);

    public static void AttachRoom(Control screen) => Build(screen, RoomAir(), combat: false);

    // The extra depth combat gets and nothing else does: a ground plane the
    // combatants read as standing on, dust motes in front of it, and a
    // stronger vignette. Kept apart from the other twelve screens because a
    // shop has no floor to stand on.
    public static void AttachCombat(Control screen) => Build(screen, CombatAir(), combat: true);

    public static void AttachMenu(Control screen) => Build(screen, Menu, combat: false);

    private static Atmosphere MapAir()
    {
        var act = RunState.CurrentAct;
        return new Atmosphere(act.Backdrop, act.MapBackground, Tint(act.MapTint), Tint(act.HazeTint), Edge(act.VignetteTint));
    }

    private static Atmosphere CombatAir()
    {
        var act = RunState.CurrentAct;
        return new Atmosphere(act.Backdrop, act.CombatBackground, Tint(act.CombatTint), Tint(act.HazeTint), Edge(act.VignetteTint));
    }

    private static Atmosphere RoomAir()
    {
        var act = RunState.CurrentAct;
        return new Atmosphere(act.Backdrop, act.RoomBackground, Tint(act.RoomTint), Tint(act.HazeTint), Edge(act.VignetteTint));
    }

    // Color.FromString falls back rather than throwing on a malformed value,
    // which is the tolerance the rest of the data layer has - ActSmokeTest is
    // what turns a typo here into a red suite instead of a silently white act.
    private static Color Tint(string hex) => Color.FromString(hex, Colors.White);

    private static Color Edge(string hex) => Color.FromString(hex, Colors.Black);

    // ------------------------------------------------------------------
    // The layers
    // ------------------------------------------------------------------

    // Vignette strength. Combat is darker than the rest because it is the one
    // screen whose subject is centred and whose corners hold nothing.
    private const float FlatEdgeAlpha = 0.62f;
    private const float CombatEdgeAlpha = 0.8f;

    // Where the wall stops and the floor starts, in design pixels. Combat sits
    // its horizon lower than the other screens because combatants stand on the
    // floor and the fight wants room in front of them; a menu or a map is
    // looking at the room rather than standing in it.
    // One horizon for every screen. It was lower on the map and the room
    // screens until the focal feature landed - a gate is 256 design pixels tall
    // and stands *on* the plinth, so a horizon high enough to look good empty
    // cropped its crown off. A shared horizon also means the three acts are the
    // same room from screen to screen, which is the point of an act having a
    // place at all.
    private const float Horizon = 232f;

    // How far the focal feature's foot sinks behind the plinth. It is drawn
    // before the plinth, so this much of its base is covered - which is what
    // makes it stand *behind* the step rather than on top of the floor.
    private const float FocalFoot = 40f;

    // One plinth tile tall at ART_SPEC section 2's 2x. Derived rather than
    // written as 128, because the two move together and a literal here is the
    // "constant that fits the best case" trap this project has shipped twice.
    private const float PlinthHeight = PixelSpec.TileGrid * PixelSpec.TileScale;

    // The colonnade's rhythm. Wider than a pillar so the wall shows between
    // them - a spacing at or below the pillar width is a second wall.
    private const float PillarSpacing = 320f;
    private const float PillarInset = 32f;

    // The back wall is further from the lamp than the ground in front of it,
    // and this is the cheapest way to say so. The art carries some of it - the
    // wall tile is bodied on `shade` where a floor is bodied on `face` - but
    // one ramp step is not enough separation across a 1152px band, and the
    // alternative is authoring a second, darker ramp family per act.
    //
    // A multiply on a whole band is lighting rather than art, the same category
    // as the vignette and the ground plane, so it is allowed off the ramp for
    // the same reason they are.
    private static readonly Color WallFalloff = new(0.5f, 0.52f, 0.6f);

    private static readonly Color FocalFalloff = new(0.96f, 0.97f, 1f);

    private static void Build(Control screen, Atmosphere air, bool combat)
    {
        var floorTile = BackdropArt(air.Floor);
        if (floorTile is null) return;

        const float horizon = Horizon;

        // A backdrop is a room, not a texture. Four bands rather than one fill:
        // a wall, the colonnade standing in front of it, the plinth where the
        // wall meets the ground, and the floor. The single tiled fill this
        // replaced was wallpaper by construction - a tile repeated 9x5 across
        // the whole canvas has no horizon, so nothing drawn in front of it has
        // a position and every screen in the game read as the same flat sheet
        // in a different colour. That was true of the seven sourced Dungeon
        // Crawl floors and stayed true when they were replaced one-for-one with
        // generated ones, which is the lesson worth keeping: the tiles were
        // never the problem.
        var wall = Tiled(BackdropArt($"{air.Set}_wall") ?? floorTile, air.Tint * WallFalloff);
        var plinth = Tiled(BackdropArt($"{air.Set}_plinth") ?? floorTile, air.Tint);
        var floor = Tiled(floorTile, air.Tint);

        // The colonnade is its own container so the pillars can be positioned
        // absolutely inside a band that is itself anchored - a pillar is the
        // only thing here that is not full-width.
        var colonnade = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        var pillarTile = BackdropArt($"{air.Set}_pillar");
        if (pillarTile is not null)
        {
            for (float x = PillarInset; x < ScreenChrome.DesignWidth; x += PillarSpacing)
            {
                var shaft = Tiled(pillarTile, air.Tint);
                shaft.Position = new Vector2(x, 0);
                shaft.Size = new Vector2(pillarTile.GetWidth(), horizon);
                colonnade.AddChild(shaft);
            }
        }

        // Two haze layers, and the pair is the depth. Nothing in this game has
        // a camera, so parallax has to come from the layers disagreeing with
        // each other rather than from anything moving past them: the far bank
        // is coarser, dimmer, slower and travels less, the near one the
        // reverse. One layer alone is weather; two at different rates is a
        // room with air in it.
        var far = HazeLayer(FarHaze, air.Haze, FarAlpha);
        var near = HazeLayer(NearHaze, air.Haze, NearAlpha);

        // The act's one placed piece, centred behind the action: a drowned
        // gate, a furnace mouth, a throne. Everything else back here is a
        // surface - this is the only thing that is a *subject*, and without it
        // three acts built from the same four bands in three palettes read as
        // one room recoloured rather than as three places.
        var focalArt = BackdropArt($"{air.Set}_focal");
        var focal = focalArt is null ? null : new TextureRect
        {
            Texture = focalArt,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            // Between the wall's falloff and full brightness: it is set into
            // the back wall, so it recedes with it, but it is what the eye is
            // meant to find back there.
            Modulate = air.Tint * FocalFalloff,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };

        var layers = new List<CanvasItem> { wall, colonnade };
        if (focal is not null) layers.Add(focal);
        layers.AddRange(new CanvasItem[] { plinth, floor, far, near });

        if (combat)
        {
            // Dark gradient band anchored to the bottom third - gives
            // combatants something to visually "stand on" instead of floating
            // on an undifferentiated tiled floor.
            layers.Add(new TextureRect
            {
                Texture = BuildGroundPlane(),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear, // smooth gradient, see above
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ShowBehindParent = true,
            });

            // Skipped entirely under Settings > Reduce Motion, unlike the haze
            // drift below - a mote crossing the screen is discrete motion the
            // eye tracks, where a fog bank at a 60-second period is closer to
            // lighting. Same split SpriteAnimator draws between the hit flash
            // and the idle breathe.
            if (!SettingsManager.Instance.ReduceMotion) layers.Add(BuildMotes());
        }

        layers.Add(new TextureRect
        {
            Texture = BuildVignette(air.Edge, combat ? CombatEdgeAlpha : FlatEdgeAlpha),
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, // smooth gradient, see above
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        });

        // Insert at the top of the tree so everything else draws over them.
        // Built as one ordered list rather than a run of MoveChild literals
        // because the motes are conditional: the old code hand-wrote indices
        // 0..3 and then had AddDustMotes insert itself at 3 afterwards, so the
        // vignette's real index depended on a setting.
        for (int i = 0; i < layers.Count; i++)
        {
            screen.AddChild(layers[i]);
            screen.MoveChild(layers[i], i);
            if (layers[i] is Control control) control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }

        // The bands, after the full-rect preset above has given each one the
        // whole canvas to cut down from.
        SpanFrom(wall, 0f, horizon);
        SpanFrom(colonnade, 0f, horizon);
        if (focal is not null && focalArt is not null)
        {
            float width = focalArt.GetWidth();
            float height = focalArt.GetHeight();
            focal.AnchorLeft = 0f;
            focal.AnchorRight = 0f;
            focal.AnchorTop = 0f;
            focal.AnchorBottom = 0f;
            focal.OffsetLeft = (ScreenChrome.DesignWidth - width) / 2f;
            focal.OffsetRight = focal.OffsetLeft + width;
            focal.OffsetBottom = horizon + FocalFoot;
            focal.OffsetTop = focal.OffsetBottom - height;
        }

        SpanFrom(plinth, horizon, horizon + PlinthHeight);
        floor.OffsetTop = horizon + PlinthHeight;

        // Amplitudes and periods, in that order: further is slower and moves
        // less, which is the whole of the parallax. The three legs of each
        // wander are prime-ish to each other and the two loops are 59s against
        // 33s, so neither lands back on a beat the eye can count.
        Drift(far, FarMargin, new Vector2(9, 6), new Vector2(-7, 4), 19f, 23f, 17f);
        Drift(near, NearMargin, new Vector2(24, 14), new Vector2(-18, 10), 9f, 11f, 13f);
    }

    // Full width, a fixed slice of the height. Anchored top on both edges
    // rather than stretched, so a band keeps its design-pixel height - the
    // canvas is letterboxed to 1152x648 at every window size (ART_SPEC section
    // 4), which is what makes an offset in design pixels right by construction.
    private static void SpanFrom(Control band, float top, float bottom)
    {
        band.AnchorTop = 0f;
        band.AnchorBottom = 0f;
        band.OffsetTop = top;
        band.OffsetBottom = bottom;
    }

    private static TextureRect Tiled(Texture2D texture, Color tint) => new()
    {
        Texture = texture,
        StretchMode = TextureRect.StretchModeEnum.Tile,
        TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        Modulate = tint,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        ShowBehindParent = true,
    };

    // Oversize + offset a haze layer relative to full-rect so it has room to
    // drift without exposing an edge, then let it wander. Ungated on Reduce
    // Motion on purpose: the rule this project settled on is to gate the
    // photosensitive flash and not the gentle ambient loop, and gating the
    // idle breathe was reported from a playthrough as "sprites don't animate".
    private static void Drift(TextureRect layer, float margin, Vector2 a, Vector2 b, float p1, float p2, float p3)
    {
        layer.OffsetLeft -= margin;
        layer.OffsetTop -= margin;
        layer.OffsetRight += margin;
        layer.OffsetBottom += margin;
        var home = layer.Position;
        // layer.CreateTween() (not GetTree().CreateTween()) so this infinite,
        // long-lived loop auto-kills when the layer leaves the tree - unlike
        // the codebase's other short one-shot tweens, this one is virtually
        // guaranteed to still be running whenever the screen is torn down.
        var tween = layer.CreateTween();
        tween.SetLoops();
        tween.TweenTo(layer, "position", home + a, Motion.Drift.Over(p1));
        tween.TweenTo(layer, "position", home + b, Motion.Drift.Over(p2));
        tween.TweenTo(layer, "position", home, Motion.Drift.Over(p3));
    }

    private const float FarMargin = 20f;
    private const float NearMargin = 40f;
    private const float FarAlpha = 0.10f;
    private const float NearAlpha = 0.16f;

    private static TextureRect HazeLayer(Texture2D noise, Color tint, float alpha) => new()
    {
        Texture = noise,
        StretchMode = TextureRect.StretchModeEnum.Tile,
        TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
        Modulate = new Color(tint.R, tint.G, tint.B, alpha),
        TextureFilter = CanvasItem.TextureFilterEnum.Linear, // smooth noise, see above
        MouseFilter = Control.MouseFilterEnum.Ignore,
        ShowBehindParent = true,
    };

    // ------------------------------------------------------------------
    // Textures
    // ------------------------------------------------------------------

    // The tile, upscaled to ART_SPEC section 2's factor. StretchMode.Tile
    // repeats a texture at its *native* size, so tiling the 64x64 file
    // directly renders at 1x - which is what this did for six phases while
    // PixelSpec.TileScale sat at 2 with no readers and the spec table claimed
    // 128x128. Resizing once here is what makes the constant mean something;
    // scaling the TextureRect instead would be a fractional-scale hazard and a
    // transform on a pixel holder, which section 9's guard forbids outright.
    //
    // Cached because thirteen screens ask for one of seven tiles and a screen
    // change is not a place to spend an image resize.
    private static readonly Dictionary<string, Texture2D> TileCache = new();

    private static Texture2D? BackdropArt(string name)
    {
        if (TileCache.TryGetValue(name, out var cached)) return cached;

        var source = ArtAssets.BackgroundTile(name);
        if (source is null) return null;

        var image = source.GetImage();
        image.Resize(
            image.GetWidth() * PixelSpec.TileScale,
            image.GetHeight() * PixelSpec.TileScale,
            Image.Interpolation.Nearest);
        var scaled = ImageTexture.CreateFromImage(image);
        TileCache[name] = scaled;
        return scaled;
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

    // Slow-drifting motes for ambient depth. Public so PixelSpecSmokeTest can
    // drive the real producer rather than assert against a copy of its
    // numbers - the CombatFx.All argument, one asset class over.
    //
    // What this replaced is the reason it is worth a comment: an 8x8 radial
    // GradientTexture2D emitted at ScaleAmountMin 0.4 / Max 1.1. That is a
    // smooth off-ramp gradient (section 5), a soft alpha edge (section 3) and
    // a fractional scale (section 2) in one node - the same three violations,
    // in the same node type, on the same screen as CombatScreen.SpawnHitSpark,
    // which docs/PIXEL_ART_ROADMAP.md section 5 retired while calling it "the
    // last smooth-gradient art on the combat screen". It was not, and it was
    // invisible for exactly the same two reasons: a CpuParticles2D is not a
    // TextureRect, so the transform scan skips it, and its texture is not a
    // file under assets/, so artgen validate never reads it.
    //
    // Min and max scale are equal because Godot samples continuously between
    // them: any min != max is a fractional-scale generator, not a range of
    // sizes. Variety moved to velocity and lifetime, which resample nothing.
    public static CpuParticles2D BuildMotes() => new()
    {
        Position = new Vector2(576, 300),
        Emitting = true,
        Amount = 22,
        Lifetime = 9.0,
        Texture = BuildMoteTexture(),
        EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
        EmissionRectExtents = new Vector2(560, 300),
        Direction = Vector2.Up,
        Spread = 25f,
        InitialVelocityMin = 3f,
        InitialVelocityMax = 10f,
        ScaleAmountMin = MoteScale,
        ScaleAmountMax = MoteScale,
        // RGB is identity so the mote renders as exactly the ramp entry its
        // texture is painted in; the alpha is what makes it ambient.
        Color = new Color(1f, 1f, 1f, 0.22f),
        Gravity = Vector2.Zero,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
    };

    public const int MoteScale = 2;

    // A mote is two source pixels square of one ramp entry, fully opaque -
    // pixel art rather than a gradient, so it survives Nearest and satisfies
    // section 3's hard-alpha rule. N8 is the ramp's brightest neutral, which is
    // what a dust speck catching the light is.
    public static Image BuildMoteImage()
    {
        var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        image.Fill(PixelSpec.Ramp.N8);
        return image;
    }

    private static Texture2D BuildMoteTexture() => ImageTexture.CreateFromImage(BuildMoteImage());

    // Radial falloff to the act's own edge colour. Black until acts authored a
    // VignetteTint: darkness is the cheapest atmosphere there is, and a
    // corner falling off to ember rather than to black is most of what makes
    // the Ember Reach read as somewhere else.
    private static Texture2D BuildVignette(Color edge, float edgeAlpha)
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0.55f, 1f },
            Colors = new Color[] { new(edge.R, edge.G, edge.B, 0f), new(edge.R, edge.G, edge.B, edgeAlpha) },
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

    // Procedural, seamlessly-tiling cloud noise - avoids sourcing and
    // attributing an external texture for something this simple, and
    // NoiseTexture2D's seamless mode guarantees no tile seams. Built once
    // rather than per screen: only the tint varies by act, and a screen change
    // is not a place to spend two noise generations.
    private static readonly Texture2D FarHaze = BuildHazeNoise(0.006f, 2, 512);
    private static readonly Texture2D NearHaze = BuildHazeNoise(0.02f, 3, 256);

    private static Texture2D BuildHazeNoise(float frequency, int octaves, int size)
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = octaves,
            Frequency = frequency,
        };
        return new NoiseTexture2D
        {
            Noise = noise,
            Seamless = true,
            Width = size,
            Height = size,
            AsNormalMap = false,
        };
    }
}
