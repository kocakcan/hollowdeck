//! Derived animation frames for the sourced creature sprites.
//!
//! The roster is 36 CC0 Dungeon Crawl tiles plus the player — static, one
//! 32x32 PNG each, and nothing in `assets/` is drawn by hand. So the frames
//! are *derived* from the source art rather than authored, which is the only
//! route to animation that keeps both of those facts true.
//!
//! **Every operation here is an integer pixel move or a palette substitution.**
//! Nothing scales, rotates or blends. That is the entire point: the animation
//! this replaces was `scale → 1.04` and `rotation_degrees → 6` tweens on a
//! nearest-filtered sprite, i.e. 5.2 device pixels per source pixel and a
//! staircased silhouette — exactly what ART_SPEC §2 calls a bug rather than a
//! judgement call. Motion that lives *inside* the 32x32 frame cannot violate
//! §2, because the node transform is never touched.
//!
//! It also dissolves a layout problem `EnemyView.cs` documents and works
//! around: its sprite sits in a `VBoxContainer`, which owns position and size,
//! so a position tween there gets overridden every layout pass. A frame swap
//! is not a transform, so the container has no opinion about it.
//!
//! Because the sources are already clamped and nothing here invents a colour
//! except the one ramp entry the flash frame paints, output is on-ramp by
//! construction and `artgen validate` covers it with no new rule.

use std::path::{Path, PathBuf};

use crate::canvas::Canvas;
use crate::palette::N8;
use crate::validate;

/// Which way is *away from the opponent*. Enemies stand to the right of the
/// player, so their retreat, their wind-up lean and their knock-back all run
/// in +x; the player's run the other way. One axis rather than four per-clip
/// direction arguments, because every clip that moves at all moves along it.
#[derive(Clone, Copy)]
pub struct Facing {
    away: i32,
}

impl Facing {
    pub const ENEMY: Facing = Facing { away: 1 };
    pub const PLAYER: Facing = Facing { away: -1 };
}

/// A named run of frames. `frames` is what the driver in `SpriteAnimator.cs`
/// expects to find on disk, so the two have to agree — `PixelSpecSmokeTest`
/// asserts that they do rather than leaving it to a comment.
pub struct Clip {
    pub name: &'static str,
    pub frames: usize,
}

/// The clip set every creature gets.
///
/// `idle` is two frames on purpose. A 32x32 breathe is a 1px squash held long,
/// not a smooth interpolation — an interpolated one is what the tween version
/// was, and it needed a fractional scale to exist. Two frames at ~0.5s each is
/// the idiom and halves the asset count.
pub const ENEMY_CLIPS: &[Clip] = &[
    Clip { name: "idle", frames: 2 },
    Clip { name: "windup", frames: 2 },
    Clip { name: "hit", frames: 2 },
    Clip { name: "death", frames: 3 },
    Clip { name: "escape", frames: 3 },
];

/// The player has no `death` or `escape`: a player death is `RunEndScreen`,
/// and the player never flees a fight. Authoring them anyway would put two
/// clips on disk that nothing can ever play.
pub const PLAYER_CLIPS: &[Clip] = &[
    Clip { name: "idle", frames: 2 },
    Clip { name: "windup", frames: 2 },
    Clip { name: "hit", frames: 2 },
];

/// One frame of one clip, derived from `source`.
///
/// Split out from the writing loop so the smoke tests are not the only thing
/// that can exercise a derivation.
pub fn frame(source: &Canvas, clip: &str, index: usize, facing: Facing) -> Canvas {
    match (clip, index) {
        // Breathing: the body compresses by 1px while the feet stay planted.
        ("idle", 0) => copy_of(source),
        ("idle", _) => squash(source, 1),

        // Wind-up: lean away from the target before the strike lands, so an
        // attack reads as building rather than arriving. The lower band holds
        // — a creature that slides bodily backwards reads as being pushed.
        ("windup", 0) => lean(source, facing, 1),
        ("windup", _) => lean(source, facing, 2),

        // Impact: the flash frame is the whole silhouette in one bright ramp
        // entry. It carries the hit almost by itself at this size, which is
        // why it is frame 0 and the displacement is only frame 1.
        ("hit", 0) => flash(source),
        ("hit", _) => shift(source, 2 * facing.away, 0),

        // Death: buckle, then come apart. The dissolve is a fixed checker
        // rather than a random scatter so a regenerated frame is byte-identical
        // and CI's drift diff stays meaningful.
        ("death", 0) => squash(source, 2),
        ("death", 1) => dissolve(&squash(source, 4), 2),
        ("death", _) => dissolve(&squash(source, 6), 4),

        // Escape: the content walks out of its own frame. No node transform,
        // which is what makes this expressible at all — the view is inside an
        // HBoxContainer that would overwrite a position tween on its next sort.
        ("escape", n) => shift(source, (n as i32 + 1) * 8 * facing.away, 0),

        _ => copy_of(source),
    }
}

/// Whole-content integer translation. Anything leaving the grid is dropped,
/// which is what lets `escape` simply run off the edge.
fn shift(source: &Canvas, dx: i32, dy: i32) -> Canvas {
    let mut out = Canvas::new(source.width, source.height);
    for y in 0..source.height as i32 {
        for x in 0..source.width as i32 {
            let pixel = source.get(x, y);
            if pixel.is_opaque() {
                out.set_raw(x + dx, y + dy, pixel);
            }
        }
    }
    out
}

/// The topmost row of the sprite's own content, and the bottom row. Derived
/// per sprite rather than assumed, because the sourced tiles are not
/// consistently inset — a squash measured from the grid edge instead of from
/// the art would do nothing at all to a sprite that sits low in its cell.
fn content_bounds(source: &Canvas) -> Option<(i32, i32)> {
    let mut top = None;
    let mut bottom = 0;
    for y in 0..source.height as i32 {
        let occupied = (0..source.width as i32).any(|x| source.get(x, y).is_opaque());
        if occupied {
            top.get_or_insert(y);
            bottom = y;
        }
    }
    top.map(|t| (t, bottom))
}

/// Compress by `amount` rows against a planted base.
///
/// Everything from the top of the content down to its vertical middle moves
/// down; everything below holds. The rows the two halves collide on are
/// consumed, so the silhouette genuinely loses height rather than duplicating
/// a row — a duplicate would read as a smear.
fn squash(source: &Canvas, amount: i32) -> Canvas {
    let Some((top, bottom)) = content_bounds(source) else {
        return copy_of(source);
    };
    let waist = top + (bottom - top) / 2;

    let mut out = Canvas::new(source.width, source.height);
    for y in 0..source.height as i32 {
        for x in 0..source.width as i32 {
            let pixel = source.get(x, y);
            if !pixel.is_opaque() {
                continue;
            }
            let dy = if y <= waist { amount } else { 0 };
            out.set_raw(x, y + dy, pixel);
        }
    }
    out
}

/// Lean `amount` px away from the opponent, hinged at the waist: the upper
/// body swings, the lower body holds. A whole-body shift would read as the
/// creature being shoved rather than winding up.
fn lean(source: &Canvas, facing: Facing, amount: i32) -> Canvas {
    let Some((top, bottom)) = content_bounds(source) else {
        return copy_of(source);
    };
    let waist = top + (bottom - top) / 2;

    let mut out = Canvas::new(source.width, source.height);
    for y in 0..source.height as i32 {
        for x in 0..source.width as i32 {
            let pixel = source.get(x, y);
            if !pixel.is_opaque() {
                continue;
            }
            let dx = if y <= waist { amount * facing.away } else { 0 };
            out.set_raw(x + dx, y, pixel);
        }
    }
    out
}

/// Every opaque pixel in one bright ramp entry. The classic impact frame, and
/// the pixel-native version of the `modulate` flash `CombatScreen` fades in and
/// out today: a modulate tween passes through every intermediate brightness,
/// which at one frame of visibility is invisible anyway.
fn flash(source: &Canvas) -> Canvas {
    let mut out = Canvas::new(source.width, source.height);
    for y in 0..source.height as i32 {
        for x in 0..source.width as i32 {
            if source.get(x, y).is_opaque() {
                out.set(x, y, N8);
            }
        }
    }
    out
}

/// Keep one pixel in `step` on a fixed lattice, dropping the rest. Coarse on
/// purpose: a fine dither reads as transparency rather than as disintegration
/// once it is scaled 5x.
///
/// `step` is what *survives*, not what is removed, and the direction matters —
/// written the other way (`% step != 0`) a larger step keeps *more*, so the
/// death clip's second dissolve came out less broken up than its first and the
/// creature visibly reassembled as it died. Caught by looking at a contact
/// sheet; nothing about the frames on disk would have said so.
fn dissolve(source: &Canvas, step: i32) -> Canvas {
    let mut out = Canvas::new(source.width, source.height);
    for y in 0..source.height as i32 {
        for x in 0..source.width as i32 {
            let pixel = source.get(x, y);
            if pixel.is_opaque() && (x + y * 3) % step == 0 {
                out.set_raw(x, y, pixel);
            }
        }
    }
    out
}

fn copy_of(source: &Canvas) -> Canvas {
    let mut out = Canvas::new(source.width, source.height);
    for y in 0..source.height as i32 {
        for x in 0..source.width as i32 {
            out.set_raw(x, y, source.get(x, y));
        }
    }
    out
}

/// Every sprite that gets frames, as (id, source path, clips, facing).
/// `files_under` already sorts, so the walk order is stable and CI's drift
/// diff is deterministic.
pub fn targets(assets: &Path) -> Vec<(String, PathBuf, &'static [Clip], Facing)> {
    let mut out: Vec<(String, PathBuf, &'static [Clip], Facing)> =
        validate::files_under(&assets.join("sprites").join("enemies"), "png")
            .into_iter()
            .filter_map(|path| {
                let id = path.file_stem()?.to_string_lossy().to_string();
                Some((id, path, ENEMY_CLIPS, Facing::ENEMY))
            })
            .collect();

    let player = assets.join("sprites").join("player.png");
    if player.is_file() {
        out.push(("player".to_string(), player, PLAYER_CLIPS, Facing::PLAYER));
    }
    out
}

/// Where a frame lives.
///
/// Deliberately `sprites/anim/` and not alongside `sprites/enemies/`: the
/// roster there is *sourced* art, credited in `CREDITS.md`, and this is
/// *generated* art. That split is the one CLAUDE.md insists on, and a
/// generated frame sitting in the sourced directory blurs it.
///
/// One PNG per frame rather than a strip, because `validate::expected_grid`
/// matches on `/sprites/` and holds everything under it to 32x32 — so frames
/// inherit the grid rule for free, and a sheet would (correctly) fail it.
pub fn frame_path(assets: &Path, id: &str, clip: &str, index: usize) -> PathBuf {
    assets
        .join("sprites")
        .join("anim")
        .join(id)
        .join(format!("{clip}_{index}.png"))
}

pub struct Report {
    pub written: usize,
    pub failures: Vec<String>,
}

pub fn run(assets: &Path, dry_run: bool) -> Report {
    let mut report = Report {
        written: 0,
        failures: Vec::new(),
    };

    for (id, path, clips, facing) in targets(assets) {
        let source = match Canvas::load(&path) {
            Ok(canvas) => canvas,
            Err(error) => {
                report.failures.push(error);
                continue;
            }
        };

        for clip in clips {
            for index in 0..clip.frames {
                let out = frame(&source, clip.name, index, facing);
                let target = frame_path(assets, &id, clip.name, index);
                if dry_run {
                    println!("would write {}", target.display());
                    report.written += 1;
                    continue;
                }
                match out.save(&target) {
                    Ok(()) => report.written += 1,
                    Err(error) => report.failures.push(error),
                }
            }
        }
    }

    report
}
