//! The twelve potions.
//!
//! One flask silhouette for all of them, varying only the liquid colour and a
//! single emblem floating in the body. That is a deliberate constraint rather
//! than laziness: potions are read out of a three-slot belt at 1x, where the
//! useful question is "which of my three is this" — a shared outline plus a
//! distinct colour answers that faster than twelve unrelated bottle shapes,
//! and the emblem is there for the case where two colours are close.
//!
//! Colours follow `UiTheme.Palette`'s existing semantics so a potion matches
//! the number it will produce: heal green (`Heal = V4`), block blue
//! (`Block = B4`), damage red (`Damage = R5`).

use super::shapes::*;
use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        Icon { category: "potions", name: "healing_potion", draw: healing },
        Icon { category: "potions", name: "elixir_of_vigor", draw: vigor },
        Icon { category: "potions", name: "block_potion", draw: block },
        Icon { category: "potions", name: "greater_block_potion", draw: greater_block },
        Icon { category: "potions", name: "fire_potion", draw: fire },
        Icon { category: "potions", name: "energy_potion", draw: energy },
        Icon { category: "potions", name: "focus_potion", draw: focus },
        Icon { category: "potions", name: "swift_potion", draw: swift },
        Icon { category: "potions", name: "strength_potion", draw: strength },
        Icon { category: "potions", name: "weak_potion", draw: weak },
        Icon { category: "potions", name: "vulnerability_potion", draw: vulnerability },
        Icon { category: "potions", name: "poison_potion", draw: poison },
    ]
}

/// Every potion is this: fill the shared flask, stamp one emblem, outline.
fn potion(liquid: Rgb, highlight: Rgb, emblem: impl Fn(&mut Canvas)) -> Canvas {
    let mut canvas = new_icon();
    flask(&mut canvas, liquid, highlight);
    emblem(&mut canvas);
    finish(&mut canvas);
    canvas
}

/// Emblems are drawn around (16, 21) — the centre of the liquid, not of the
/// canvas, since the neck takes the top third.
const EMBLEM_Y: i32 = 21;

fn healing() -> Canvas {
    potion(V3, V5, |canvas| {
        canvas.rect(14, EMBLEM_Y - 5, 5, 11, N8);
        canvas.rect(11, EMBLEM_Y - 2, 11, 5, N8);
    })
}

fn vigor() -> Canvas {
    potion(G3, G5, |canvas| {
        sparkle(canvas, 16, EMBLEM_Y, 5, G5);
    })
}

fn block() -> Canvas {
    potion(B3, B5, |canvas| {
        shield(canvas, 16, EMBLEM_Y - 6, 12, 13, B5, N8);
    })
}

/// The greater version says "more of the same" the way the game's numbers do —
/// same emblem on brighter liquid, with a double rim rather than a second
/// shield. Two overlapping shields were tried and read as one dented one.
fn greater_block() -> Canvas {
    potion(B4, B5, |canvas| {
        shield(canvas, 16, EMBLEM_Y - 7, 15, 16, N8, B1);
        shield(canvas, 16, EMBLEM_Y - 5, 11, 12, B4, B1);
    })
}

fn fire() -> Canvas {
    potion(E2, E4, |canvas| {
        flame(canvas, 16, EMBLEM_Y + 6, 12, E3, E4);
    })
}

fn energy() -> Canvas {
    potion(G2, G4, |canvas| {
        bolt(canvas, 16, EMBLEM_Y, G5);
    })
}

/// Draw effects get the card-stack glyph the pile counters already use, so
/// "this makes cards happen" is the same mark everywhere in the game.
fn focus() -> Canvas {
    potion(P2, P4, |canvas| {
        card_stack(canvas, 17, EMBLEM_Y, N5, N8);
    })
}

/// Swift is draw *and* energy. Both marks at once was tried and turned the
/// 14px liquid into confetti; a bolt trailing two speed lines carries the
/// "faster" reading on its own, and the belt tooltip carries the rest.
fn swift() -> Canvas {
    potion(B2, B4, |canvas| {
        bolt(canvas, 18, EMBLEM_Y, G4);
        canvas.hline(9, EMBLEM_Y - 4, 5, B5);
        canvas.hline(8, EMBLEM_Y, 6, B5);
        canvas.hline(10, EMBLEM_Y + 4, 4, B5);
    })
}

fn strength() -> Canvas {
    potion(R3, R5, |canvas| {
        chevron(canvas, 16, EMBLEM_Y - 4, 7, -1, N8);
        chevron(canvas, 16, EMBLEM_Y + 2, 7, -1, N8);
    })
}

fn weak() -> Canvas {
    potion(N4, N6, |canvas| {
        chevron(canvas, 16, EMBLEM_Y - 2, 7, 1, N7);
        chevron(canvas, 16, EMBLEM_Y + 4, 7, 1, N7);
    })
}

fn vulnerability() -> Canvas {
    potion(E1, E3, |canvas| {
        shield(canvas, 16, EMBLEM_Y - 7, 14, 15, E2, E4);
        crack(
            canvas,
            &[(16, EMBLEM_Y - 7), (13, EMBLEM_Y - 2), (18, EMBLEM_Y + 1), (15, EMBLEM_Y + 8)],
            N0,
        );
    })
}

fn poison() -> Canvas {
    potion(V1, V3, |canvas| {
        canvas.disc(13, EMBLEM_Y + 2, 3, V0);
        canvas.disc(20, EMBLEM_Y, 2, V0);
        canvas.disc(16, EMBLEM_Y - 5, 2, V4);
        canvas.disc(21, EMBLEM_Y + 5, 1, V4);
    })
}

// -- emblem parts ----------------------------------------------------------

/// Lightning bolt — the energy mark, matching the orb the combat HUD uses.
fn bolt(canvas: &mut Canvas, cx: i32, cy: i32, colour: Rgb) {
    canvas.poly(
        &[
            (cx + 3, cy - 6),
            (cx - 3, cy + 1),
            (cx, cy + 1),
            (cx - 3, cy + 6),
            (cx + 3, cy - 1),
            (cx, cy - 1),
        ],
        colour,
    );
}

/// Two offset cards, the same glyph `PileCounterBar` draws in the HUD — each
/// with its own dark border, which is the only thing that stops the pair
/// reading as one rectangle.
fn card_stack(canvas: &mut Canvas, cx: i32, cy: i32, back: Rgb, front: Rgb) {
    canvas.rect(cx - 2, cy - 6, 8, 11, N0);
    canvas.rect(cx - 1, cy - 5, 6, 9, back);
    canvas.rect(cx - 6, cy - 3, 8, 11, N0);
    canvas.rect(cx - 5, cy - 2, 6, 9, front);
}

/// A chevron pointing up (`direction` -1) or down (+1).
fn chevron(canvas: &mut Canvas, cx: i32, cy: i32, width: i32, direction: i32, colour: Rgb) {
    let half = width / 2;
    let depth = 3 * direction;
    canvas.poly(
        &[
            (cx - half, cy + depth),
            (cx, cy),
            (cx + half, cy + depth),
            (cx + half, cy + depth + 2 * direction),
            (cx, cy + 2 * direction),
            (cx - half, cy + depth + 2 * direction),
        ],
        colour,
    );
}
