//! The thirty-three relics.
//!
//! Unlike the potions these are *objects*, not variations on one container.
//! The relic row already frames each one in a `ChromeStyles.SlotStyle` bezel,
//! so the set does not need a shared backing plate to read as a collection —
//! and a row of twenty-two identical medallions distinguished only by a tiny
//! emblem would be unreadable at the 1x the HUD shows them at. Distinct
//! silhouettes are what make "which relic is that" answerable without a
//! tooltip.
//!
//! The pairs that *do* share a shape do it deliberately: the two fangs and the
//! two books differ by colour and by one detail, because their effects are
//! genuinely siblings.

use super::shapes::*;
use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        Icon { category: "relics", name: "anchor_stone", draw: anchor_stone },
        Icon { category: "relics", name: "battle_drum", draw: battle_drum },
        Icon { category: "relics", name: "bulwark_charm", draw: bulwark_charm },
        Icon { category: "relics", name: "clockwork_gear", draw: clockwork_gear },
        Icon { category: "relics", name: "coiled_serpent", draw: coiled_serpent },
        Icon { category: "relics", name: "coin_press", draw: coin_press },
        Icon { category: "relics", name: "conduit_sigil", draw: conduit_sigil },
        Icon { category: "relics", name: "cracked_hourglass", draw: cracked_hourglass },
        Icon { category: "relics", name: "focusing_lens", draw: focusing_lens },
        Icon { category: "relics", name: "frugal_satchel", draw: frugal_satchel },
        Icon { category: "relics", name: "gamblers_deck", draw: gamblers_deck },
        Icon { category: "relics", name: "grave_moss", draw: grave_moss },
        Icon { category: "relics", name: "hollow_crown", draw: hollow_crown },
        Icon { category: "relics", name: "iron_resolve", draw: iron_resolve },
        Icon { category: "relics", name: "ledger_of_ruin", draw: ledger_of_ruin },
        Icon { category: "relics", name: "merchants_scale", draw: merchants_scale },
        Icon { category: "relics", name: "momentum_token", draw: momentum_token },
        Icon { category: "relics", name: "ossuary_bell", draw: ossuary_bell },
        Icon { category: "relics", name: "palsy_shackle", draw: palsy_shackle },
        Icon { category: "relics", name: "pilgrims_ash", draw: pilgrims_ash },
        Icon { category: "relics", name: "reapers_tally", draw: reapers_tally },
        Icon { category: "relics", name: "restless_grimoire", draw: restless_grimoire },
        Icon { category: "relics", name: "rusted_portcullis", draw: rusted_portcullis },
        Icon { category: "relics", name: "scavengers_charm", draw: scavengers_charm },
        Icon { category: "relics", name: "second_wind", draw: second_wind },
        Icon { category: "relics", name: "skirmishers_sash", draw: skirmishers_sash },
        Icon { category: "relics", name: "sunken_idol", draw: sunken_idol },
        Icon { category: "relics", name: "thorned_carapace", draw: thorned_carapace },
        Icon { category: "relics", name: "toxic_fang", draw: toxic_fang },
        Icon { category: "relics", name: "vampire_fang", draw: vampire_fang },
        Icon { category: "relics", name: "vengeful_spirit", draw: vengeful_spirit },
        Icon { category: "relics", name: "warded_bracer", draw: warded_bracer },
        Icon { category: "relics", name: "whispering_coin", draw: whispering_coin },
    ]
}

/// Gain 8 Block at combat start — a weight that holds you in place.
fn anchor_stone() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(16, 7, 4, 2, N6);
    canvas.rect(15, 9, 3, 18, N6);
    canvas.rect(9, 12, 15, 3, N6);
    canvas.poly(&[(4, 19), (9, 19), (16, 28), (7, 27)], N6);
    canvas.poly(&[(28, 19), (23, 19), (16, 28), (25, 27)], N6);
    canvas.rect(15, 9, 1, 18, N7);
    finish(&mut canvas);
    canvas
}

/// Draw a card at the start of each turn — a marching beat.
fn battle_drum() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(6, 11, 21, 13, WOOD);
    canvas.rect(6, 11, 21, 3, N7);
    canvas.rect(6, 21, 21, 3, N7);
    for x in (7..26).step_by(6) {
        canvas.line(x, 14, x + 4, 21, G3);
        canvas.line(x + 4, 14, x, 21, G3);
    }
    canvas.thick_line(3, 8, 12, 4, 2, N5);
    canvas.thick_line(21, 4, 29, 8, 2, N5);
    finish(&mut canvas);
    canvas
}

/// A charm, not a shield: it grants Block once per turn, so it is jewellery
/// that behaves like armour rather than armour itself.
fn bulwark_charm() -> Canvas {
    let mut canvas = new_icon();
    canvas.line(6, 4, 16, 9, G2);
    canvas.line(26, 4, 16, 9, G2);
    canvas.ring(16, 10, 3, 1, G3);
    shield(&mut canvas, 16, 12, 20, 18, SHIELD_FACE, G3);
    canvas.vline(16, 15, 9, B4);
    canvas.hline(11, 18, 11, B4);
    finish(&mut canvas);
    canvas
}

/// Extra Energy each turn — the one relic that is literally a machine part.
fn clockwork_gear() -> Canvas {
    let mut canvas = new_icon();
    for step in 0..8 {
        let angle = step as f32 * std::f32::consts::TAU / 8.0;
        let x = 16.0 + angle.cos() * 12.0;
        let y = 16.0 + angle.sin() * 12.0;
        canvas.disc(x.round() as i32, y.round() as i32, 3, G2);
    }
    canvas.disc(16, 16, 10, G2);
    canvas.disc(16, 16, 8, G3);
    canvas.disc(16, 16, 3, N2);
    finish(&mut canvas);
    canvas
}

/// 2 Strength at combat start. Coiled rather than striking — the bonus is
/// there before the fight starts.
fn coiled_serpent() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(16, 19, 11, 4, V2);
    canvas.ring(16, 19, 6, 3, V3);
    canvas.disc(23, 8, 5, V3);
    canvas.rect(19, 10, 5, 6, V2);
    canvas.set(21, 6, N0);
    canvas.set(25, 6, N0);
    canvas.line(27, 10, 30, 12, R4);
    finish(&mut canvas);
    canvas
}

/// Gold for every kill — a coin *being struck*, not a coin. The Shop tier is
/// three gold-economy relics and the set already has two coins in it
/// (`momentum_token`, `whispering_coin`), so this one is the machine around
/// the coin: the press reads at 1x by its frame, and the disc under the plate
/// is small enough that it never competes with the other two.
fn coin_press() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(4, 3, 24, 3, N5);
    canvas.rect(4, 6, 2, 21, N5);
    canvas.rect(26, 6, 2, 21, N5);
    // A threaded shaft rather than a plain post: two posts and a crossbar on
    // their own read as a picture frame, which is what the first pass did.
    canvas.rect(15, 6, 2, 8, N6);
    for y in (7..14).step_by(2) {
        canvas.hline(13, y, 6, N4);
    }
    canvas.rect(9, 14, 14, 3, N6);
    canvas.hline(9, 14, 14, N7);
    canvas.disc(16, 23, 5, G2);
    canvas.disc(16, 23, 3, G4);
    canvas.disc(16, 23, 1, G0);
    canvas.line(9, 18, 7, 20, G4);
    canvas.line(23, 18, 25, 20, G4);
    canvas.rect(4, 27, 24, 3, N4);
    finish(&mut canvas);
    canvas
}

/// Energy back whenever you play a Power — a bound sigil, and the only flat
/// glyph in a set otherwise made of objects. The bolt is gold because that is
/// what Energy is everywhere else here (`clockwork_gear`, `cracked_hourglass`);
/// the ward around it is cold, so the two channels never merge into one blob.
fn conduit_sigil() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(16, 2), (30, 16), (16, 30), (2, 16)], B1);
    canvas.poly(&[(16, 5), (27, 16), (16, 27), (5, 16)], B2);
    canvas.poly(&[(16, 8), (24, 16), (16, 24), (8, 16)], N1);
    canvas.poly(&[(19, 8), (13, 17), (16, 17), (12, 25), (21, 14), (17, 14)], G4);
    canvas.line(16, 2, 2, 16, B4);
    canvas.line(16, 2, 30, 16, B4);
    finish(&mut canvas);
    canvas
}

/// Extra Energy at combat start — one burst, then it is spent, which is what
/// the crack says.
fn cracked_hourglass() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(6, 3, 21, 3, G2);
    canvas.rect(6, 26, 21, 3, G2);
    canvas.poly(&[(8, 6), (25, 6), (18, 16), (25, 26), (8, 26), (15, 16)], N7);
    canvas.poly(&[(11, 8), (22, 8), (17, 15), (16, 15)], E3);
    canvas.poly(&[(12, 24), (21, 24), (17, 18), (16, 18)], E3);
    crack(&mut canvas, &[(25, 7), (20, 12), (24, 17), (19, 24)], N0);
    finish(&mut canvas);
    canvas
}

/// 1 Strength each turn, accumulating — a lens gathering light to a point.
fn focusing_lens() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(4, 27, 13, 18, 3, G1);
    canvas.disc(19, 12, 11, G2);
    canvas.disc(19, 12, 9, B1);
    canvas.disc(19, 12, 7, B2);
    canvas.line(14, 9, 18, 5, B5);
    canvas.line(13, 12, 16, 8, B5);
    finish(&mut canvas);
    canvas
}

/// Converts unspent Energy into Block — thrift, so a drawstring pouch.
fn frugal_satchel() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(7, 13), (25, 13), (28, 26), (16, 29), (4, 26)], G1);
    canvas.poly(&[(9, 15), (23, 15), (25, 25), (16, 27), (7, 25)], G2);
    canvas.rect(10, 10, 12, 4, N4);
    canvas.rect(11, 8, 10, 3, N5);
    canvas.disc(16, 20, 3, B3);
    canvas.hline(13, 20, 7, B5);
    finish(&mut canvas);
    canvas
}

/// Draw an extra card at combat start — three cards splayed by rotation, not
/// by offset. Four upright cards side by side were tried and read as a
/// barcode; the fan only exists if the outer cards lean.
fn gamblers_deck() -> Canvas {
    let mut canvas = new_icon();
    // Staggered upright cards, not a rotated fan. Two attempts at a fan
    // failed the same way: a leaning card is five or six pixels across at its
    // narrow end, so once it carries the dark border that keeps it separate
    // from its neighbour there is nothing left to be a face, and the whole
    // spread collapses into one black mass.
    playing_card(&mut canvas, 1, 9, N5);
    playing_card(&mut canvas, 19, 9, N5);
    playing_card(&mut canvas, 10, 4, N8);
    canvas.poly(&[(16, 11), (19, 15), (16, 20), (13, 15)], R4);
    finish(&mut canvas);
    canvas
}

/// A card as a light face inside a dark border, so overlapping cards stay
/// separate where they meet.
fn playing_card(canvas: &mut Canvas, x: i32, y: i32, face: Rgb) {
    canvas.rect(x, y, 12, 22, N0);
    canvas.rect(x + 1, y + 1, 10, 20, face);
}

/// Poison to whatever hits you — a grave marker gone green. `palsy_shackle`
/// is the other "punish the attacker" relic and it is iron and violet, so this
/// one is stone and V-ramp: at 1x the two never resolve to the same smudge.
fn grave_moss() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(9, 11), (12, 6), (20, 6), (23, 11), (23, 27), (9, 27)], N5);
    canvas.poly(&[(11, 12), (13, 8), (19, 8), (21, 12), (21, 25), (11, 25)], N6);
    canvas.hline(13, 14, 6, N4);
    canvas.hline(13, 17, 6, N4);
    canvas.disc(10, 24, 3, V2);
    canvas.disc(14, 26, 3, V2);
    canvas.disc(21, 23, 3, V2);
    canvas.disc(10, 23, 1, V3);
    canvas.disc(21, 22, 1, V3);
    canvas.rect(6, 28, 20, 2, N4);
    finish(&mut canvas);
    canvas
}

/// The Boss tier's Strength engine. A crown for the tier that only a boss
/// hands over, and *hollow* is the whole read: the centre stone is a void in
/// the N0 the outline is drawn in, so the gap is a hole rather than a gem that
/// failed to render.
fn hollow_crown() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(4, 22), (4, 8), (10, 16), (16, 5), (22, 16), (28, 8), (28, 22)], G2);
    canvas.poly(&[(6, 20), (6, 12), (10, 18), (16, 8), (22, 18), (26, 12), (26, 20)], G3);
    canvas.rect(4, 22, 25, 5, G2);
    canvas.hline(4, 22, 25, G4);
    canvas.disc(9, 25, 1, R3);
    canvas.disc(23, 25, 1, R3);
    canvas.ring(16, 24, 3, 1, G4);
    canvas.erase_disc(16, 24, 2);
    finish(&mut canvas);
    canvas
}

/// 12 Block at combat start — the heaviest defensive relic, so a full tower
/// shield with a centre ridge rather than the heater shape the lighter ones
/// use. A plain rectangle was tried and read as a door.
fn iron_resolve() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(8, 5), (24, 5), (26, 9), (26, 21), (16, 30), (6, 21), (6, 9)], N4);
    canvas.poly(&[(9, 7), (23, 7), (24, 10), (24, 20), (16, 27), (8, 20), (8, 10)], N6);
    canvas.rect(15, 7, 3, 20, N7);
    canvas.rect(8, 13, 17, 2, N4);
    canvas.disc(16, 15, 3, N4);
    canvas.disc(16, 15, 1, N7);
    finish(&mut canvas);
    canvas
}

/// Closed and clasped: the ledger records the first Attack of a combat and
/// pays out for the rest of it.
fn ledger_of_ruin() -> Canvas {
    let mut canvas = new_icon();
    book_closed(&mut canvas, R2, R3);
    canvas.rect(14, 10, 5, 12, G3);
    canvas.disc(16, 16, 2, G5);
    finish(&mut canvas);
    canvas
}

/// Bonus gold for every win — balance scales, which is the one shape in the
/// set that says "commerce" without being a coin. Loaded on one side: a
/// perfectly level beam reads as a justice emblem rather than a trade.
fn merchants_scale() -> Canvas {
    let mut canvas = new_icon();
    canvas.vline(16, 5, 21, N5);
    canvas.rect(11, 26, 11, 3, N5);
    canvas.rect(13, 24, 7, 2, N4);
    canvas.disc(16, 5, 2, G3);
    canvas.line(5, 12, 27, 8, G2);
    canvas.line(5, 13, 27, 9, G2);
    canvas.vline(6, 13, 4, N6);
    canvas.vline(26, 9, 4, N6);
    canvas.poly(&[(2, 17), (10, 17), (8, 21), (4, 21)], G2);
    canvas.poly(&[(22, 13), (30, 13), (28, 17), (24, 17)], G2);
    canvas.disc(6, 15, 2, G4);
    finish(&mut canvas);
    canvas
}

/// Every third card played deals damage — a struck coin with a chase arrow.
fn momentum_token() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(16, 16, 14, G1);
    canvas.disc(16, 16, 12, G3);
    canvas.ring(16, 16, 10, 1, G1);
    // Two stacked chevrons rather than one arrow: an arrowhead inside a 20px
    // disc collapses into the shaft and the whole thing reads as a slash.
    for offset in [-4, 1] {
        canvas.poly(
            &[(10, 20 + offset), (16, 13 + offset), (22, 20 + offset),
              (22, 23 + offset), (16, 16 + offset), (10, 23 + offset)],
            G0,
        );
    }
    finish(&mut canvas);
    canvas
}

/// Poison to every enemy at the end of your turn — one thing rung over the
/// whole room, which is what "ALL enemies" has to read as. The mouth flares
/// straight: `vengeful_spirit` is this silhouette with a ragged hem, and its
/// own comment says the hem is the only thing keeping it a ghost.
fn ossuary_bell() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(16, 5, 3, 2, G2);
    canvas.poly(&[(12, 7), (20, 7), (25, 21), (7, 21)], N6);
    canvas.poly(&[(14, 9), (18, 9), (20, 21), (12, 21)], N7);
    canvas.rect(6, 21, 21, 3, N5);
    canvas.hline(6, 23, 21, N4);
    // Clapper tinted with what the bell actually delivers. It overlaps the
    // lip rather than hanging clear of it - a disc floating below the mouth
    // gets its own N0 outline from finish() and stops being part of the bell.
    canvas.vline(16, 20, 5, N4);
    canvas.disc(16, 26, 3, V2);
    canvas.disc(16, 26, 1, V4);
    sparkle(&mut canvas, 4, 13, 3, V3);
    sparkle(&mut canvas, 27, 13, 3, V3);
    finish(&mut canvas);
    canvas
}

/// Weak to whatever hits you. An OPEN cuff with the chain snapped off it —
/// closing the ring would make it a bracelet, and the set already has enough
/// jewellery. Iron in the N ramp, the hex in purple, so which half is the
/// mechanism and which is the curse stays legible at 1x.
fn palsy_shackle() -> Canvas {
    let mut canvas = new_icon();
    canvas.ring(13, 19, 11, 4, N5);
    canvas.ring(13, 19, 9, 1, N6);
    canvas.erase_poly(&[(18, 9), (30, 9), (30, 20), (18, 20)]);
    canvas.ring(22, 10, 4, 2, N5);
    canvas.ring(27, 5, 3, 2, N4);
    sparkle(&mut canvas, 13, 19, 5, P4);
    finish(&mut canvas);
    canvas
}

/// 1 Artifact at combat start — the only relic that grants a ward, so it is
/// the only one drawn as something *carried on a pilgrimage* rather than worn
/// or wielded: a stoppered urn with the ash still rising out of it. The
/// sparkle is P-ramp to match how `Artifact` is framed everywhere else.
fn pilgrims_ash() -> Canvas {
    let mut canvas = new_icon();
    // A bellied urn with handles and a narrow foot. The taper is the whole
    // difference: the first pass was a straight-sided pot widening upward,
    // which is a flowerpot, and no amount of drifting ash above it read as
    // anything but a plant.
    canvas.disc(16, 20, 7, N4);
    canvas.rect(10, 17, 13, 6, N4);
    canvas.disc(15, 19, 4, N5);
    canvas.ring(9, 15, 3, 1, N5);
    canvas.ring(23, 15, 3, 1, N5);
    canvas.rect(13, 11, 7, 4, N5);
    canvas.rect(11, 9, 11, 2, N6);
    canvas.rect(12, 26, 9, 2, N5);
    canvas.rect(10, 28, 13, 2, N4);
    canvas.disc(11, 6, 1, N7);
    canvas.disc(21, 5, 1, N6);
    sparkle(&mut canvas, 16, 4, 3, P4);
    finish(&mut canvas);
    canvas
}

/// Strength for every kill — notches cut into a bone, so the count *is* the
/// icon. The fourth stroke crosses the other three, which is the whole
/// difference between marks that read as counting and marks that read as
/// hatching.
fn reapers_tally() -> Canvas {
    let mut canvas = new_icon();
    for (x, y) in [(11, 7), (21, 7), (11, 25), (21, 25)] {
        canvas.disc(x, y, 5, N7);
    }
    canvas.rect(12, 7, 8, 18, N7);
    canvas.rect(12, 7, 2, 18, N8);
    // N4, not N2: a notch in near-black is indistinguishable from finish()'s
    // outline once the icon is drawn at 1x, so the cuts read as holes.
    for y in [11, 15, 19] {
        canvas.rect(12, y, 8, 2, N4);
    }
    canvas.thick_line(9, 23, 23, 8, 2, G4);
    finish(&mut canvas);
    canvas
}

/// Heals every turn on its own — restless, so it is the open book.
fn restless_grimoire() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(3, 10), (15, 13), (15, 27), (3, 24)], P2);
    canvas.poly(&[(29, 10), (17, 13), (17, 27), (29, 24)], P2);
    canvas.poly(&[(5, 12), (14, 14), (14, 25), (5, 23)], N7);
    canvas.poly(&[(27, 12), (18, 14), (18, 25), (27, 23)], N7);
    canvas.rect(15, 13, 2, 14, P1);
    sparkle(&mut canvas, 16, 6, 4, P4);
    finish(&mut canvas);
    canvas
}

/// Block the first time you are hit each combat. Deliberately NOT a shield:
/// `vulnerable` is already a cracked shield and `bulwark_charm` an intact one
/// on a cord, so a third would say nothing. A portcullis drops once and stays
/// down, which is the once-per-combat limit drawn rather than described.
fn rusted_portcullis() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(3, 3, 26, 4, N5);
    canvas.hline(3, 3, 26, N6);
    for x in [5, 11, 17, 23] {
        canvas.rect(x, 7, 4, 17, E1);
    }
    canvas.rect(3, 11, 26, 2, E2);
    canvas.rect(3, 18, 26, 2, E2);
    // The spiked feet are what separate a portcullis from a window grille.
    for x in [5, 11, 17, 23] {
        canvas.poly(&[(x, 24), (x + 4, 24), (x + 2, 29)], E2);
    }
    finish(&mut canvas);
    canvas
}

/// Bonus gold for a clean win — a hooked charm on a thong.
fn scavengers_charm() -> Canvas {
    let mut canvas = new_icon();
    canvas.line(5, 3, 16, 8, N4);
    canvas.line(27, 3, 16, 8, N4);
    canvas.disc(16, 10, 3, N5);
    canvas.poly(&[(13, 12), (19, 12), (22, 22), (16, 29), (10, 22)], G2);
    canvas.poly(&[(14, 14), (18, 14), (20, 22), (16, 26), (12, 22)], G4);
    canvas.disc(16, 20, 2, G0);
    finish(&mut canvas);
    canvas
}

/// Heals on a win — a feather, the lightest thing in the set, against
/// `iron_resolve`'s slab at the other end.
fn second_wind() -> Canvas {
    let mut canvas = new_icon();
    // A broad vane on both sides of the shaft. The first version put the
    // whole vane on one side and read as a green stick with hatching.
    canvas.poly(&[(26, 2), (30, 12), (16, 26), (10, 24)], V2);
    canvas.poly(&[(26, 2), (20, 6), (8, 22), (7, 29), (14, 27)], V2);
    canvas.poly(&[(25, 5), (27, 12), (17, 23), (13, 22)], V4);
    // Barbs, split from the shaft outwards on both sides.
    for step in 1..8 {
        let t = step as f32 / 8.0;
        let x = (26.0 - t * 17.0) as i32;
        let y = (3.0 + t * 23.0) as i32;
        canvas.line(x, y, x + 4, y - 2, V1);
        canvas.line(x, y, x - 3, y + 3, V1);
    }
    canvas.line(27, 2, 8, 27, N7);
    finish(&mut canvas);
    canvas
}

/// Block for every Skill played — a soldier's sash, worn not wielded.
fn skirmishers_sash() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(3, 6), (11, 3), (29, 23), (21, 27)], R2);
    canvas.poly(&[(5, 7), (10, 5), (26, 23), (22, 25)], R3);
    canvas.disc(19, 16, 4, G2);
    canvas.disc(19, 16, 2, G4);
    canvas.line(3, 6, 11, 3, N4);
    canvas.line(21, 27, 29, 23, N4);
    finish(&mut canvas);
    canvas
}

/// Heals at combat start — a carved stone face, weathered smooth.
fn sunken_idol() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(7, 3), (25, 3), (27, 20), (16, 29), (5, 20)], N5);
    canvas.poly(&[(9, 5), (23, 5), (24, 19), (16, 26), (8, 19)], N6);
    canvas.rect(11, 10, 4, 3, N2);
    canvas.rect(17, 10, 4, 3, N2);
    canvas.rect(14, 15, 4, 6, N2);
    canvas.hline(11, 22, 11, N2);
    canvas.rect(9, 5, 14, 2, N7);
    finish(&mut canvas);
    canvas
}

/// Damage back to attackers — a spiked shell.
fn thorned_carapace() -> Canvas {
    let mut canvas = new_icon();
    for (x, y) in [(4, 12), (10, 4), (22, 4), (28, 12), (16, 2)] {
        canvas.poly(&[(x - 3, y + 6), (x, y - 3), (x + 3, y + 6)], N6);
    }
    canvas.disc(16, 18, 12, E1);
    canvas.disc(16, 18, 10, E2);
    canvas.ring(16, 18, 6, 2, E1);
    canvas.ring(16, 18, 2, 2, E1);
    finish(&mut canvas);
    canvas
}

/// Applies Poison on Attack. Shares `vampire_fang`'s silhouette on purpose —
/// same mechanism, different payload — and differs by drip colour.
fn toxic_fang() -> Canvas {
    fang(V2, V4)
}

/// Heals whenever you deal damage.
fn vampire_fang() -> Canvas {
    fang(R3, R5)
}

/// A curved canine with a flat root, not a symmetrical wedge — the straight
/// cone that was here first read unmistakably as an ice cream.
fn fang(drip: Rgb, drip_highlight: Rgb) -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(
        &[(10, 3), (21, 3), (22, 9), (21, 16), (18, 24), (16, 22), (14, 14), (11, 8)],
        N7,
    );
    canvas.poly(&[(12, 5), (16, 5), (16, 18), (14, 12)], N8);
    canvas.rect(10, 3, 12, 3, N5);
    canvas.hline(11, 6, 10, N4);
    droplet(&mut canvas, 19, 28, 3, drip, drip_highlight);
    finish(&mut canvas);
    canvas
}

/// Heals on a kill — a shade that follows you out of the fight.
fn vengeful_spirit() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(16, 13, 10, P3);
    canvas.rect(6, 13, 21, 10, P3);
    // Ragged hem: three tails rather than a straight cut, which is the
    // difference between a ghost and a bell.
    canvas.poly(&[(6, 22), (11, 29), (16, 22), (21, 29), (26, 22)], P3);
    // Slanted sockets — level ones make a friendly ghost, and this one
    // is called Vengeful Spirit.
    canvas.poly(&[(9, 9), (14, 11), (14, 15), (9, 13)], N0);
    canvas.poly(&[(23, 9), (18, 11), (18, 15), (23, 13)], N0);
    canvas.rect(11, 11, 2, 2, R4);
    canvas.rect(19, 11, 2, 2, R4);
    canvas.line(9, 7, 13, 4, P4);
    finish(&mut canvas);
    canvas
}

/// Block every turn — a vambrace with a ward cut into it. The open mouth at
/// the top is load-bearing: without it the taper reads as a pouch, which is
/// what `frugal_satchel` already is.
fn warded_bracer() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(6, 6), (26, 6), (24, 27), (16, 30), (8, 27)], N5);
    canvas.poly(&[(8, 8), (24, 8), (22, 25), (16, 27), (10, 25)], N6);
    canvas.disc(16, 6, 10, N5);
    canvas.rect(6, 6, 21, 3, N5);
    canvas.disc(16, 6, 8, N2);
    canvas.rect(8, 6, 17, 2, N2);
    canvas.rect(8, 13, 16, 2, G2);
    canvas.rect(9, 23, 14, 2, G2);
    canvas.vline(16, 16, 7, B4);
    canvas.hline(12, 18, 9, B4);
    canvas.line(13, 16, 19, 22, B4);
    finish(&mut canvas);
    canvas
}

/// Gold for unspent Energy — a coin that talks, so the sound is the icon.
/// `momentum_token` is the set's other coin and is centred and struck; this
/// one sits left with its arcs to the right, which is the only reliable way to
/// tell two gold discs apart in a HUD row at 1x.
fn whispering_coin() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(12, 16, 9, G2);
    canvas.disc(12, 16, 7, G3);
    canvas.disc(9, 13, 1, G0);
    canvas.hline(8, 20, 6, G0);
    canvas.disc(14, 20, 1, G0);
    canvas.line(23, 10, 26, 16, B3);
    canvas.line(26, 16, 23, 22, B3);
    canvas.line(27, 7, 31, 16, B4);
    canvas.line(31, 16, 27, 25, B4);
    finish(&mut canvas);
    canvas
}

/// The closed-book base `ledger_of_ruin` uses; `restless_grimoire` is drawn
/// open so the pair separates at a glance.
fn book_closed(canvas: &mut Canvas, cover: Rgb, spine: Rgb) {
    canvas.rect(5, 5, 22, 22, cover);
    canvas.rect(5, 5, 5, 22, spine);
    canvas.rect(26, 7, 3, 18, N7);
    canvas.hline(26, 10, 3, N5);
    canvas.hline(26, 16, 3, N5);
    canvas.hline(26, 22, 3, N5);
}
