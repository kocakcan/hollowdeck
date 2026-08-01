//! One illustration per authored event.
//!
//! These are the only icons in the set that are never shown at 1x. `EventScreen`
//! renders them at `PixelSpec.SpriteScale` (5x, 160px) as the screen's focal
//! art, the same size an enemy sprite occupies in combat, so they can carry a
//! little more structure than a map node or a HUD status can. They are still
//! authored on the 32x32 `IconGrid` rather than a larger one: a second grid
//! would need its own rule in `validate.rs` and its own scale constant, to buy
//! detail that a 5x nearest-neighbour blowup of a busier 32x32 does not
//! actually lack.
//!
//! The names are event ids from `data/events/events.json`. `ArtAssets
//! .EventIcon` resolves by convention like everything else, so authoring a new
//! event without art degrades to the scroll — it does not break.

use super::shapes::*;
use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        Icon { category: "events", name: "quiet_shrine", draw: quiet_shrine },
        Icon { category: "events", name: "desperate_traveler", draw: desperate_traveler },
        Icon { category: "events", name: "overgrown_library", draw: overgrown_library },
        Icon { category: "events", name: "sealed_reliquary", draw: sealed_reliquary },
        Icon { category: "events", name: "grasping_shade", draw: grasping_shade },
        Icon { category: "events", name: "bone_orchard", draw: bone_orchard },
        Icon { category: "events", name: "flooded_crypt", draw: flooded_crypt },
        Icon { category: "events", name: "whetstone_altar", draw: whetstone_altar },
        Icon { category: "events", name: "the_confessor", draw: the_confessor },
        Icon { category: "events", name: "alchemists_remains", draw: alchemists_remains },
        Icon { category: "events", name: "hollow_bargain", draw: hollow_bargain },
        Icon { category: "events", name: "starving_hound", draw: starving_hound },
        Icon { category: "events", name: "gamblers_skull", draw: gamblers_skull },
        Icon { category: "events", name: "mirror_pool", draw: mirror_pool },
        Icon { category: "events", name: "hanged_scholar", draw: hanged_scholar },
    ]
}

/// A weathered stone shrine, moss creeping up the base, a small idol standing
/// in the alcove over a votive flame.
///
/// The idol is load-bearing. The first version put a full-height flame in the
/// alcove and read as a *fireplace* — which is doubly wrong, because the rest
/// site is already a campfire and the two would have been the same picture.
/// A figure in a niche is what makes stone-with-a-hole-in-it a shrine.
fn quiet_shrine() -> Canvas {
    let mut canvas = new_icon();

    // Plinth, then the arched housing above it.
    canvas.rect(4, 25, 25, 5, N4);
    canvas.rect(6, 22, 21, 4, N5);
    canvas.rect(7, 8, 19, 15, N5);
    canvas.disc(16, 9, 9, N5);
    canvas.erase_rect(0, 18, GRID, GRID - 18);
    canvas.rect(7, 17, 19, 6, N5);

    // The alcove is cut out and then filled, so its occupant sits *inside*
    // the stone rather than in front of it.
    canvas.rect(11, 11, 11, 12, N2);
    canvas.disc(16, 12, 5, N2);

    // The idol: head, shoulders, tapering robe. Deliberately faceless.
    canvas.disc(16, 12, 2, N7);
    canvas.poly(&[(13, 16), (19, 16), (20, 22), (12, 22)], N6);
    canvas.hline(12, 16, 9, N7);

    // Votive flame at its feet, small enough that it reads as an offering
    // rather than as the subject.
    flame(&mut canvas, 16, 23, 5, E2, E3);

    // Moss: a few irregular clumps, never a continuous band. A band reads as
    // a painted stripe.
    canvas.disc(6, 24, 2, V1);
    canvas.disc(9, 27, 2, V2);
    canvas.disc(26, 25, 2, V1);
    canvas.disc(23, 28, 1, V2);
    canvas.disc(7, 13, 1, V1);

    finish(&mut canvas);
    canvas
}

/// A hooded figure, head bowed, one hand out. The cupped hand is the whole
/// icon — a cloaked silhouette on its own is every hooded figure in the genre,
/// and this one is asking for something.
fn desperate_traveler() -> Canvas {
    let mut canvas = new_icon();

    // Cloak: a tapering trapezoid, widest at the hem.
    canvas.poly(&[(12, 9), (20, 9), (25, 29), (7, 29)], N3);
    canvas.poly(&[(13, 11), (19, 11), (22, 28), (10, 28)], N4);

    // Hood, with the face left as an unlit void rather than drawn.
    canvas.disc(16, 8, 6, N3);
    canvas.disc(16, 9, 4, N1);
    canvas.disc(16, 10, 3, N0);

    // The outstretched arm and open palm. The palm is a squared-off block
    // with three finger stubs, not a disc with a bite taken out of it — the
    // bitten disc was tried and read as a claw, which is a different event.
    canvas.thick_line(20, 15, 26, 18, 3, N3);
    canvas.rect(25, 17, 5, 5, N6);
    for x in [25, 27, 29] {
        canvas.vline(x, 14, 4, N6);
    }
    canvas.vline(26, 15, 3, N4);
    canvas.vline(28, 15, 3, N4);

    // A staff, so the silhouette has one straight edge to read against the
    // cloak's curves.
    canvas.vline(8, 6, 24, G0);
    canvas.disc(8, 6, 2, G1);

    finish(&mut canvas);
    canvas
}

/// A leaning shelf of rotted books with vines through it, and the one volume
/// that is still legible pulled proud of the rest in a lighter binding.
fn overgrown_library() -> Canvas {
    let mut canvas = new_icon();

    canvas.rect(3, 4, 27, 25, N2);
    canvas.rect(5, 6, 23, 9, N1);
    canvas.rect(5, 18, 23, 9, N1);

    // Upper shelf: a run of spines, one of them fallen against the others.
    for (x, colour) in [(6, R2), (9, N4), (12, V1), (15, R1), (18, N3)] {
        canvas.rect(x, 6, 3, 9, colour);
    }
    canvas.poly(&[(22, 15), (27, 8), (28, 11), (24, 15)], N4);

    // Lower shelf, and the legible book: brightest thing here, pulled forward.
    for (x, colour) in [(6, N3), (9, R1), (19, V1), (22, N4), (25, R2)] {
        canvas.rect(x, 18, 3, 9, colour);
    }
    canvas.rect(12, 16, 6, 11, G2);
    canvas.rect(13, 17, 4, 9, N8);
    canvas.vline(12, 16, 11, G4);

    // Vines: down the frame and across the shelves, never symmetric. Drawn in
    // V3/V4 rather than the V1/V2 tried first — the dark greens are within a
    // step or two of the rotted spines they cross and simply disappeared, and
    // "overgrown" is the entire point of the icon.
    crack(&mut canvas, &[(3, 4), (5, 12), (2, 19), (4, 28)], V3);
    crack(&mut canvas, &[(30, 6), (27, 14), (30, 22)], V3);
    crack(&mut canvas, &[(5, 16), (11, 15), (17, 16), (24, 15), (28, 16)], V3);
    canvas.disc(5, 13, 1, V4);
    canvas.disc(3, 24, 1, V4);
    canvas.disc(28, 15, 1, V4);
    canvas.disc(20, 15, 1, V4);

    finish(&mut canvas);
    canvas
}

/// A stone case bound shut, with a sigil burning on the lid. The bands and the
/// sigil are both required: bands alone read as a crate, sigil alone as a coin.
fn sealed_reliquary() -> Canvas {
    let mut canvas = new_icon();

    canvas.rect(4, 9, 25, 19, N4);
    canvas.rect(6, 11, 21, 15, N3);
    // Chamfered lid, so the box has a top face and reads as solid.
    canvas.poly(&[(4, 9), (8, 4), (25, 4), (29, 9)], N5);
    canvas.poly(&[(7, 8), (10, 6), (23, 6), (26, 8)], N6);

    // Iron bands.
    canvas.vline(10, 4, 24, N2);
    canvas.vline(22, 4, 24, N2);
    canvas.hline(4, 20, 25, N2);

    // The seal: a ringed sigil, the one thing on the icon that is lit. The
    // core is a diamond rather than a `sparkle` — a four-armed sparkle inside
    // a ring is a plus inside a circle, which at 5x reads unmistakably as a
    // medical cross.
    canvas.ring(16, 17, 6, 2, P3);
    canvas.disc(16, 17, 4, P1);
    canvas.poly(&[(16, 13), (20, 17), (16, 21), (12, 17)], P4);
    canvas.poly(&[(16, 15), (18, 17), (16, 19), (14, 17)], P2);
    canvas.disc(26, 6, 1, P4);
    canvas.disc(7, 24, 1, P4);

    finish(&mut canvas);
    canvas
}

/// A hand reaching up out of the dark, fingers spread. Drawn in the cold blue
/// end of the ramp with the wrist fading into an unlit base rather than ending
/// in a cuff — "translucent" has no alpha channel available here (the spec
/// forbids soft edges), so it has to be carried by hue and by the shape simply
/// not having a bottom.
fn grasping_shade() -> Canvas {
    let mut canvas = new_icon();

    // Palm, then the wrist trailing off into the dark it came from.
    canvas.rect(10, 14, 13, 9, B2);
    canvas.poly(&[(11, 22), (22, 22), (24, 30), (9, 30)], B1);
    canvas.rect(12, 27, 9, 3, B0);

    // Four fingers and a thumb, each a different length so the spread reads.
    for (x, top) in [(11, 6), (15, 3), (19, 5)] {
        canvas.rect(x, top, 3, 16 - top + 6, B3);
        canvas.disc(x + 1, top, 1, B4);
    }
    canvas.poly(&[(23, 10), (26, 12), (24, 20), (21, 18)], B3);
    canvas.poly(&[(9, 11), (6, 14), (9, 20), (11, 18)], B3);

    // Knuckle shading, cut dark rather than one ramp step down — at 5x a
    // single step vanishes into the palm.
    canvas.hline(11, 15, 11, B1);
    canvas.vline(14, 6, 10, B1);
    canvas.vline(18, 4, 12, B1);

    finish(&mut canvas);
    canvas
}

/// Rows of bone trees with pale fruit hanging from the branches. The trunks
/// are drawn as jointed bone rather than wood — segmented, with a knuckle at
/// each joint — because a grey trunk on its own is just a dead tree, and the
/// name is the whole premise.
fn bone_orchard() -> Canvas {
    let mut canvas = new_icon();

    canvas.rect(0, 27, GRID, 5, N2);
    canvas.hline(0, 27, GRID, N3);

    // Three trees at different depths: the near one lit, the two behind it a
    // step down, which is what makes it read as rows rather than a clump.
    for (x, top, shade, fruit) in [(6, 12, N5, N6), (25, 14, N5, N6), (16, 6, N7, N8)] {
        canvas.vline(x, top, 28 - top, shade);
        canvas.vline(x + 1, top, 28 - top, shade);
        // Knuckles: the joints that make the trunk a bone.
        canvas.disc(x, top + 5, 2, shade);
        canvas.disc(x + 1, top + 12, 2, shade);
        // Branches, forking up and out from the crown.
        canvas.line(x, top + 2, x - 5, top - 3, shade);
        canvas.line(x + 1, top + 2, x + 6, top - 3, shade);
        canvas.disc(x - 5, top - 3, 1, shade);
        canvas.disc(x + 6, top - 3, 1, shade);
        // The fruit, hung below the branch ends so it reads as weight.
        canvas.disc(x - 5, top + 1, 2, fruit);
        canvas.disc(x + 6, top + 1, 2, fruit);
        canvas.set(x - 6, top, N8);
    }

    finish(&mut canvas);
    canvas
}

/// Black water up a stone stair, with something gold under the surface.
///
/// The waterline is the icon. It is drawn as a hard horizontal edge with the
/// submerged steps a full family cooler and two ramp steps darker than the dry
/// ones — at 5x, "underwater" has to be a colour shift across a straight line,
/// because the spec allows no transparency to fade one.
fn flooded_crypt() -> Canvas {
    let mut canvas = new_icon();

    // Dry steps descending from the left, then the same flight continuing
    // under the surface.
    for (index, y) in (4..16).step_by(4).enumerate() {
        let x = 2 + index as i32 * 5;
        canvas.rect(x, y, 30 - x, 4, N4);
        canvas.hline(x, y, 30 - x, N6);
    }
    for (index, y) in (16..28).step_by(4).enumerate() {
        let x = 17 + index as i32 * 4;
        canvas.rect(x, y, 30 - x, 4, B1);
        canvas.hline(x, y, 30 - x, B2);
    }

    // The waterline, and the wall the stair runs down.
    canvas.rect(0, 16, GRID, 16, B0);
    for (index, y) in (16..28).step_by(4).enumerate() {
        let x = 17 + index as i32 * 4;
        canvas.rect(x, y, 30 - x, 3, B1);
    }
    canvas.hline(0, 16, GRID, B3);
    canvas.hline(0, 17, GRID, B2);

    // The glint, two steps under - small, and the only warm pixel below the
    // line, which is what makes it findable.
    canvas.disc(9, 23, 2, G3);
    canvas.set(8, 22, G5);

    finish(&mut canvas);
    canvas
}

/// A grinding wheel on its frame, turning, throwing sparks. The wheel is
/// deliberately off-centre and large enough to leave the frame cramped: the
/// stone is the subject and the trestle is only there to say it is a tool
/// rather than a millstone.
fn whetstone_altar() -> Canvas {
    let mut canvas = new_icon();

    // Trestle legs, crossed, behind the wheel.
    canvas.thick_line(6, 30, 14, 18, 2, WOOD);
    canvas.thick_line(24, 30, 16, 18, 2, WOOD);
    canvas.rect(4, 29, 24, 3, G0);

    // The wheel: rim, face, hub. Two rings rather than a filled disc so it
    // reads as stone with a mounted centre.
    canvas.disc(15, 15, 12, N3);
    canvas.disc(15, 15, 10, N5);
    canvas.ring(15, 15, 10, 2, N6);
    canvas.disc(15, 15, 3, N2);
    canvas.disc(15, 15, 1, G2);

    // Wet sheen on the upper left, where a turning wheel would carry it.
    canvas.line(8, 10, 12, 7, N7);
    canvas.line(7, 13, 9, 10, N7);

    // Sparks off the contact point, thrown up and away.
    for (x, y, colour) in [(26, 8, E3), (29, 5, E4), (24, 4, E3), (28, 12, E2)] {
        canvas.disc(x, y, 1, colour);
    }
    canvas.line(23, 10, 27, 6, E3);

    finish(&mut canvas);
    canvas
}

/// A slatted mask behind a grille. Two grids at right angles - the mask's
/// horizontal slats and the grille's vertical bars - which is what makes the
/// face read as being *behind* something rather than wearing a striped helmet.
fn the_confessor() -> Canvas {
    let mut canvas = new_icon();

    // The booth's dark interior, then the masked head inside it. The mask sits
    // high on the ramp: it is *behind* a set of bars that are themselves light,
    // and a mid-ramp face read as more grille rather than as a head - the icon
    // came out a bare prison window.
    canvas.rect(3, 3, 26, 28, N1);
    canvas.disc(16, 14, 8, N7);
    canvas.rect(8, 14, 17, 10, N7);
    canvas.poly(&[(8, 22), (25, 22), (22, 29), (11, 29)], N7);

    // Mask slats: horizontal, evenly spaced, cut dark.
    for y in (9..26).step_by(4) {
        canvas.hline(9, y, 15, N2);
    }
    canvas.hline(9, 11, 15, N0);

    // The grille: vertical bars across the whole opening, mid-ramp so they
    // read as iron in front of a pale mask rather than competing with it.
    for x in (4..30).step_by(5) {
        canvas.vline(x, 3, 28, N4);
    }
    canvas.rect(3, 3, 26, 2, N5);
    canvas.rect(3, 29, 26, 2, N5);

    finish(&mut canvas);
    canvas
}

/// A slumped body over a workbench, one hand on a flask and one on a purse.
/// The two objects are the choice, so they are the two lit things on the icon
/// and the alchemist is drawn as an unlit mass between them.
fn alchemists_remains() -> Canvas {
    let mut canvas = new_icon();

    // Bench.
    canvas.rect(1, 22, 30, 4, N4);
    canvas.hline(1, 22, 30, N5);
    canvas.vline(4, 26, 6, N3);
    canvas.vline(27, 26, 6, N3);

    // The body: head down on the bench, shoulders humped, arms out to each
    // side. A silhouette rather than a subject, but at N2 it was invisible
    // against the unlit background and the icon read as an empty bench with
    // two objects on it - so it sits at N3/N4, still the darkest mass here.
    canvas.disc(16, 17, 6, N4);
    canvas.poly(&[(8, 22), (24, 22), (22, 14), (10, 14)], N3);
    canvas.thick_line(11, 18, 5, 21, 3, N3);
    canvas.thick_line(21, 18, 27, 21, 3, N3);
    canvas.hline(11, 16, 11, N2);

    // Flask, left hand - the same glass-and-cork the potion icons use.
    canvas.rect(3, 14, 5, 3, CORK);
    canvas.poly(&[(2, 17), (9, 17), (10, 22), (1, 22)], GLASS);
    canvas.poly(&[(3, 19), (8, 19), (9, 21), (2, 21)], V3);

    // Purse, right hand.
    canvas.disc(27, 19, 4, G1);
    canvas.rect(25, 15, 5, 3, G0);
    canvas.disc(26, 18, 1, G4);
    canvas.disc(29, 20, 1, G3);

    finish(&mut canvas);
    canvas
}

/// An open hand offering a relic, with nothing attached to the arm. The empty
/// space above the wrist is the whole idea - the voice has no source - and it
/// is the same trick `grasping_shade` uses, run the other way up: that hand
/// takes and fades downward, this one gives and fades upward.
fn hollow_bargain() -> Canvas {
    let mut canvas = new_icon();

    // Forearm rising from the bottom edge, fading out as it goes.
    canvas.rect(13, 24, 7, 8, P1);
    canvas.rect(13, 20, 7, 5, P2);

    // Open palm, fingers curled up around the offering.
    canvas.rect(10, 15, 13, 7, P3);
    for x in [10, 14, 18, 22] {
        canvas.vline(x, 11, 5, P3);
        canvas.set(x, 11, P4);
    }

    // The relic: a ringed stone hovering just clear of the palm, which is
    // what says "offered" rather than "held".
    canvas.ring(16, 6, 5, 2, G3);
    canvas.disc(16, 6, 3, G1);
    canvas.disc(16, 6, 1, G5);
    sparkle(&mut canvas, 26, 12, 3, P4);
    sparkle(&mut canvas, 6, 9, 2, P4);

    finish(&mut canvas);
    canvas
}

/// A dog drawn as ribs first. Every proportion is wrong on purpose - the head
/// too large, the legs too long, the barrel of the body collapsed to a bare
/// cage - because a correctly-drawn hound at this size is just a dog, and the
/// event is about how far gone it is.
fn starving_hound() -> Canvas {
    let mut canvas = new_icon();

    // Legs: four thin verticals, splayed.
    for (x, top) in [(9, 18), (12, 19), (21, 18), (24, 19)] {
        canvas.vline(x, top, 30 - top, N4);
    }

    // Body: a shallow arc for the spine and a hollow underline, leaving the
    // belly visibly empty between them.
    canvas.thick_line(9, 14, 24, 13, 2, N5);
    canvas.line(11, 20, 22, 19, N4);

    // Ribs, spaced wide so each one is countable.
    for x in (11..22).step_by(3) {
        canvas.vline(x, 14, 6, N6);
    }

    // Head: oversized, hung low, with a long muzzle and one lit eye.
    canvas.disc(25, 12, 5, N5);
    canvas.poly(&[(27, 10), (31, 12), (31, 16), (27, 16)], N5);
    canvas.poly(&[(22, 6), (26, 8), (24, 12)], N4);
    canvas.set(27, 11, E3);
    canvas.set(30, 14, N2);

    // Tail, tucked.
    canvas.line(8, 14, 5, 19, N4);

    finish(&mut canvas);
    canvas
}

/// A skull with knucklebones in its jaw. The bones sit *in the mouth* rather
/// than scattered in front of it, which is the difference between "a skull,
/// and also some dice" and one object that is mid-throw.
fn gamblers_skull() -> Canvas {
    let mut canvas = new_icon();

    skull(&mut canvas, 15, 2, BONE, BONE_SHADE);

    // The jaw dropped open below the cranium, with the throw held in it.
    canvas.rect(10, 20, 12, 3, BONE_SHADE);
    canvas.rect(9, 22, 14, 6, N1);
    canvas.hline(9, 27, 14, BONE_SHADE);

    // Four knucklebones, at four angles - a matched set reads as a pattern.
    for (x, y, size) in [(11, 23, 3), (15, 24, 3), (19, 23, 3), (15, 20, 2)] {
        canvas.rect(x, y, size, size, N8);
        canvas.set(x + 1, y + 1, N2);
    }
    canvas.set(12, 24, N2);
    canvas.set(20, 24, N2);

    finish(&mut canvas);
    canvas
}

/// Still water showing a figure that is not quite where you are. Two
/// silhouettes, one above the waterline and one below, deliberately offset by
/// three pixels - a correct mirror is a mirror, and an incorrect one is the
/// event.
fn mirror_pool() -> Canvas {
    let mut canvas = new_icon();

    // The pool: an ellipse of still water filling the lower half.
    canvas.disc(16, 22, 14, B0);
    canvas.erase_rect(0, 0, GRID, 15);
    canvas.rect(2, 15, 28, 8, B0);
    canvas.hline(3, 15, 26, B2);

    // The figure above: head, shoulders, standing on the near edge. High on
    // the ramp - the reflection below is drawn in the B family against near
    // black, so a mid-grey figure above the line was the *dimmer* of the two
    // and the pair read backwards.
    canvas.disc(13, 6, 4, N7);
    canvas.poly(&[(9, 11), (17, 11), (19, 15), (7, 15)], N6);

    // The reflection: offset right, one family cooler, and drawn *upside
    // down* so it is a reflection rather than a second person.
    canvas.disc(19, 25, 4, B2);
    canvas.poly(&[(15, 20), (27, 20), (25, 24), (17, 24)], B2);

    // Surface: two broken highlight lines, never a continuous band.
    canvas.hline(6, 19, 7, B3);
    canvas.hline(20, 27, 6, B3);

    finish(&mut canvas);
    canvas
}

/// A body hanging in academic robes, notes pinned to the sleeves. Drawn from
/// the beam down so the rope is the first thing read; the notes are the only
/// bright thing on it, because they are what the choice is actually about.
fn hanged_scholar() -> Canvas {
    let mut canvas = new_icon();

    // Beam and rope.
    canvas.rect(0, 1, GRID, 3, WOOD);
    canvas.hline(0, 1, GRID, G1);
    canvas.vline(16, 4, 6, N6);

    // Head, then the robe hanging straight and heavy below it.
    canvas.disc(16, 12, 4, N5);
    canvas.poly(&[(11, 16), (21, 16), (24, 31), (8, 31)], N3);
    canvas.poly(&[(12, 18), (20, 18), (22, 30), (10, 30)], N4);

    // Sleeves, hanging away from the body.
    canvas.poly(&[(11, 17), (7, 19), (6, 27), (10, 26)], N3);
    canvas.poly(&[(21, 17), (25, 19), (26, 27), (22, 26)], N3);

    // The notes: pale scraps pinned to both sleeves and the hem, the only
    // high-ramp values on the icon.
    for (x, y, w, h) in [(6, 21, 4, 5), (23, 22, 4, 5), (14, 24, 5, 6)] {
        canvas.rect(x, y, w, h, N8);
        canvas.hline(x, y + 2, w, N5);
        canvas.hline(x, y + 4, w, N5);
    }

    finish(&mut canvas);
    canvas
}
