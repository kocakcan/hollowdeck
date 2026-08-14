//! The generated icon set — 206 icons across eight categories, replacing the
//! game-icons.net SVGs (79 of them) plus the five event illustrations that
//! never had a vector equivalent at all.
//!
//! Seven of the eight are icons in the ordinary sense and land under
//! `assets/icons/<category>/`. `chrome` is the exception: it is 9-slice border
//! art on the 16/24 grids and writes to `assets/theme/`, which is what puts it
//! under `validate::expected_grid`'s `/theme/` rule rather than the 32x32 icon
//! one. `main::output_dir` holds that single divergence; the alternative — a
//! separate `artgen chrome` subcommand — would have put chrome outside the
//! plain `generate` that CI re-runs to catch committed art drifting from source.
//!
//! Why generate rather than hand-place pixels in an editor: `ArtAssets.cs`
//! resolves art by convention (`assets/icons/cards/<card_id>.png`), so the
//! only thing an icon has to be is *a file with the right name on the right
//! grid* — and the tool can guarantee the grid, the palette and the outline
//! for all of them at once, which is precisely what the vector set could not.
//! A palette edit is then one constant and a re-run.
//!
//! Legibility budget: an icon is 32x32 authored, shown at 1x in the HUD
//! (`PixelSpec.HudIconScale`) and 3x in a card's art window
//! (`PixelSpec.CardArtScale`). 1x is the binding constraint — anything that
//! needs more than about six distinct shapes stops reading there, which is why
//! these are silhouettes with one accent rather than illustrations. The
//! `events` category is the one exception and says so in its own header: it is
//! never drawn below 5x.

use crate::canvas::Canvas;
use crate::palette::*;

mod cards;
mod chrome;
mod events;
mod misc;
mod potions;
mod relics;
mod shapes;

// Shared material colours. Named for what the material *is*, not for the ramp
// entry, so a "steel looks too cold" decision is one edit here rather than a
// search-and-replace across seventy-eight call sites.
pub const BLADE: Rgb = N6;
pub const BLADE_EDGE: Rgb = N8;
pub const GUARD: Rgb = G2;
pub const GRIP: Rgb = N3;
pub const BONE: Rgb = N7;
pub const BONE_SHADE: Rgb = N5;
pub const SHIELD_FACE: Rgb = B2;
pub const SHIELD_RIM: Rgb = B4;
pub const GLASS: Rgb = B1;
pub const CORK: Rgb = G1;
pub const WOOD: Rgb = G0;

pub struct Icon {
    /// Doubles as the subdirectory under `assets/icons/`, so the registry and
    /// the on-disk layout cannot disagree. `chrome` is routed elsewhere — see
    /// `main::output_dir`.
    pub category: &'static str,
    /// The definition id `ArtAssets` looks up. A typo here is a silently
    /// missing icon, which is why the smoke test cross-checks these names
    /// against the JSON content instead of trusting the list.
    pub name: &'static str,
    pub draw: fn() -> Canvas,
}

pub fn all() -> Vec<Icon> {
    let mut icons = Vec::new();
    icons.extend(cards::icons());
    icons.extend(relics::icons());
    icons.extend(potions::icons());
    icons.extend(misc::icons());
    icons.extend(events::icons());
    icons.extend(chrome::icons());
    icons
}
