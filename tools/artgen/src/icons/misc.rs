//! Map nodes, statuses and enemy intents — the three small categories.
//!
//! These are the icons under the most pressure to read at a glance: map nodes
//! are scanned across a whole act at once, and an intent icon is the single
//! piece of information the combat loop is built to telegraph. So they lean
//! harder on silhouette than the card and relic sets do, and each one is a
//! single shape with at most one accent.

use super::shapes::*;
use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        // Map
        Icon { category: "map", name: "fight", draw: fight },
        Icon { category: "map", name: "elite", draw: elite },
        Icon { category: "map", name: "boss", draw: boss },
        Icon { category: "map", name: "rest", draw: rest },
        Icon { category: "map", name: "shop", draw: shop },
        Icon { category: "map", name: "treasure", draw: treasure },
        Icon { category: "map", name: "event", draw: event },
        Icon { category: "map", name: "unknown", draw: unknown },
        // Status
        Icon { category: "status", name: "strength", draw: strength },
        Icon { category: "status", name: "weak", draw: weak },
        Icon { category: "status", name: "vulnerable", draw: vulnerable },
        Icon { category: "status", name: "poison", draw: poison },
        Icon { category: "status", name: "metallicize", draw: metallicize },
        Icon { category: "status", name: "ritual", draw: ritual },
        // Intents
        Icon { category: "intents", name: "attack", draw: intent_attack },
        Icon { category: "intents", name: "defend", draw: intent_defend },
        Icon { category: "intents", name: "buff", draw: intent_buff },
    ]
}

// -- map ------------------------------------------------------------------

/// Crossed swords, the universal "a fight happens here". Drawn tip-up so the
/// X sits high enough that the pommels don't crowd the bottom outline.
fn crossed_swords(blade_body: Rgb, blade_edge: Rgb) -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (9, 25), (26, 6), blade_body, blade_edge);
    sword(&mut canvas, (23, 25), (6, 6), blade_body, blade_edge);
    finish(&mut canvas);
    canvas
}

fn fight() -> Canvas {
    crossed_swords(BLADE, BLADE_EDGE)
}

/// The elite variant is the same weapon in gold under a crown: same encounter
/// grammar, raised stakes. Making it a different *shape* would have cost the
/// instant "this is a fight" read that the pair shares.
fn elite() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (9, 27), (25, 10), G3, G5);
    sword(&mut canvas, (23, 27), (7, 10), G3, G5);
    canvas.poly(&[(9, 9), (12, 4), (16, 8), (20, 4), (23, 9)], G4);
    canvas.rect(9, 8, 15, 2, G4);
    finish(&mut canvas);
    canvas
}

/// Horned skull. The one map icon allowed to be the biggest thing on the
/// screen — it is the act's terminus and the node the whole map funnels to.
fn boss() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(2, 3), (8, 8), (6, 13), (3, 10)], R3);
    canvas.poly(&[(29, 3), (23, 8), (25, 13), (28, 10)], R3);
    skull(&mut canvas, 16, 6, BONE, BONE_SHADE);
    finish_heavy(&mut canvas);
    canvas
}

/// Campfire: two crossed logs under a flame. Rest is the only node whose
/// reward is *not* an object, so it gets a scene instead of an item.
fn rest() -> Canvas {
    let mut canvas = new_icon();
    canvas.thick_line(6, 24, 25, 27, 2, WOOD);
    canvas.thick_line(6, 27, 25, 24, 2, WOOD);
    flame(&mut canvas, 16, 24, 17, E2, E3);
    flame(&mut canvas, 16, 24, 10, E3, E4);
    finish(&mut canvas);
    canvas
}

/// Coin purse. A storefront or a hut would say "shop" more literally and read
/// as a grey blob at 32px; the purse is the smallest shape that carries it.
fn shop() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(
        &[(8, 12), (24, 12), (27, 27), (16, 29), (5, 27)],
        G1,
    );
    canvas.poly(&[(10, 14), (22, 14), (24, 26), (16, 27), (8, 26)], G3);
    canvas.rect(11, 9, 10, 4, N4);
    canvas.rect(12, 7, 8, 3, N5);
    canvas.disc(16, 21, 3, G5);
    finish(&mut canvas);
    canvas
}

/// Treasure chest, lid ajar, one gleam. The gleam is doing real work: a closed
/// box reads as a crate, and a crate is not a reward.
fn treasure() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(4, 16, 25, 12, WOOD);
    canvas.rect(6, 18, 21, 8, G1);
    canvas.poly(&[(4, 16), (7, 8), (26, 8), (29, 16)], WOOD);
    canvas.poly(&[(7, 15), (9, 10), (24, 10), (26, 15)], G2);
    canvas.rect(14, 14, 5, 7, G4);
    canvas.rect(15, 16, 3, 3, N2);
    sparkle(&mut canvas, 25, 6, 3, G5);
    finish(&mut canvas);
    canvas
}

/// A sealed scroll. `ArtAssets.MapIcon` has always looked for this file and
/// there has never been one, so Event was the single node type rendering as
/// the word "EVENT" while its six neighbours had icons — the map's most
/// obvious inconsistency, and it was only ever a missing asset.
fn event() -> Canvas {
    let mut canvas = new_icon();
    canvas.rect(6, 5, 20, 22, N7);
    canvas.rect(4, 3, 24, 5, N5);
    canvas.rect(4, 24, 24, 5, N5);
    canvas.hline(9, 12, 14, N4);
    canvas.hline(9, 16, 14, N4);
    canvas.hline(9, 20, 9, N4);
    canvas.disc(22, 20, 4, R3);
    canvas.disc(22, 20, 2, R4);
    finish(&mut canvas);
    canvas
}

/// The `?` of an unresolved node type. Built from rectangles rather than
/// typeset: the bitmap face's glyph is 8px tall and this has to fill 32.
fn unknown() -> Canvas {
    let mut canvas = new_icon();
    canvas.disc(16, 16, 14, P2);
    canvas.disc(16, 16, 12, P1);
    canvas.rect(11, 8, 10, 4, P4);
    canvas.rect(18, 8, 4, 8, P4);
    canvas.rect(14, 14, 8, 4, P4);
    canvas.rect(14, 16, 4, 5, P4);
    canvas.rect(14, 23, 4, 4, P4);
    finish(&mut canvas);
    canvas
}

// -- statuses --------------------------------------------------------------

/// Strength: a raised fist. Reads as power without an up-arrow, which is
/// already spoken for by the buff intent. Shares `raised_fist` with the Flex
/// card, which is the card that grants this status.
fn strength() -> Canvas {
    let mut canvas = new_icon();
    raised_fist(&mut canvas);
    finish(&mut canvas);
    canvas
}

/// Weak: a downward-broken arrow. Explicitly the inverse of `intent_buff`'s
/// rising arrow, so the pair reads as one axis.
fn weak() -> Canvas {
    let mut canvas = new_icon();
    arrow(&mut canvas, (16, 4), (16, 27), 3, 7, N5);
    // The break: two notched wedges cut out of the shaft, refilled dark, so
    // it reads as a snapped arrow rather than a plain "down".
    canvas.poly(&[(12, 14), (17, 12), (17, 16), (12, 18)], N2);
    canvas.poly(&[(21, 14), (16, 12), (16, 16), (21, 18)], N2);
    finish(&mut canvas);
    canvas
}

/// Vulnerable: a cracked shield. Shares `shield`'s silhouette with the defend
/// intent and Block on purpose — the information is that the shield is *the
/// broken one*, which only lands if the intact one is recognisable.
fn vulnerable() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 4, 24, 25, E1, E2);
    crack(
        &mut canvas,
        &[(16, 4), (13, 11), (18, 15), (14, 22), (16, 28)],
        N0,
    );
    crack(&mut canvas, &[(13, 11), (6, 9)], N0);
    crack(&mut canvas, &[(18, 15), (25, 13)], N0);
    finish(&mut canvas);
    canvas
}

/// Poison: a droplet with bubbles rising out of it. A skull was tried and
/// loses to the healing potion at 1x — both end up a green blob with a light
/// mark in the middle.
fn poison() -> Canvas {
    let mut canvas = new_icon();
    droplet(&mut canvas, 16, 21, 8, V2, V4);
    canvas.disc(11, 20, 2, V0);
    canvas.disc(19, 24, 2, V0);
    canvas.disc(20, 17, 1, V4);
    canvas.disc(24, 9, 2, V3);
    canvas.disc(8, 11, 1, V3);
    finish(&mut canvas);
    canvas
}

/// Metallicize: overlapping armour plates.
///
/// Deliberately *not* a shield, even though it grants Block. `shield` is
/// already spoken for three times over - the defend intent, the Block potion,
/// and (cracked) Vulnerable - and a fourth shield variant at 1x would read as
/// one of those three. Banded plating is the same idea with its own
/// silhouette: no point, no taper, all horizontals.
fn metallicize() -> Canvas {
    let mut canvas = new_icon();
    let plates = [(4, 6, 24, 6), (5, 13, 22, 6), (7, 20, 18, 6), (9, 26, 14, 4)];
    for (index, (x, y, w, h)) in plates.iter().enumerate() {
        canvas.rect(*x, *y, *w, *h, if index % 2 == 0 { B2 } else { B1 });
        // Top edge one step brighter, so each plate reads as sitting proud of
        // the one below rather than the stack reading as a single striped box.
        canvas.hline(*x, *y, *w, B4);
        // Rivets: two per plate, which is what says "metal" rather than
        // "stairs" at this size.
        canvas.set(*x + 2, *y + h / 2, B5);
        canvas.set(*x + *w - 3, *y + h / 2, B5);
    }
    finish(&mut canvas);
    canvas
}

/// Ritual: a horned sigil ring. Violet because the ramp already reserves that
/// family for the arcane, and horns because the card that grants it is Demon
/// Form - the pair should look like the same idea.
fn ritual() -> Canvas {
    let mut canvas = new_icon();
    canvas.poly(&[(4, 3), (11, 11), (7, 13), (3, 8)], P3);
    canvas.poly(&[(28, 3), (21, 11), (25, 13), (29, 8)], P3);
    canvas.ring(16, 19, 10, 3, P3);
    canvas.disc(16, 19, 6, P1);
    // An upward chevron inside the ring: the "and it keeps going up" half,
    // which a plain sigil does not carry.
    canvas.poly(&[(16, 13), (22, 21), (19, 21), (16, 17), (13, 21), (10, 21)], P4);
    canvas.disc(16, 24, 2, P4);
    finish(&mut canvas);
    canvas
}

// -- intents ---------------------------------------------------------------

/// The intent set is the game's most load-bearing telegraph, so all three are
/// drawn oversized and centred with no secondary detail at all.
fn intent_attack() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (8, 24), (27, 5), BLADE, BLADE_EDGE);
    finish(&mut canvas);
    canvas
}

fn intent_defend() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 3, 26, 27, SHIELD_FACE, SHIELD_RIM);
    canvas.vline(16, 8, 14, B4);
    canvas.hline(9, 13, 15, B4);
    finish(&mut canvas);
    canvas
}

fn intent_buff() -> Canvas {
    let mut canvas = new_icon();
    arrow(&mut canvas, (16, 28), (16, 5), 4, 8, G3);
    canvas.vline(15, 12, 14, G4);
    sparkle(&mut canvas, 26, 8, 4, G5);
    sparkle(&mut canvas, 6, 15, 3, G4);
    finish(&mut canvas);
    canvas
}
