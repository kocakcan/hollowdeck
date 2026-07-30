//! `artgen clamp` — snap every pixel of an asset onto the shared ramp.
//!
//! The single biggest cohesion lever in the whole roadmap and the cheapest:
//! the 25 creature sprites were contributed to Dungeon Crawl Stone Soup by
//! different artists over roughly twenty years, and several carry 100–380
//! distinct colours apiece. Clamping them to 43 shared entries is what makes
//! them read as one game's art rather than as a tile pack.
//!
//! Two passes, both idempotent — running this twice changes nothing, which is
//! what lets it be re-run over the whole tree after a palette edit:
//!
//! 1. **Harden alpha.** Anything below the cutoff becomes fully transparent,
//!    anything above becomes fully opaque. Soft mask edges are anti-aliasing
//!    by another name; under Nearest filtering they show as a halo of
//!    half-lit pixels around the silhouette (§3).
//! 2. **Snap colour.** Nearest ramp entry by squared RGB distance, matching
//!    `PixelSpec.Clamp` exactly.
//!
//! No dithering. Dithering trades banding for noise, and at 32x32 with icons
//! displayed at 1x there is no room for a dither pattern to resolve — it just
//! reads as dirt.

use std::path::Path;

use crate::canvas::Canvas;
use crate::palette::{self, Rgba, TRANSPARENT};

/// Anything at or below this is dropped. Deliberately low: DCSS sprites use
/// genuine soft shadows at the sprite's feet that read as intentional at
/// 5x scale, and a 50% cutoff would eat them entirely. This only kills the
/// faint resampling fringe.
const ALPHA_CUTOFF: u8 = 24;

pub struct Outcome {
    pub changed: bool,
    pub colours_before: usize,
    pub colours_after: usize,
}

pub fn clamp_file(path: &Path, dry_run: bool) -> Result<Outcome, String> {
    let mut canvas = Canvas::load(path)?;
    let before = distinct_colours(&canvas);

    let mut changed = false;
    canvas.map(|pixel| {
        let hardened = if pixel.3 <= ALPHA_CUTOFF {
            TRANSPARENT
        } else {
            Rgba(pixel.0, pixel.1, pixel.2, 255)
        };
        let snapped = palette::clamp(hardened);
        if snapped != pixel {
            changed = true;
        }
        snapped
    });

    let after = distinct_colours(&canvas);
    if changed && !dry_run {
        canvas.save(path)?;
    }
    Ok(Outcome {
        changed,
        colours_before: before,
        colours_after: after,
    })
}

fn distinct_colours(canvas: &Canvas) -> usize {
    let mut seen: Vec<(u8, u8, u8)> = Vec::new();
    for pixel in canvas.pixels() {
        if !pixel.is_opaque() {
            continue;
        }
        let key = (pixel.0, pixel.1, pixel.2);
        if !seen.contains(&key) {
            seen.push(key);
        }
    }
    seen.len()
}
