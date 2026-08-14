//! artgen — Hollowdeck's asset tool.
//!
//! Emits and checks the game's pixel art. The engine decision in `CLAUDE.md`
//! rejected Rust as the *engine* (weak `bevy_ui`, weak tweening, ECS on top of
//! engine-learning); none of that applies to a program that writes PNGs, and
//! the game never needs Rust at build time.
//!
//! The argument for generating icons rather than drawing them in an editor is
//! the same one behind `docs/ART_SPEC.md`: art as code means a palette change
//! is a one-line edit and a re-run, and a spec violation is a compile-or-
//! validate failure rather than something you have to notice.
//!
//!     cargo run --release -- generate     # write every generated asset
//!     cargo run --release -- clamp        # snap authored art onto the ramp
//!     cargo run --release -- validate     # enforce ART_SPEC §1/§3/§5/§8
//!
//! Paths are resolved relative to the repository root, found by walking up
//! from the working directory, so it runs the same from `tools/artgen/` and
//! from the repo root.

mod anim;
mod canvas;
mod clamp;
mod icons;
mod palette;
mod validate;

use std::path::{Path, PathBuf};
use std::process::ExitCode;

fn main() -> ExitCode {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let root = match repository_root() {
        Some(root) => root,
        None => {
            eprintln!("error: not inside the Hollowdeck repository (no project.godot above cwd)");
            return ExitCode::FAILURE;
        }
    };
    let assets = root.join("assets");

    let dry_run = arguments.iter().any(|a| a == "--dry-run");
    let positional: Vec<&str> = arguments
        .iter()
        .filter(|a| !a.starts_with("--"))
        .map(|s| s.as_str())
        .collect();

    match positional.first().copied() {
        Some("generate") => generate(&assets, positional.get(1).copied(), dry_run),
        Some("animate") => run_animate(&assets, dry_run),
        Some("clamp") => run_clamp(&root, &assets, &positional[1..], dry_run),
        Some("validate") => run_validate(&assets),
        Some(other) => {
            eprintln!("error: unknown command '{other}'");
            usage();
            ExitCode::FAILURE
        }
        None => {
            usage();
            ExitCode::FAILURE
        }
    }
}

fn usage() {
    eprintln!(
        "usage: artgen <command> [--dry-run]

  generate [category]   write generated icons into assets/icons/ (chrome into
                        assets/theme/)
                        category: cards relics potions map status intents
                                  events chrome
  animate               derive sprite animation frames into assets/sprites/anim/
  clamp [paths...]      snap PNGs onto the shared ramp (default: all of assets/)
  validate              enforce ART_SPEC §1/§3/§5/§8; nonzero exit on failure"
    );
}

fn generate(assets: &Path, category: Option<&str>, dry_run: bool) -> ExitCode {
    let requested: Vec<icons::Icon> = match category {
        Some(name) => {
            let filtered: Vec<icons::Icon> = icons::all()
                .into_iter()
                .filter(|icon| icon.category == name)
                .collect();
            if filtered.is_empty() {
                eprintln!("error: no icons in category '{name}'");
                return ExitCode::FAILURE;
            }
            filtered
        }
        None => icons::all(),
    };

    let mut written = 0;
    for icon in &requested {
        let path = output_dir(assets, icon.category).join(format!("{}.png", icon.name));
        let canvas = (icon.draw)();
        if dry_run {
            println!("would write {}", path.display());
            continue;
        }
        if let Err(error) = canvas.save(&path) {
            eprintln!("error: {error}");
            return ExitCode::FAILURE;
        }
        written += 1;
    }

    println!(
        "generate: {} icon(s){}",
        if dry_run { requested.len() } else { written },
        if dry_run { " (dry run)" } else { " written" }
    );
    ExitCode::SUCCESS
}

/// Where a category's PNGs land. `Icon::category` is the subdirectory for the
/// seven icon categories, which is what keeps the registry and the on-disk
/// layout from disagreeing; `chrome` is the one divergence, because 9-slice
/// border art is on the 16/24 grids rather than 32x32 and so has to sit
/// somewhere `validate::expected_grid` reads differently. `assets/theme/` is
/// that place, and it is already where `hollowdeck_theme.tres` lives.
///
/// The routing is here rather than in a separate `artgen chrome` subcommand on
/// purpose: CI re-runs plain `generate` and diffs the tree to catch committed
/// art drifting from its source, and a subcommand of its own would have been
/// outside that check.
fn output_dir(assets: &Path, category: &str) -> PathBuf {
    match category {
        "chrome" => assets.join("theme"),
        other => assets.join("icons").join(other),
    }
}

fn run_animate(assets: &Path, dry_run: bool) -> ExitCode {
    let report = anim::run(assets, dry_run);
    for failure in &report.failures {
        eprintln!("error: {failure}");
    }
    println!(
        "animate: {} frame(s){}",
        report.written,
        if dry_run { " (dry run)" } else { " written" }
    );
    if report.failures.is_empty() {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}

fn run_clamp(root: &Path, assets: &Path, paths: &[&str], dry_run: bool) -> ExitCode {
    let targets: Vec<PathBuf> = if paths.is_empty() {
        validate::files_under(assets, "png")
    } else {
        paths.iter().map(PathBuf::from).collect()
    };

    let mut changed = 0;
    for path in &targets {
        match clamp::clamp_file(path, dry_run) {
            Ok(outcome) if outcome.changed => {
                changed += 1;
                println!(
                    "{:<44} {:>4} → {:>3} colours{}",
                    validate::relative(root, path),
                    outcome.colours_before,
                    outcome.colours_after,
                    if dry_run { " (dry run)" } else { "" }
                );
            }
            Ok(_) => {}
            Err(error) => {
                eprintln!("error: {error}");
                return ExitCode::FAILURE;
            }
        }
    }

    println!("clamp: {changed} of {} file(s) changed", targets.len());
    ExitCode::SUCCESS
}

fn run_validate(assets: &Path) -> ExitCode {
    let report = validate::run(assets);
    for failure in &report.failures {
        println!("FAIL {failure}");
    }
    if report.failures.is_empty() {
        println!(
            "validate: {} asset(s) conform to ART_SPEC",
            report.checked
        );
        ExitCode::SUCCESS
    } else {
        println!(
            "validate: {} failure(s) across {} asset(s)",
            report.failures.len(),
            report.checked
        );
        ExitCode::FAILURE
    }
}

fn repository_root() -> Option<PathBuf> {
    let mut directory = std::env::current_dir().ok()?;
    loop {
        if directory.join("project.godot").is_file() {
            return Some(directory);
        }
        if !directory.pop() {
            return None;
        }
    }
}
