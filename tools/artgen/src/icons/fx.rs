//! Combat effect frames — `docs/PIXEL_ART_ROADMAP.md` §5.
//!
//! Four bursts of four frames each, played one-shot by `SpriteAnimator` over a
//! creature at `PixelSpec.SpriteScale`. They replace `CombatScreen`'s
//! `CpuParticles2D` spark, whose texture was a smooth 24x24 radial gradient
//! drawn at `ScaleAmountMin 0.4 / Max 0.9` — off the §5 ramp, soft-edged
//! against §3, fractionally scaled against §2, and invisible to every check the
//! project has, because it was neither a `TextureRect` nor a file under
//! `assets/`.
//!
//! ## These are frame runs, not icons
//!
//! This is the one category under `assets/icons/` whose files are not one per
//! definition id. `impact_0.png` … `venom_3.png` are a *sequence*; nothing
//! resolves them by content id and `ArtAssets.FxFrames` loads them until the
//! first gap the way `AnimFrames` does. `main::output_dir` needs no arm for
//! this — the default one already routes an unknown category to
//! `assets/icons/<category>/`, which is also what puts the frames under CI's
//! existing `assets/icons` drift diff and `validate`'s 32x32 icon rule.
//!
//! The names are deliberately not `impact`/`block`/`heal`/`poison`: these sit
//! one directory over from `assets/icons/status/poison.png`, all flat 32x32
//! PNGs, and a name that reads as a status id makes a grep ambiguous about
//! which asset is meant.
//!
//! ## Emissive, so §10 does not apply
//!
//! A burst *is* the light, like `shapes::flame`, `orb` and `sparkle`. Giving one
//! a lit side would say it is lit by something else, so nothing here consults
//! `light.rs` and nothing here is covered by its blade sweep. What covers these
//! instead is the four rules at the bottom of this file, each pointed at
//! something no other layer can see: `validate` reads finished pixels and
//! cannot tell a burst from a blob, and the C# side can only count files.
//!
//! ## One shape, four pigments
//!
//! Every effect is the same four-beat `burst` — flash, ring, arms, motes —
//! separated only by which ramp family it is drawn in. That is not laziness: a
//! burst reads as *this landed here* and the colour is what says which kind of
//! thing landed, exactly as `EnemyView`'s intent icons share one silhouette
//! budget. It is also the only way to keep four effects on the ramp without a
//! tint, which is unavailable — `ModulateColor` multiplies, so a blue `impact`
//! would land off §5 the same way a rarity-tinted 9-slice would.

use super::shapes::{finish, new_icon, GRID};
use super::*;
use crate::canvas::Canvas;

pub fn icons() -> Vec<Icon> {
    vec![
        // A landed hit. Ember rather than oxblood: R5 is the damage *number*,
        // and sparks off a struck body read warm - which is also the hue the
        // CpuParticles2D burst this replaces was tinted, so the beat is
        // recognisably the same one.
        Icon { category: "fx", name: "impact_0", draw: impact_0 },
        Icon { category: "fx", name: "impact_1", draw: impact_1 },
        Icon { category: "fx", name: "impact_2", draw: impact_2 },
        Icon { category: "fx", name: "impact_3", draw: impact_3 },
        // A hit Block absorbed whole. Steel, the §5 Block semantic, and the
        // hue the blue spark already used.
        Icon { category: "fx", name: "ward_0", draw: ward_0 },
        Icon { category: "fx", name: "ward_1", draw: ward_1 },
        Icon { category: "fx", name: "ward_2", draw: ward_2 },
        Icon { category: "fx", name: "ward_3", draw: ward_3 },
        // A heal. Verdigris, matching the +N text that was previously the
        // whole of this beat.
        Icon { category: "fx", name: "bloom_0", draw: bloom_0 },
        Icon { category: "fx", name: "bloom_1", draw: bloom_1 },
        Icon { category: "fx", name: "bloom_2", draw: bloom_2 },
        Icon { category: "fx", name: "bloom_3", draw: bloom_3 },
        // Poison landing. Violet rather than the green venom convention
        // suggests, because `bloom` has the green: two effects that differ
        // only in what they mean have to differ in what they look like, and
        // these are the two most likely to fire in the same fight.
        Icon { category: "fx", name: "venom_0", draw: venom_0 },
        Icon { category: "fx", name: "venom_1", draw: venom_1 },
        Icon { category: "fx", name: "venom_2", draw: venom_2 },
        Icon { category: "fx", name: "venom_3", draw: venom_3 },
    ]
}

/// The centre every burst is symmetric about. `GRID / 2` rather than the true
/// centre of 15.5: a burst has to be symmetric about a *pixel* or its two
/// halves come out different widths, and 15 keeps the widest frame plus its
/// outline inside the grid where 16 would clip it.
const CX: i32 = GRID / 2 - 1;

/// Frames per effect. Restated in `PixelSpecSmokeTest` and asserted against
/// what is actually on disk, the way `anim.rs`'s clip table is.
pub const FRAMES: usize = 4;

/// The four beats, in order:
///
/// 0. **Flash** — a solid disc with an `N8` core. The brightest frame by a
///    wide margin, which is what makes it the one `SpriteAnimator` declines
///    under Reduce Motion; `anim.rs`'s `hit` clip opens on an `N8` flash for
///    the same reason and gets skipped by the same mechanism.
/// 1. **Ring** — the flash has thrown its material outward and left a core.
/// 2. **Arms** — the ring breaks into eight spokes.
/// 3. **Motes** — what is left, travelling.
///
/// Two properties hold across all four and both are asserted below, because
/// both are the kind of thing that is obvious in motion and invisible in a
/// still: the burst's **extent never shrinks** (it expands; written backwards
/// it implodes) and its **mass falls after the ring** (it dissipates; written
/// backwards it reassembles as it dies, which is the exact bug `anim.rs`
/// records against `dissolve`'s step direction).
fn burst(frame: usize, hot: Rgb, mid: Rgb, cool: Rgb) -> Canvas {
    let mut canvas = new_icon();
    match frame {
        0 => {
            canvas.disc(CX, CX, 6, hot);
            canvas.disc(CX, CX, 3, N8);
        }
        1 => {
            canvas.ring(CX, CX, 9, 3, mid);
            canvas.disc(CX, CX, 3, hot);
        }
        2 => {
            for spoke in 0..8 {
                let (dx, dy) = direction(spoke);
                let from = (CX + (dx * 6.0).round() as i32, CX + (dy * 6.0).round() as i32);
                let to = (CX + (dx * 11.0).round() as i32, CX + (dy * 11.0).round() as i32);
                canvas.line(from.0, from.1, to.0, to.1, mid);
                canvas.set(to.0, to.1, hot);
            }
        }
        3 => {
            // 2x2 rather than single pixels: a mote is drawn at 1x here and
            // shown at 5x, but it is also the *last* thing the player sees of
            // the beat, and eight lone pixels under an N0 outline read at
            // speed as dirt on the screen rather than as the effect ending.
            for mote in 0..8 {
                let (dx, dy) = direction(mote);
                let x = CX + (dx * 12.0).round() as i32;
                let y = CX + (dy * 12.0).round() as i32;
                canvas.rect(x, y, 2, 2, cool);
            }
        }
        // Exhaustive rather than `_`, so a fifth frame registered without a
        // beat written for it fails the generation run. Under a catch-all it
        // would silently emit a second copy of the motes, which every check
        // downstream would accept: the file count would be right, the pixels
        // would be on the ramp, and only `consecutive_frames_differ` would
        // have anything to say - about a stall, not about the missing beat.
        other => unreachable!("a burst has {FRAMES} frames; asked for frame {other}"),
    }
    finish(&mut canvas);
    canvas
}

/// Eight directions at 45°, so every burst is symmetric about both axes and
/// both diagonals. Symmetry is what keeps the centroid on the centre pixel,
/// which is what lets the effect be positioned by its centre at the call site
/// without the art secretly aiming it somewhere.
fn direction(index: i32) -> (f32, f32) {
    const D: f32 = std::f32::consts::FRAC_1_SQRT_2;
    match index {
        0 => (1.0, 0.0),
        1 => (D, D),
        2 => (0.0, 1.0),
        3 => (-D, D),
        4 => (-1.0, 0.0),
        5 => (-D, -D),
        6 => (0.0, -1.0),
        _ => (D, -D),
    }
}

fn impact_0() -> Canvas { burst(0, E4, E3, E2) }
fn impact_1() -> Canvas { burst(1, E4, E3, E2) }
fn impact_2() -> Canvas { burst(2, E4, E3, E2) }
fn impact_3() -> Canvas { burst(3, E4, E3, E2) }

fn ward_0() -> Canvas { burst(0, B5, B4, B3) }
fn ward_1() -> Canvas { burst(1, B5, B4, B3) }
fn ward_2() -> Canvas { burst(2, B5, B4, B3) }
fn ward_3() -> Canvas { burst(3, B5, B4, B3) }

fn bloom_0() -> Canvas { burst(0, V5, V4, V3) }
fn bloom_1() -> Canvas { burst(1, V5, V4, V3) }
fn bloom_2() -> Canvas { burst(2, V5, V4, V3) }
fn bloom_3() -> Canvas { burst(3, V5, V4, V3) }

fn venom_0() -> Canvas { burst(0, P4, P3, P2) }
fn venom_1() -> Canvas { burst(1, P4, P3, P2) }
fn venom_2() -> Canvas { burst(2, P4, P3, P2) }
fn venom_3() -> Canvas { burst(3, P4, P3, P2) }

#[cfg(test)]
mod tests {
    use super::*;
    use crate::palette::N0;

    /// Every frame of every effect, so a rule holds for the set rather than
    /// for the one effect somebody remembered to list.
    fn every_run() -> Vec<(&'static str, Vec<Canvas>)> {
        vec![
            ("impact", vec![impact_0(), impact_1(), impact_2(), impact_3()]),
            ("ward", vec![ward_0(), ward_1(), ward_2(), ward_3()]),
            ("bloom", vec![bloom_0(), bloom_1(), bloom_2(), bloom_3()]),
            ("venom", vec![venom_0(), venom_1(), venom_2(), venom_3()]),
        ]
    }

    /// The burst itself, excluding `finish`'s outline. Measuring with the
    /// outline in would make every rule below partly a rule about a perimeter,
    /// which is not what any of them is trying to say.
    fn body(canvas: &Canvas) -> Vec<(i32, i32)> {
        let mut out = Vec::new();
        for y in 0..canvas.height as i32 {
            for x in 0..canvas.width as i32 {
                let pixel = canvas.get(x, y);
                if pixel.is_opaque() && (pixel.0, pixel.1, pixel.2) != (N0.0, N0.1, N0.2) {
                    out.push((x, y));
                }
            }
        }
        out
    }

    /// Chebyshev radius from the centre pixel — how far the burst has thrown
    /// its material, in the metric a square grid actually has.
    fn extent(canvas: &Canvas) -> i32 {
        body(canvas)
            .iter()
            .map(|(x, y)| (x - CX).abs().max((y - CX).abs()))
            .max()
            .unwrap_or(0)
    }

    #[test]
    fn every_effect_has_the_declared_frame_count() {
        for (name, frames) in every_run() {
            assert_eq!(frames.len(), FRAMES, "{name} does not have FRAMES frames");
        }
        // Every entry in the registry is one of those frames and vice versa -
        // an effect drawn and never registered generates no file at all, and
        // nothing else in the toolchain would say so.
        let registered = icons().len();
        assert_eq!(
            registered,
            every_run().len() * FRAMES,
            "the registry has {registered} entries for {} effects x {FRAMES} frames",
            every_run().len()
        );
    }

    /// A burst that repeats a frame stalls mid-flight. Frames on disk and a
    /// driver that never advances them look identical to every static check,
    /// and this is the half of that pair the C# side cannot see: it can count
    /// four files without any of them differing.
    #[test]
    fn consecutive_frames_differ() {
        for (name, frames) in every_run() {
            for i in 1..frames.len() {
                assert_ne!(
                    frames[i - 1].pixels(),
                    frames[i].pixels(),
                    "{name} frames {} and {i} are identical",
                    i - 1
                );
            }
        }
    }

    /// A burst expands. The reverse reads as an implosion, and at 0.06s a
    /// frame nobody would be able to say why it looked wrong.
    #[test]
    fn a_burst_never_contracts() {
        for (name, frames) in every_run() {
            for i in 1..frames.len() {
                let (previous, current) = (extent(&frames[i - 1]), extent(&frames[i]));
                assert!(
                    current >= previous,
                    "{name} frame {i} reaches {current}, in from frame {}'s {previous}",
                    i - 1
                );
            }
        }
    }

    /// And it thins as it goes. Frame 0 is exempt because it is the flash — a
    /// compact solid disc that the ring frame legitimately has more material
    /// than; the rule is about what happens *after* the throw.
    ///
    /// This is `anim.rs`'s `dissolve` lesson in another shape: written the
    /// other way round, the creature visibly reassembled as it died.
    #[test]
    fn a_burst_dissipates_after_the_ring() {
        for (name, frames) in every_run() {
            for i in 2..frames.len() {
                let (previous, current) = (body(&frames[i - 1]).len(), body(&frames[i]).len());
                assert!(
                    current < previous,
                    "{name} frame {i} carries {current} pixels, up from frame {}'s {previous}",
                    i - 1
                );
            }
        }
    }

    /// The call site positions an effect by its centre and the art must not
    /// quietly aim it somewhere else — an off-centre burst over a 160px sprite
    /// reads as landing beside the creature rather than on it.
    ///
    /// Tolerance is one pixel because the grid has no centre: 32 is even, so a
    /// shape symmetric about pixel `CX` sits half a pixel off the true middle
    /// by construction. Anything looser than 1 would admit a real miss.
    #[test]
    fn every_burst_is_centred() {
        let middle = (GRID - 1) as f32 / 2.0;
        for (name, frames) in every_run() {
            for (i, frame) in frames.iter().enumerate() {
                let pixels = body(frame);
                assert!(!pixels.is_empty(), "{name} frame {i} drew nothing");
                let count = pixels.len() as f32;
                let cx = pixels.iter().map(|p| p.0 as f32).sum::<f32>() / count;
                let cy = pixels.iter().map(|p| p.1 as f32).sum::<f32>() / count;
                assert!(
                    (cx - middle).abs() <= 1.0 && (cy - middle).abs() <= 1.0,
                    "{name} frame {i} centres on ({cx:.2}, {cy:.2}), not ({middle}, {middle})"
                );
            }
        }
    }
}
