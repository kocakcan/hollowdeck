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

/// A blade from `hilt` to `tip`: parallel-sided for most of its length, then
/// tapering to a point. `half_width` 1 gives a 2–3px blade, which is the
/// thinnest that still reads once outlined.
///
/// `edge` paints the leading side one pixel bright, which is where nearly all
/// of a weapon icon's readability comes from at this size — a flat silhouette
/// reads as a stick.
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

    let from = offset(hilt, across, -half_width);
    let to = offset(shoulder, across, -half_width);
    canvas.line(from.0, from.1, to.0, to.1, edge);
}

/// Blade plus crossguard, grip and pommel — the full sword used by Strike and
/// every icon that quotes it.
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

/// Heater shield: square shoulders, straight sides for the top 45%, then a
/// taper to a point. `width` is the full span.
pub fn shield(canvas: &mut Canvas, cx: i32, top: i32, width: i32, height: i32, face: Rgb, rim: Rgb) {
    let half = width / 2;
    let shoulder = top + height * 45 / 100;
    let outline = [
        (cx - half, top),
        (cx + half, top),
        (cx + half, shoulder),
        (cx, top + height),
        (cx - half, shoulder),
    ];
    canvas.poly(&outline, rim);
    let inset = [
        (cx - half + 2, top + 2),
        (cx + half - 2, top + 2),
        (cx + half - 2, shoulder),
        (cx, top + height - 3),
        (cx - half + 2, shoulder),
    ];
    canvas.poly(&inset, face);
}

/// Teardrop with the point up — poison, blood, any liquid.
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
    canvas.set(cx - radius + 1, cy, highlight);
    canvas.set(cx - radius + 1, cy - 1, highlight);
}

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

/// Skull: cranium, two socket voids and a jaw. Deliberately blocky — a
/// detailed skull at 32px turns to noise the moment it is outlined.
pub fn skull(canvas: &mut Canvas, cx: i32, top: i32, bone: Rgb, shade: Rgb) {
    canvas.disc(cx, top + 7, 7, bone);
    canvas.rect(cx - 6, top + 7, 13, 5, bone);
    canvas.rect(cx - 4, top + 12, 9, 4, bone);
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

/// Potion flask: round body, short neck, cork. The liquid fills the body from
/// `fill_top` down, so the same base reads as full or half-empty.
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
pub fn raised_fist(canvas: &mut Canvas) {
    canvas.rect(7, 7, 18, 15, R3);
    canvas.rect(4, 13, 5, 8, R3);
    for x in [9, 13, 17, 21] {
        canvas.disc(x, 8, 2, R4);
    }
    for x in [11, 15, 19] {
        canvas.vline(x, 6, 9, N0);
    }
    canvas.hline(8, 15, 17, N0);
    canvas.rect(9, 22, 14, 7, R2);
}

/// A jagged crack — damage, vulnerability, anything broken.
pub fn crack(canvas: &mut Canvas, points: &[(i32, i32)], colour: Rgb) {
    for pair in points.windows(2) {
        canvas.line(pair[0].0, pair[0].1, pair[1].0, pair[1].1, colour);
    }
}

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
    canvas.poly(
        &[
            (cx - crown + 1, top + 2),
            (cx + crown - 1, top + 2),
            (cx + half_width - 3, shoulder),
            (cx, top + height - 3),
            (cx - half_width + 3, shoulder),
        ],
        face,
    );
    // Nearly all of a gem's readability at this size is the table being one
    // step lighter than everything under it.
    canvas.poly(
        &[
            (cx - crown + 1, top + 2),
            (cx + crown - 1, top + 2),
            (cx + half_width - 4, shoulder - 1),
            (cx - half_width + 4, shoulder - 1),
        ],
        table,
    );
}

/// One armour scale: a rounded plate with a lit upper arc. `misc::plating`
/// tiles it into a field, `cards::scaled_hide` lays it over a body.
pub fn scale(canvas: &mut Canvas, cx: i32, cy: i32, radius: i32, body: Rgb, face: Rgb, lit: Rgb) {
    canvas.disc(cx, cy, radius, body);
    canvas.disc(cx, cy - 1, radius - 1, face);
    canvas.hline(cx - radius + 2, cy - radius, radius * 2 - 3, lit);
}

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
