//! The floors the game is played on: nine seamless 64x64 tiles, three per act.
//!
//! This is the third category that breaks the "one 32x32 file per definition
//! id under `assets/icons/`" shape, and it breaks both halves at once — its own
//! grid (§1's 64x64 tile row), its own output directory (`assets/backgrounds/`,
//! which `validate::expected_grid` already had an arm for), and a name that is
//! an act's surface rather than a content id. So it costs the two
//! infrastructure lines `main::output_dir`'s docstring names: the arm there,
//! and the path list in CI's "Generated art is up to date".
//!
//! **What it replaced was sourced art, and that is the point of the change.**
//! `assets/backgrounds/` held seven CC0 Dungeon Crawl dungeon floors
//! (`crypt0`, `black_cobalt01`, `dirt0`, …), palette-clamped and tinted per
//! act. Clamping put them on the ramp and tinting made them warmer or cooler,
//! but neither could make them *this game*: they are photographic-ish rubble
//! textures authored for a top-down roguelike at 32x32, and against a card
//! game's chrome they read as generic wallpaper. Every other pixel in the
//! project is composed from a small shape vocabulary under one lamp; a floor
//! that is not is the largest surface on screen disagreeing with all of it.
//!
//! Three rules hold the set together, and each one is a thing `artgen validate`
//! cannot see:
//!
//! 1. **Seamless.** Every write goes through `put`, which wraps both axes mod
//!    64, and every course/block subdivision below sums to exactly 64. A tile
//!    is drawn 5x9 times on a 1152x648 canvas, so a one-pixel seam is a visible
//!    grid across the whole screen.
//! 2. **Opaque.** A backdrop has nothing behind it. `Canvas::new` starts
//!    transparent, so each pattern fills first; a hole would show the clear
//!    colour through the floor.
//! 3. **One lamp.** §10's light is up and to the left at 45°, and these obey it
//!    through `light::is_lit` rather than by hand: a *raised* block is lit on
//!    the faces whose outward normal turns back against the light and shaded on
//!    the others, and a *carved* groove is the same rule with the normals
//!    reversed, which is why `vault`'s inlay is bright on the side a block is
//!    dark on. Authoring either as a literal is how `blade` ended up lighting a
//!    different screen side at every angle.
//!
//! Value range is deliberately narrow and low. This is the surface every card,
//! label and panel in the game sits on top of, so the contrast budget belongs
//! to them: the three tones of a stone are adjacent ramp entries, and the
//! accent — the one thing that carries an act's identity — is sparse rather
//! than bright.

use super::light;
use super::*;
use crate::canvas::Canvas;

/// §1's background grid. Not `shapes::GRID`, which is the 32x32 icon one.
pub const TILE: i32 = 64;

pub fn icons() -> Vec<Icon> {
    vec![
        // Act I - The Sunken Ward. Cold wet stone, verdigris in the joints.
        Icon { category: "backgrounds", name: "ward_flags", draw: ward_flags },
        Icon { category: "backgrounds", name: "ward_drowned", draw: ward_drowned },
        Icon { category: "backgrounds", name: "ward_cistern", draw: ward_cistern },
        Icon { category: "backgrounds", name: "ward_wall", draw: ward_wall },
        Icon { category: "backgrounds", name: "ward_plinth", draw: ward_plinth },
        Icon { category: "backgrounds", name: "ward_pillar", draw: ward_pillar },
        // Act II - The Ember Reach. Scorched brick, heat still in the cracks.
        Icon { category: "backgrounds", name: "reach_cinders", draw: reach_cinders },
        Icon { category: "backgrounds", name: "reach_scorch", draw: reach_scorch },
        Icon { category: "backgrounds", name: "reach_forge", draw: reach_forge },
        Icon { category: "backgrounds", name: "reach_wall", draw: reach_wall },
        Icon { category: "backgrounds", name: "reach_plinth", draw: reach_plinth },
        Icon { category: "backgrounds", name: "reach_pillar", draw: reach_pillar },
        // Act III - The Hollow Throne. Black stone, gilt worked into it.
        Icon { category: "backgrounds", name: "throne_inlay", draw: throne_inlay },
        Icon { category: "backgrounds", name: "throne_obsidian", draw: throne_obsidian },
        Icon { category: "backgrounds", name: "throne_reliquary", draw: throne_reliquary },
        Icon { category: "backgrounds", name: "throne_wall", draw: throne_wall },
        Icon { category: "backgrounds", name: "throne_plinth", draw: throne_plinth },
        Icon { category: "backgrounds", name: "throne_pillar", draw: throne_pillar },
    ]
}

/// Which band of the room a tile is for. The floors are the three patterns
/// below; the other three are what turn a tiled floor into a place.
///
/// **This distinction is the whole second draft of this file.** The first one
/// generated three good floor tiles per act and `ScreenBackground` filled the
/// whole 1152x648 canvas with one of them. That is wallpaper by construction -
/// no amount of detail in a tile survives being repeated 9x5 with nothing else
/// in the frame, because what makes a backdrop read as somewhere is
/// *composition*: a horizon, a wall behind it, and something standing up in it.
/// The sourced Dungeon Crawl floors were the same shape of wrong, which is why
/// replacing them one-for-one changed so little.
pub enum Band {
    Floor,
    Wall,
    Plinth,
    Pillar,
}

/// The five tones a floor is built from. Five rather than three because a
/// stone needs a lit face, a body and a shaded face to have form at all, and
/// the other two are the joint between stones and the one colour that says
/// which act this is.
///
/// `accent` is the only entry allowed off the stone's own hue family, and it is
/// the whole per-act identity: verdigris in a drowned ward, live ember in a
/// scorched one, gilt in a throne room. Kept sparse on purpose - a floor whose
/// accent reads at a glance is a floor competing with the cards on top of it.
#[derive(Clone, Copy)]
struct Stone {
    joint: Rgb,
    shade: Rgb,
    face: Rgb,
    lit: Rgb,
    /// Two ramp steps above `face`, and used by exactly two things: the top
    /// surface of the plinth and the lit band of a pillar shaft. Both are
    /// surfaces the 45-degree lamp hits close to square, and both are the
    /// reason a backdrop reads as a room - the horizon and the colonnade have
    /// to separate from the masonry behind them or they are texture again.
    /// Everything else in the set stays inside `shade`/`face`/`lit`, which is
    /// the contrast budget a surface with cards on top of it gets.
    sheen: Rgb,
    accent: Rgb,
}

const WARD: Stone = Stone { joint: N1, shade: B0, face: B1, lit: B2, sheen: B3, accent: V2 };
const REACH: Stone = Stone { joint: N1, shade: E0, face: E1, lit: E2, sheen: E3, accent: R3 };
const THRONE: Stone = Stone { joint: N0, shade: P0, face: P1, lit: P2, sheen: P3, accent: G2 };

// Each act gets all three patterns rather than one pattern in three colours,
// so act II's map is not act I's map repainted. The rotation is what spreads
// them: pattern index (act + surface) % 3.
fn ward_flags() -> Canvas { flagstone(WARD, 0x51f3) }
fn ward_drowned() -> Canvas { slab(WARD, 0x8c17) }
fn ward_cistern() -> Canvas { vault(WARD, 0x2ba9) }
fn ward_wall() -> Canvas { wall(WARD, 0x33a1) }
fn ward_plinth() -> Canvas { plinth(WARD, 0x6d02) }
fn ward_pillar() -> Canvas { pillar(WARD, 0x91be) }

fn reach_cinders() -> Canvas { slab(REACH, 0x7d41) }
fn reach_scorch() -> Canvas { vault(REACH, 0x1e6b) }
fn reach_forge() -> Canvas { flagstone(REACH, 0x9a25) }
fn reach_wall() -> Canvas { wall(REACH, 0x4c88) }
fn reach_plinth() -> Canvas { plinth(REACH, 0xa317) }
fn reach_pillar() -> Canvas { pillar(REACH, 0x2f5d) }

fn throne_inlay() -> Canvas { vault(THRONE, 0x46d7) }
fn throne_obsidian() -> Canvas { flagstone(THRONE, 0xb38f) }
fn throne_reliquary() -> Canvas { slab(THRONE, 0x0fc3) }
fn throne_wall() -> Canvas { wall(THRONE, 0x7e14) }
fn throne_plinth() -> Canvas { plinth(THRONE, 0xc50a) }
fn throne_pillar() -> Canvas { pillar(THRONE, 0x18f6) }

/// Which band each tile belongs to, for the tests below - they hold different
/// rules, and a pillar is the only piece that is allowed to be see-through.
fn band_of(name: &str) -> Band {
    if name.ends_with("_wall") {
        Band::Wall
    } else if name.ends_with("_plinth") {
        Band::Plinth
    } else if name.ends_with("_pillar") {
        Band::Pillar
    } else {
        Band::Floor
    }
}

// ---------------------------------------------------------------------------
// Wrapping, and the deterministic jitter
// ---------------------------------------------------------------------------

/// Every write in this file goes through here. `Canvas::set` drops
/// out-of-bounds writes silently, which is right for a 32x32 icon drawing a
/// shape that overhangs its grid and exactly wrong for a tile: a block that
/// straddles the edge has to come back on the other side or the tile does not
/// meet itself.
fn put(canvas: &mut Canvas, x: i32, y: i32, colour: Rgb) {
    canvas.set(x.rem_euclid(TILE), y.rem_euclid(TILE), colour);
}

fn hspan(canvas: &mut Canvas, x: i32, y: i32, len: i32, colour: Rgb) {
    for i in 0..len {
        put(canvas, x + i, y, colour);
    }
}

fn vspan(canvas: &mut Canvas, x: i32, y: i32, len: i32, colour: Rgb) {
    for i in 0..len {
        put(canvas, x, y + i, colour);
    }
}

/// Deterministic by construction: an xorshift seeded from a constant per tile,
/// never from the clock or from address order. Generated art is committed and
/// CI regenerates it to diff against what is in the tree, so a tile that came
/// out differently on two machines would fail that step rather than merely look
/// different.
struct Jitter(u32);

impl Jitter {
    fn next(&mut self) -> u32 {
        let mut x = self.0;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        self.0 = x;
        x
    }

    fn below(&mut self, n: u32) -> u32 {
        self.next() % n
    }

    /// True `numerator` times in `denominator`, for sparse decoration.
    fn chance(&mut self, numerator: u32, denominator: u32) -> bool {
        self.below(denominator) < numerator
    }
}

// ---------------------------------------------------------------------------
// The light
// ---------------------------------------------------------------------------

/// Which tone an axis-aligned face takes, asked of §10's lamp rather than
/// answered here. `raised` flips the normals: a groove cut *into* the floor
/// presents its far wall to the light, so the bright edge of an engraving is
/// on the side the bright edge of a block is not. Getting that backwards is
/// what makes carved detail read as embossed, and it is invisible in a single
/// tile - it only shows once a raised block and a cut groove sit side by side,
/// which is exactly what `vault` does.
fn face_tone(normal: (f32, f32), raised: bool, stone: Stone) -> Rgb {
    let normal = if raised { normal } else { (-normal.0, -normal.1) };
    if light::is_lit(normal) {
        stone.lit
    } else {
        stone.shade
    }
}

const TOP: (f32, f32) = (0.0, -1.0);
const BOTTOM: (f32, f32) = (0.0, 1.0);
const LEFT: (f32, f32) = (-1.0, 0.0);
const RIGHT: (f32, f32) = (1.0, 0.0);

/// One block: body, then the four faces under the lamp. Drawn with `put`
/// throughout, so a block whose x or y runs past 64 simply continues on the
/// other side of the tile - which is what lets the course tables below start
/// at an offset without special-casing the block that straddles the seam.
fn block(canvas: &mut Canvas, x: i32, y: i32, w: i32, h: i32, body: Rgb, stone: Stone, raised: bool) {
    for dy in 0..h {
        hspan(canvas, x, y + dy, w, body);
    }
    hspan(canvas, x, y, w, face_tone(TOP, raised, stone));
    hspan(canvas, x, y + h - 1, w, face_tone(BOTTOM, raised, stone));
    vspan(canvas, x, y, h, face_tone(LEFT, raised, stone));
    vspan(canvas, x + w - 1, y, h, face_tone(RIGHT, raised, stone));
}

// ---------------------------------------------------------------------------
// The three patterns
// ---------------------------------------------------------------------------

/// Course heights and the block widths within them. Every row sums to 64 on
/// both axes, which is the whole seamlessness argument: no modulo cleverness
/// afterwards can rescue a subdivision that does not close.
///
/// Deliberately irregular. A 16x16 grid tiles just as seamlessly and reads as
/// graph paper - the eye finds the repeat immediately, which is precisely the
/// complaint the sourced tiles earned.
const COURSES: [(i32, [i32; 3]); 4] = [
    (13, [21, 25, 18]),
    (17, [15, 27, 22]),
    (14, [24, 19, 21]),
    (20, [28, 16, 20]),
];

/// Mortared flagstone. Raised blocks in running bond, a 1px joint between
/// them, per-stone value jitter so no two neighbours are the same tone, and
/// the act's accent creeping out of the joints.
fn flagstone(stone: Stone, seed: u32) -> Canvas {
    let mut canvas = Canvas::new(TILE as u32, TILE as u32);
    let mut rng = Jitter(seed);

    // Joint colour first, so every gap the courses leave is already filled and
    // no pixel can be left transparent (rule 2 in the header).
    for y in 0..TILE {
        hspan(&mut canvas, 0, y, TILE, stone.joint);
    }

    let mut y = 0;
    for (course, (height, widths)) in COURSES.iter().enumerate() {
        // Running bond: each course starts a different distance in, so the
        // vertical joints never line up into a column across the tile.
        let mut x = (course as i32 * 23) % TILE;
        for width in widths.iter() {
            // Body tone jitter, one ramp step either side of the face colour -
            // weighted hard toward the face. An even three-way split reads as a
            // patchwork rather than as stone, because the ramp's steps are
            // sized for a 32px icon that has to carry form at 1x, and a floor
            // is asking them to do the opposite job.
            let body = match rng.below(12) {
                0 => stone.lit,
                1 | 2 => stone.shade,
                _ => stone.face,
            };
            block(&mut canvas, x, y, width - 1, height - 1, body, stone, true);

            // Wear: a few pixels knocked off an edge, always on the shaded
            // faces, because that is where a chip catches no light.
            if rng.chance(1, 2) {
                let bite = 1 + rng.below(3) as i32;
                for i in 0..bite {
                    put(&mut canvas, x + width - 2 - i, y + height - 2, stone.joint);
                }
            }

            // The act's colour, in the joint rather than on the stone: damp in
            // the ward, embers in the reach, gilt in the throne.
            if rng.chance(1, 3) {
                let at = rng.below((*height as u32).max(1)) as i32;
                put(&mut canvas, x - 1, y + at, stone.accent);
            }

            x += width;
        }
        y += height;
    }

    canvas
}

/// Big cut slabs, two to a course, with cracks running through them. Fewer,
/// larger stones than `flagstone`, so it reads as a floor laid by someone with
/// a budget - and the cracks are what keep it from reading as a grid.
fn slab(stone: Stone, seed: u32) -> Canvas {
    let mut canvas = Canvas::new(TILE as u32, TILE as u32);
    let mut rng = Jitter(seed);

    for y in 0..TILE {
        hspan(&mut canvas, 0, y, TILE, stone.joint);
    }

    const SLABS: [(i32, [i32; 2]); 2] = [(29, [37, 27]), (35, [25, 39])];
    let mut y = 0;
    for (course, (height, widths)) in SLABS.iter().enumerate() {
        let mut x = (course as i32 * 31) % TILE;
        for width in widths.iter() {
            let body = if rng.chance(1, 5) { stone.shade } else { stone.face };
            block(&mut canvas, x, y, width - 1, height - 1, body, stone, true);

            // A crack, on some slabs and not others. Every slab cracked is a
            // corrugation - four slabs to a tile means the eye reads one crack
            // per cell as the pattern rather than as damage.
            if rng.chance(2, 3) {
                let mut cx = x + 3 + rng.below((*width as u32).saturating_sub(6).max(1)) as i32;
                let mut cy = y + 2;
                for _ in 0..(height - 4) {
                    put(&mut canvas, cx, cy, stone.joint);
                    // The lower-right lip of the crack catches no light, which
                    // is the same lamp the block edges answer to.
                    put(&mut canvas, cx + 1, cy, stone.shade);
                    cy += 1;
                    match rng.below(3) {
                        0 => cx -= 1,
                        1 => cx += 1,
                        _ => {}
                    }
                    cx = cx.clamp(x + 1, x + width - 3);
                }
            }

            // Sparse mineral glint, on the lit faces only.
            for _ in 0..2 {
                if rng.chance(1, 2) {
                    let gx = x + 2 + rng.below((*width as u32).saturating_sub(4).max(1)) as i32;
                    let gy = y + 2 + rng.below((*height as u32).saturating_sub(4).max(1)) as i32;
                    put(&mut canvas, gx, gy, stone.accent);
                }
            }

            x += width;
        }
        y += height;
    }

    canvas
}

/// Dressed stone with a cut inlay - the one pattern that carries a *mark*
/// rather than only a texture, and the reason this set reads as belonging to
/// this game rather than to any dungeon. Four 32x32 cells, each holding a
/// diamond cut into the floor with the act's accent worked into it: the same
/// diamond the map's node ring and the card frames' corner brackets are built
/// from.
///
/// The inlay is `raised: false` throughout, which is where `face_tone`'s
/// inversion earns itself - a cut groove takes light on the face a raised block
/// keeps in shadow, and the two sit 2px apart here.
fn vault(stone: Stone, seed: u32) -> Canvas {
    let mut canvas = Canvas::new(TILE as u32, TILE as u32);
    let mut rng = Jitter(seed);

    for y in 0..TILE {
        hspan(&mut canvas, 0, y, TILE, stone.joint);
    }

    const CELL: i32 = 32;

    // Which of the four cells carry the mark. Never all four: a 2x2 of
    // identical cells is a 64px repeat, and at ART_SPEC section 2's 2x that
    // puts an identical diamond every 64 screen pixels across 1152 of them -
    // which is the "solitaire backdrop" failure the sourced tiles had, rebuilt
    // out of the game's own vocabulary. Two marked cells and two plain ones
    // give the pattern a 128px period and somewhere for the eye to rest.
    let first = rng.below(4) as usize;
    let second = (first + 1 + rng.below(3) as usize) % 4;

    for cell in 0..4usize {
        let ox = (cell as i32 % 2) * CELL;
        let oy = (cell as i32 / 2) * CELL;

        // The dressed stone the mark is cut into, and the one place in this
        // file a body tone is *not* jittered. The cut's two walls are one ramp
        // step either side of `face`, so a cell jittered down to `shade`
        // swallowed its own shaded wall and the diamond rendered as a bare V -
        // visible only on the one cell in four that rolled dark, which is
        // exactly the kind of thing a single-tile look does not catch.
        block(&mut canvas, ox, oy, CELL - 1, CELL - 1, stone.face, stone, true);

        if cell == first || cell == second {
            // The diamond, as four cut edges. Each side of a diamond faces a
            // corner, so the normals are the diagonals - which is what makes
            // this the one shape in the set where the lamp's 45 degrees is not
            // a tiebreak between two faces but the answer for all four.
            let cx = ox + CELL / 2 - 1;
            let cy = oy + CELL / 2 - 1;
            let radius = 7 + rng.below(3) as i32;
            for step in 0..radius {
                let far = radius - step;
                put(&mut canvas, cx - step, cy - far, face_tone(TOP, false, stone));
                put(&mut canvas, cx + step, cy - far, face_tone(TOP, false, stone));
                put(&mut canvas, cx - step, cy + far, face_tone(BOTTOM, false, stone));
                put(&mut canvas, cx + step, cy + far, face_tone(BOTTOM, false, stone));
            }

            // The accent sits in the deepest part of the cut - a bead at the
            // centre, not a fill, so the mark reads at 2x without the floor
            // shouting.
            put(&mut canvas, cx, cy, stone.accent);
            put(&mut canvas, cx - 1, cy, stone.accent);
            put(&mut canvas, cx + 1, cy, stone.accent);
            put(&mut canvas, cx, cy - 1, stone.accent);
            put(&mut canvas, cx, cy + 1, stone.accent);
        }

        // Tooling marks on the dressed face, so the flat area between the mark
        // and the block edge is not dead.
        for _ in 0..5 {
            let tx = ox + 2 + rng.below((CELL as u32) - 4) as i32;
            let ty = oy + 2 + rng.below((CELL as u32) - 4) as i32;
            put(&mut canvas, tx, ty, stone.shade);
        }
    }

    canvas
}

// ---------------------------------------------------------------------------
// The room: wall, plinth, pillar
// ---------------------------------------------------------------------------

/// The back wall. Coarser and darker than any floor - two big ashlar courses
/// rather than a floor's four, and bodied on `shade` instead of `face`, because
/// a wall is further from the lamp than the ground in front of it and because
/// everything the player actually reads is drawn against this band.
///
/// Seamless on both axes: the wall is tiled horizontally across 1152px *and*
/// vertically down whatever the horizon leaves, so unlike a floor it cannot get
/// away with only closing in x.
fn wall(stone: Stone, seed: u32) -> Canvas {
    let mut canvas = Canvas::new(TILE as u32, TILE as u32);
    let mut rng = Jitter(seed);

    for y in 0..TILE {
        hspan(&mut canvas, 0, y, TILE, stone.joint);
    }

    const COURSES: [(i32, [i32; 2]); 2] = [(31, [35, 29]), (33, [27, 37])];
    let mut y = 0;
    for (course, (height, widths)) in COURSES.iter().enumerate() {
        let mut x = (course as i32 * 19) % TILE;
        for width in widths.iter() {
            let body = if rng.chance(1, 4) { stone.face } else { stone.shade };
            block(&mut canvas, x, y, width - 1, height - 1, body, stone, true);

            // Damp streaks running down the wall, in the joint colour. Vertical
            // rather than scattered because gravity is the one direction a wall
            // has, and it is what stops the ashlar reading as a brick swatch.
            if rng.chance(1, 2) {
                let sx = x + 2 + rng.below((*width as u32).saturating_sub(4).max(1)) as i32;
                let run = 4 + rng.below(*height as u32) as i32;
                for i in 0..run {
                    put(&mut canvas, sx, y + 1 + i, stone.joint);
                }
            }

            x += width;
        }
        y += height;
    }

    canvas
}

/// The plinth: the course where the wall stops and the floor starts, and the
/// single most valuable 64 pixels in the set. A backdrop with no horizon is a
/// texture; a backdrop with one is a room, and everything drawn in front of it
/// acquires a position rather than floating.
///
/// Read top to bottom: a shadowed reveal under the wall, the lit top surface of
/// the step (the one surface in the whole set the lamp hits square on), the
/// step's shaded riser, and a base fillet. Only the horizontal direction has to
/// tile - this is drawn as a single row.
fn plinth(stone: Stone, seed: u32) -> Canvas {
    let mut canvas = Canvas::new(TILE as u32, TILE as u32);
    let mut rng = Jitter(seed);

    // The reveal: the wall's own shadow falling onto the top of the step.
    for y in 0..10 {
        hspan(&mut canvas, 0, y, TILE, stone.joint);
    }
    // The step's top face. A horizontal surface under a 45-degree lamp is the
    // brightest thing a floor tile is allowed to be, and it is what draws the
    // eye to the horizon rather than to the repeat.
    for y in 10..14 {
        hspan(&mut canvas, 0, y, TILE, stone.sheen);
    }
    for y in 14..20 {
        hspan(&mut canvas, 0, y, TILE, stone.lit);
    }
    hspan(&mut canvas, 0, 20, TILE, stone.face);
    // The riser, in shadow, with a moulding groove cut across it.
    for y in 21..52 {
        hspan(&mut canvas, 0, y, TILE, stone.shade);
    }
    hspan(&mut canvas, 0, 30, TILE, stone.joint);
    hspan(&mut canvas, 0, 31, TILE, face_tone(BOTTOM, false, stone));
    // The base fillet where the riser meets the floor.
    for y in 52..TILE {
        hspan(&mut canvas, 0, y, TILE, stone.face);
    }
    hspan(&mut canvas, 0, 52, TILE, stone.lit);
    hspan(&mut canvas, 0, 53, TILE, stone.face);

    // Vertical joints between the plinth's own blocks, so the band is masonry
    // rather than a painted stripe.
    let mut x = rng.below(TILE as u32) as i32;
    for _ in 0..3 {
        vspan(&mut canvas, x, 10, TILE - 10, stone.joint);
        vspan(&mut canvas, x + 1, 10, TILE - 10, face_tone(RIGHT, true, stone));
        x += 17 + rng.below(9) as i32;
    }

    // The act's colour, pooled in the reveal where the wall meets the step.
    for _ in 0..3 {
        if rng.chance(1, 2) {
            put(&mut canvas, rng.below(TILE as u32) as i32, 9, stone.accent);
        }
    }

    canvas
}

/// A pillar segment, tiled vertically to whatever height the wall band is.
///
/// **The one piece with transparent pixels, and that is what it is for**: the
/// flanks read through to the wall behind, so a colonnade is a rhythm laid over
/// the masonry rather than a second wall with columns painted on it. `validate`
/// is fine with it - ART_SPEC section 3 forbids *partial* alpha, and these are
/// 0 or 255 like everything else.
///
/// Lit down the left third and shaded down the right, because a cylinder under
/// a 45-degree lamp is the one shape in the set whose shading is a gradient
/// across its width rather than an edge - three bands is as close as the ramp
/// gets without dithering.
fn pillar(stone: Stone, seed: u32) -> Canvas {
    let mut canvas = Canvas::new(TILE as u32, TILE as u32);
    let mut rng = Jitter(seed);

    const LEFT_EDGE: i32 = 8;
    const RIGHT_EDGE: i32 = 56;

    for y in 0..TILE {
        // The turn of the cylinder, left to right: the lamp is up and to the
        // left, so the bright band sits left of centre and the terminator falls
        // well right of it.
        for x in LEFT_EDGE..RIGHT_EDGE {
            let tone = match x - LEFT_EDGE {
                0..=1 => stone.joint,
                2..=6 => stone.face,
                7..=17 => stone.sheen,
                18..=30 => stone.face,
                31..=43 => stone.shade,
                _ => stone.joint,
            };
            put(&mut canvas, x, y, tone);
        }
    }

    // The shadow the shaft throws onto the wall behind it. Opaque, inside the
    // tile, on the flank away from the lamp - a column with no cast shadow
    // reads as a stripe painted on the masonry rather than as something
    // standing in front of it, which is exactly how the first draft rendered.
    for y in 0..TILE {
        for x in RIGHT_EDGE..(RIGHT_EDGE + 5) {
            put(&mut canvas, x, y, stone.joint);
        }
    }

    // Drum joints: the horizontal seams between the stones a column is stacked
    // from. Placed at a divisor of 64 so they stay evenly spaced when the
    // segment repeats up the wall - an irregular rhythm here would beat against
    // the tile height and read as a mistake rather than as masonry.
    for row in [0, 32] {
        hspan(&mut canvas, LEFT_EDGE, row, RIGHT_EDGE - LEFT_EDGE, stone.joint);
        hspan(&mut canvas, LEFT_EDGE, row + 1, RIGHT_EDGE - LEFT_EDGE, face_tone(TOP, true, stone));
    }

    // Wear and the act's colour, down the shaded flank only.
    for _ in 0..6 {
        if rng.chance(1, 3) {
            let x = RIGHT_EDGE - 4 - rng.below(8) as i32;
            put(&mut canvas, x, rng.below(TILE as u32) as i32, stone.accent);
        }
    }

    canvas
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::palette::is_on_ramp;

    /// Every tile, so a rule holds for the set rather than for the one somebody
    /// remembered to list - the argument `fx::every_run` makes one category
    /// over.
    fn every_tile() -> Vec<(&'static str, Canvas)> {
        icons()
            .into_iter()
            .map(|icon| (icon.name, (icon.draw)()))
            .collect()
    }

    /// Opaque pixels only. A pillar's flanks are transparent on purpose and a
    /// cleared pixel has no colour to have an opinion about, so folding them
    /// into a luma statistic measures the hole rather than the art.
    fn tones(canvas: &Canvas) -> Vec<i32> {
        canvas
            .pixels()
            .iter()
            .filter(|p| p.3 == 255)
            .map(|p| p.0 as i32 + p.1 as i32 + p.2 as i32)
            .collect()
    }

    #[test]
    fn the_registry_covers_every_band_of_every_act() {
        assert_eq!(icons().len(), 18, "six pieces for each of three acts");

        for act in ["ward", "reach", "throne"] {
            for band in ["wall", "plinth", "pillar"] {
                let name = format!("{act}_{band}");
                assert!(
                    icons().iter().any(|i| i.name == name),
                    "{name} is missing, so that act has no {band}"
                );
            }
            let floors = icons()
                .iter()
                .filter(|i| i.name.starts_with(act) && matches!(band_of(i.name), Band::Floor))
                .count();
            assert_eq!(floors, 3, "{act} needs a floor for each of map, combat and room");
        }
    }

    #[test]
    fn every_tile_is_on_the_background_grid() {
        for (name, canvas) in every_tile() {
            assert_eq!(
                (canvas.width, canvas.height),
                (TILE as u32, TILE as u32),
                "{name} is not 64x64"
            );
        }
    }

    /// A floor, wall or plinth has nothing behind it, and `Canvas::new` starts
    /// transparent - so a pattern that failed to fill leaves a hole that shows
    /// through the room. `validate` checks that alpha is 0 *or* 255 and would
    /// pass a tile that is half missing.
    ///
    /// A pillar is the exception and is asserted in the other direction, because
    /// its transparency is the feature: an opaque pillar tile is a second wall
    /// with a column painted on it, and it would look like a column right up
    /// until the wall behind it changed.
    #[test]
    fn only_a_pillar_sees_through_itself() {
        for (name, canvas) in every_tile() {
            let clear = canvas.pixels().iter().filter(|p| p.3 != 255).count();
            match band_of(name) {
                Band::Pillar => assert!(
                    clear > 0,
                    "{name} is fully opaque, so its flanks hide the wall instead of showing it"
                ),
                _ => assert_eq!(clear, 0, "{name} has {clear} non-opaque pixels"),
            }
        }
    }

    /// The rule `artgen validate` is structurally blind to, and the one that
    /// matters most: a tile is repeated across 1152px, so a discontinuity at the
    /// seam is a line down the screen rather than a blemish.
    ///
    /// **What this does not measure is whether the two edges look alike**, and
    /// the first version of this test did exactly that - it compared column 63
    /// against column 0 and failed every tile in the set. Adjacent columns of a
    /// mortared floor are *supposed* to differ; one can be a lit block face and
    /// its neighbour the joint. Similarity is a property of a gradient, not of
    /// a pattern.
    ///
    /// What separates a wrapped tile from a cut one is that its seam is an
    /// *ordinary* boundary: every column pair has some discontinuity, and the
    /// wrap should sit inside that spread rather than above all of it.
    ///
    /// Which axes have to close is per band. A floor and a wall tile both ways;
    /// a pillar repeats only up the wall, and a plinth is a single row, so
    /// asking either for a seam it never meets would be a rule with no failure
    /// behind it.
    #[test]
    fn every_tile_meets_itself_on_the_axes_it_repeats() {
        for (name, canvas) in every_tile() {
            let axes: &[&str] = match band_of(name) {
                Band::Floor | Band::Wall => &["x", "y"],
                Band::Plinth => &["x"],
                Band::Pillar => &["y"],
            };

            for axis in axes {
                let luma = |p: crate::palette::Rgba| p.0 as i32 + p.1 as i32 + p.2 as i32;
                let boundary: Vec<i64> = (0..TILE)
                    .map(|k| {
                        (0..TILE)
                            .map(|i| {
                                let (a, b) = if *axis == "x" {
                                    (canvas.get(k, i), canvas.get((k + 1) % TILE, i))
                                } else {
                                    (canvas.get(i, k), canvas.get(i, (k + 1) % TILE))
                                };
                                (luma(a) - luma(b)).abs() as i64
                            })
                            .sum()
                    })
                    .collect();

                let seam = boundary[(TILE - 1) as usize];
                let worst_interior = boundary[..(TILE - 1) as usize].iter().copied().max().unwrap();

                assert!(
                    seam <= worst_interior,
                    "{name}: the {axis} seam ({seam}) is a sharper break than any \
                     boundary inside the tile ({worst_interior}), i.e. the pattern is cut \
                     rather than wrapped"
                );
            }
        }
    }

    /// Eighteen parameterised calls with a copy-pasted argument compile,
    /// generate, validate and ship as eighteen files of the same picture. This
    /// is the check that a palette or a seed was actually varied.
    #[test]
    fn no_two_tiles_are_the_same_picture() {
        let tiles = every_tile();
        for i in 0..tiles.len() {
            for j in (i + 1)..tiles.len() {
                assert_ne!(
                    tiles[i].1.pixels(),
                    tiles[j].1.pixels(),
                    "{} and {} are identical",
                    tiles[i].0,
                    tiles[j].0
                );
            }
        }
    }

    /// `shapes::finish` outlines a canvas in `N0`, which is right for an icon
    /// that needs a silhouette and catastrophic for a tile: it draws a 1px black
    /// border round every repeat, i.e. a grid over the entire screen. Nothing
    /// stops a later edit calling it, so this is what says not to.
    #[test]
    fn no_tile_carries_an_icon_outline() {
        for (name, canvas) in every_tile() {
            let uniform = (0..TILE).all(|i| canvas.get(i, 0) == canvas.get(0, 0))
                && (0..TILE).all(|i| canvas.get(0, i) == canvas.get(0, 0));
            assert!(
                !uniform,
                "{name}: every border pixel is one colour, which is what finish() produces"
            );
        }
    }

    /// The ramp is `validate`'s job over the written files, but a failure there
    /// names a file rather than a draw call. Asserting it here means a bad
    /// colour fails in the function that chose it.
    #[test]
    fn every_tile_draws_only_ramp_colours() {
        for (name, canvas) in every_tile() {
            for pixel in canvas.pixels().iter().filter(|p| p.3 == 255) {
                assert!(
                    is_on_ramp(pixel.rgb()),
                    "{name} draws {:?}, which is off the ramp",
                    pixel.rgb()
                );
            }
        }
    }

    /// The contrast budget. A backdrop that swings the full ramp fights every
    /// card and label drawn on top of it, and that is not something `validate`
    /// can have an opinion about - it only knows each colour is legal.
    ///
    /// The plinth and the pillar are allowed more than the rest and need it:
    /// they are the two pieces carrying `sheen`, and being *found* is their
    /// entire job - a horizon nobody notices and a colonnade that sinks into
    /// the masonry are the first draft of this file, which rendered as a flat
    /// sheet with a faint line across it. A wall or a floor spending that range
    /// would be competing with the cards instead.
    #[test]
    fn no_tile_spends_more_of_the_ramp_than_its_band_allows() {
        for (name, canvas) in every_tile() {
            let tones = tones(&canvas);
            let span = tones.iter().max().unwrap() - tones.iter().min().unwrap();
            let budget = match band_of(name) {
                Band::Plinth | Band::Pillar => 470,
                _ => 420,
            };
            assert!(span < budget, "{name} spans {span} of the ramp; a floor is not the subject");
        }
    }
}
