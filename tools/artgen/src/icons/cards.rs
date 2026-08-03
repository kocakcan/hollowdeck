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
        Icon { category: "cards", name: "inflame", draw: inflame },
        Icon { category: "cards", name: "metallicize", draw: metallicize },
        Icon { category: "cards", name: "demon_form", draw: demon_form },
        // Attacks
        Icon { category: "cards", name: "sand_kick", draw: sand_kick },
        Icon { category: "cards", name: "pommel_strike", draw: pommel_strike },
        Icon { category: "cards", name: "sweeping_blow", draw: sweeping_blow },
        Icon { category: "cards", name: "wild_swing", draw: wild_swing },
        Icon { category: "cards", name: "dagger_throw", draw: dagger_throw },
        Icon { category: "cards", name: "venomous_nick", draw: venomous_nick },
        Icon { category: "cards", name: "rupture", draw: rupture },
        Icon { category: "cards", name: "feint", draw: feint },
        Icon { category: "cards", name: "blade_flurry", draw: blade_flurry },
        Icon { category: "cards", name: "sever", draw: sever },
        Icon { category: "cards", name: "all_in", draw: all_in },
        Icon { category: "cards", name: "annihilate", draw: annihilate },
        // Skills and Powers
        Icon { category: "cards", name: "deflect", draw: deflect },
        Icon { category: "cards", name: "brace", draw: brace },
        Icon { category: "cards", name: "hunker_down", draw: hunker_down },
        Icon { category: "cards", name: "entrench", draw: entrench },
        Icon { category: "cards", name: "aegis", draw: aegis },
        Icon { category: "cards", name: "stone_skin", draw: stone_skin },
        Icon { category: "cards", name: "war_paint", draw: war_paint },
        Icon { category: "cards", name: "bandage_up", draw: bandage_up },
        Icon { category: "cards", name: "intimidate", draw: intimidate },
        Icon { category: "cards", name: "scorched_earth", draw: scorched_earth },
        Icon { category: "cards", name: "gambit", draw: gambit },
        Icon { category: "cards", name: "mending_light", draw: mending_light },
        Icon { category: "cards", name: "phoenix_heart", draw: phoenix_heart },
        // Second content pass
        Icon { category: "cards", name: "backstep", draw: backstep },
        Icon { category: "cards", name: "cinder_slash", draw: cinder_slash },
        Icon { category: "cards", name: "lash_out", draw: lash_out },
        Icon { category: "cards", name: "steel_nerve", draw: steel_nerve },
        Icon { category: "cards", name: "sift", draw: sift },
        Icon { category: "cards", name: "jab", draw: jab },
        Icon { category: "cards", name: "barbed_guard", draw: barbed_guard },
        Icon { category: "cards", name: "cheap_shot", draw: cheap_shot },
        Icon { category: "cards", name: "footwork", draw: footwork },
        Icon { category: "cards", name: "tithe", draw: tithe },
        Icon { category: "cards", name: "second_sight", draw: second_sight },
        Icon { category: "cards", name: "hemorrhage", draw: hemorrhage },
        Icon { category: "cards", name: "bloodhound", draw: bloodhound },
        Icon { category: "cards", name: "flurry", draw: flurry },
        Icon { category: "cards", name: "bloodrite", draw: bloodrite },
        Icon { category: "cards", name: "blight_storm", draw: blight_storm },
        Icon { category: "cards", name: "purge", draw: purge },
        Icon { category: "cards", name: "stoke", draw: stoke },
        Icon { category: "cards", name: "gravebind", draw: gravebind },
        Icon { category: "cards", name: "resolve", draw: resolve },
        Icon { category: "cards", name: "skullcrack", draw: skullcrack },
        Icon { category: "cards", name: "bloodpact", draw: bloodpact },
        Icon { category: "cards", name: "deep_focus", draw: deep_focus },
        Icon { category: "cards", name: "cataclysm", draw: cataclysm },
        Icon { category: "cards", name: "last_rite", draw: last_rite },
        Icon { category: "cards", name: "reap", draw: reap },
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

/// The first Power. A fist wreathed in flame — it quotes `raised_fist` like
/// Flex and the Strength status do, because it grants the same stat, but the
/// gold wrist wrap is replaced by fire climbing past the knuckles: Flex is a
/// buff you keep re-applying, this one burns itself into the fight and leaves
/// the deck.
fn inflame() -> Canvas {
    let mut canvas = new_icon();

    raised_fist(&mut canvas);

    // Fire under the fist, not around it. Flames flanking it at the same
    // height were tried first and read as dark shoulders — at 32px there is
    // no room either side of the fist for a flame to taper, so it lands as
    // mass, and once outlined the whole thing became one blob.
    //
    // Below the wrist there is room, and "held up out of a fire" is the same
    // reading with a silhouette that survives.
    flame(&mut canvas, 16, 31, 13, E2, E3);
    flame(&mut canvas, 9, 31, 8, E1, E2);
    flame(&mut canvas, 23, 31, 8, E1, E2);

    // Embers rising past the knuckles, in the bright end of the ember ramp so
    // they read against the fist rather than into it.
    canvas.disc(5, 8, 1, E3);
    canvas.disc(27, 11, 1, E4);
    canvas.disc(29, 4, 1, E3);
    canvas.disc(3, 17, 1, E4);

    finish(&mut canvas);
    canvas
}

/// Metallicize the card: a riveted breastplate, which is the status icon's
/// banded plating wrapped onto a body. The two share a material and a rivet
/// pattern on purpose - the card grants the status, so they should read as the
/// same substance at two scales.
fn metallicize() -> Canvas {
    let mut canvas = new_icon();

    // Breastplate. Pauldrons wider than the waist, with a notch cut for the
    // neck: a plate that simply tapered from square shoulders to a point read
    // as a heater shield, which is the one shape this icon had to stay clear
    // of - Block already has three shields.
    canvas.poly(&[(3, 8), (13, 8), (16, 11), (19, 8), (29, 8), (26, 15), (24, 26), (16, 29), (8, 26), (6, 15)], B2);
    canvas.poly(&[(6, 10), (12, 10), (16, 13), (20, 10), (26, 10), (24, 15), (22, 25), (16, 27), (10, 25), (8, 15)], B1);
    // Pauldron caps, one step brighter, so the shoulders read as separate
    // pieces bolted on rather than as part of one silhouette.
    canvas.rect(3, 8, 5, 4, B3);
    canvas.rect(24, 8, 5, 4, B3);

    // Bands across it, brightening downward so the plate reads as curved.
    for (y, colour) in [(11, B2), (16, B3), (21, B2)] {
        canvas.hline(9, y, 15, colour);
        canvas.hline(9, y + 1, 15, B0);
    }
    // Rivets, the same pair-per-band the status icon uses.
    for y in [16, 21] {
        canvas.set(11, y, B5);
        canvas.set(21, y, B5);
    }

    finish(&mut canvas);
    canvas
}

/// Demon Form: a horned head, eyes lit. Quotes `ritual`'s horns because that
/// is the status it grants, and stays clear of the boss node's skull - this
/// one has a solid face rather than a cranium and sockets, so the two do not
/// collide on a reward screen.
fn demon_form() -> Canvas {
    let mut canvas = new_icon();

    canvas.poly(&[(3, 2), (12, 10), (8, 13), (2, 7)], P2);
    canvas.poly(&[(29, 2), (20, 10), (24, 13), (30, 7)], P2);

    // Head: wide brow tapering to a chin.
    canvas.poly(&[(8, 9), (24, 9), (22, 22), (16, 29), (10, 22)], P2);
    canvas.poly(&[(10, 11), (22, 11), (20, 21), (16, 26), (12, 21)], P1);

    // Eyes are the whole face. Angled inward, which is the difference between
    // a demon and an owl.
    canvas.poly(&[(11, 14), (16, 16), (16, 18), (11, 17)], R4);
    canvas.poly(&[(21, 14), (16, 16), (16, 18), (21, 17)], R4);
    canvas.set(13, 15, R5);
    canvas.set(19, 15, R5);

    // Mouth, one dark line - a full jaw of teeth turns to noise at 32px.
    canvas.hline(13, 22, 7, P0);

    finish_heavy(&mut canvas);
    canvas
}

// -- attacks: the Dexterity/Frail generation --------------------------------

/// A boot throwing a spray of grit. Sand Kick applies Frail, and the whole
/// point of the icon is that it is *not* a weapon — the card is a Common
/// Attack, but what lands is dirt in the eyes, which is also why it debuffs
/// rather than hits hard. The one Attack in the set allowed to break the
/// weapon rule, because a blade here would promise damage it doesn't deal.
fn sand_kick() -> Canvas {
    let mut canvas = new_icon();

    // Boot: shin, then a forward-jutting sole.
    canvas.poly(&[(4, 6), (12, 6), (13, 20), (5, 20)], N4);
    canvas.poly(&[(4, 18), (22, 20), (22, 26), (4, 26)], N3);
    canvas.hline(4, 25, 19, N5);
    canvas.hline(4, 18, 9, N5);

    // The grit, thrown forward and up in a widening cone. Irregular sizes
    // on purpose - an even scatter reads as a pattern, not as debris.
    for (x, y, r) in [(24, 20, 2), (28, 16, 2), (25, 11, 1), (29, 24, 1), (22, 8, 2), (30, 8, 1)] {
        canvas.disc(x, y, r, G2);
    }
    for (x, y) in [(26, 18), (23, 13), (29, 21)] {
        canvas.set(x, y, G4);
    }

    finish(&mut canvas);
    canvas
}

/// A sword held reversed, striking with the pommel. Pommel Strike draws a
/// card, and a hit that leaves you *better set up* is the one Attack that
/// should not lead with its edge — so the icon inverts Strike's own geometry
/// rather than inventing a new weapon.
fn pommel_strike() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (22, 8), (5, 27), BLADE, BLADE_EDGE);
    // The pommel enlarged and brought forward, with the impact burst Bash
    // uses, so the business end reads as the blunt one.
    canvas.disc(25, 5, 4, GUARD);
    canvas.disc(25, 5, 2, G4);
    impact(&mut canvas, 25, 5, G5);
    finish(&mut canvas);
    canvas
}

/// A low horizontal arc with the shared shield behind it: Sweeping Blow hits
/// everything and gives Block, so the icon carries both halves the way Iron
/// Wave does, but swung flat rather than held.
fn sweeping_blow() -> Canvas {
    let mut canvas = new_icon();

    // The sweep is carved first and the shield laid over it. Order matters:
    // the crescent is cut with erases, and anything already on the canvas
    // when they run gets cut too - the shield was drawn first once and the
    // erase_rect took the whole of it, leaving a bare arc.
    canvas.disc(16, 16, 15, BLADE_EDGE);
    canvas.disc(16, 16, 13, BLADE);
    canvas.erase_disc(16, 13, 14);
    canvas.erase_rect(0, 0, GRID, 18);
    canvas.thick_line(2, 25, 9, 29, 2, WOOD);

    shield(&mut canvas, 16, 1, 20, 19, SHIELD_FACE, SHIELD_RIM);

    finish(&mut canvas);
    canvas
}

/// A blade swung so hard cards come loose. Wild Swing discards at random, and
/// the loose cards are the cost made visible — they quote Focus's card-stack
/// glyph so the two read as the same object arriving and leaving.
fn wild_swing() -> Canvas {
    let mut canvas = new_icon();

    // Blade angled hard across the icon, tip into the *top-left* corner. The
    // first version pointed it top-right into the loose cards, and a pale
    // quadrilateral sitting on a blade tip reads as a hammer head - the cards
    // have to be clear of the weapon's line entirely.
    blade(&mut canvas, (22, 29), (3, 8), 2.0, BLADE, BLADE_EDGE);
    canvas.thick_line(19, 26, 26, 31, 2, GUARD);

    // Two cards knocked loose to the right, drawn as tall leaning rectangles
    // rather than tumbling diamonds - a rotated square is a gem at this size,
    // and only the long edges say "card".
    canvas.poly(&[(20, 2), (29, 5), (25, 17), (16, 14)], N0);
    canvas.poly(&[(21, 4), (27, 6), (24, 15), (18, 13)], N8);
    canvas.disc(22, 9, 1, N3);
    canvas.poly(&[(23, 18), (31, 22), (28, 31), (20, 27)], N0);
    canvas.poly(&[(24, 20), (29, 23), (27, 29), (22, 26)], N7);

    finish(&mut canvas);
    canvas
}

/// A dagger in flight, point-first, with the motion lines Quick Slash uses.
/// Shorter than any blade in the set and drawn with no grip in a hand, which
/// is what says "thrown" — and it exhausts, so it is gone once it lands.
fn dagger_throw() -> Canvas {
    let mut canvas = new_icon();
    canvas.line(2, 8, 12, 12, N5);
    canvas.line(2, 16, 11, 18, N5);
    canvas.line(3, 24, 12, 23, N5);
    blade(&mut canvas, (12, 21), (28, 9), 2.0, BLADE, BLADE_EDGE);
    canvas.thick_line(9, 18, 15, 25, 2, GUARD);
    canvas.disc(8, 23, 2, GRIP);
    finish(&mut canvas);
    canvas
}

/// The smallest blade in the poison family, carrying the same V-ramp drip
/// Venom Strike and the Poison status use. A nick, not a wound: the blade is
/// short and the drip is the larger shape, because the damage is 2 and the
/// Poison is the card.
fn venomous_nick() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (6, 26), (19, 12), 1.5, BLADE, BLADE_EDGE);
    canvas.thick_line(3, 23, 9, 29, 2, GUARD);
    droplet(&mut canvas, 23, 12, 5, V2, V4);
    canvas.disc(20, 20, 2, V1);
    canvas.disc(27, 21, 1, V1);
    finish(&mut canvas);
    canvas
}

/// The heavy poison card: the same drip, burst open. Where Venomous Nick is
/// one droplet leaving the blade, Rupture is the droplet hitting something —
/// the splash crown is what separates 2 Poison from 4 at a glance.
fn rupture() -> Canvas {
    let mut canvas = new_icon();

    // Pool first, for the same ordering reason as Sweeping Blow - it is built
    // with erases and would take the blade with it.
    canvas.disc(16, 27, 11, V1);
    canvas.erase_rect(0, 0, GRID, 24);
    canvas.disc(16, 27, 8, V2);
    canvas.erase_rect(0, 0, GRID, 25);

    blade(&mut canvas, (2, 6), (19, 19), 2.0, BLADE, BLADE_EDGE);
    canvas.thick_line(1, 3, 6, 9, 2, GUARD);

    // Thrown droplets over the pool: the splash crown that separates 4 Poison
    // from Venomous Nick's single drip.
    for (x, y, r) in [(8, 20, 2), (22, 15, 3), (28, 21, 2)] {
        droplet(&mut canvas, x, y, r, V2, V4);
    }
    canvas.disc(13, 27, 1, V4);
    canvas.disc(21, 29, 1, V4);

    finish(&mut canvas);
    canvas
}

/// A blade coming in from behind a raised shield. Feint hits, blocks and
/// applies Frail all at once, so it borrows from both halves of the set —
/// but the shield is in front, because the card is a trick rather than a
/// trade.
fn feint() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (28, 28), (10, 4), 1.5, BLADE, BLADE_EDGE);
    shield(&mut canvas, 14, 10, 20, 21, SHIELD_FACE, SHIELD_RIM);
    // The Frail mark: the same fracture the Vulnerable status and Crippling
    // Blow use, so a cracked surface always means a debuff landed.
    crack(&mut canvas, &[(14, 12), (11, 18), (17, 22), (13, 29)], N0);
    finish(&mut canvas);
    canvas
}

/// Three blades fanned out. The AoE Attacks each solve "hits everything"
/// differently on purpose — Cleave has one axe arc, Whirlwind closes it into
/// a ring — and a fan is the third answer: one strike per enemy rather than
/// one strike across them.
fn blade_flurry() -> Canvas {
    let mut canvas = new_icon();
    for (tip_x, tip_y) in [(3, 3), (16, 1), (29, 3)] {
        blade(&mut canvas, (16, 29), (tip_x, tip_y), 1.5, BLADE, BLADE_EDGE);
    }
    canvas.thick_line(11, 26, 21, 26, 2, GUARD);
    canvas.disc(16, 30, 2, GUARD);
    finish(&mut canvas);
    canvas
}

/// A blade already through its target, drawn as a clean diagonal cut with the
/// two halves of the cut surface pulling apart. Sever is the biggest single
/// hit in the Uncommons and applies both Frail and Vulnerable — the gap is
/// the icon, not the sword.
fn sever() -> Canvas {
    let mut canvas = new_icon();

    // The object being cut: a stone block, split along the blade's line.
    canvas.poly(&[(4, 5), (20, 5), (16, 17), (2, 17)], N4);
    canvas.poly(&[(5, 7), (18, 7), (15, 15), (4, 15)], N5);
    canvas.poly(&[(14, 19), (30, 19), (27, 30), (11, 30)], N4);
    canvas.poly(&[(15, 21), (28, 21), (25, 28), (13, 28)], N5);

    blade(&mut canvas, (2, 29), (30, 2), 1.5, BLADE, BLADE_EDGE);
    sparkle(&mut canvas, 22, 11, 3, G5);

    finish(&mut canvas);
    canvas
}

/// Every card you hold, thrown at once. All In exhausts the hand for one huge
/// AoE hit, so the icon is the cost rather than the damage: a fan of cards
/// going up in flame, with the blade behind them.
fn all_in() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (2, 30), (17, 12), 1.5, BLADE, BLADE_EDGE);

    // One flame behind, three overlapping cards in front.
    //
    // Two earlier versions drew separated upright cards with a flame over
    // each and both read as a candelabra - three tall pale shapes with fire
    // on top is a candle, whatever the proportions. The fix is not sizing but
    // arrangement: cards that *overlap* read as a held hand, and a single
    // flame behind them cannot be mistaken for three wicks.
    flame(&mut canvas, 16, 18, 16, E2, E4);
    canvas.disc(16, 9, 4, E3);

    for (index, (x, lean)) in [(2, -3), (10, 0), (18, 3)].iter().enumerate() {
        canvas.poly(&[(*x, 31), (*x + 12, 31), (*x + 12 + lean, 16), (*x + lean, 16)], N0);
        canvas.poly(
            &[(*x + 1, 30), (*x + 11, 30), (*x + 11 + lean, 18), (*x + 1 + lean, 18)],
            if index == 2 { N8 } else { N6 },
        );
        // A pip, so they read as playing cards rather than as blank tiles.
        canvas.disc(*x + 6 + lean, 23, 1, N2);
    }

    finish(&mut canvas);
    canvas
}

/// Two blades driven through a burst. Annihilate is the other AoE Rare and
/// had to differ from All In at a glance: All In is cards, this is nothing but
/// weapon, and the X is the one blade arrangement the set has kept in reserve
/// (Twin Strike is parallel, precisely so this could be crossed).
fn annihilate() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (2, 4), (28, 28), 2.0, BLADE, BLADE_EDGE);
    blade(&mut canvas, (30, 4), (4, 28), 2.0, BLADE, BLADE_EDGE);
    canvas.disc(16, 16, 5, E1);
    canvas.disc(16, 16, 3, E3);
    impact(&mut canvas, 16, 16, E3);
    finish(&mut canvas);
    canvas
}

// -- skills: the block family ----------------------------------------------

/// The cheapest shield in the set, drawn small and off-centre. Deflect is 0
/// energy for 4 Block, and the icon says "a bit of Block" by literally being
/// a smaller Defend rather than by changing shape.
fn deflect() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 18, 8, 18, 20, SHIELD_FACE, SHIELD_RIM);
    // A glancing stroke off the rim, so the shield is deflecting something
    // rather than just being small.
    canvas.line(2, 6, 13, 12, N6);
    canvas.line(2, 12, 11, 16, N6);
    finish(&mut canvas);
    canvas
}

/// Defend's shield with the Dexterity chevron over it.
///
/// The chevron is a direct quote of the Dexterity status icon, and every card
/// that grants Dexterity carries it: Brace, Hunker Down, Entrench, War Paint,
/// Aegis, Stone Skin. That mark is doing the same job the poison family's drip
/// does — telling the player what a card grants before they read the text.
fn brace() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 4, 24, 25, SHIELD_FACE, SHIELD_RIM);
    dexterity_mark(&mut canvas, 16, 11, 1);
    finish(&mut canvas);
    canvas
}

/// A shield planted in the ground and braced from behind. Hunker Down is the
/// 2-energy Common, so it is the same shield as Brace with weight added —
/// wider stance, ground line, two chevrons instead of one.
fn hunker_down() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(1, 26, 30, 4, N4);
    canvas.hline(1, 26, 30, N5);
    shield(&mut canvas, 16, 2, 26, 25, SHIELD_FACE, SHIELD_RIM);
    // The braces: two struts angling back into the ground.
    canvas.thick_line(6, 27, 12, 18, 2, WOOD);
    canvas.thick_line(26, 27, 20, 18, 2, WOOD);
    dexterity_mark(&mut canvas, 16, 8, 2);
    finish(&mut canvas);
    canvas
}

/// The shield dug *into* the earth rather than standing on it — Entrench is
/// the Uncommon upgrade of the same idea, and burying the lower third is what
/// separates it from Hunker Down at 1x without changing the silhouette.
fn entrench() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 1, 24, 26, SHIELD_FACE, SHIELD_RIM);
    dexterity_mark(&mut canvas, 16, 6, 2);

    // Earth piled over the base, drawn as overlapping mounds so the top edge
    // is irregular. A straight band reads as a table the shield sits on.
    canvas.rect(0, 24, GRID, 8, N3);
    for (x, r) in [(4, 4), (12, 3), (20, 4), (28, 3)] {
        canvas.disc(x, 24, r, N3);
    }
    canvas.hline(0, 23, 6, N4);
    canvas.hline(9, 24, 7, N4);
    canvas.hline(18, 23, 8, N4);

    finish(&mut canvas);
    canvas
}

/// The best shield in the game: gold-rimmed, bossed, and the only one drawn
/// with an ornament. Aegis exhausts, so it is a single perfect defence rather
/// than a repeatable one, and it should look like an heirloom next to
/// Defend's plain heater.
fn aegis() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 2, 28, 29, B1, G3);
    shield(&mut canvas, 16, 5, 22, 23, SHIELD_FACE, G4);
    canvas.disc(16, 12, 5, G3);
    canvas.disc(16, 12, 3, G5);
    // One chevron, not two: the shield tapers to a point, and a second row
    // lower down had its ends hanging outside the silhouette.
    dexterity_mark(&mut canvas, 16, 19, 1);
    sparkle(&mut canvas, 27, 6, 3, G5);
    finish(&mut canvas);
    canvas
}

/// A Power, so it is drawn on a body rather than as a held object — the same
/// argument Metallicize's plating makes. Stone Skin grants Dexterity
/// permanently, so the plates *are* the skin: a torso with the chevron cut
/// into the chest.
fn stone_skin() -> Canvas {
    let mut canvas = new_icon();

    // A head and shoulders, so the plating is clearly *on someone*. The first
    // version drew a bare tapering torso and read as a grey helmet - without
    // a neck and a gap between head and shoulder there is nothing to tell a
    // body from an object.
    canvas.disc(16, 7, 5, N6);
    canvas.rect(14, 11, 5, 3, N5);
    canvas.poly(&[(3, 17), (29, 17), (27, 31), (5, 31)], N4);
    canvas.disc(6, 18, 4, N4);
    canvas.disc(26, 18, 4, N4);

    // Stone courses across the chest, staggered like Fortify's brickwork.
    for (row, y) in [(0, 19), (1, 24)] {
        let stagger = if row == 0 { 0 } else { 5 };
        for x in (5 + stagger..28).step_by(9) {
            canvas.rect(x, y, 7, 4, N6);
            canvas.hline(x, y, 7, N7);
        }
    }

    // On the chest, over the courses rather than above the head - the B-ramp
    // reads straight off the grey, and a chevron floating over the skull
    // would have collided with it.
    dexterity_mark(&mut canvas, 16, 20, 1);
    finish(&mut canvas);
    canvas
}

/// A handprint of paint dragged down a face. War Paint grants Strength *and*
/// Dexterity, which no other Common does, so it needed an icon that is
/// neither a fist nor a shield — the ritual before the fight rather than the
/// fight.
fn war_paint() -> Canvas {
    let mut canvas = new_icon();

    // Face: a tapered oval, light enough that paint reads *on* it. The first
    // version banded a dark grey head at eye level and read as a visored
    // helm - a full-width horizontal bar across a grey oval is a helmet slot,
    // so the eyes have to sit clear of the paint, not inside it.
    canvas.disc(16, 13, 10, N5);
    canvas.rect(6, 13, 21, 7, N5);
    canvas.poly(&[(6, 19), (27, 19), (22, 28), (11, 28)], N5);
    canvas.disc(16, 13, 8, N7);
    canvas.rect(8, 13, 17, 6, N7);
    canvas.poly(&[(8, 18), (25, 18), (21, 26), (12, 26)], N7);

    // Eyes above the paint, dark and wide, so the face is looking at you
    // before any colour is read.
    canvas.rect(11, 11, 3, 3, N0);
    canvas.rect(19, 11, 3, 3, N0);

    // Two marks, not two bands: red across the brow (Strength), blue streaked
    // down each cheek (Dexterity). The card grants both, and this is the only
    // Common that does.
    canvas.rect(9, 6, 15, 3, R3);
    canvas.hline(9, 6, 15, R4);
    for x in [12, 20] {
        canvas.rect(x, 17, 2, 8, B3);
        canvas.rect(x + 3, 17, 1, 6, B2);
    }

    finish(&mut canvas);
    canvas
}

/// A roll of bandage, part-unwound. Bandage Up exhausts for a small heal, and
/// the loose tail is what says "one use" — a sealed roll would read as a
/// stock of them.
fn bandage_up() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(13, 14, 10, N6);
    canvas.disc(13, 14, 7, N7);
    canvas.disc(13, 14, 3, N4);
    // The wound edge, then the tail unrolling to the corner.
    canvas.thick_line(13, 4, 22, 6, 3, N8);
    canvas.thick_line(22, 6, 27, 14, 3, N7);
    canvas.thick_line(27, 14, 24, 24, 3, N8);
    canvas.thick_line(24, 24, 29, 30, 3, N7);
    canvas.disc(9, 11, 2, R3);
    finish(&mut canvas);
    canvas
}

/// A shout: an open mouth in a dark silhouette with the sound thrown wide.
/// Intimidate applies Weak to everything, and the arcs are what make it an
/// area effect — the same "this reaches all of them" job Whirlwind's ring
/// does, without a weapon in it.
fn intimidate() -> Canvas {
    let mut canvas = new_icon();

    // Three broken arrows, fanned outward and downward.
    //
    // Two shouting-head versions were drawn before this and both failed the
    // same way: the mouth is a void, voids are the background colour, and a
    // dark shape with a wedge missing reads as a bitten disc, not a shout.
    // So the icon quotes the Weak *status* glyph instead - the same snapped
    // downward arrow - three times, which says "Weak, to all of them" with no
    // silhouette to misread. It is the `blade_flurry` trick applied to a
    // debuff: fan the mark, one per enemy.
    for (tip_x, tip_y, colour) in [(4, 28, N5), (16, 30, N6), (28, 28, N5)] {
        arrow(&mut canvas, (16, 5), (tip_x, tip_y), 3, 6, colour);
    }

    // The breaks: notched wedges bitten out of each shaft and refilled dark,
    // exactly as `weak` does it.
    canvas.poly(&[(6, 17), (12, 15), (12, 19), (6, 21)], N2);
    canvas.poly(&[(13, 18), (19, 16), (19, 20), (13, 22)], N2);
    canvas.poly(&[(20, 17), (26, 15), (26, 19), (20, 21)], N2);

    finish(&mut canvas);
    canvas
}

/// A hand of cards burning. Scorched Earth exhausts everything you hold to
/// draw three fresh, and this is the other half of the pair with All In — the
/// same cost drawn the same way, so the two read as siblings, but with no
/// weapon in it because the payoff is cards rather than damage.
fn scorched_earth() -> Canvas {
    let mut canvas = new_icon();

    // Burnt-out stubs at the bottom, fresh cards arriving at the top.
    //
    // Drawn as *before and after* rather than as a fire, which is what
    // separates it from All In - both cards exhaust the hand, so the icons
    // cannot both be "cards alight" or the pair is indistinguishable. Here
    // the burning has already happened and the draw is the subject; upright
    // cards with flames on top were tried twice and read as a candelabra
    // both times.
    for x in [3, 11, 19, 27] {
        canvas.rect(x - 2, 26, 5, 6, N2);
        canvas.rect(x - 1, 27, 3, 5, N3);
        // Ragged burnt top edge: each stub ends at a different height.
        canvas.hline(x - 2, 26, 5, N1);
    }
    for (x, y, colour) in [(5, 22, E3), (13, 19, E4), (21, 23, E2), (28, 20, E3)] {
        canvas.disc(x, y, 1, colour);
    }

    // The three drawn cards, fanned and clean, above the ash.
    for (index, (x, lean)) in [(4, -2), (12, 0), (20, 2)].iter().enumerate() {
        canvas.poly(&[(*x, 16), (*x + 9, 16), (*x + 9 + lean, 1), (*x + lean, 1)], N0);
        canvas.poly(
            &[(*x + 1, 15), (*x + 8, 15), (*x + 8 + lean, 3), (*x + 1 + lean, 3)],
            if index == 1 { N8 } else { N6 },
        );
        canvas.disc(*x + 4 + lean, 8, 1, N3);
    }

    finish(&mut canvas);
    canvas
}

/// Two dice mid-throw. Gambit discards two cards at random for two energy —
/// the only card in the set whose cost is decided by chance, and dice are the
/// one glyph that says that without a paragraph.
fn gambit() -> Canvas {
    let mut canvas = new_icon();

    canvas.rect(3, 12, 13, 13, N7);
    canvas.rect(4, 13, 11, 11, N8);
    for (x, y) in [(7, 16), (11, 16), (7, 20), (11, 20)] {
        canvas.disc(x, y, 1, N1);
    }

    canvas.rect(17, 5, 12, 12, N5);
    canvas.rect(18, 6, 10, 10, N6);
    for (x, y) in [(21, 9), (25, 12)] {
        canvas.disc(x, y, 1, N1);
    }

    // The energy the throw pays out, so the icon carries the payoff as well
    // as the risk. Same bolt as Adrenaline.
    canvas.poly(&[(22, 19), (16, 26), (20, 26), (17, 31), (26, 23), (21, 23), (25, 19)], G5);

    finish(&mut canvas);
    canvas
}

/// Light falling on the Regen heart. Mending Light is the Power that grants
/// Regen, so it quotes the status icon directly — the sprouting heart — and
/// adds the rays that make it a source rather than a state.
fn mending_light() -> Canvas {
    let mut canvas = new_icon();

    for (dx, dy) in [(-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1)] {
        canvas.line(16 + dx * 5, 18 + dy * 5, 16 + dx * 15, 18 + dy * 15, G3);
    }
    canvas.line(1, 18, 8, 18, G3);
    canvas.line(24, 18, 31, 18, G3);

    canvas.disc(11, 17, 6, R2);
    canvas.disc(21, 17, 6, R2);
    canvas.poly(&[(5, 19), (27, 19), (16, 31)], R2);
    canvas.disc(11, 16, 4, R4);
    canvas.disc(21, 16, 4, R4);
    canvas.poly(&[(7, 18), (25, 18), (16, 29)], R4);

    canvas.vline(16, 4, 8, V2);
    canvas.disc(12, 7, 2, V3);
    canvas.disc(20, 5, 2, V3);

    finish(&mut canvas);
    canvas
}

/// The same heart, on fire. Phoenix Heart is the Rare that grants Regen *and*
/// Metallicize, and it deliberately shares Mending Light's subject: one is
/// the Uncommon version of an idea and the other is the Rare, so they should
/// be recognisably the same organ. The flame is what the extra rarity buys.
fn phoenix_heart() -> Canvas {
    let mut canvas = new_icon();

    // Wings of flame behind, drawn first so the heart sits in front of them.
    flame(&mut canvas, 5, 24, 16, E1, E3);
    flame(&mut canvas, 27, 24, 16, E1, E3);
    flame(&mut canvas, 16, 12, 12, E2, E4);

    canvas.disc(11, 17, 6, R1);
    canvas.disc(21, 17, 6, R1);
    canvas.poly(&[(5, 19), (27, 19), (16, 31)], R1);
    canvas.disc(11, 16, 4, R3);
    canvas.disc(21, 16, 4, R3);
    canvas.poly(&[(7, 18), (25, 18), (16, 29)], R3);
    canvas.disc(11, 15, 2, R5);

    // The Metallicize half: two plate bands across the heart, the same B-ramp
    // rivetted plating the status icon uses.
    canvas.rect(7, 20, 18, 3, B2);
    canvas.hline(7, 20, 18, B4);
    canvas.rect(10, 25, 12, 3, B2);
    canvas.hline(10, 25, 12, B4);

    finish_heavy(&mut canvas);
    canvas
}

// -- shared parts ----------------------------------------------------------

/// The Dexterity chevron stack, quoted from the status icon of the same name.
/// Every card that grants Dexterity carries it, at the count it grants (capped
/// at what fits), which is the same "the art says what it does" contract the
/// poison drip already holds up.
fn dexterity_mark(canvas: &mut Canvas, cx: i32, top: i32, count: i32) {
    for index in 0..count.min(3) {
        let y = top + index * 5;
        canvas.thick_line(cx - 6, y + 4, cx, y, 2, B4);
        canvas.thick_line(cx, y, cx + 6, y + 4, 2, B4);
    }
}

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

// -- second content pass ---------------------------------------------------
//
// Same two rules as the set above: Attacks quote a weapon shape and Skills
// never do, and a card that grants a status is drawn from the same parts as
// the status itself (Resolve/Bloodrite share Flex's fist, the Foresight pair
// share the eye, Bloodpact shares Fervor's orb).

/// A shield already stepped back, with the ground it gave up behind it.
fn backstep() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 21, 8, 18, 21, SHIELD_FACE, SHIELD_RIM);
    for (x, y) in [(11, 13), (6, 13)] {
        canvas.thick_line(x, y, x - 4, y + 4, 2, N6);
        canvas.thick_line(x - 4, y + 4, x, y + 8, 2, N6);
    }
    finish(&mut canvas);
    canvas
}

/// The blade family with two embers riding the edge — the cheap opener that
/// leaves the target Vulnerable.
fn cinder_slash() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (9, 24), (26, 6), BLADE, BLADE_EDGE);
    canvas.disc(23, 14, 2, E2);
    canvas.disc(27, 19, 2, E2);
    canvas.set(23, 13, E4);
    canvas.set(27, 18, E4);
    canvas.disc(18, 9, 1, E3);
    finish(&mut canvas);
    canvas
}

/// A lash uncoiling across the icon with three barbs on it. The only weapon
/// in the set that isn't straight, which is what separates the sweep of it
/// from Blade Flurry's arcs.
fn lash_out() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(3, 29, 9, 24, 4, GRIP);
    canvas.disc(3, 29, 2, GUARD);
    // One line, tapering as it travels, with the barbs standing clear of it -
    // a curled lash reads as a horn at 1x, and barbs sunk into the curve read
    // as nothing at all.
    canvas.thick_line(9, 24, 19, 17, 3, N5);
    canvas.thick_line(19, 17, 28, 6, 2, N7);
    for (x, y) in [(13, 21), (18, 17), (24, 11)] {
        canvas.thick_line(x, y, x - 4, y - 5, 2, N6);
        canvas.set(x - 5, y - 6, N8);
    }
    finish(&mut canvas);
    canvas
}

/// Shield with a bright spine and rivets: holding still, and healing for it.
fn steel_nerve() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 4, 24, 26, SHIELD_FACE, SHIELD_RIM);
    canvas.vline(16, 8, 15, N8);
    for y in [10, 16, 22] {
        canvas.disc(16, y, 2, N7);
    }
    canvas.disc(16, 16, 1, R3);
    finish(&mut canvas);
    canvas
}

/// Three cards, one of them falling out of the pile — the trade the card is.
fn sift() -> Canvas {
    let mut canvas = new_icon();
    for (x, top) in [(3, 3), (11, 5)] {
        canvas.poly(&[(x, top), (x + 10, top), (x + 10, top + 18), (x, top + 18)], N0);
        canvas.poly(
            &[(x + 1, top + 1), (x + 9, top + 1), (x + 9, top + 17), (x + 1, top + 17)],
            N7,
        );
        canvas.hline(x + 3, top + 6, 5, N4);
        canvas.hline(x + 3, top + 10, 5, N4);
    }
    // The discarded one, face-down and already past the others.
    canvas.poly(&[(21, 19), (30, 24), (25, 31), (17, 27)], N0);
    canvas.poly(&[(22, 21), (28, 24), (25, 29), (19, 26)], N3);
    finish(&mut canvas);
    canvas
}

/// Two short blades thrown in sequence. Deliberately small and low-contrast
/// against Twin Strike's crossed pair: this is the cheap one.
fn jab() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (4, 10), (19, 10), 1.5, BLADE, BLADE_EDGE);
    canvas.thick_line(5, 7, 5, 13, 2, GUARD);
    blade(&mut canvas, (4, 22), (19, 22), 1.5, BLADE, BLADE_EDGE);
    canvas.thick_line(5, 19, 5, 25, 2, GUARD);
    canvas.line(23, 8, 27, 6, N6);
    canvas.line(23, 24, 27, 26, N6);
    finish(&mut canvas);
    canvas
}

/// The shield family with the poison family's green growing out of it.
fn barbed_guard() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 6, 22, 24, SHIELD_FACE, SHIELD_RIM);
    for (x, y) in [(6, 12), (16, 4), (26, 12)] {
        canvas.poly(&[(x - 3, y + 6), (x, y - 4), (x + 3, y + 6)], V2);
        canvas.vline(x, y - 3, 4, V4);
    }
    finish(&mut canvas);
    canvas
}

/// A low blade and the buckling it leaves behind.
fn cheap_shot() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (3, 24), (18, 16), 1.5, BLADE, BLADE_EDGE);
    canvas.thick_line(2, 21, 6, 28, 2, GUARD);
    for y in [8, 15] {
        canvas.thick_line(20, y, 25, y + 5, 2, N5);
        canvas.thick_line(25, y + 5, 30, y, 2, N5);
    }
    finish(&mut canvas);
    canvas
}

/// The Dexterity mark on its own, at the size the other Dexterity cards only
/// ever draw it beside a shield. This is the card that is *only* the status.
fn footwork() -> Canvas {
    let mut canvas = new_icon();
    dexterity_mark(&mut canvas, 16, 9, 3);
    canvas.thick_line(3, 6, 7, 10, 2, N5);
    canvas.thick_line(29, 6, 25, 10, 2, N5);
    finish(&mut canvas);
    canvas
}

/// Coins. The one card that pays in gold rather than in tempo, so it is drawn
/// as the currency and nothing else.
fn tithe() -> Canvas {
    let mut canvas = new_icon();
    for (x, y) in [(11, 22), (21, 22), (16, 13)] {
        canvas.disc(x, y, 6, G2);
        canvas.disc(x, y, 4, G3);
        canvas.ring(x, y, 2, 1, G1);
    }
    sparkle(&mut canvas, 27, 6, 3, G5);
    finish(&mut canvas);
    canvas
}

/// The eye, plain — the Uncommon rung of the Foresight ladder.
fn second_sight() -> Canvas {
    let mut canvas = new_icon();
    eye(&mut canvas, 16, 16, 13, 8, B3);
    finish(&mut canvas);
    canvas
}

/// The poison family's blade, bleeding harder: a spray rather than the two
/// beads Venom Strike carries.
fn hemorrhage() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (5, 26), (22, 7), 2.0, BLADE, BLADE_EDGE);
    canvas.thick_line(2, 23, 9, 28, 2, GUARD);
    for (x, y, r) in [(24, 12, 3), (28, 19, 2), (21, 21, 2), (26, 26, 1)] {
        droplet(&mut canvas, x, y, r, V2, V4);
    }
    finish(&mut canvas);
    canvas
}

/// A blade with the blood coming back up it.
fn bloodhound() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (7, 28), (24, 8), 2.0, BLADE, BLADE_EDGE);
    canvas.thick_line(4, 25, 11, 30, 2, GUARD);
    droplet(&mut canvas, 9, 14, 3, R3, R5);
    arrow(&mut canvas, (9, 24), (9, 8), 2, 5, R4);
    finish(&mut canvas);
    canvas
}

/// Three blades in a row. The count is the card, so nothing else is on it.
fn flurry() -> Canvas {
    let mut canvas = new_icon();
    for x in [5, 14, 23] {
        blade(&mut canvas, (x, 28), (x, 5), 1.5, BLADE, BLADE_EDGE);
        canvas.thick_line(x - 2, 26, x + 2, 26, 2, GUARD);
    }
    finish(&mut canvas);
    canvas
}

/// Flex's fist with the price on it: the Strength is the same gesture, the
/// blood is what it costs.
fn bloodrite() -> Canvas {
    let mut canvas = new_icon();
    raised_fist(&mut canvas);
    droplet(&mut canvas, 27, 9, 3, R3, R5);
    droplet(&mut canvas, 28, 20, 2, R3, R5);
    finish(&mut canvas);
    canvas
}

/// Poison falling on everything. Toxic Cloud is the cloud; this is the rain
/// off it, which is the half that also does damage.
fn blight_storm() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(11, 8, 6, V1);
    canvas.disc(20, 8, 5, V1);
    canvas.rect(6, 8, 21, 4, V1);
    canvas.hline(7, 6, 8, V2);
    for (x, y) in [(8, 17), (15, 20), (22, 16), (11, 25), (19, 26)] {
        droplet(&mut canvas, x, y, 2, V2, V4);
    }
    finish(&mut canvas);
    canvas
}

/// A shield standing in the fire the rest of the hand went into.
fn purge() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 3, 22, 21, SHIELD_FACE, SHIELD_RIM);
    flame(&mut canvas, 8, 31, 12, E2, E3);
    flame(&mut canvas, 16, 31, 9, E2, E3);
    flame(&mut canvas, 24, 31, 12, E2, E3);
    finish(&mut canvas);
    canvas
}

/// A flame with the draught under it — the Uncommon rung of the Ritual
/// ladder, where Demon Form is the Rare one.
fn stoke() -> Canvas {
    let mut canvas = new_icon();
    // A brazier, not a campfire: the map's Rest node is already crossed logs
    // under a flame, and two icons that differ only in their kindling is the
    // collision the relic naming rule exists to catch.
    canvas.poly(&[(6, 22), (26, 22), (22, 30), (10, 30)], N4);
    canvas.rect(4, 20, 24, 3, N6);
    canvas.hline(13, 30, 7, N5);
    flame(&mut canvas, 16, 21, 18, E2, E3);
    flame(&mut canvas, 16, 21, 10, E3, E4);
    sparkle(&mut canvas, 26, 8, 3, E4);
    sparkle(&mut canvas, 6, 13, 2, E3);
    finish(&mut canvas);
    canvas
}

/// Two links closed over a slab. Skills never quote a weapon, and a binding
/// is the one shape that says "both of these, to everyone".
fn gravebind() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(5, 6), (27, 6), (25, 30), (7, 30)], N3);
    canvas.poly(&[(7, 8), (25, 8), (23, 28), (9, 28)], N4);
    canvas.hline(9, 12, 15, N3);
    // The chain runs corner to corner, so the links read as a binding across
    // the stone rather than as a pair of eyes on it.
    for (x, y) in [(7, 8), (14, 15), (21, 22), (27, 28)] {
        canvas.ring(x, y, 4, 2, N6);
        canvas.set(x - 2, y - 2, N8);
    }
    finish(&mut canvas);
    canvas
}

/// Flex's fist again, over the card it draws.
fn resolve() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(17, 2), (29, 6), (25, 21), (13, 17)], N0);
    canvas.poly(&[(18, 4), (27, 7), (24, 19), (15, 16)], N7);
    raised_fist(&mut canvas);
    finish(&mut canvas);
    canvas
}

/// The skull the set had never used, and the crack it takes.
fn skullcrack() -> Canvas {
    let mut canvas = new_icon();
    skull(&mut canvas, 15, 6, BONE, BONE_SHADE);
    crack(&mut canvas, &[(9, 6), (14, 11), (11, 15), (16, 20)], N0);
    canvas.line(24, 3, 30, 9, N8);
    canvas.line(23, 5, 29, 11, N6);
    finish(&mut canvas);
    canvas
}

/// The energy orb with blood going into it: the cost of the card is a whole
/// turn, and what comes back is Energy every turn after.
fn bloodpact() -> Canvas {
    let mut canvas = new_icon();
    orb(&mut canvas, 16, 19, 11, G2, G4);
    droplet(&mut canvas, 16, 6, 3, R3, R5);
    sparkle(&mut canvas, 27, 8, 3, G5);
    sparkle(&mut canvas, 5, 10, 2, G4);
    finish(&mut canvas);
    canvas
}

/// The same eye as Second Sight, opened wider and given rays. The pair has to
/// read as one ladder, so the difference is degree rather than subject.
fn deep_focus() -> Canvas {
    let mut canvas = new_icon();
    eye(&mut canvas, 16, 16, 11, 9, B4);
    for (x0, y0, x1, y1) in [(2, 4, 6, 8), (30, 4, 26, 8), (2, 28, 6, 24), (30, 28, 26, 24)] {
        canvas.line(x0, y0, x1, y1, B5);
    }
    finish(&mut canvas);
    canvas
}

/// The heaviest attack in the game: a blade brought down through the whole
/// icon, and the ground giving way under it.
fn cataclysm() -> Canvas {
    let mut canvas = new_icon();
    blade(&mut canvas, (16, 2), (16, 23), 3.0, BLADE, BLADE_EDGE);
    canvas.thick_line(10, 5, 22, 5, 3, GUARD);
    canvas.rect(5, 25, 23, 3, E1);
    crack(&mut canvas, &[(16, 25), (10, 28), (3, 26)], E3);
    crack(&mut canvas, &[(16, 25), (22, 29), (29, 26)], E3);
    crack(&mut canvas, &[(16, 25), (15, 31)], E3);
    impact(&mut canvas, 16, 18, E3);
    finish(&mut canvas);
    canvas
}

/// A chalice under a halo. Nothing else in the set is a vessel, which is what
/// keeps the game's one big heal from reading as another shield.
fn last_rite() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(16, 13, 11, 2, G3);
    canvas.poly(&[(9, 11), (23, 11), (20, 21), (12, 21)], G2);
    canvas.poly(&[(11, 13), (21, 13), (19, 19), (13, 19)], G4);
    canvas.rect(15, 21, 3, 5, G2);
    canvas.rect(11, 26, 11, 3, G3);
    finish(&mut canvas);
    canvas
}

/// A scythe, curved the one way no other weapon here is, and taking blood
/// back with it.
fn reap() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(6, 30, 24, 6, 3, WOOD);
    canvas.poly(&[(24, 6), (7, 8), (3, 14), (12, 11), (23, 10)], BLADE);
    canvas.line(7, 8, 3, 14, BLADE_EDGE);
    canvas.line(3, 14, 12, 11, BLADE_EDGE);
    droplet(&mut canvas, 9, 20, 3, R3, R5);
    finish(&mut canvas);
    canvas
}
