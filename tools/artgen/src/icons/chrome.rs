//! The fourteen 9-slice chrome frames — `docs/ART_SPEC.md` §6, and the item
//! `docs/PIXEL_ART_ROADMAP.md` §2 tracked as the spec's own unbuilt claim.
//!
//! This is the one category that does **not** write into `assets/icons/`. It
//! lands in `assets/theme/` beside `hollowdeck_theme.tres`, which is what makes
//! `validate::expected_grid`'s `/theme/` arm (16x16 or 24x24) apply to it
//! instead of the 32x32 icon rule — the same trick `anim.rs` plays by writing
//! under `/sprites/`. `main::output_dir` is where that routing lives.
//!
//! Why generated at all, when a frame is four lines and a corner: the same
//! reason as every other icon. A ramp edit is one constant and a re-run, CI
//! re-runs `generate` and diffs the result, and a slice off the ramp or off the
//! grid fails `validate` rather than shipping.
//!
//! ## What a slice has to survive
//!
//! A `StyleBoxTexture` draws the four corners 1:1, tiles the four edges along
//! their axis, and tiles the centre. So:
//!
//! - **The edges carry no motif.** Every pixel in the top edge strip is a
//!   function of `y` alone (and the left edge of `x` alone), so the tile seam is
//!   invisible at any width. A repeating notch would be legible as banding on a
//!   long panel and would land at a different phase on every panel in the game.
//! - **The corner budget is `size / 3`** (ART_SPEC §1: "corners must be ≤ 1/3 of
//!   the slice"), i.e. 5 at 16 and 8 at 24. Everything decorative therefore has
//!   to fit in the first five pixels, which is why the pip is three pixels and
//!   sits at inset 3–4. `ChromeStyles.SliceStyle` sets the matching texture
//!   margin and `PixelSpecSmokeTest.TestChromeCornersFitTheSlice` holds the two
//!   together.
//!
//! ## Two sizes
//!
//! The slice size follows the *smallest box that uses it*, because a
//! `StyleBoxTexture` whose box is shorter than `2 × margin` overlaps its own
//! corners:
//!
//! - **16x16 (margin 5)** — panels and HUD slots. `ScreenChrome`'s HP and gold
//!   panels set `ContentMarginTop/Bottom = 4` and are the shortest boxes in the
//!   game; 24 would fold their top and bottom corners into each other.
//! - **24x24 (margin 8)** — buttons and the art plinth, all of which are at
//!   least 28px tall, and all of which have the room to carry more frame.

use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        // Panels. N1 face over a G1/G2 bezel - the same pair the HP-bar bezel
        // uses, and the reason PanelStyle's off-ramp Color(0.5, 0.4, 0.22)
        // literal could be retired rather than approximated.
        Icon { category: "chrome", name: "panel", draw: panel },
        Icon { category: "chrome", name: "panel_focus", draw: panel_focus },
        // A single HUD slot - one relic, one potion. An empty slot recedes via
        // a dimmer bezel over the deep face rather than disappearing, which is
        // the difference between "room for two more" and "nothing here".
        Icon { category: "chrome", name: "slot_filled", draw: slot_filled },
        Icon { category: "chrome", name: "slot_empty", draw: slot_empty },
        // The art plinth: the deep face an icon sits on at 5x, so the art
        // separates from the panel around it.
        Icon { category: "chrome", name: "plinth", draw: plinth },
        // Ordinary buttons, driven from hollowdeck_theme.tres. Hover brightens
        // the bezel rather than swapping hue - a pure brightness signal, the
        // same rule CardView's frame follows.
        Icon { category: "chrome", name: "button_normal", draw: button_normal },
        Icon { category: "chrome", name: "button_hover", draw: button_hover },
        Icon { category: "chrome", name: "button_pressed", draw: button_pressed },
        Icon { category: "chrome", name: "button_disabled", draw: button_disabled },
        Icon { category: "chrome", name: "button_focus", draw: button_focus },
        // The emphasis treatment - End Turn, the main menu's entries. This is
        // the ornament that replaces the sourced CC0 wooden frame retired in
        // CREDITS.md: a heavier bezel and a brighter pip than the ordinary
        // button, in a medium that can sit beside a 32x32 sprite.
        Icon { category: "chrome", name: "emphasis_normal", draw: emphasis_normal },
        Icon { category: "chrome", name: "emphasis_hover", draw: emphasis_hover },
        Icon { category: "chrome", name: "emphasis_pressed", draw: emphasis_pressed },
        Icon { category: "chrome", name: "emphasis_disabled", draw: emphasis_disabled },
    ]
}

/// Every slice is this: a 1px `outer` rule flush to the edge, a 1px gutter of
/// the face colour, a 1px `inner` rule, and a three-pixel stepped `pip` tucked
/// into each corner.
///
/// `face: None` leaves the interior transparent, which is what a focus ring
/// wants - the theme's `sb_btn_focus` keeps `draw_center = false`, but Godot
/// still paints the corner and edge patches, so their interior pixels have to
/// be transparent rather than merely unpainted-looking.
///
/// The pip is deliberately the *only* asymmetric thing here, and it lives
/// entirely inside the corner patch. Anything drawn between inset 5 and the
/// centre would fall in the edge strip and tile.
fn frame(size: u32, face: Option<Rgb>, outer: Rgb, inner: Rgb, pip: Rgb) -> Canvas {
    let mut canvas = Canvas::new(size, size);
    let n = size as i32;

    if let Some(face) = face {
        canvas.rect(0, 0, n, n, face);
    }

    // Outer rule, flush to the grid edge. Nothing outlines this the way
    // shapes::finish outlines an icon - a slice tiles against its own
    // neighbours, so an N0 halo would draw a seam down the middle of a panel.
    canvas.hline(0, 0, n, outer);
    canvas.hline(0, n - 1, n, outer);
    canvas.vline(0, 0, n, outer);
    canvas.vline(n - 1, 0, n, outer);

    // Inner rule at inset 2, leaving the face colour showing between the two.
    canvas.hline(2, 2, n - 4, inner);
    canvas.hline(2, n - 3, n - 4, inner);
    canvas.vline(2, 2, n - 4, inner);
    canvas.vline(2, n - 3, n - 4, inner);

    // The pip, mirrored into all four corners: two pixels along the top of the
    // corner and one down its side, so it reads as a stepped bracket rather
    // than a dot. Inset 3-4, i.e. the last two pixels of the 5px corner budget.
    for (dx, dy) in [(1, 1), (-1, 1), (1, -1), (-1, -1)] {
        let ox = if dx > 0 { 3 } else { n - 4 };
        let oy = if dy > 0 { 3 } else { n - 4 };
        canvas.set(ox, oy, pip);
        canvas.set(ox + dx, oy, pip);
        canvas.set(ox, oy + dy, pip);
    }

    canvas
}

/// Panels and slots. See the module header for why these are not 24.
const SMALL: u32 = 16;
/// Buttons and the plinth.
const LARGE: u32 = 24;

fn panel() -> Canvas {
    frame(SMALL, Some(N1), G1, G2, G3)
}

/// Focus is G5 (the top of the gold ramp) at both rules, not a brighter pip on
/// the same bezel: hollowdeck_theme.tres's sb_btn_focus already establishes
/// that focus is FocusRing and that G4 is spoken for by hover, so a focused
/// tile and a hovered one must not be the same picture.
fn panel_focus() -> Canvas {
    frame(SMALL, Some(N1), G3, G5, G5)
}

fn slot_filled() -> Canvas {
    frame(SMALL, Some(N1), G1, G2, G3)
}

fn slot_empty() -> Canvas {
    frame(SMALL, Some(N0), G0, G0, G1)
}

fn plinth() -> Canvas {
    frame(LARGE, Some(N0), G1, G2, G3)
}

fn button_normal() -> Canvas {
    frame(LARGE, Some(N2), G1, G1, G2)
}

fn button_hover() -> Canvas {
    frame(LARGE, Some(N3), G4, G3, G5)
}

fn button_pressed() -> Canvas {
    frame(LARGE, Some(N1), G3, G2, G3)
}

fn button_disabled() -> Canvas {
    frame(LARGE, Some(N1), N3, N3, N4)
}

fn button_focus() -> Canvas {
    frame(LARGE, None, G5, G5, G5)
}

fn emphasis_normal() -> Canvas {
    frame(LARGE, Some(N2), G2, G1, G3)
}

fn emphasis_hover() -> Canvas {
    frame(LARGE, Some(N3), G4, G3, G5)
}

fn emphasis_pressed() -> Canvas {
    frame(LARGE, Some(N1), G3, G2, G4)
}

fn emphasis_disabled() -> Canvas {
    frame(LARGE, Some(N1), N3, N2, N4)
}
