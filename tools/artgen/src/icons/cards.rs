//! The thirty card icons.
//!
//! These carry the most identity of any category — a card's art window is 96px
//! (`PixelSpec.CardArtScale` 3x), the largest any icon is ever drawn, and it is
//! what a player reads when deciding what to play. So this is the one set
//! where shapes are allowed a second and third detail.
//!
//! Two rules hold the set together. **Attacks are weapons, Skills are not:**
//! every Attack quotes `shapes::blade`, a hammer or a spear, and no Skill
//! does, so card type is legible from the art before the frame colour is read.
//! **Families share a base:** the four poison cards all carry the same V-ramp
//! drip, the shield cards all carry the same heater silhouette. A card that
//! looks like Strike does something like Strike.

use super::shapes::*;
use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        Icon { category: "cards", name: "strike", draw: strike },
        Icon { category: "cards", name: "defend", draw: defend },
        Icon { category: "cards", name: "bash", draw: bash },
        Icon { category: "cards", name: "cleave", draw: cleave },
        Icon { category: "cards", name: "iron_wave", draw: iron_wave },
        Icon { category: "cards", name: "shrug_it_off", draw: shrug_it_off },
        Icon { category: "cards", name: "thunderclap", draw: thunderclap },
        Icon { category: "cards", name: "flex", draw: flex },
        Icon { category: "cards", name: "clothesline", draw: clothesline },
        Icon { category: "cards", name: "focus", draw: focus },
        Icon { category: "cards", name: "twin_strike", draw: twin_strike },
        Icon { category: "cards", name: "heavy_blow", draw: heavy_blow },
        Icon { category: "cards", name: "quick_slash", draw: quick_slash },
        Icon { category: "cards", name: "poison_dart", draw: poison_dart },
        Icon { category: "cards", name: "venom_strike", draw: venom_strike },
        Icon { category: "cards", name: "toxic_cloud", draw: toxic_cloud },
        Icon { category: "cards", name: "reckless_charge", draw: reckless_charge },
        Icon { category: "cards", name: "blood_ritual", draw: blood_ritual },
        Icon { category: "cards", name: "second_skin", draw: second_skin },
        Icon { category: "cards", name: "fortify", draw: fortify },
        Icon { category: "cards", name: "battle_trance", draw: battle_trance },
        Icon { category: "cards", name: "adrenaline", draw: adrenaline },
        Icon { category: "cards", name: "war_cry", draw: war_cry },
        Icon { category: "cards", name: "riposte", draw: riposte },
        Icon { category: "cards", name: "crippling_blow", draw: crippling_blow },
        Icon { category: "cards", name: "whirlwind", draw: whirlwind },
        Icon { category: "cards", name: "meditate", draw: meditate },
        Icon { category: "cards", name: "bloodletting", draw: bloodletting },
        Icon { category: "cards", name: "impale", draw: impale },
        Icon { category: "cards", name: "last_stand", draw: last_stand },
    ]
}

// -- attacks: blades -------------------------------------------------------

/// The base card the whole Attack half of the set quotes.
fn strike() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (8, 25), (27, 6), BLADE, BLADE_EDGE);
    finish(&mut canvas);
    canvas
}

/// Two blades, parallel rather than crossed — crossed would read as the map's
/// `fight` node, and this is a card.
fn twin_strike() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (4, 27), (17, 4), 1.5, BLADE, BLADE_EDGE);
    canvas.thick_line(2, 24, 8, 28, 2, GUARD);
    blade(&mut canvas, (15, 27), (28, 4), 1.5, BLADE, BLADE_EDGE);
    canvas.thick_line(13, 24, 19, 28, 2, GUARD);
    finish(&mut canvas);
    canvas
}

/// Cheap and fast: a thin blade with motion lines instead of a hilt, so the
/// silhouette itself says "less weapon, more speed".
fn quick_slash() -> Canvas {
    let mut canvas = new_icon();
    canvas.line(3, 9, 13, 4, N5);
    canvas.line(2, 15, 11, 10, N5);
    canvas.line(3, 21, 10, 17, N5);
    blade(&mut canvas, (9, 27), (28, 6), 1.0, BLADE, BLADE_EDGE);
    finish(&mut canvas);
    canvas
}

/// A green drip on the shared blade — the poison family's mark, matching the
/// Poison status icon's V ramp.
fn venom_strike() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (8, 24), (26, 6), BLADE, BLADE_EDGE);
    droplet(&mut canvas, 12, 24, 3, V2, V4);
    droplet(&mut canvas, 21, 15, 2, V2, V4);
    finish(&mut canvas);
    canvas
}

/// A parry: one blade catching another, with the spark at the contact point
/// doing the narrative work.
fn riposte() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (2, 6), (24, 20), 1.5, N5, N6);
    sword(&mut canvas, (9, 28), (27, 7), BLADE, BLADE_EDGE);
    sparkle(&mut canvas, 20, 15, 5, G5);
    finish(&mut canvas);
    canvas
}

/// An axe, not a sword: Cleave hits every enemy, and a crescent head is the
/// shape that carries a sweep. A sword under a swing-arc was tried first and
/// read as a pickaxe — two thin shapes crossing at a shallow angle merge.
fn cleave() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(19, 14, 12, BLADE_EDGE);
    canvas.disc(19, 14, 10, BLADE);
    canvas.erase_disc(9, 14, 11);
    canvas.thick_line(3, 29, 16, 16, 3, WOOD);
    canvas.disc(4, 28, 2, GUARD);
    finish(&mut canvas);
    canvas
}

/// A full circle of the same swing — the AoE sibling of Cleave, and the only
/// icon in the set that closes its arc.
fn whirlwind() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(16, 16, 15, 3, N6);
    canvas.ring(16, 16, 10, 2, N4);
    // Break the ring where the blade sweeps out of it, so the two shapes read
    // as one motion rather than as a sword laid on a doughnut.
    canvas.erase_rect(2, 2, 13, 10);
    blade(&mut canvas, (6, 8), (26, 25), 1.5, BLADE, BLADE_EDGE);
    finish(&mut canvas);
    canvas
}

/// A spear, not a sword: single target, high damage, long reach.
fn impale() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(3, 28, 23, 9, 2, WOOD);
    canvas.poly(&[(21, 11), (30, 2), (27, 12), (19, 15)], BLADE);
    canvas.line(28, 4, 22, 12, BLADE_EDGE);
    canvas.thick_line(18, 16, 24, 11, 2, GUARD);
    finish(&mut canvas);
    canvas
}

/// A broken blade planted point-down: the last-ditch attack, and the only
/// weapon in the set drawn as damaged.
fn last_stand() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(3, 26, 27, 4, N4);
    canvas.hline(3, 26, 27, N5);
    blade(&mut canvas, (16, 28), (16, 6), 2.0, BLADE, BLADE_EDGE);
    canvas.thick_line(10, 13, 23, 13, 2, GUARD);
    canvas.erase_poly(&[(11, 4), (21, 8), (11, 11)]);
    canvas.poly(&[(20, 3), (14, 7), (21, 9)], BLADE);
    finish(&mut canvas);
    canvas
}

// -- attacks: blunt and thrown --------------------------------------------

/// A mace head mid-impact. Bash applies Vulnerable, so it carries the same
/// E-ramp burst the Vulnerable status uses.
fn bash() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(2, 29, 13, 18, 3, WOOD);
    canvas.disc(18, 13, 8, N5);
    canvas.disc(18, 13, 6, N6);
    for (dx, dy) in [(-9, 0), (9, 0), (0, -9), (0, 9), (-7, -7), (7, 7), (-7, 7), (7, -7)] {
        canvas.disc(18 + dx, 13 + dy, 2, N5);
    }
    impact(&mut canvas, 18, 13, E3);
    finish(&mut canvas);
    canvas
}

/// The heavy version of Bash: a two-handed hammer, no impact burst — the
/// weight is the point, not the effect.
fn heavy_blow() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(3, 29, 16, 16, 3, WOOD);
    canvas.poly(&[(10, 13), (24, 3), (30, 11), (17, 21)], N5);
    canvas.poly(&[(12, 13), (23, 5), (27, 10), (17, 18)], N6);
    canvas.rect(18, 9, 4, 6, N4);
    finish(&mut canvas);
    canvas
}

/// Crippling Blow applies Weak, so the hammer lands on a crack — the same
/// jagged N0 fracture the Vulnerable icon uses.
fn crippling_blow() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(2, 4, 13, 13, 3, WOOD);
    canvas.poly(&[(10, 11), (20, 2), (27, 9), (17, 19)], N5);
    canvas.poly(&[(12, 11), (20, 5), (24, 9), (17, 16)], N6);
    canvas.disc(18, 26, 8, N4);
    canvas.disc(18, 26, 6, N5);
    crack(&mut canvas, &[(11, 23), (17, 27), (14, 31), (22, 31)], N0);
    crack(&mut canvas, &[(17, 27), (25, 23)], N0);
    finish(&mut canvas);
    canvas
}

/// Reckless Charge: horns, head-down. The one Attack with no weapon at all,
/// which is exactly what "reckless" means here.
fn reckless_charge() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(2, 3), (11, 10), (13, 19), (7, 14), (2, 8)], N7);
    canvas.poly(&[(30, 3), (21, 10), (19, 19), (25, 14), (30, 8)], N7);
    canvas.disc(16, 19, 9, R2);
    canvas.disc(16, 19, 7, R3);
    canvas.rect(11, 16, 4, 3, N0);
    canvas.rect(17, 16, 4, 3, N0);
    canvas.hline(13, 25, 7, N0);
    finish(&mut canvas);
    canvas
}

/// An outstretched arm, not a weapon — the same R-ramp limb the Strength
/// status draws, thrown forward instead of flexed.
fn clothesline() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(2, 13, 17, 8, R2);
    canvas.hline(3, 14, 16, R3);
    // The same fist as Strength and Flex, turned to face right. A smooth ball
    // on the end of a bar is a mallet, which is what the first version was.
    canvas.rect(17, 9, 12, 15, R3);
    canvas.rect(15, 7, 8, 5, R3);
    for y in [11, 15, 19] {
        canvas.disc(28, y, 2, R4);
    }
    for y in [13, 17, 21] {
        canvas.hline(17, y, 12, N0);
    }
    canvas.vline(21, 9, 15, N0);
    canvas.line(3, 26, 13, 24, N5);
    canvas.line(3, 8, 13, 10, N5);
    finish(&mut canvas);
    canvas
}

/// A thrown dart, tail-first from the lower left, with the poison family's
/// green tip.
fn poison_dart() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(6, 26, 22, 10, 2, WOOD);
    canvas.poly(&[(20, 12), (30, 2), (27, 13), (18, 16)], V3);
    canvas.line(28, 4, 21, 13, V5);
    canvas.poly(&[(2, 29), (11, 24), (8, 21), (2, 22)], N6);
    finish(&mut canvas);
    canvas
}

/// A lightning burst with a shock ring — Thunderclap hits every enemy, so the
/// ring is the AoE tell it shares with Whirlwind.
fn thunderclap() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(16, 17, 14, 2, G2);
    canvas.ring(16, 17, 9, 1, G1);
    canvas.poly(
        &[(19, 2), (9, 17), (15, 17), (11, 30), (24, 13), (17, 13), (23, 2)],
        G4,
    );
    canvas.poly(&[(18, 5), (12, 16), (16, 16), (14, 24)], G5);
    finish(&mut canvas);
    canvas
}

/// A cloud, not a weapon, because Toxic Cloud is the AoE poison card — but it
/// is still an Attack, so it keeps the family's drips.
fn toxic_cloud() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(9, 14, 6, V1);
    canvas.disc(21, 12, 7, V1);
    canvas.disc(16, 17, 7, V1);
    canvas.disc(26, 17, 5, V1);
    canvas.disc(9, 13, 4, V2);
    canvas.disc(21, 11, 5, V2);
    canvas.disc(16, 16, 4, V3);
    droplet(&mut canvas, 7, 27, 2, V3, V5);
    droplet(&mut canvas, 16, 29, 2, V3, V5);
    droplet(&mut canvas, 25, 26, 2, V3, V5);
    finish(&mut canvas);
    canvas
}

/// Block *and* damage in one card, so the icon is literally both halves of
/// the set: the shared shield with the shared blade over it.
fn iron_wave() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 13, 6, 22, 24, SHIELD_FACE, SHIELD_RIM);
    blade(&mut canvas, (7, 29), (29, 4), 1.5, BLADE, BLADE_EDGE);
    finish(&mut canvas);
    canvas
}

// -- skills ----------------------------------------------------------------

/// The base Skill, and the shield every defensive card quotes.
fn defend() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 3, 26, 27, SHIELD_FACE, SHIELD_RIM);
    canvas.vline(16, 8, 15, B4);
    canvas.hline(9, 13, 15, B4);
    finish(&mut canvas);
    canvas
}

/// Block that survives the turn: the same shield with a brick face. The
/// bricks are cut to the silhouette rather than clipped by hand, so the
/// outline stays identical to Defend's.
fn fortify() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 3, 26, 27, B1, SHIELD_RIM);

    let mut bricks = new_icon();
    for (row, y) in (6..26).step_by(5).enumerate() {
        let stagger = if row % 2 == 0 { 0 } else { 4 };
        for x in (2 + stagger..30).step_by(8) {
            bricks.rect(x, y, 7, 4, SHIELD_FACE);
        }
    }
    let mut interior = new_icon();
    shield(&mut interior, 16, 5, 22, 24, SHIELD_FACE, SHIELD_FACE);
    bricks.intersect(&interior);
    canvas.blit(&bricks, 0, 0);

    finish(&mut canvas);
    canvas
}

/// An arrow glancing off the shield — Block plus draw, so the shield is
/// doing something rather than just standing there.
fn shrug_it_off() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 13, 6, 22, 24, SHIELD_FACE, SHIELD_RIM);
    arrow(&mut canvas, (30, 2), (19, 13), 3, 6, N6);
    canvas.line(24, 16, 30, 20, N5);
    canvas.line(22, 20, 29, 27, N5);
    finish(&mut canvas);
    canvas
}

/// Layered plates — Block that stacks, drawn as armour that stacks.
fn second_skin() -> Canvas {
    let mut canvas = new_icon();
    for (index, y) in (4..27).step_by(7).enumerate() {
        let inset = index as i32 * 2;
        canvas.rect(3 + inset, y, 27 - inset * 2, 6, B1);
        canvas.rect(4 + inset, y + 1, 25 - inset * 2, 4, SHIELD_FACE);
        canvas.hline(4 + inset, y + 1, 25 - inset * 2, B4);
    }
    finish(&mut canvas);
    canvas
}

/// Temporary Strength: the same flexed arm as the status icon, but wrapped in
/// gold, so it reads as the buff rather than as the stat.
fn flex() -> Canvas {
    let mut canvas = new_icon();
    raised_fist(&mut canvas);
    // The wrap goes on the wrist, which is the one part of the fist carrying
    // no finger detail — banding the knuckles flattens the whole shape.
    canvas.rect(8, 23, 16, 3, G3);
    canvas.rect(9, 27, 14, 2, G2);
    sparkle(&mut canvas, 28, 5, 3, G5);
    finish(&mut canvas);
    canvas
}

/// Draw cards: the card-stack glyph the pile counters and the draw potions
/// use, at full size.
/// Draw cards: the card-stack glyph the pile counters and the draw potions
/// use, at full size. The front card leans — two upright rectangles read as
/// picture frames.
fn focus() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(13, 2, 17, 23, N0);
    canvas.rect(14, 3, 15, 21, N5);
    canvas.poly(&[(3, 12), (16, 6), (24, 25), (11, 30)], N0);
    canvas.poly(&[(5, 13), (15, 8), (22, 24), (12, 28)], N7);
    canvas.poly(&[(9, 15), (14, 13), (17, 22), (12, 24)], N4);
    finish(&mut canvas);
    canvas
}

/// Energy: the bolt, matching the combat HUD's energy orb.
fn adrenaline() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(16, 16, 13, G1);
    canvas.disc(16, 16, 11, G2);
    canvas.poly(
        &[(19, 3), (8, 18), (15, 18), (12, 29), (25, 13), (18, 13), (23, 3)],
        G5,
    );
    finish(&mut canvas);
    canvas
}

/// A war horn. Nothing else in the set is a horn, which matters more here
/// than realism does — War Cry needs one unmistakable shape.
/// A horn, blown up and to the right: tapered tube, flared bell, mouthpiece.
/// The first attempt drew it as one irregular curve and read as a gold blob —
/// the three parts have to be separately legible.
fn war_cry() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(3, 25), (8, 29), (23, 15), (19, 11)], G2);
    canvas.poly(&[(4, 25), (7, 28), (21, 14), (19, 12)], G3);
    canvas.poly(&[(18, 10), (29, 3), (30, 17), (24, 21)], G2);
    canvas.poly(&[(19, 12), (28, 6), (28, 16), (23, 19)], G4);
    canvas.disc(4, 27, 3, G3);
    canvas.line(19, 11, 23, 15, G1);
    // Sound: three short strokes off the bell's mouth.
    for (x, y) in [(28, 1), (31, 8), (28, 23)] {
        canvas.line(x, y, x - 3, y + 2, E3);
    }
    finish(&mut canvas);
    canvas
}

/// Blood Ritual: a chalice under a cut. Costs HP for a benefit, so the icon
/// leads with the cost.
fn blood_ritual() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(6, 11), (26, 11), (23, 22), (9, 22)], G2);
    canvas.poly(&[(8, 13), (24, 13), (22, 20), (10, 20)], R3);
    canvas.rect(15, 22, 3, 5, G2);
    canvas.rect(9, 27, 15, 3, G2);
    canvas.hline(8, 13, 16, R5);
    droplet(&mut canvas, 16, 7, 3, R4, R5);
    finish(&mut canvas);
    canvas
}

/// Bloodletting: the cut itself, on the blade rather than in a cup — the
/// sibling of Blood Ritual that pays HP for Energy instead.
fn bloodletting() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (5, 28), (25, 6), 1.0, BLADE, BLADE_EDGE);
    canvas.rect(2, 13, 13, 9, R2);
    canvas.rect(3, 14, 11, 7, R3);
    canvas.line(4, 16, 13, 20, R5);
    droplet(&mut canvas, 8, 28, 3, R4, R5);
    droplet(&mut canvas, 17, 25, 2, R4, R5);
    finish(&mut canvas);
    canvas
}

/// Battle Trance: a spiral, the one abstract mark in the set. Draw-heavy
/// cards are hard to picture, and a vortex at least reads as "a state you
/// enter" rather than as an object.
fn battle_trance() -> Canvas {
    let mut canvas = new_icon();
    spiral(&mut canvas, 16, 16, 14, R3);
    spiral(&mut canvas, 16, 16, 10, R4);
    canvas.disc(16, 16, 3, G4);
    finish_heavy(&mut canvas);
    canvas
}

/// Meditate: a seated figure. Distinct from every other Skill because it is
/// the only one whose subject is the player rather than an object.
fn meditate() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(16, 8, 5, P3);
    canvas.poly(&[(11, 13), (21, 13), (23, 22), (9, 22)], P3);
    // Arms out to the knees and crossed legs as one wide flattened diamond.
    // Without them the torso-plus-head silhouette is a chess bishop.
    canvas.thick_line(11, 15, 6, 23, 2, P3);
    canvas.thick_line(21, 15, 26, 23, 2, P3);
    canvas.poly(&[(3, 26), (16, 20), (29, 26), (16, 30)], P2);
    canvas.poly(&[(8, 26), (16, 23), (24, 26), (16, 28)], P3);
    sparkle(&mut canvas, 16, 2, 3, P4);
    finish(&mut canvas);
    canvas
}

// -- shared parts ----------------------------------------------------------

/// An Archimedean spiral, three turns, stepped fine enough that the line has
/// no gaps at this radius.
fn spiral(canvas: &mut Canvas, cx: i32, cy: i32, radius: i32, colour: Rgb) {
    let turns = 3.0;
    let steps = 300;
    for step in 0..steps {
        let t = step as f32 / steps as f32;
        let angle = t * turns * std::f32::consts::TAU;
        let r = t * radius as f32;
        canvas.set(
            cx + (angle.cos() * r).round() as i32,
            cy + (angle.sin() * r).round() as i32,
            colour,
        );
    }
}

/// A four-armed impact burst, the "this landed" mark.
fn impact(canvas: &mut Canvas, cx: i32, cy: i32, colour: Rgb) {
    for (dx, dy) in [(-1, -1), (1, -1), (-1, 1), (1, 1)] {
        canvas.line(cx + dx * 10, cy + dy * 10, cx + dx * 13, cy + dy * 13, colour);
    }
}
