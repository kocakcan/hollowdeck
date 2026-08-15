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
        Icon { category: "status", name: "dexterity", draw: dexterity },
        Icon { category: "status", name: "frail", draw: frail },
        Icon { category: "status", name: "regen", draw: regen },
        Icon { category: "status", name: "fervor", draw: fervor },
        Icon { category: "status", name: "foresight", draw: foresight },
        Icon { category: "status", name: "artifact", draw: artifact },
        Icon { category: "status", name: "thorns", draw: thorns },
        Icon { category: "status", name: "intangible", draw: intangible },
        Icon { category: "status", name: "plating", draw: plating },
        // Intents
        Icon { category: "intents", name: "attack", draw: intent_attack },
        Icon { category: "intents", name: "defend", draw: intent_defend },
        Icon { category: "intents", name: "buff", draw: intent_buff },
        Icon { category: "intents", name: "debuff", draw: intent_debuff },
        Icon { category: "intents", name: "summon", draw: intent_summon },
        Icon { category: "intents", name: "escape", draw: intent_escape },
        Icon { category: "intents", name: "dormant", draw: intent_dormant },
        // Deliberately the same `?` the map uses, under a second category.
        // ArtAssets.IntentIcon and MapIcon both fall back to "unknown", and
        // only map/ had the file - so an IntentType with no arm rendered as
        // no icon at all rather than as an unresolved one. The shape is
        // shared rather than redrawn because the meaning is identical.
        Icon { category: "intents", name: "unknown", draw: unknown },
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

/// The `?` of an unresolved node type - and, emitted a second time under
/// `intents`, of an intent type nothing has drawn yet. Built from rectangles
/// rather than typeset: the bitmap face's glyph is 8px tall and this has to
/// fill 32.
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
    // The shadow rim falls back to the face colour, because the ember ramp has
    // no step between E1 and E2 to put it on. The rim therefore shows only
    // where the light lands, which is a thinner statement of the same fact and
    // the honest one at five entries.
    shield(&mut canvas, 16, 4, 24, 25, E1, E2, E1);
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
        // The left edge too: a top edge on its own is a lamp directly
        // overhead, and §10's is up and to the *left*.
        canvas.hline(*x, *y, *w, B4);
        canvas.vline(*x, *y, *h, B4);
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

/// Dexterity: three rising chevrons, brightest at the top.
///
/// Explicitly *not* a shield, for the reason `metallicize` already gives — the
/// silhouette is spoken for three times over (defend intent, Block potion,
/// cracked Vulnerable) and a fourth variant would read as one of those at 1x.
/// Stacked chevrons say "the number goes up" with no outline shape at all,
/// which is also what makes the `frail` inversion below legible.
fn dexterity() -> Canvas {
    let mut canvas = new_icon();
    for (y, colour) in [(21, B2), (14, B3), (7, B4)] {
        chevron(&mut canvas, 16, y, 10, 7, 3, colour, true);
    }
    finish(&mut canvas);
    canvas
}

/// Frail: the same three chevrons, falling instead of rising, in the neutral
/// family and with the lowest one snapped.
///
/// The pairing is the whole icon. Dexterity/Frail are Strength/Weak applied to
/// Block, and `weak` is likewise the inverse of the buff intent's rising arrow
/// with notches cut out of it — same trick, same reading, so the four debuffs
/// stay one visual language.
fn frail() -> Canvas {
    let mut canvas = new_icon();
    for (y, colour) in [(4, N6), (11, N5), (18, N4)] {
        chevron(&mut canvas, 16, y, 10, 7, 3, colour, false);
    }
    // The break: a notch bitten out of the bottom chevron's apex, refilled
    // dark, so the stack reads as collapsing rather than merely pointing down.
    canvas.erase_poly(&[(12, 24), (20, 24), (20, 30), (12, 30)]);
    canvas.poly(&[(13, 25), (16, 28), (19, 25), (19, 29), (13, 29)], N2);
    finish(&mut canvas);
    canvas
}

/// Regen: a heart with a sprout coming out of it.
///
/// The heart is a silhouette nothing else in the set uses — the healing potion
/// is a flask and HP is a number, so there is no collision to design around.
/// The sprout is the half that says *recurring*: a bare heart is "health", a
/// heart growing something is health that comes back, which is the only thing
/// separating Regen from a one-off heal.
fn regen() -> Canvas {
    let mut canvas = new_icon();

    // Two lobes and a taper. Drawn in three passes (body, face, highlight)
    // rather than one filled poly, so the heart has interior shading at the
    // 1x size the HUD actually draws it at.
    canvas.disc(11, 15, 6, R2);
    canvas.disc(21, 15, 6, R2);
    canvas.poly(&[(5, 17), (27, 17), (16, 30)], R2);
    canvas.disc(11, 14, 4, R4);
    canvas.disc(21, 14, 4, R4);
    canvas.poly(&[(7, 16), (25, 16), (16, 27)], R4);
    canvas.disc(11, 13, 2, R5);

    // The sprout rises out of the notch between the lobes, which is the one
    // place a stem can sit without breaking the heart's outline.
    canvas.vline(16, 2, 8, V2);
    canvas.disc(12, 5, 2, V3);
    canvas.disc(20, 3, 2, V3);
    canvas.set(12, 4, V4);
    canvas.set(20, 2, V4);

    finish(&mut canvas);
    canvas
}

/// A single chevron band: two thick strokes meeting at an apex. `up` points
/// the apex at the top of the band, `false` at the bottom.
fn chevron(
    canvas: &mut Canvas,
    cx: i32,
    y: i32,
    half_width: i32,
    height: i32,
    weight: i32,
    colour: Rgb,
    up: bool,
) {
    let (apex, ends) = if up { (y, y + height) } else { (y + height, y) };
    canvas.thick_line(cx - half_width, ends, cx, apex, weight, colour);
    canvas.thick_line(cx, apex, cx + half_width, ends, weight, colour);
}

/// Fervor: the combat HUD's own energy orb, with a lick of flame off the top.
/// Drawn as the orb rather than as a bolt because the thing it hands you is
/// literally the number in that orb - and the energy potion already owns the
/// bolt.
fn fervor() -> Canvas {
    let mut canvas = new_icon();
    orb(&mut canvas, 16, 19, 12, G2, G4);
    flame(&mut canvas, 16, 10, 9, E2, E3);
    canvas.set(11, 15, G5);
    finish(&mut canvas);
    canvas
}

/// Foresight: the eye its two Powers are drawn from, plus the sparkle the set
/// uses everywhere for "and something extra". A closed-then-open pair was
/// tried; at 1x a closed eye is a horizontal line and reads as nothing.
fn foresight() -> Canvas {
    let mut canvas = new_icon();
    eye(&mut canvas, 16, 17, 14, 9, B3);
    sparkle(&mut canvas, 25, 5, 4, B5);
    finish(&mut canvas);
    canvas
}

/// Artifact: a faceted gem, flat-topped and tapering to a point.
///
/// Violet because the ramp reserves that family for the arcane, which puts it
/// next to `ritual` — but a ring and a gem share no outline, and that is the
/// whole reason a gem was picked over the obvious ward-sigil. The set already
/// has two rings (`ritual`, `thorned_carapace`) and a third would be one at 1x.
///
/// Hard facets rather than a glow: what the status does is *refuse* something,
/// and a cut stone reads harder than anything soft-edged.
fn artifact() -> Canvas {
    let mut canvas = new_icon();

    gem(&mut canvas, 16, 5, 12, 24, P1, P3, P4);

    // Facet seams, drawn dark so they read as edges rather than as cracks —
    // `vulnerable` owns cracks and the two must not converge.
    canvas.line(8, 13, 16, 26, P1);
    canvas.line(24, 13, 16, 26, P1);
    canvas.line(12, 7, 13, 13, P2);
    canvas.line(20, 7, 19, 13, P2);
    canvas.set(13, 9, N7);

    finish(&mut canvas);
    canvas
}

/// Thorns: a bramble stem with barbs raking back down it.
///
/// Explicitly *not* a spiked disc — `thorned_carapace` in the relic set is
/// already spikes-around-a-circle, and the relic is close enough in meaning
/// that two similar icons would read as the same thing rather than as a pair.
/// A bare stem has no enclosed shape at all, so the collision cannot happen.
///
/// The barbs are oxblood while the stem is green: what the status does is bill
/// the *attacker*, and that colour break is the only place the icon can say so.
fn thorns() -> Canvas {
    let mut canvas = new_icon();

    // Upright rather than diagonal, which is what buys the barbs their length:
    // a diagonal stem leaves each one about five pixels to be a triangle in,
    // and five-pixel triangles read as specks rather than as thorns.
    canvas.thick_line(16, 3, 16, 30, 5, V1);
    canvas.thick_line(15, 4, 15, 29, 2, V3);

    // Six barbs, alternating sides and raking upwards, each with a wide base on
    // the stem so it reads as growing out of it rather than stuck to it.
    for y in [9, 17, 25] {
        barb(&mut canvas, (14, y), 3, (4, y - 5), R2, R4);
    }
    for y in [13, 21, 29] {
        barb(&mut canvas, (18, y), 3, (28, y - 5), R2, R4);
    }

    finish(&mut canvas);
    canvas
}

/// Intangible: a pale shade with a bitten hem.
///
/// The one silhouette in the whole set that is a *creature* rather than an
/// object or a mark, which is what makes it findable in a crowded status row.
/// Nothing else is a dome over a wavy skirt, and the reading — attacks pass
/// through it — needs a body to pass through.
fn intangible() -> Canvas {
    let mut canvas = new_icon();

    // Pale rather than saturated: this is a shade, and the ramp's bright end is
    // what says "you can see through it" without any actual transparency, which
    // the spec's hard-alpha rule does not allow.
    canvas.disc(16, 13, 9, B2);
    canvas.rect(7, 13, 19, 14, B2);
    canvas.disc(16, 12, 7, B4);
    canvas.rect(9, 12, 15, 14, B4);

    // The hem, bitten out rather than drawn on, so the three lobes are the
    // shape's own outline and pick up the finishing pass.
    canvas.erase_disc(11, 28, 4);
    canvas.erase_disc(21, 28, 4);
    canvas.erase_rect(0, 29, 32, 3);

    canvas.disc(12, 12, 2, N0);
    canvas.disc(20, 12, 2, N0);
    canvas.vline(10, 8, 5, B5);

    finish(&mut canvas);
    canvas
}

/// Plating: a field of scales with one flaked off the corner.
///
/// Deliberately in the same visual family as `metallicize` — they are the same
/// kind of thing, both granting Block at the start of every turn, and a player
/// comparing them should see that at a glance. What separates them is texture,
/// not subject: four wide horizontals with rivets against a field of small
/// curves, which do not read as each other at 1x even though both say "armour".
///
/// The missing scale is the second half of the mechanic. Metallicize is
/// permanent; this erodes, one stack per hit that gets through, and the chip is
/// the only place the icon can carry that.
fn plating() -> Canvas {
    let mut canvas = new_icon();

    // A dark hide behind the scales, so the gaps between them are a material
    // rather than the background showing through - without it the rows read as
    // loose stones piled up.
    canvas.rect(4, 4, 24, 25, B0);

    for (row, y) in [8, 15, 22, 29].iter().enumerate() {
        let stagger = if row % 2 == 0 { 0 } else { 4 };
        let mut x = 5 + stagger;
        while x < 29 {
            // Lit upper arc per scale, so each sits proud of the row beneath it
            // - the same trick metallicize uses on its plate edges.
            scale(&mut canvas, x, *y, 4, B2, B3, B4);
            canvas.set(x - 1, *y - 3, B5);
            x += 8;
        }
    }

    // The flaked corner. Bitten out down to bare hide rather than to the
    // background, so it reads as a scale missing rather than as the icon being
    // clipped - and it is the only place the erosion half of the mechanic can
    // live.
    canvas.erase_disc(25, 25, 5);
    canvas.disc(25, 25, 4, B0);
    canvas.erase_rect(28, 0, 4, 32);
    canvas.erase_rect(0, 30, 32, 2);

    // Corners knocked off. Every other icon in the set has a shape; a bare
    // rectangle is the one silhouette that reads as "unfinished" rather than as
    // anything, and rounding it turns the swatch into a pauldron.
    canvas.erase_poly(&[(0, 0), (9, 0), (0, 9)]);
    canvas.erase_poly(&[(31, 0), (22, 0), (31, 9)]);
    canvas.erase_poly(&[(0, 31), (8, 31), (0, 23)]);
    canvas.erase_poly(&[(31, 31), (23, 31), (31, 23)]);

    finish(&mut canvas);
    canvas
}

// -- intents ---------------------------------------------------------------

/// The intent set is the game's most load-bearing telegraph, so all six are
/// drawn oversized and centred with no secondary detail at all.
fn intent_attack() -> Canvas {
    let mut canvas = new_icon();
    sword(&mut canvas, (8, 24), (27, 5), BLADE, BLADE_EDGE);
    finish(&mut canvas);
    canvas
}

fn intent_defend() -> Canvas {
    let mut canvas = new_icon();
    shield(&mut canvas, 16, 3, 26, 27, SHIELD_FACE, SHIELD_RIM, SHIELD_SHADE);
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

/// Debuff: buff's arrow inverted, in oxblood rather than gold. The pair has to
/// be readable as a pair — same shaft weight, same sparkles, opposite direction
/// and opposite half of the ramp — because the two intents are the same
/// question asked about opposite sides of the fight.
///
/// The colour is what separates it from the Weak and Frail *status* icons,
/// which are also downward and also grey: those say "you have this", this one
/// says "you are about to get something". A grey down-arrow here would have
/// been indistinguishable from Weak's at 1x.
fn intent_debuff() -> Canvas {
    let mut canvas = new_icon();
    arrow(&mut canvas, (16, 4), (16, 27), 4, 8, R3);
    canvas.vline(15, 6, 18, R4);
    sparkle(&mut canvas, 26, 24, 4, R4);
    sparkle(&mut canvas, 6, 17, 3, R3);
    finish(&mut canvas);
    canvas
}

/// Summon: a skull rising out of a violet portal. The other four intents are
/// all a change to a *number*, and this one is a change to who is on the
/// board, so it deliberately shares no shape vocabulary with them — no arrow,
/// no shield, no blade. Violet because it is the only ramp the intent set does
/// not already spend, which is what keeps it separable at 1x from the buff it
/// otherwise sits closest to in meaning.
fn intent_summon() -> Canvas {
    let mut canvas = new_icon();
    // The portal, as a flattened lens rather than a ring: a circle at this
    // size reads as a coin or an orb, and the whole job of the shape is to
    // read as ground the skull is coming *through*.
    for (y, half) in [(23, 4), (24, 8), (25, 10), (26, 9), (27, 5)] {
        canvas.hline(16 - half, y, half * 2 + 1, P2);
    }
    canvas.hline(9, 25, 15, P4);
    skull(&mut canvas, 16, 3, N7, N5);
    sparkle(&mut canvas, 27, 12, 3, P4);
    sparkle(&mut canvas, 5, 16, 3, P3);
    finish(&mut canvas);
    canvas
}

/// Dormant: three `z`s rising left to right. The only intent that is not aimed
/// at the player, so like `intent_summon` it deliberately borrows no shape from
/// the other six — no arrow, no shield, no blade — and sits on cold blue rather
/// than a signal colour. A row that reads as a threat is the one thing this
/// telegraph must not do; the number beside it is a price, not damage.
///
/// It is the zs alone, and getting there cost two drafts. A shut eye with the zs
/// above it is the obvious composition and does not survive the grid: an eye
/// closed is an arc, an arc with lashes under it is a bench with legs, and the
/// mark has to carry at 32px against five icons with hard silhouettes. Nor is
/// `shapes::eye` available for it — that diamond is the arcane family's *open*
/// eye, and closing it means striking it through, which reads as blindness.
/// Freed of the eye, the zs get the whole canvas and the diagonal they need.
fn intent_dormant() -> Canvas {
    let mut canvas = new_icon();

    // Growing and brightening as they rise, which is what makes the three read
    // as one sound receding rather than as three letters.
    zed(&mut canvas, 3, 21, 5, B3);
    zed(&mut canvas, 10, 12, 7, B4);
    zed(&mut canvas, 19, 2, 9, B5);

    finish(&mut canvas);
    canvas
}

/// A `z` with its top-left corner at (x, y) and both bars `size` wide. Shared by
/// all three in `intent_dormant` so they cannot drift into different letters.
fn zed(canvas: &mut Canvas, x: i32, y: i32, size: i32, colour: Rgb) {
    canvas.hline(x, y, size, colour);
    canvas.line(x + size - 1, y, x, y + size - 1, colour);
    canvas.hline(x, y + size - 1, size, colour);
}

/// Escape: an arrow leaving at speed. Horizontal where buff and debuff are
/// vertical, and *going somewhere* rather than pointing at the fight, which is
/// the whole distinction being telegraphed — this enemy is about to stop being
/// a target.
///
/// Gold rather than the buff green, because what an escape costs the player is
/// gold: the label beside it reads `-25g`, and the icon should not be the one
/// element of the telegraph disagreeing about that.
///
/// A doorway to leave *through* was the first draft and is the better idea on
/// paper. It does not survive the grid: half the width is already spent on the
/// arrow, so the door gets nine pixels, and nine pixels of arch outlined in N0
/// reads as a post the arrow has run into. Speed lines say the same thing with
/// the pixels available.
fn intent_escape() -> Canvas {
    let mut canvas = new_icon();
    arrow(&mut canvas, (11, 16), (28, 16), 4, 9, G3);
    canvas.hline(12, 14, 8, G4);
    // Bright enough to read as part of the mark. At G1 they were dashes the
    // eye discarded, and an escape icon with the trail discarded is a buff
    // arrow lying on its side.
    canvas.hline(2, 8, 7, G2);
    canvas.hline(1, 16, 6, G3);
    canvas.hline(2, 24, 7, G2);
    finish(&mut canvas);
    canvas
}
