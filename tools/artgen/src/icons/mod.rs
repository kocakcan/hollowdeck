//! The generated icon set — 78 icons across six categories, replacing the
//! game-icons.net SVGs.
//!
//! Why generate rather than hand-place pixels in an editor: `ArtAssets.cs`
//! resolves art by convention (`assets/icons/cards/<card_id>.png`), so the
//! only thing an icon has to be is *a file with the right name on the right
//! grid* — and the tool can guarantee the grid, the palette and the outline
//! for all 78 at once, which is precisely what the vector set could not. A
//! palette edit is then one constant and a re-run.
//!
//! Legibility budget: an icon is 32x32 authored, shown at 1x in the HUD
//! (`PixelSpec.HudIconScale`) and 3x in a card's art window
//! (`PixelSpec.CardArtScale`). 1x is the binding constraint — anything that
//! needs more than about six distinct shapes stops reading there, which is why
//! these are silhouettes with one accent rather than illustrations.

use crate::canvas::Canvas;
use crate::palette::*;

mod cards;
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
    /// the on-disk layout cannot disagree.
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
    icons
}
