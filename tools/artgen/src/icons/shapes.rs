//! The shared vocabulary every icon is built from.
//!
//! Seventy-eight bespoke icons drawn pixel by pixel would be seventy-eight
//! chances to draw a blade at a different width or a shield with a different
//! taper — the vector set's exact failure, restated in a new medium. So the
//! recurring forms live here once and each icon composes them. A blade is a
//! blade everywhere in the game because there is one `blade`.
//!
//! Everything is authored on the 32x32 `IconGrid` inset one pixel, leaving
//! room for `finish`'s outline without clipping. Colours are always ramp
//! constants — nothing here darkens or lightens arithmetically, because a
//! multiplied colour lands off-ramp and `artgen validate` would (correctly)
//! reject it.
//!
//! ## Light
//!
//! One form vocabulary was never enough on its own: the set was consistent in
//! form and inconsistent in lighting, because each shape picked its own lit
//! edge. `light.rs` is the other half — one source, up and to the left, for
//! every icon (`docs/ART_SPEC.md` §10).
//!
//! Every shape below is in exactly one of three classes, and each one says
//! which in its own doc comment rather than leaving a reader to infer it:
//!
//! - **Directional** — derives its lit side from `LIGHT`. `blade`, `sword`,
//!   `shield`, `droplet`, `flask`, `gem`, `scale`, `skull`, `raised_fist`.
//! - **Emissive** — a light source, so it is lit from inside and the direction
//!   does not apply. `flame`, `orb`, `sparkle`.
//! - **Symmetric** — concentric or single-colour; there is no face to light.
//!   `eye`, `arrow`, `crack`, `barb`.
//!
//! The colour parameters (`edge`, `lit`, `highlight`, `shade`) stay callers'
//! business, because a material's pigment is a content decision. Where those
//! colours *land* is not a parameter and must never become one.

use crate::icons::light::{by_face, lit_offset, LIGHT};
use crate::canvas::Canvas;
use crate::palette::*;

pub const GRID: i32 = 32;

pub fn new_icon() -> Canvas {
    Canvas::new(GRID as u32, GRID as u32)
}

/// The finishing pass every icon ends with: a hard N0 outline so the shape
/// separates from whatever it is drawn over. Card art sits on a lit frame,
/// HUD icons sit on near-black — without this, half the set disappears
/// against one background or the other.
pub fn finish(canvas: &mut Canvas) {
    canvas.outline(N0);
}

/// Heavier outline that also fills diagonal neighbours, closing the staircase
/// gaps a steep silhouette leaves. Right for chunky shapes, too fat for thin
/// ones — hence the choice rather than one rule.
pub fn finish_heavy(canvas: &mut Canvas) {
    canvas.outline_with(N0, true);
}

fn unit(from: (i32, i32), to: (i32, i32)) -> (f32, f32) {
    let (dx, dy) = ((to.0 - from.0) as f32, (to.1 - from.1) as f32);
    let length = (dx * dx + dy * dy).sqrt().max(0.001);
    (dx / length, dy / length)
}

fn offset(point: (i32, i32), direction: (f32, f32), distance: f32) -> (i32, i32) {
    (
        (point.0 as f32 + direction.0 * distance).round() as i32,
        (point.1 as f32 + direction.1 * distance).round() as i32,
    )
}

/// **Directional.** A blade from `hilt` to `tip`: parallel-sided for most of
/// its length, then tapering to a point. `half_width` 1 gives a 2–3px blade,
/// which is the thinnest that still reads once outlined.
///
/// `edge` paints the lit side one pixel bright, which is where nearly all of a
/// weapon icon's readability comes from at this size — a flat silhouette reads
/// as a stick.
///
/// **Which side that is comes from `light.rs`, not from the blade.** It used
/// to be the `-across` side unconditionally — described as "the leading side",
/// which is a direction relative to the blade's own rotation and therefore a
/// different *screen* side for every angle. Two thirds of the authored blades
/// happened to point somewhere that made it right. The three that did not were
/// `pommel_strike` (down-left), `cataclysm` (straight down) and — the one that
/// shows the bug rather than merely having it — `annihilate`, whose crossed
/// pair came out with one blade lit from the upper left and the other from the
/// lower right, two suns in one 32x32 icon.
///
/// Deriving it fixed all three with no call site touched, and there is now no
/// parameter through which a fourth could be authored.
pub fn blade(
    canvas: &mut Canvas,
    hilt: (i32, i32),
    tip: (i32, i32),
    half_width: f32,
    body: Rgb,
    edge: Rgb,
) {
    let along = unit(hilt, tip);
    let across = (-along.1, along.0);
    let shoulder = offset(tip, along, -3.0);

    canvas.poly(
        &[
            offset(hilt, across, half_width),
            offset(shoulder, across, half_width),
            tip,
            offset(shoulder, across, -half_width),
            offset(hilt, across, -half_width),
        ],
        body,
    );

    let lit = lit_offset(across) * half_width;
    let from = offset(hilt, across, lit);
    let to = offset(shoulder, across, lit);
    canvas.line(from.0, from.1, to.0, to.1, edge);
}

/// **Directional**, inherited whole from `blade`. Blade plus crossguard, grip
/// and pommel — the full sword used by Strike and every icon that quotes it.
/// The furniture is flat on purpose: a crossguard is 2px and a pommel 4, and
/// there is no room to light either without turning both to noise at 1x.
pub fn sword(canvas: &mut Canvas, hilt: (i32, i32), tip: (i32, i32), body: Rgb, edge: Rgb) {
    let along = unit(hilt, tip);
    let across = (-along.1, along.0);

    let grip_end = offset(hilt, along, -5.0);
    canvas.thick_line(hilt.0, hilt.1, grip_end.0, grip_end.1, 2, super::GRIP);

    let pommel = offset(hilt, along, -6.0);
    canvas.disc(pommel.0, pommel.1, 2, super::GUARD);

    let guard_a = offset(hilt, across, 4.0);
    let guard_b = offset(hilt, across, -4.0);
    canvas.thick_line(guard_a.0, guard_a.1, guard_b.0, guard_b.1, 2, super::GUARD);

    blade(canvas, hilt, tip, 1.5, body, edge);
}

/// Everything on one side of the terminator running through `(cx, cy)`, as a
/// polygon `erase_poly` can cut with. `toward` picks the side: `-LIGHT` for the
/// lit half, `LIGHT` for the shadowed one.
///
/// A parallelogram rather than a true half-plane because there is no
/// half-plane primitive and there does not need to be: `REACH` is twice the
/// grid, so the quad covers the whole canvas from any centre a 32x32 icon can
/// have, and `Canvas::set` drops the rest.
fn half_plane(cx: i32, cy: i32, toward: (f32, f32)) -> [(i32, i32); 4] {
    const REACH: f32 = 64.0;
    // The terminator is perpendicular to LIGHT — direction (-1, 1).
    let along = (-LIGHT.1, LIGHT.0);
    let a = offset((cx, cy), along, REACH);
    let b = offset((cx, cy), along, -REACH);
    [
        a,
        b,
        offset(b, toward, REACH * 2.0),
        offset(a, toward, REACH * 2.0),
    ]
}

/// The half the light reaches. Cut this away and a shape keeps its shadow side.
fn lit_half(cx: i32, cy: i32) -> [(i32, i32); 4] {
    half_plane(cx, cy, (-LIGHT.0, -LIGHT.1))
}

/// The half the light misses. Cut this away and a shape keeps its lit side.
fn shadow_half(cx: i32, cy: i32) -> [(i32, i32); 4] {
    half_plane(cx, cy, LIGHT)
}

/// **Directional.** Heater shield: square shoulders, straight sides for the top
/// half, then a bowed taper to a blunt base. `width` is the full span.
///
/// The taper is the part worth stating, because the obvious version is wrong
/// and shipped. Straight sides for the top 45% and then two straight lines to
/// a single pixel is not a heater shield, it is a spearhead — the bottom 55% of
/// the silhouette is a plain triangle, and at 1x the eye reads the point rather
/// than the plate. Two things fix it and both are needed:
///
/// - **The taper bows outward** rather than running straight to the tip. One
///   extra vertex per flank at `knee` is enough: real plate carries its
///   material down and *then* turns in, where a straight line does the turning
///   evenly all the way, which is exactly what reads as a blade.
/// - **The base is blunt.** A shield is a thing you hide behind, so it ends in
///   a short flat rather than a point. `half / 5` keeps that proportional, so
///   the 11px potion emblems and the 28px Aegis get the same silhouette rather
///   than the small ones degenerating back into the triangle.
///
/// The shoulder stayed near where it was (50%, from 45%), and that is worth
/// saying because moving it was tried first and is the wrong lever: holding the
/// flanks to 60% makes a *longer* shield rather than a rounder one, and it
/// leaves so few rows for the taper that the small emblems come out as blobs.
/// The bow is what does the work.
///
/// The rim is two colours, split at the terminator: `rim` on the top edge and
/// the left flank, `shade` on the right flank and the taper the light does not
/// reach. It was one colour all the way round for the whole life of the set —
/// concentric rather than lit, so the second most-quoted form in the game
/// (twenty-six call sites) had no light direction at all rather than a wrong
/// one. A shield reads as a curved plate now instead of as an outlined
/// sticker.
///
/// The three passes have to run in this order. The shadow pass paints the
/// whole unlit *silhouette*, not just its rim, and it is the inset `face`
/// landing last that cuts it back to a rim. Drawing the face before the shadow
/// would bury the interior in `shade`.
pub fn shield(
    canvas: &mut Canvas,
    cx: i32,
    top: i32,
    width: i32,
    height: i32,
    face: Rgb,
    rim: Rgb,
    shade: Rgb,
) {
    let half = width / 2;
    let outline = shield_outline(cx, top, half, height);
    canvas.poly(&outline, rim);

    let mut shadow = Canvas::new(canvas.width, canvas.height);
    shadow.poly(&outline, shade);
    shadow.erase_poly(&lit_half(cx, top + height / 2));
    canvas.blit(&shadow, 0, 0);

    // The face is the same silhouette shrunk, not a second hand-written one.
    // Two outlines that were meant to be parallel drifted apart the moment the
    // taper stopped being straight - the old inset kept the tip 3px above the
    // rim's, which is a rim of a different thickness at the bottom than at the
    // sides. Deriving it means the rim is 2px the whole way round by
    // construction, and a future change to the profile moves both.
    canvas.poly(&shield_outline(cx, top + 2, half - 2, height - 4), face);
}

/// The heater silhouette, as eight points: flat top, straight flanks to
/// `shoulder`, a bowed turn through `knee`, and a short flat base.
///
/// Split out so the rim and the face are the same shape at two sizes — see
/// `shield`. Its own two numbers are proportions rather than tuning: flanks to
/// 50% of the height and a knee at 80%. What happens *below* that knee belongs
/// to `blunt_taper`, which every plate in the set shares.
fn shield_outline(cx: i32, top: i32, half: i32, height: i32) -> [(i32, i32); 8] {
    let shoulder = top + height * 50 / 100;
    let knee = top + height * 80 / 100;
    let bottom = top + height;
    let [knee_r, base_r, base_l, knee_l] = blunt_taper(cx, half, knee, bottom);
    [
        (cx - half, top),
        (cx + half, top),
        (cx + half, shoulder),
        knee_r,
        base_r,
        base_l,
        knee_l,
        (cx - half, shoulder),
    ]
}

/// The bottom four vertices every plate in the set shares: a bowed knee on each
/// flank, then a short flat base. Returned right-knee, right-base, left-base,
/// left-knee, so a caller splices them into a clockwise outline between its own
/// two shoulder points.
///
/// This is the whole of "a shield is a plate you hide behind, not a spearhead",
/// in one place. It was inlined in `shield_outline` and therefore reached only
/// the shapes that call `shield` — `iron_resolve` drew its own tower shield from
/// two literals and kept the straight-taper-to-a-point profile the fix had
/// already deleted, along with the drifted rim that came with it. A rule that
/// lives in one shape's body is a rule the next shape does not inherit.
///
/// Four fifths of the span at the knee is what makes the taper convex: a
/// straight line from the shoulder to the base would be at roughly half by this
/// row, so the extra material is the bow.
///
/// Three quarters is the number this wants to be and it is not safe. At the
/// narrowest authored shield (11px, the potion emblems) integer division lands
/// `half * 3 / 4` exactly *on* the straight line — a bow of zero, at precisely
/// the size where the taper has the fewest rows to read in and where the pointed
/// version was worst. The sweep in `no_plate_outline_is_a_spearhead_at_any_size`
/// is what found that; one call at one size would not have.
///
/// `knee_y` is an explicit row rather than a percentage of the shoulder-to-base
/// span, and that is not a style choice. `shield_outline` places its knee at 80%
/// of the *total* height with the shoulder at 50%; re-deriving that as 60% of the
/// span between them lands on a different row under integer division (at
/// `height = 25`, row 20 against row 19), which would have moved all 23 icons the
/// shield fix already shipped.
pub fn blunt_taper(cx: i32, half: i32, knee_y: i32, bottom_y: i32) -> [(i32, i32); 4] {
    let knee_half = half * 4 / 5;
    // At least 1, so the smallest authored shield (11px wide) still ends in a
    // 3px flat rather than collapsing to the point this shape exists to avoid.
    let base = (half / 5).max(1);
    [
        (cx + knee_half, knee_y),
        (cx + base, bottom_y),
        (cx - base, bottom_y),
        (cx - knee_half, knee_y),
    ]
}

/// The tower silhouette: a flat top on bevelled shoulders, long straight flanks,
/// and the same `blunt_taper` bottom the heater shield has.
///
/// A tower is a different object from a heater and has to keep reading as one —
/// it is the 12-Block relic standing beside `bulwark_charm`'s heater, and two
/// icons for two relics that resolve to the same silhouette is a worse bug than
/// the one this fixes. The separation is the bevel and the long flanks, which is
/// where a tower's material actually differs; the *taper* is shared, because a
/// pointed bottom was never the distinction, it was just the old shape.
fn tower_shield_outline(cx: i32, top: i32, half: i32, height: i32) -> [(i32, i32); 10] {
    // The top flat is inset and the flanks bevel out to full span just below it.
    // That shoulder is what says "tower" at 1x, so it is proportional rather
    // than a constant 2px: at the sizes below 16 a fixed inset eats the whole
    // top edge and the shape reads as a heater again.
    let top_half = half * 4 / 5;
    let bevel = top + height * 16 / 100;
    let shoulder = top + height * 64 / 100;
    let knee = top + height * 86 / 100;
    let bottom = top + height;
    let [knee_r, base_r, base_l, knee_l] = blunt_taper(cx, half, knee, bottom);
    [
        (cx - top_half, top),
        (cx + top_half, top),
        (cx + half, bevel),
        (cx + half, shoulder),
        knee_r,
        base_r,
        base_l,
        knee_l,
        (cx - half, shoulder),
        (cx - half, bevel),
    ]
}

/// The pauldroned breastplate, as eleven points: a flat top notched for the
/// neck, shoulders wider than the waist, and the shared `blunt_taper` hem.
///
/// `metallicize` and `bramble_mail` each carried their own copy of this — the
/// second quotes the first by design ("the same substance at two scales"), and
/// what it actually quoted was a hand-transcribed near-copy that had drifted on
/// every number: pauldron half 13 against 12, waist row 15 against 16, caps at
/// x=3/24 against 4/23. Two literals that are meant to be one shape is the
/// defect `shield` removed from its own rim and face, one icon family over.
///
/// The hem was a point on both, which is the third instance of the same thing:
/// `metallicize`'s comment says a plate "that simply tapered from square
/// shoulders to a point read as a heater shield, which is the one shape this
/// icon had to stay clear of" — and that was true of the *old* heater. Now that
/// a shield ends in a flat, a point is what makes a breastplate read as the
/// shape it was avoiding. The notch and the pauldrons are what separate them,
/// and they still do.
pub fn breastplate_outline(
    cx: i32,
    top: i32,
    half: i32,
    waist_y: i32,
    bottom_y: i32,
) -> [(i32, i32); 11] {
    let waist = half - 3;
    let knee = waist_y + (bottom_y - waist_y) * 70 / 100;
    let [knee_r, base_r, base_l, knee_l] = blunt_taper(cx, waist, knee, bottom_y);
    [
        (cx - half, top),
        (cx - 3, top),
        (cx, top + 3),
        (cx + 3, top),
        (cx + half, top),
        (cx + waist, waist_y),
        knee_r,
        base_r,
        base_l,
        knee_l,
        (cx - waist, waist_y),
    ]
}

/// **Directional.** Tower shield: bevelled square shoulders, long straight
/// flanks, blunt base. `width` is the full span.
///
/// `shield`'s three passes, in `shield`'s order, over `tower_shield_outline` —
/// see that function for why the order is load-bearing (the shadow pass paints
/// the whole unlit *silhouette*, and it is the inset face landing last that cuts
/// it back to a rim; drawing the face first buries the interior in `shade`).
///
/// The face is derived from the same outline rather than hand-written, which is
/// the specific defect this replaced: `iron_resolve`'s two literals had drifted
/// so that the inner tip sat 3px above the outer, i.e. a rim thicker at the
/// bottom than at the sides. Deriving it makes the rim 2px the whole way round
/// by construction, and a future change to the profile moves both.
pub fn tower_shield(
    canvas: &mut Canvas,
    cx: i32,
    top: i32,
    width: i32,
    height: i32,
    face: Rgb,
    rim: Rgb,
    shade: Rgb,
) {
    let half = width / 2;
    let outline = tower_shield_outline(cx, top, half, height);
    canvas.poly(&outline, rim);

    let mut shadow = Canvas::new(canvas.width, canvas.height);
    shadow.poly(&outline, shade);
    shadow.erase_poly(&lit_half(cx, top + height / 2));
    canvas.blit(&shadow, 0, 0);

    canvas.poly(&tower_shield_outline(cx, top + 2, half - 2, height - 4), face);
}

/// **Directional.** Teardrop with the point up — poison, blood, any liquid.
///
/// The highlight is a specular on a round wet body, so it sits where the
/// surface normal points straight back at the source: one step in from the rim
/// along `-LIGHT`, spread one pixel along the terminator so it reads as a
/// short arc rather than a stray dot. It was a vertical pair on the left flank
/// before, which was already roughly on the light and only roughly — derived,
/// it cannot drift, and twenty call sites move together.
pub fn droplet(canvas: &mut Canvas, cx: i32, cy: i32, radius: i32, body: Rgb, highlight: Rgb) {
    canvas.disc(cx, cy, radius, body);
    canvas.poly(
        &[
            (cx - radius, cy),
            (cx, cy - radius * 2 - 1),
            (cx + radius, cy),
        ],
        body,
    );
    let specular = offset((cx, cy), (-LIGHT.0, -LIGHT.1), radius as f32 - 0.5);
    canvas.set(specular.0, specular.1, highlight);
    canvas.set(specular.0 - 1, specular.1 + 1, highlight);
}

/// **Emissive** — a flame is the light, so §10's direction does not apply to
/// it. The core is brighter because it is deeper into the fire, not because it
/// faces anywhere; giving it a lit side would say the flame is lit by
/// something else.
///
/// A flame: a wide base narrowing to an off-centre lick, with an inner core
/// two ramp steps brighter. `height` is measured up from `base_y`.
pub fn flame(canvas: &mut Canvas, cx: i32, base_y: i32, height: i32, outer: Rgb, core: Rgb) {
    let half = height / 3;
    canvas.poly(
        &[
            (cx - half, base_y),
            (cx - half + 1, base_y - height / 2),
            (cx - 1, base_y - height + 2),
            (cx + 1, base_y - height),
            (cx + half, base_y - height / 3),
            (cx + half, base_y),
        ],
        outer,
    );
    canvas.poly(
        &[
            (cx - half + 2, base_y),
            (cx - 1, base_y - height / 2),
            (cx + 1, base_y - height / 2 - 1),
            (cx + half - 2, base_y),
        ],
        core,
    );
}

/// **Symmetric** — one colour, no faces. An arrow is a mark rather than an
/// object, so there is nothing for the light to fall on.
///
/// Arrow from `from` to `to` with a solid triangular head.
pub fn arrow(
    canvas: &mut Canvas,
    from: (i32, i32),
    to: (i32, i32),
    weight: i32,
    head: i32,
    colour: Rgb,
) {
    let along = unit(from, to);
    let across = (-along.1, along.0);
    let neck = offset(to, along, -(head as f32));
    canvas.thick_line(from.0, from.1, neck.0, neck.1, weight, colour);
    canvas.poly(
        &[
            offset(neck, across, head as f32 * 0.8),
            to,
            offset(neck, across, -(head as f32) * 0.8),
        ],
        colour,
    );
}

/// **Emissive.** A sparkle emits; it is the reason a thing near it is lit, so
/// it takes no lit side of its own. This is also why it stays one flat colour
/// rather than gaining a ramp — a shaded sparkle reads as a metal star.
///
/// Four-point sparkle — the "something magical happened" mark shared by buff,
/// upgrade and the arcane relics. Thin arms over a small solid core, not a
/// filled diamond: the concave silhouette is the whole difference between
/// reading as a sparkle and reading as a gem.
pub fn sparkle(canvas: &mut Canvas, cx: i32, cy: i32, radius: i32, colour: Rgb) {
    canvas.vline(cx, cy - radius, radius * 2 + 1, colour);
    canvas.hline(cx - radius, cy, radius * 2 + 1, colour);
    let core = radius / 2;
    for step in 0..=core {
        let arm = core - step;
        canvas.hline(cx - arm, cy - step, arm * 2 + 1, colour);
        canvas.hline(cx - arm, cy + step, arm * 2 + 1, colour);
    }
}

/// **Directional.** Skull: cranium, two socket voids and a jaw. Deliberately
/// blocky — a detailed skull at 32px turns to noise the moment it is outlined.
///
/// Two different dark passes, and they are not the same thing. The band across
/// the top of the jaw is **contact shadow** — the cranium sitting on it — and
/// is correct under any light, which is why it was already here. The crescent
/// added below is **form shadow**, the side of a sphere the light misses, and
/// it is what §10 was owed.
pub fn skull(canvas: &mut Canvas, cx: i32, top: i32, bone: Rgb, shade: Rgb) {
    canvas.disc(cx, top + 7, 7, bone);
    canvas.rect(cx - 6, top + 7, 13, 5, bone);
    canvas.rect(cx - 4, top + 12, 9, 4, bone);

    // A sphere's shadow is a crescent, and a crescent is the same disc pushed
    // toward the source and cut away — cheaper than any lighting model and
    // exactly right at this size. Trimming to the shadow half as well keeps
    // the cheeks from picking up a lit-side rim.
    let mut shadow = Canvas::new(canvas.width, canvas.height);
    shadow.disc(cx, top + 7, 7, shade);
    shadow.rect(cx - 6, top + 7, 13, 5, shade);
    let pushed = offset((cx, top + 7), (-LIGHT.0, -LIGHT.1), 2.5);
    shadow.erase_disc(pushed.0, pushed.1, 7);
    shadow.erase_poly(&lit_half(cx, top + 7));
    canvas.blit(&shadow, 0, 0);

    canvas.rect(cx - 4, top + 12, 9, 1, shade);
    // Sockets read only as absence — filling them with a dark ramp entry
    // instead makes the skull look like it is wearing goggles at 1x.
    canvas.rect(cx - 5, top + 5, 4, 4, N0);
    canvas.rect(cx + 2, top + 5, 4, 4, N0);
    canvas.rect(cx - 1, top + 10, 2, 2, N0);
    canvas.set(cx - 2, top + 14, N0);
    canvas.set(cx, top + 14, N0);
    canvas.set(cx + 2, top + 14, N0);
}

/// **Directional.** Potion flask: round body, short neck, cork. The liquid
/// fills the body from `fill_top` down, so the same base reads as full or
/// half-empty.
///
/// The glass ring between the body's edge and the liquid was one flat `GLASS`
/// all the way round, which is a vessel with no near or far side. It now
/// brightens on the upper-left arc: the wall the light strikes, drawn in
/// `SHIELD_FACE`'s step of the same blue rather than in a new colour, because
/// glass and steel are the same material family on the §5 ramp.
///
/// The glint keeps its authored pixels. It was already on the light — a
/// vertical catch down the left flank stepping up at the top — and a derived
/// replacement would have moved all twelve potions to say the same thing.
pub fn flask(canvas: &mut Canvas, liquid: Rgb, highlight: Rgb) {
    let (cx, cy) = (16, 20);
    canvas.disc(cx, cy, 9, super::GLASS);
    canvas.rect(cx - 3, 6, 7, 8, super::GLASS);
    canvas.rect(cx - 4, 5, 9, 3, super::CORK);

    canvas.disc(cx, cy, 7, liquid);
    canvas.rect(cx - 7, cy, 15, 8, liquid);
    // Re-cut the bottom of the glass the fill just squared off.
    let mut mask = new_icon();
    mask.disc(cx, cy, 9, super::GLASS);
    for y in cy..GRID {
        for x in 0..GRID {
            if !mask.get(x, y).is_opaque() {
                canvas.set_raw(x, y, crate::palette::TRANSPARENT);
            }
        }
    }

    canvas.rect(cx - 3, 12, 7, 3, liquid);

    // The lit arc of the glass wall: the body disc minus the same disc pushed
    // away from the source, trimmed to the half the light reaches.
    let mut wall = Canvas::new(canvas.width, canvas.height);
    wall.disc(cx, cy, 9, B2);
    let pushed = offset((cx, cy), LIGHT, 1.5);
    wall.erase_disc(pushed.0, pushed.1, 9);
    wall.erase_poly(&shadow_half(cx, cy));
    canvas.blit(&wall, 0, 0);

    canvas.vline(cx - 6, cy - 3, 4, highlight);
    canvas.set(cx - 5, cy - 4, highlight);
}

/// A raised fist. Shared by the Strength status and the Flex card on purpose —
/// the card grants the status, and they should be recognisably the same
/// gesture.
///
/// A full flexed arm (bicep, elbow, forearm) was drawn first and read as a red
/// boot at 1x: at 32px there is not enough room for a limb to have both a
/// bend and a hand. The fist alone has one silhouette and one meaning.
///
/// The finger grooves cut to `N0` rather than a step darker — `R2` on `R3` is
/// one ramp step and disappears at 1x, which is what made the earlier version
/// read as a mitten.
///
/// **Directional.** It was already top-lit — `R4` knuckles over an `R3` body
/// over an `R2` wrist — which is the half of §10 that comes free. What it had
/// no side to was left and right, so the far flank is `R2` now.
///
/// **All four knuckles keep their `R4`, and that is a decision rather than an
/// oversight.** Dropping the furthest one to `R3` is what the light says, and
/// it was tried: at 1x the fourth finger vanished into the body and the fist
/// read as having three. That is this function's own warning — "`R2` on `R3`
/// is one ramp step and disappears at 1x" — arriving from the other
/// direction, and §1's legibility budget outranks §10 when they disagree. The
/// flank carries the light instead, where there is room for it.
pub fn raised_fist(canvas: &mut Canvas) {
    canvas.rect(7, 7, 18, 15, R3);
    canvas.rect(4, 13, 5, 8, R3);
    canvas.rect(23, 11, 2, 11, R2);
    for x in [9, 13, 17, 21] {
        canvas.disc(x, 8, 2, R4);
    }
    for x in [11, 15, 19] {
        canvas.vline(x, 6, 9, N0);
    }
    canvas.hline(8, 15, 17, N0);
    canvas.rect(9, 22, 14, 7, R2);
}

/// **Symmetric** — a 1px polyline has no volume and therefore no faces.
///
/// A jagged crack — damage, vulnerability, anything broken.
pub fn crack(canvas: &mut Canvas, points: &[(i32, i32)], colour: Rgb) {
    for pair in points.windows(2) {
        canvas.line(pair[0].0, pair[0].1, pair[1].0, pair[1].1, colour);
    }
}

/// **Symmetric.** The `N8` field is an *aperture* — a hole — and the iris and
/// pupil sit inside it. Lighting one side of a hole is a mistake rather than a
/// convention, which is why this shape takes no light despite being the most
/// three-dimensional thing in the vocabulary.
///
/// An open eye: a diamond aperture, an iris and a hard pupil. Lives here
/// rather than in one icon because three things share it — the Foresight
/// status and both Powers that grant it — and "seeing more" is the whole
/// family's mark. A round eye was tried and reads as a ring at 1x; the
/// diamond's corners are what say *eye* rather than *button*.
pub fn eye(canvas: &mut Canvas, cx: i32, cy: i32, half_width: i32, half_height: i32, iris: Rgb) {
    canvas.poly(
        &[
            (cx - half_width, cy),
            (cx, cy - half_height),
            (cx + half_width, cy),
            (cx, cy + half_height),
        ],
        N8,
    );
    canvas.disc(cx, cy, half_height - 1, iris);
    canvas.disc(cx, cy, (half_height - 1) / 2, N0);
}

/// **Emissive.** An energy orb is a light source; its bright `core` is depth
/// into the glow, not a face turned toward anything. Same argument as `flame`,
/// and the reason both keep a centred core rather than an offset one.
///
/// The energy orb, the same octagon the combat HUD draws its number inside.
/// Shared by the Fervor status and the Power that grants it, for the reason
/// `raised_fist` is shared: the card hands you the status, so they had better
/// be the same object.
pub fn orb(canvas: &mut Canvas, cx: i32, cy: i32, radius: i32, body: Rgb, core: Rgb) {
    let cut = radius / 2;
    canvas.poly(
        &[
            (cx - cut, cy - radius),
            (cx + cut, cy - radius),
            (cx + radius, cy - cut),
            (cx + radius, cy + cut),
            (cx + cut, cy + radius),
            (cx - cut, cy + radius),
            (cx - radius, cy + cut),
            (cx - radius, cy - cut),
        ],
        body,
    );
    canvas.disc(cx, cy, radius / 2, core);
}

/// **Directional in the pavilion, symmetric in the table** — and that split is
/// the point rather than a compromise. The crown faces the viewer, so a 45°
/// lamp reaches both halves of it equally and there is no left/right asymmetry
/// to express; the pavilion is where a real table cut shows its light, and it
/// is where this one does too.
///
/// The `face` inset is 1px on the lit flank and 4px on the shadow flank, so
/// the dark `body` reads as a wide facet on the side turned away. Note the
/// amounts are the **inverse** of `shield`'s: what a gem reveals under its
/// face is darker than the face, so the wide band belongs in shadow, where a
/// shield reveals something brighter and wants the wide band in the light.
/// Both go through `by_face`; only the arguments differ.
///
/// A table-cut gem: flat crown, tapering pavilion, one bright table face.
///
/// The Artifact status draws it full-bleed and the two ward cards quote it at
/// smaller sizes, for the reason `raised_fist` and `orb` are shared - the cards
/// hand you the status, so they have to be the same object rather than two
/// takes on one idea.
pub fn gem(
    canvas: &mut Canvas,
    cx: i32,
    top: i32,
    half_width: i32,
    height: i32,
    body: Rgb,
    face: Rgb,
    table: Rgb,
) {
    let crown = half_width / 2;
    let shoulder = top + height / 3;
    canvas.poly(
        &[
            (cx - crown, top),
            (cx + crown, top),
            (cx + half_width, shoulder),
            (cx, top + height),
            (cx - half_width, shoulder),
        ],
        body,
    );
    let left = by_face((-1.0, 0.0), 1, 4);
    let right = by_face((1.0, 0.0), 1, 4);
    canvas.poly(
        &[
            (cx - crown + 1, top + 2),
            (cx + crown - 1, top + 2),
            (cx + half_width - right, shoulder),
            (cx, top + height - 3),
            (cx - half_width + left, shoulder),
        ],
        face,
    );
    // Nearly all of a gem's readability at this size is the table being one
    // step lighter than everything under it.
    canvas.poly(
        &[
            (cx - crown + 1, top + 2),
            (cx + crown - 1, top + 2),
            (cx + half_width - right - 1, shoulder - 1),
            (cx - half_width + left + 1, shoulder - 1),
        ],
        table,
    );
}

/// **Directional.** One armour scale: a rounded plate with a lit upper arc.
/// `misc::plating` tiles it into a field, `cards::scaled_hide` lays it over a
/// body.
///
/// The `face` disc is pushed toward the source, so `body` survives as a
/// crescent on the far side — that crescent is the shadow. It used to be
/// pushed straight up, which is a light from directly overhead and from
/// nowhere else: right by half, and the half it got wrong is the half that
/// made the field of scales read as a stack of coins rather than as armour.
pub fn scale(canvas: &mut Canvas, cx: i32, cy: i32, radius: i32, body: Rgb, face: Rgb, lit: Rgb) {
    canvas.disc(cx, cy, radius, body);
    let toward_source = offset((cx, cy), (-LIGHT.0, -LIGHT.1), 1.0);
    canvas.disc(toward_source.0, toward_source.1, radius - 1, face);
    canvas.hline(cx - radius + 1, cy - radius, radius * 2 - 3, lit);
}

/// **Symmetric**, and this is the one exception in the list that could be
/// argued the other way. The bright `point` is there because the tip is
/// *thin*, not because it faces anywhere, and it has to read at every rake
/// angle the six call sites use — `misc.rs` mirrors barbs left and right with
/// identical colours, and a light-derived tip would make one thorn of a pair
/// duller than the other for no reason a player could name.
///
/// One thorn: a wide-based wedge raking away from a stem toward `tip`.
///
/// The base is a vertical span rather than a point because a thorn narrow at
/// both ends reads as a speck at 1x - the first draft of the Thorns status was
/// exactly that, and looked like dirt on the stem.
pub fn barb(
    canvas: &mut Canvas,
    base: (i32, i32),
    spread: i32,
    tip: (i32, i32),
    body: Rgb,
    point: Rgb,
) {
    let (bx, by) = base;
    canvas.poly(&[(bx, by - spread), (bx, by + spread), tip], body);
    canvas.set(tip.0, tip.1, point);
    canvas.set(tip.0, tip.1 + 1, point);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::palette::{B1, B2, B5, N6, N8, P1, P3};

    /// Centre of mass of every pixel of exactly `colour`. `None` if the shape
    /// drew none, which is a drawing failure rather than a lighting one and is
    /// worth separating from a wrong-side highlight in the message.
    fn centroid(canvas: &Canvas, colour: Rgb) -> Option<(f32, f32)> {
        let (mut sx, mut sy, mut n) = (0.0f32, 0.0f32, 0u32);
        for y in 0..canvas.height as i32 {
            for x in 0..canvas.width as i32 {
                if canvas.get(x, y).is_opaque() && canvas.get(x, y).rgb() == colour {
                    sx += x as f32;
                    sy += y as f32;
                    n += 1;
                }
            }
        }
        (n > 0).then(|| (sx / n as f32, sy / n as f32))
    }

    /// How far `to` sits toward the lamp from `from`. Positive means `to` is
    /// the lit side.
    fn toward_light(from: (f32, f32), to: (f32, f32)) -> f32 {
        let delta = (to.0 - from.0, to.1 - from.1);
        -(delta.0 * LIGHT.0 + delta.1 * LIGHT.1)
    }

    /// A blade's bright edge lands on the side the lamp is on, **at every
    /// angle**.
    ///
    /// This is the assertion the whole item is really about. All three icons
    /// that shipped lit from the lower right were blades pointing outside the
    /// arc the old hardcoded `-half_width` happened to be right for, and
    /// nothing in the repository could see it: `artgen validate` reads raw
    /// bytes, and a wrong-side highlight is still on the ramp, still 32x32 and
    /// still hard-alpha. Sweeping the circle rather than checking the authored
    /// call sites is the point — the call sites only cover the angles somebody
    /// already thought of, which is the mistake `TestNoTweenTransformsAPixel-
    /// Sprite` was rewritten over one language away.
    ///
    /// **The measurement is lateral, and it has to be.** The first version of
    /// this test compared the two centroids directly and failed on a blade
    /// pointing up-left that was in fact lit correctly: `edge` runs hilt to
    /// shoulder while `body` includes the tip, so the edge's centroid sits
    /// *behind* the body's along the blade's own axis, and that along-axis
    /// offset swamped the one-pixel lateral one the rule actually decides.
    /// Projecting onto `across` first removes a term the rule never touches.
    #[test]
    fn a_blade_edge_faces_the_light_at_every_angle() {
        for step in 0..32 {
            let angle = step as f32 * std::f32::consts::TAU / 32.0;
            let tip = (
                16 + (angle.cos() * 13.0).round() as i32,
                16 + (angle.sin() * 13.0).round() as i32,
            );
            let mut canvas = new_icon();
            blade(&mut canvas, (16, 16), tip, 2.0, N6, N8);

            let body = centroid(&canvas, N6).expect("blade drew no body");
            let edge = centroid(&canvas, N8).expect("blade drew no edge");

            let along = unit((16, 16), tip);
            let across = (-along.1, along.0);
            let delta = (edge.0 - body.0, edge.1 - body.1);
            let lateral = delta.0 * across.0 + delta.1 * across.1;
            assert!(
                lateral.abs() > 0.01,
                "blade to {tip:?} drew its edge down the middle: no side at all"
            );

            // The face the edge actually landed on, as an outward normal.
            let normal = (
                across.0 * lateral.signum(),
                across.1 * lateral.signum(),
            );
            let incidence = -(normal.0 * LIGHT.0 + normal.1 * LIGHT.1);

            if crate::icons::light::incidence(across).abs() <= crate::icons::light::GRAZING {
                // Grazing: the blade points at the lamp and both long faces
                // receive the same light. Pin the documented tiebreak rather
                // than exempting the case, or the rule has a hole exactly
                // where the ambiguity is.
                assert!(
                    normal.1 <= 0.0,
                    "blade to {tip:?} grazes the lamp, so its edge must break \
                     upward; got a face normal of {normal:?}"
                );
            } else {
                assert!(
                    incidence > 0.0,
                    "blade to {tip:?} is lit from the far side: \
                     edge on the face normal to {normal:?}, incidence {incidence:.2}"
                );
            }
        }
    }

    /// How much of `colour` falls either side of the terminator through
    /// `(cx, cy)`, as `(lit, shadow)` pixel counts.
    fn split_mass(
        canvas: &Canvas,
        colour: Rgb,
        cx: i32,
        cy: i32,
        rows: std::ops::Range<i32>,
    ) -> (u32, u32) {
        let (mut lit, mut shadow) = (0, 0);
        for y in rows {
            for x in 0..canvas.width as i32 {
                if !canvas.get(x, y).is_opaque() || canvas.get(x, y).rgb() != colour {
                    continue;
                }
                if crate::icons::light::is_lit(((x - cx) as f32, (y - cy) as f32)) {
                    lit += 1;
                } else {
                    shadow += 1;
                }
            }
        }
        (lit, shadow)
    }

    /// Centroid of `colour` restricted to a row band, for shapes whose new
    /// pixels sit in a band that older pixels of the same colour do not.
    fn centroid_in_rows(canvas: &Canvas, colour: Rgb, rows: std::ops::Range<i32>) -> Option<(f32, f32)> {
        let (mut sx, mut sy, mut n) = (0.0f32, 0.0f32, 0u32);
        for y in rows {
            for x in 0..canvas.width as i32 {
                if canvas.get(x, y).is_opaque() && canvas.get(x, y).rgb() == colour {
                    sx += x as f32;
                    sy += y as f32;
                    n += 1;
                }
            }
        }
        (n > 0).then(|| (sx / n as f32, sy / n as f32))
    }

    /// Every other directional shape, drawn alone on a blank canvas with
    /// colours the test picks.
    ///
    /// The centroid heuristic this uses would be worthless over a *finished*
    /// icon — `BLADE_EDGE` is `N8` and a whole shield rim is `B4`, so material
    /// uses of a highlight entry swamp the actual highlights, and the emissive
    /// shapes would rank worst precisely because they are correct. Over one
    /// shape in isolation, with its two colours chosen here, the same
    /// measurement is exact. That difference is why this is a test and why
    /// there is no `artgen light` report next to it.
    ///
    /// **Each case measures the pixels this shape's light pass actually
    /// writes, not the shape's overall bright mass**, and that distinction is
    /// the whole difference between an assertion and a decoration. The first
    /// version of this test compared whole-colour centroids and was green on
    /// the *previous* code for four of its six rows: `flask` measured its
    /// pre-existing authored glint rather than the new lit wall (deleting the
    /// wall entirely left it passing), and `skull`, `droplet` and `scale` were
    /// each dominated by pixels that predate the change. Every row below now
    /// fails when the pass it names is deleted *and* when it is inverted.
    #[test]
    fn every_directional_shape_lights_toward_the_lamp() {
        // droplet: the specular is derived, so pin it against the derivation
        // rather than against "somewhere up and left". The form it replaced sat
        // on the left flank at the same y as the centre, which is up-left of
        // the body centroid and would pass a looser check.
        let mut canvas = new_icon();
        droplet(&mut canvas, 16, 20, 6, B2, B5);
        let spec = centroid(&canvas, B5).expect("droplet drew no highlight");
        let want = offset((16, 20), (-LIGHT.0, -LIGHT.1), 5.5);
        let drift = ((spec.0 - want.0 as f32).powi(2) + (spec.1 - want.1 as f32).powi(2)).sqrt();
        assert!(
            drift < 1.5,
            "droplet specular at {spec:?} is {drift:.1}px off the rim point facing \
             the lamp, {want:?} — a highlight on the flank passes a centroid test \
             and is still a lamp in the wrong place"
        );

        // flask: the lit wall is B2 over the B1 glass. Measuring B5 would
        // measure the authored glint, which this change never touched.
        let mut canvas = new_icon();
        flask(&mut canvas, B1, B5);
        let (lit, shadow) = split_mass(&canvas, B2, 16, 20, 0..32);
        assert!(
            lit > 0 && shadow == 0,
            "flask's lit glass wall must sit entirely on the lamp's side of the \
             body: {lit} lit px, {shadow} shadow px"
        );

        // scale: the face disc is pushed toward the lamp, so `body` survives as
        // a crescent on the far side. Pushed straight up — the form this
        // replaced — the face centroid sits on cx and this fails.
        let mut canvas = new_icon();
        scale(&mut canvas, 16, 16, 6, B1, B2, B5);
        let face = centroid(&canvas, B2).expect("scale drew no face");
        assert!(
            face.0 < 16.0 && face.1 < 16.0,
            "scale's face at {face:?} is not offset toward the lamp from (16, 16); \
             straight up is a lamp directly overhead and nowhere else"
        );

        // skull: the form-shadow crescent, which is a different phenomenon from
        // the jaw's contact band. The row range is what makes this an
        // assertion — the band at `top + 12` is also `shade` and is correct
        // under any light, and unrestricted it satisfies the check on its own,
        // leaving the crescent free to be deleted with the suite green.
        let mut canvas = new_icon();
        skull(&mut canvas, 16, 4, N8, N6);
        let (lit, shadow) = split_mass(&canvas, N6, 16, 11, 4..16);
        assert!(
            shadow > 4 && shadow > lit * 3,
            "skull's cranium must carry a form shadow on the side away from the \
             lamp: {lit} lit px against {shadow} shadow px above the jaw"
        );

        // gem and shield: whole-colour centroids are honest here, because the
        // light pass *is* what places those colours.
        let mut canvas = new_icon();
        gem(&mut canvas, 16, 4, 10, 24, P1, P3, B5);
        let body = centroid(&canvas, P1).expect("gem drew no body");
        let face = centroid(&canvas, P3).expect("gem drew no face");
        assert!(
            toward_light(body, face) > 0.0,
            "gem's face at {face:?} is not toward the lamp from its body at {body:?}"
        );

        let mut canvas = new_icon();
        shield(&mut canvas, 16, 4, 24, 24, B1, B5, B2);
        let rim = centroid(&canvas, B5).expect("shield drew no lit rim");
        let shade = centroid(&canvas, B2).expect("shield drew no shadow rim");
        assert!(
            toward_light(shade, rim) > 0.0,
            "shield's lit rim at {rim:?} is not toward the lamp from its \
             shadow rim at {shade:?}"
        );
    }

    /// The two silhouette rules, over one outline. Shared so that a new plate
    /// shape inherits the check by being added to the sweep below rather than by
    /// someone remembering to re-derive what "not a spearhead" means.
    ///
    /// Both properties are needed. **A blunt base** — the widest authored shield
    /// and the narrowest must both end in a flat, or the small ones degenerate
    /// back into the shape this replaced. And **a convex taper** — the flank at
    /// the knee has to sit outside the straight line from the shoulder to the
    /// base, because a straight taper turns in evenly all the way down and that
    /// is exactly what reads as a blade.
    ///
    /// The shoulder and the knee are found by row rather than by index, because
    /// the heater carries eight vertices and the tower ten: on the right flank,
    /// the lowest vertex above the base is the knee and the one above it is the
    /// shoulder. Indexing would have made this helper silently wrong for the
    /// second caller while staying green for the first.
    fn assert_not_a_spearhead(outline: &[(i32, i32)], bottom: i32, what: &str) {
        let mut base: Vec<i32> = outline
            .iter()
            .filter(|(_, y)| *y == bottom)
            .map(|(x, _)| *x)
            .collect();
        assert_eq!(base.len(), 2, "{what}: base is not a flat pair");
        base.sort_unstable();
        assert!(
            (base[1] - base[0]) / 2 >= 1,
            "{what}: ends in a {}px base - a point, which is the spearhead this \
             shape exists to stop being",
            base[1] - base[0] + 1
        );

        let mut flank: Vec<(i32, i32)> = outline
            .iter()
            .copied()
            .filter(|(x, y)| *x > 16 && *y < bottom)
            .collect();
        flank.sort_by_key(|(_, y)| *y);
        let knee = flank[flank.len() - 1];
        let shoulder = flank[flank.len() - 2];

        let t = (knee.1 - shoulder.1) as f32 / (bottom - shoulder.1) as f32;
        let straight = shoulder.0 as f32 + (base[1] - shoulder.0) as f32 * t;
        assert!(
            knee.0 as f32 > straight,
            "{what}: the knee at x={} is inside the straight taper's {straight:.1}, \
             so the flank runs straight from shoulder to base",
            knee.0
        );
    }

    /// No plate in the set may read as a spearhead, at any authored size.
    ///
    /// This is a *silhouette* rule rather than a lighting one, which is why it
    /// is its own test: the shape shipped for the whole life of the set with
    /// straight flanks for the top 45% and then two straight lines to a single
    /// pixel, so the bottom 55% of it was a plain triangle and the eye read the
    /// point instead of the plate. Every check that existed stayed green, since
    /// a triangle is as on-ramp, on-grid and hard-alpha as a shield is.
    ///
    /// Swept over the authored size range rather than one call, for the reason
    /// the blade sweep covers 32 angles: the call sites span 11px to 28px wide,
    /// and proportions expressed as percentages of height are precisely the
    /// thing that survives at one size and collapses at another. Fixing the
    /// heater alone is what left the tower pointed for a release, so both
    /// outlines are driven here.
    ///
    /// **What this does not cover, stated so it is not mistaken for coverage:**
    /// the four hand-drawn plates that splice `blunt_taper` onto their own tops
    /// — `metallicize`, `bramble_mail` and `scaled_hide` in `cards.rs`, and
    /// `warded_bracer` in `relics.rs`. Their outlines are literals inside icon
    /// bodies rather than functions, so nothing here can reach them; they are
    /// held only by the shared taper they call. This is the same standing
    /// exemption ART_SPEC section 10 records for the hand-placed highlights.
    #[test]
    fn no_plate_outline_is_a_spearhead_at_any_size() {
        for width in [11, 15, 18, 22, 26, 28] {
            let height = width + 1;
            let half = width / 2;
            let bottom = 2 + height;

            assert_not_a_spearhead(
                &shield_outline(16, 2, half, height),
                bottom,
                &format!("heater at width {width}"),
            );
            assert_not_a_spearhead(
                &tower_shield_outline(16, 2, half, height),
                bottom,
                &format!("tower at width {width}"),
            );
        }
    }

    /// The shared taper itself, held directly rather than only through the two
    /// outlines that splice it in.
    ///
    /// Worth its own test because `blunt_taper` is now the single place the rule
    /// lives: a caller that stopped using it would fail the sweep above, but a
    /// caller that kept using it while the *primitive* collapsed would fail
    /// nothing else. `(half / 5).max(1)` is exactly the kind of clamp that reads
    /// as belt-and-braces and is load-bearing at 11px, where `half / 5` is 0.
    #[test]
    fn the_shared_taper_always_ends_in_a_flat() {
        for width in [11, 15, 18, 22, 26, 28] {
            let half = width / 2;
            let [knee_r, base_r, base_l, knee_l] = blunt_taper(16, half, 20, 24);

            assert_eq!(base_r.1, 24, "width {width}: right base is off the bottom row");
            assert_eq!(base_l.1, 24, "width {width}: left base is off the bottom row");
            assert!(
                base_r.0 > base_l.0,
                "width {width}: the base collapsed to a {}px point",
                base_r.0 - base_l.0 + 1
            );
            assert!(
                knee_r.0 > base_r.0 && knee_l.0 < base_l.0,
                "width {width}: the knee is no wider than the base, so there is no taper"
            );
            assert_eq!(
                (knee_r.0 - 16, knee_r.1),
                (16 - knee_l.0, knee_l.1),
                "width {width}: the taper is not symmetric about the centre"
            );
        }
    }

    /// `raised_fist` takes no colour parameters, so it is checked against the
    /// ramp entries it hardcodes rather than through the table above.
    ///
    /// Restricted to the flank rows, and that is what makes it an assertion.
    /// Measured over the whole icon, the `R2` centroid is dominated by the
    /// pre-existing wrist block sitting fourteen rows below the knuckles — so
    /// the check passed with the new flank deleted, and passed again with the
    /// flank moved to the *lit* side.
    #[test]
    fn the_fist_shades_the_flank_the_lamp_misses() {
        let mut canvas = new_icon();
        raised_fist(&mut canvas);
        let flank = centroid_in_rows(&canvas, R2, 11..22)
            .expect("fist drew no shadow between the knuckles and the wrist");
        assert!(
            flank.0 > 16.0,
            "fist's flank shadow at {flank:?} is on the lamp's side of the hand"
        );
        let knuckles = centroid(&canvas, R4).expect("fist drew no highlight");
        assert!(
            knuckles.1 < flank.1,
            "fist's knuckles at {knuckles:?} are not above its flank shadow at {flank:?}"
        );
    }
}
