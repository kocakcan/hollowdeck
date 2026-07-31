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
