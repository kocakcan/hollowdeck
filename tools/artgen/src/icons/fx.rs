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
//! ## Two shapes, six pigments
//!
//! Four of the six are the same four-beat `burst` — flash, ring, arms, motes —
//! separated only by which ramp family they are drawn in. That is not laziness:
//! a burst reads as *this landed here* and the colour is what says which kind
//! of thing landed, exactly as `EnemyView`'s intent icons share one silhouette
//! budget. It is also the only way to keep four effects on the ramp without a
//! tint, which is unavailable — `ModulateColor` multiplies, so a blue `impact`
//! would land off §5 the same way a rarity-tinted 9-slice would.
//!
//! The other two are the same `swipe`, and they are the sharper instance of the
//! same argument, because there the shared half is *forced* rather than chosen.
//! A slash is drawn along an axis, and an axis is **undirected** — the player's
//! attack vector spans about −13° to −49° and the enemy's spans 131° to 167°,
//! which is the same line read from the other end. So `gash` cannot be a second
//! *orientation* of `swipe`; there is nothing to reorient. What separates them
//! is pigment alone, and `the_two_blades_share_a_shape_and_differ_in_pigment`
//! pins both halves of that so the second run cannot quietly become a place to
//! redraw the blade.

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
        // The player's blade reaching an enemy. Bone rather than any of the
        // six chromatic families: the other four say *what kind of thing
        // landed*, and this one says a weapon arrived - it is the only effect
        // in the set whose subject is the attack rather than its result. It is
        // also the only run that travels, which is what lets one authored
        // orientation cover every angle the fight can produce.
        Icon { category: "fx", name: "swipe_0", draw: swipe_0 },
        Icon { category: "fx", name: "swipe_1", draw: swipe_1 },
        Icon { category: "fx", name: "swipe_2", draw: swipe_2 },
        Icon { category: "fx", name: "swipe_3", draw: swipe_3 },
        // The same blade coming the other way. Oxblood rather than bone,
        // and the pigment is the whole of the difference: the geometry is
        // `swipe`'s, called with other colours, because the axis a slash is
        // drawn along is *undirected* - the outbound band and the inbound one
        // are the same line, so one silhouette already serves both.
        //
        // R is the family `impact` declined ("Ember rather than oxblood: R5 is
        // the damage *number*"), which is exactly what recommends it here: the
        // blade coming at the player is the colour of the number about to land
        // on their own HP bar.
        //
        // Named `gash` because `rend` — the first choice — is already a move id
        // on `rot_hound` in `enemies.json`, which is the rule at the top of this
        // file catching something for the second time. An asset name that reads
        // as a definition id makes a grep ambiguous about which is meant, and
        // that is worth more here than the better verb.
        Icon { category: "fx", name: "gash_0", draw: gash_0 },
        Icon { category: "fx", name: "gash_1", draw: gash_1 },
        Icon { category: "fx", name: "gash_2", draw: gash_2 },
        Icon { category: "fx", name: "gash_3", draw: gash_3 },
    ]
}

/// The centre every burst is symmetric about. `GRID / 2` rather than the true
/// centre of 15.5: a burst has to be symmetric about a *pixel* or its two
/// halves come out different widths, and 15 keeps the widest frame plus its
/// outline inside the grid where 16 would clip it.
const CX: i32 = GRID / 2 - 1;

/// The radius of the `N8` core every run's opening frame carries — the flash
/// `SpriteAnimator.FlashOpeningClips` declines under Reduce Motion.
///
/// Named because it is the one part of the set that is deliberately *not*
/// per-effect: all four bursts and both blades open on it, so it is the colour
/// two runs are allowed to share. `the_two_blades_share_a_shape_and_differ_in_pigment`
/// reads this rather than exempting `N8` by name — `swipe` is drawn in bone
/// throughout, so a colour-keyed exemption would quietly excuse a `gash`
/// redrawn in the same family, which is the "tolerance that can contain a real
/// answer" trap `light.rs`'s `GRAZING` already paid for once.
const FLASH_CORE_RADIUS: i32 = 3;

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
            canvas.disc(CX, CX, FLASH_CORE_RADIUS, N8);
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

/// The axis every blade frame is drawn along: up and to the right, 45°.
///
/// One orientation rather than the eight-direction set
/// `docs/PIXEL_ART_ROADMAP.md` §5 forecast, and the reason is measured rather
/// than assumed. The attacker is not arbitrary — `CombatScreen.tscn` pins
/// `PlayerSprite` at canvas centre (120, 350) and every target is an
/// `EnemyView` centre inside `EnemyRow` (176..976 x 20..330) — so across one
/// to four enemies the attack vector only ever spans about -13° to -49°.
/// That is one and a half octants, never down and never left: an eight-way
/// set would author eight images and ever show two.
///
/// What carries the remaining spread is **motion**, not silhouette.
/// `CombatFx.PlayTravelling` tweens the frame run from the attacker to the
/// target, so the direction a player reads is the direction the sprite
/// actually goes, and the art only has to agree with the *mean* of the band
/// (-31°, which is this diagonal). Same argument `anim::Facing` makes one
/// asset class over: one axis rather than four per-clip direction arguments,
/// because everything that moves at all moves along it.
///
/// It is one constant for *both* blades, and that is the half `PIXEL_ART_ROADMAP`
/// §5's deferral got wrong. It forecast that an enemy hitting the player "would
/// be an authored second orientation rather than a reuse". An axis has no
/// direction: 149° is the same line as −31°, and `bar` draws through the centre
/// pixel in both directions at once, so every frame here is invariant under a
/// half turn (`an_axial_run_is_unchanged_by_a_half_turn`). The incoming blade
/// needed a colour, not a silhouette.
const SWIPE: (f32, f32) = (std::f32::consts::FRAC_1_SQRT_2, -std::f32::consts::FRAC_1_SQRT_2);

/// A bar of `weight` through the centre pixel, `half_len` either way along
/// `SWIPE`.
///
/// `Canvas::thick_line` stamps its weight block down and to the right of each
/// step rather than around it, so a bar drawn straight through `CX` lands
/// displaced by half its own weight on both axes. Every rule at the bottom of
/// this file measures a centroid, and `every_effect_frame_is_centred` fails on
/// a shape that is otherwise exactly right. The correction is applied here
/// rather than in `Canvas`, because the other callers of `thick_line` draw
/// shapes whose centroid nothing measures and moving it would move all of them.
fn bar(canvas: &mut Canvas, half_len: f32, weight: i32, colour: Rgb) {
    let (dx, dy) = SWIPE;
    let (reach_x, reach_y) = ((dx * half_len).round() as i32, (dy * half_len).round() as i32);
    let nudge = (weight - 1) / 2;
    canvas.thick_line(
        CX - reach_x - nudge,
        CX - reach_y - nudge,
        CX + reach_x - nudge,
        CX + reach_y - nudge,
        weight,
        colour,
    );
}

/// The four beats of a blade arriving, in order:
///
/// 0. **Contact** — a short heavy bar with an `N8` core. The brightest frame,
///    which is what makes it the one `SpriteAnimator` declines under Reduce
///    Motion, exactly as `burst`'s flash is.
/// 1. **Draw** — the edge has swept through: longer, thinner, still hot at
///    the middle where the blade met the body.
/// 2. **Tail** — longer and thinner again, down to the trailing edge.
/// 3. **Motes** — what is left, thrown along the axis.
///
/// It satisfies the same four rules `burst` does — the extent grows across all
/// four frames and the mass falls after frame 1 — which is not a coincidence
/// forced on it: a slash that shortened as it faded would read as the blade
/// being pulled back rather than followed through.
fn swipe(frame: usize, hot: Rgb, mid: Rgb, cool: Rgb) -> Canvas {
    let mut canvas = new_icon();
    match frame {
        0 => {
            bar(&mut canvas, 6.0, 5, hot);
            canvas.disc(CX, CX, FLASH_CORE_RADIUS, N8);
        }
        1 => {
            bar(&mut canvas, 10.0, 3, mid);
            bar(&mut canvas, 5.0, 3, hot);
        }
        2 => {
            bar(&mut canvas, 13.0, 2, mid);
            bar(&mut canvas, 4.0, 2, hot);
        }
        3 => {
            // Mirrored pairs, so what is left of the blade is still centred on
            // the axis it travelled: an odd mote out would drag the centroid
            // off and aim the whole effect somewhere the call site did not.
            let (dx, dy) = SWIPE;
            for reach in [10.0f32, 14.0] {
                for sign in [-1.0f32, 1.0] {
                    let x = CX + (dx * reach * sign).round() as i32;
                    let y = CX + (dy * reach * sign).round() as i32;
                    canvas.rect(x, y, 2, 2, cool);
                }
            }
        }
        other => unreachable!("a swipe has {FRAMES} frames; asked for frame {other}"),
    }
    finish(&mut canvas);
    canvas
}

fn swipe_0() -> Canvas { swipe(0, N8, N7, N6) }
fn swipe_1() -> Canvas { swipe(1, N8, N7, N6) }
fn swipe_2() -> Canvas { swipe(2, N8, N7, N6) }
fn swipe_3() -> Canvas { swipe(3, N8, N7, N6) }

fn gash_0() -> Canvas { swipe(0, R5, R4, R3) }
fn gash_1() -> Canvas { swipe(1, R5, R4, R3) }
fn gash_2() -> Canvas { swipe(2, R5, R4, R3) }
fn gash_3() -> Canvas { swipe(3, R5, R4, R3) }

#[cfg(test)]
mod tests {
    use super::*;
    use crate::palette::N0;

    /// What an effect is drawn *about*: a point, or an axis.
    ///
    /// Declared per run rather than inferred, which is `backgrounds.rs`'s
    /// per-band tiling rule in another shape — a floor closes both axes, a
    /// plinth only x, and asking a piece for a property it never had is a rule
    /// with no failure behind it. Here the two classes want opposite
    /// assertions: a burst that came out long would be aiming itself
    /// somewhere, and a swipe that came out round would be leaving the whole
    /// "direction comes from motion" claim resting on the tween alone.
    enum Shape {
        /// Symmetric about the centre pixel. It landed *here*.
        Radial,
        /// Drawn along `SWIPE`. It came *from* somewhere.
        Axial,
    }

    /// Every frame of every effect, so a rule holds for the set rather than
    /// for the one effect somebody remembered to list.
    fn every_run() -> Vec<(&'static str, Shape, Vec<Canvas>)> {
        vec![
            ("impact", Shape::Radial, vec![impact_0(), impact_1(), impact_2(), impact_3()]),
            ("ward", Shape::Radial, vec![ward_0(), ward_1(), ward_2(), ward_3()]),
            ("bloom", Shape::Radial, vec![bloom_0(), bloom_1(), bloom_2(), bloom_3()]),
            ("venom", Shape::Radial, vec![venom_0(), venom_1(), venom_2(), venom_3()]),
            ("swipe", Shape::Axial, vec![swipe_0(), swipe_1(), swipe_2(), swipe_3()]),
            ("gash", Shape::Axial, vec![gash_0(), gash_1(), gash_2(), gash_3()]),
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
        for (name, _, frames) in every_run() {
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
        for (name, _, frames) in every_run() {
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
    fn an_effect_never_contracts() {
        for (name, _, frames) in every_run() {
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

    /// And it thins as it goes. Frame 0 is exempt because it is the opening
    /// flash — a compact solid the second frame legitimately has more material
    /// than, whether that second frame is a burst's ring or a swipe's drawn
    /// edge; the rule is about what happens *after* the throw.
    ///
    /// This is `anim.rs`'s `dissolve` lesson in another shape: written the
    /// other way round, the creature visibly reassembled as it died.
    #[test]
    fn an_effect_dissipates_after_its_second_frame() {
        for (name, _, frames) in every_run() {
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
    fn every_effect_frame_is_centred() {
        let middle = (GRID - 1) as f32 / 2.0;
        for (name, _, frames) in every_run() {
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

    /// The spread of a run's material along `SWIPE` against its spread across
    /// `SWIPE`, at its widest. 1.0 is a shape with no axis; a bar is its
    /// length over its weight.
    fn elongation(canvas: &Canvas) -> f32 {
        let (ax, ay) = SWIPE;
        let (mut along, mut across) = (0.0f32, 0.0f32);
        for (x, y) in body(canvas) {
            let (dx, dy) = ((x - CX) as f32, (y - CX) as f32);
            along = along.max((dx * ax + dy * ay).abs());
            across = across.max((dx * -ay + dy * ax).abs());
        }
        if across == 0.0 {
            return f32::INFINITY;
        }
        along / across
    }

    /// One drawn axis serves travel in both directions, and this is the
    /// property that makes that true rather than lucky.
    ///
    /// `CombatFx.PlayTravelling` carries a blade from the attacker to the
    /// target, and it is spawned for both directions off one art set — the
    /// player's vector spans about −13° to −49°, the enemy's 131° to 167°, and
    /// those are the same undirected line. A frame that is symmetric under a
    /// half turn therefore looks right coming and going. A frame that is *not*
    /// — a hooked tip, a tail thicker at one end, a barb — points backwards
    /// along exactly one of the two paths, and nothing else in this project
    /// could say so: `validate` reads finished pixels, the C# side counts
    /// files, and `a_swipe_is_drawn_along_its_axis` below is satisfied by any
    /// long shape whatever.
    ///
    /// Measured about the bounding box rather than about `CX`, because the
    /// bars are stamped weight-blocks and an even weight puts a run's true
    /// centre on a half pixel (see `bar`'s `nudge`). What is being asserted is
    /// that the shape has a centre of symmetry, not where it is.
    #[test]
    fn an_axial_run_is_unchanged_by_a_half_turn() {
        for (name, shape, frames) in every_run() {
            if !matches!(shape, Shape::Axial) {
                continue;
            }
            for (i, frame) in frames.iter().enumerate() {
                let pixels = body(frame);
                let (min_x, max_x) = (
                    pixels.iter().map(|p| p.0).min().unwrap(),
                    pixels.iter().map(|p| p.0).max().unwrap(),
                );
                let (min_y, max_y) = (
                    pixels.iter().map(|p| p.1).min().unwrap(),
                    pixels.iter().map(|p| p.1).max().unwrap(),
                );
                let set: std::collections::HashSet<(i32, i32)> = pixels.iter().copied().collect();
                let turned: std::collections::HashSet<(i32, i32)> = pixels
                    .iter()
                    .map(|(x, y)| (min_x + max_x - x, min_y + max_y - y))
                    .collect();
                assert_eq!(
                    set, turned,
                    "{name} frame {i} is not symmetric under a half turn - it points backwards \
                     along one of the two directions CombatFx.PlayTravelling carries it"
                );
            }
        }
    }

    /// `swipe` and `gash` are one blade in two pigments, and both halves of
    /// that have to hold.
    ///
    /// Shared geometry is the point: the axis is undirected, so a second
    /// *shape* would be a second answer to a question that has one. Different
    /// pigment is the point too, and it is the only thing separating "the
    /// player swung" from "something swung at the player" once the travel is
    /// over. Drawn as two assertions rather than one because they fail
    /// independently — a redrawn `gash` still differs in colour, and a `gash`
    /// recoloured to bone still matches in shape.
    ///
    /// The flash core is excluded, and finding out why is what this assertion
    /// was worth. Frame 0 of every run in this file — all four bursts and both
    /// blades — opens on an `N8` disc, because that frame is the flash
    /// `SpriteAnimator.FlashOpeningClips` declines under Reduce Motion. It is
    /// the one thing the whole set shares by design.
    ///
    /// It is excluded by **position** rather than by colour, and the first
    /// draft had that backwards. `swipe` is drawn in bone from end to end, so
    /// its `hot` *is* `N8`: exempting the colour exempts most of the blade, and
    /// a `gash` recoloured into the same family would have passed. Excluding
    /// the `FLASH_CORE_RADIUS` disc excludes exactly the shared core and
    /// nothing else.
    #[test]
    fn the_two_blades_share_a_shape_and_differ_in_pigment() {
        let swipe = [swipe_0(), swipe_1(), swipe_2(), swipe_3()];
        let gash = [gash_0(), gash_1(), gash_2(), gash_3()];
        for i in 0..FRAMES {
            assert_eq!(
                body(&swipe[i]),
                body(&gash[i]),
                "swipe and gash differ in shape at frame {i} - the axis is undirected, so the \
                 incoming blade is a pigment rather than a second silhouette"
            );

            let colours = |canvas: &Canvas| -> std::collections::HashSet<(u8, u8, u8)> {
                body(canvas)
                    .iter()
                    .filter(|(x, y)| {
                        // Canvas::disc's own test, `+ 0.25` included: measuring
                        // the core with a tighter radius than the one that drew
                        // it leaves its outermost pixels in the comparison, and
                        // they are N8 in both runs.
                        let limit = (FLASH_CORE_RADIUS as f32 + 0.25).powi(2);
                        let (dx, dy) = ((x - CX) as f32, (y - CX) as f32);
                        dx * dx + dy * dy > limit
                    })
                    .map(|(x, y)| {
                        let pixel = canvas.get(*x, *y);
                        (pixel.0, pixel.1, pixel.2)
                    })
                    .collect()
            };
            let shared: Vec<_> = colours(&swipe[i])
                .intersection(&colours(&gash[i]))
                .copied()
                .collect();
            assert!(
                shared.is_empty(),
                "swipe and gash share {shared:?} at frame {i} - with the shape shared, the \
                 pigment is the whole of how the two blades read apart (the flash core every \
                 run opens on is excluded by position, not by colour)"
            );
        }
    }

    /// A swipe is a line and a burst is not, and neither half of that is
    /// checkable anywhere else.
    ///
    /// `validate` reads finished pixels and cannot tell a bar from a blob; the
    /// C# side can only count files and measure where a rect was placed. So
    /// the whole of "the art agrees with what the call site does with it"
    /// lives here — and it has to run in both directions. A swipe redrawn as a
    /// disc passes every other rule in this file while leaving
    /// `CombatFx.PlayTravelling`'s tween as the only thing saying the attack
    /// had a direction, and a burst that drifted into a bar would be quietly
    /// aiming a beat the call site positions by its centre.
    ///
    /// The bands are wide because the measured gap is: the four bursts sit at
    /// 0.94–1.00 and the swipe's frames at 3.00–10.50, so nothing here is a
    /// constant fitted to the best case.
    #[test]
    fn a_swipe_is_drawn_along_its_axis_and_a_burst_is_not() {
        for (name, shape, frames) in every_run() {
            for (i, frame) in frames.iter().enumerate() {
                let ratio = elongation(frame);
                match shape {
                    Shape::Axial => assert!(
                        ratio >= 2.0,
                        "{name} frame {i} is {ratio:.2} long against its width - \
                         an axial effect that round leaves the tween carrying the direction alone"
                    ),
                    Shape::Radial => assert!(
                        ratio <= 1.5,
                        "{name} frame {i} is {ratio:.2} long against its width - \
                         a radial effect is positioned by its centre and must not aim itself"
                    ),
                }
            }
        }
    }
}
