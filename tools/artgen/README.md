# artgen

Hollowdeck's asset tool. It generates the game's 206 icons, snaps every authored
asset onto the shared palette, and enforces `docs/ART_SPEC.md`.

```bash
cargo run --release -- generate            # write every icon into assets/icons/
cargo run --release -- generate potions    # just one category
cargo run --release -- generate chrome     # the 9-slices, into assets/theme/
cargo run --release -- clamp               # snap all of assets/ onto the ramp
cargo run --release -- validate            # enforce ART_SPEC §1/§3/§5/§8
cargo test --release                       # the shape rules ART_SPEC §10 states
```

`--dry-run` on `generate` and `clamp` reports without writing. Paths resolve
against the repository root (found by walking up for `project.godot`), so it
runs the same from here or from the repo root.

`tools/run-smoke-tests.sh` runs `cargo test` and then `validate` before the
engine suites, so a broken shape rule or a non-conforming asset fails the build.
Both degrade to a warning if `cargo` is not installed — the game itself never
needs a Rust toolchain.

The two are not redundant, and the split is structural rather than tidy.
`validate` reads finished PNGs, so it can only see properties a pixel has:
dimensions, ramp membership, alpha. §10's light direction is not one of those —
a highlight on the wrong side of a blade is on the ramp, on the grid and
hard-alpha, and passes every rule that command has. Some rules can only be
asserted about the code that draws.

## Why Rust, and why a tool at all

The engine decision in `CLAUDE.md` rejected Rust because `bevy_ui` and tweening
are weak and ECS stacks onto engine-learning. None of that applies to a program
that writes PNGs. It lives here beside `run-smoke-tests.sh` rather than in its
own repo; a second repo would mean syncing generated assets across two build
systems from day one, and it earns that only if it ever becomes useful to
someone else.

Generating icons rather than drawing them buys three things that the vector set
could not have:

- **One palette, enforced.** Every colour is a `palette.rs` constant. There is
  no way to author an off-ramp pixel, and `validate` catches any that arrive
  from outside.
- **One shape vocabulary.** `icons/shapes.rs` holds the blade, shield, flask,
  droplet, flame and fist that the set is composed from — plus the gem, scale
  and barb added for the Phase 8 statuses — so a blade is the same blade in all
  28 icons that use one.
- **One light.** `icons/light.rs` holds the direction (up and to the left) and
  answers which of two faces it falls on, so a shape derives *where* its
  highlight goes instead of hardcoding a side. One vocabulary without the other
  is what let `annihilate` ship with its two crossed blades lit from opposite
  corners.
- **Re-runnable.** Change a ramp entry, re-run, and all 206 icons regenerate
  consistently. That is the property that made the whole pixel-art commitment
  worth making.

## Layout

| File | What it owns |
| --- | --- |
| `palette.rs` | the 43-colour ramp, mirrored from `PixelSpec.Ramp` |
| `canvas.rs` | RGBA8 buffer, hard-edged drawing primitives, PNG load/save |
| `clamp.rs` | alpha hardening + nearest-ramp snap |
| `validate.rs` | the ART_SPEC checks and their failure messages |
| `icons/shapes.rs` | the shared shape vocabulary |
| `icons/light.rs` | the one light direction, which face it falls on, and the named exceptions |
| `icons/{cards,relics,potions,misc,events}.rs` | the 192 icons themselves |
| `icons/chrome.rs` | the 14 9-slice chrome frames (the one category that writes to `assets/theme/`, not `assets/icons/`) |

## Three invariants that will bite

**`palette.rs` must match `PixelSpec.Ramp`.** The game clamps at runtime, this
clamps offline; if they disagree, `validate` rejects assets the game draws
happily. `PixelSpecSmokeTest.TestArtgenRampMatchesPixelSpec` parses the `pub
const` lines in `palette.rs` and compares them entry by entry, so the
declarations have to keep their exact literal shape.

**Icon names are definition ids.** `ArtAssets.cs` resolves art by convention
(`assets/icons/cards/<card_id>.png`), so a typo in an `Icon { name: ... }` is a
silently missing icon rather than an error.
`PixelSpecSmokeTest.TestEveryDefinitionHasAnIcon` checks both directions
against the content JSON — a definition with no icon, and an icon with no
definition.

**A directional shape takes its highlight *position* from `light.rs`.** Colours
stay parameters, because pigment is a content decision; which side they land on
must not be, or the next shape hardcodes a side that is right at one angle and
wrong at the other 31. The only way out is the exception list in `light.rs`'s
header — emissive, aperture, or a mark with no volume — and that list is a list:
nothing enforces membership of it.

## Adding an icon

One entry in the relevant `icons/*.rs` `icons()` list plus a `fn` that returns a
`Canvas`. Compose from `shapes.rs` where you can; end with `finish` (or
`finish_heavy` for chunky silhouettes), which lays the `N0` outline that keeps
the shape legible on both a lit card frame and a near-black HUD.

Author for **1x**. Icons are shown at 32px in the HUD and 96px in a card's art
window, and the small one is what constrains you: past roughly six distinct
shapes an icon stops reading there. Every icon in the set that had to be redrawn
failed this way — a flexed arm that read as a boot, a fanned deck that read as a
barcode, a horn that read as a gold blob.

If you place a highlight by hand rather than through a shape, **put it up and to
the left** (ART_SPEC §10). Nothing checks that — the ~186 existing hand-placed
highlights were audited once by reading them, and a new one in the bottom-right
corner leaves every suite green. And when §10 and the 1x budget above disagree,
the budget wins: `raised_fist` keeps a highlight on the knuckle the lamp cannot
reach, because without it the hand read as having three fingers.

The `events` category is the one exception: `EventScreen` draws it at 5x and
nowhere smaller, so those can carry more structure. They still fail in the same
way, just at a different threshold — the ones that had to be redrawn read as a
fireplace instead of a shrine, a claw instead of an open hand, and a medical
cross instead of an arcane seal. Whatever the scale, look at the output before
believing it works.
