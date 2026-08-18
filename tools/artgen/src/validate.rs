//! `artgen validate` — the enforceable half of `docs/ART_SPEC.md` §8.
//!
//! This is the command that makes the pixel-art commitment a spec rather than
//! a preference: it fails on an asset whose dimensions are off-grid (§1), on a
//! colour outside the ramp (§5), on a soft alpha edge (§3), and on any SVG
//! left under `assets/` (§8). `tools/run-smoke-tests.sh` runs it before the
//! engine suites, so a non-conforming asset fails the build.
//!
//! The grid an asset must sit on comes from *where it lives*, not from a
//! manifest — `ArtAssets.cs` already resolves art by directory convention, and
//! a second place to declare the same thing is a second place for it to drift.

use std::path::{Path, PathBuf};

use crate::canvas::Canvas;
use crate::palette::{self, Rgb};

pub struct Report {
    pub checked: usize,
    pub failures: Vec<String>,
}

/// Legal dimensions for a path, by the directory it sits in. `None` means the
/// directory carries no dimension rule and only the palette rules apply.
///
/// Chrome was that case until `assets/theme/` gained the 16/24 arm below, and
/// this comment still named it as an example afterwards. Nothing is left in the
/// `None` bucket today except one-off UI textures outside the four directories.
fn expected_grid(path: &Path) -> Option<&'static [(u32, u32)]> {
    let text = path.to_string_lossy().replace('\\', "/");
    if text.contains("/sprites/") {
        // 32x48 is the tall-boss allowance from §1.
        Some(&[(32, 32), (32, 48)])
    } else if text.contains("/backgrounds/") {
        // 256x128 is the backdrop-feature allowance from §1: a focal piece is
        // placed once rather than tiled, so it is a different asset class from
        // the 64x64 floors, walls, plinths and pillars beside it.
        Some(&[(64, 64), (256, 128)])
    } else if text.contains("/icons/") {
        Some(&[(32, 32)])
    } else if text.contains("/theme/") {
        Some(&[(16, 16), (24, 24)])
    } else {
        None
    }
}

pub fn run(root: &Path) -> Report {
    let mut report = Report {
        checked: 0,
        failures: Vec::new(),
    };

    for path in files_under(root, "svg") {
        report.failures.push(format!(
            "{}: SVG under assets/ (ART_SPEC §8) — vector art is the wrong medium; \
             convert it to a 32x32 PNG",
            relative(root, &path)
        ));
    }

    for path in files_under(root, "png") {
        report.checked += 1;
        let name = relative(root, &path);
        let canvas = match Canvas::load(&path) {
            Ok(canvas) => canvas,
            Err(error) => {
                report.failures.push(format!("{name}: unreadable — {error}"));
                continue;
            }
        };

        if let Some(allowed) = expected_grid(&path) {
            if !allowed.contains(&(canvas.width, canvas.height)) {
                let list: Vec<String> = allowed.iter().map(|(w, h)| format!("{w}x{h}")).collect();
                report.failures.push(format!(
                    "{name}: {}x{} is off-grid (ART_SPEC §1, expected {})",
                    canvas.width,
                    canvas.height,
                    list.join(" or ")
                ));
            }
        }

        // Report the palette once per file with a count, not once per pixel:
        // a single unclamped 32x32 icon would otherwise bury every other
        // failure under a thousand lines.
        let mut off_ramp: Vec<Rgb> = Vec::new();
        let mut soft_alpha = 0usize;
        for pixel in canvas.pixels() {
            if pixel.3 != 0 && pixel.3 != 255 {
                soft_alpha += 1;
            }
            if pixel.3 == 0 {
                continue;
            }
            let rgb = pixel.rgb();
            if !palette::is_on_ramp(rgb) && !off_ramp.contains(&rgb) {
                off_ramp.push(rgb);
            }
        }

        if !off_ramp.is_empty() {
            let sample: Vec<String> = off_ramp
                .iter()
                .take(4)
                .map(|&c| format!("{} → {}", palette::hex(c), palette::name_of(palette::nearest(c))))
                .collect();
            let more = off_ramp.len().saturating_sub(4);
            report.failures.push(format!(
                "{name}: {} colour(s) off the ramp (ART_SPEC §5): {}{} — run `artgen clamp`",
                off_ramp.len(),
                sample.join(", "),
                if more > 0 {
                    format!(", +{more} more")
                } else {
                    String::new()
                }
            ));
        }

        if soft_alpha > 0 {
            report.failures.push(format!(
                "{name}: {soft_alpha} partially-transparent pixel(s) (ART_SPEC §3) — \
                 a soft mask edge reads as anti-aliasing under Nearest; \
                 run `artgen clamp` to harden it"
            ));
        }
    }

    report
}

pub fn relative(root: &Path, path: &Path) -> String {
    path.strip_prefix(root)
        .unwrap_or(path)
        .to_string_lossy()
        .replace('\\', "/")
}

pub fn files_under(root: &Path, extension: &str) -> Vec<PathBuf> {
    let mut found = Vec::new();
    collect(root, extension, &mut found);
    found.sort();
    found
}

fn collect(directory: &Path, extension: &str, found: &mut Vec<PathBuf>) {
    let Ok(entries) = std::fs::read_dir(directory) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            collect(&path, extension, found);
        } else if path.extension().is_some_and(|e| e == extension) {
            found.push(path);
        }
    }
}
