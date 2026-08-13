# Hollowdeck Pixel Art Roadmap

`docs/ART_SPEC.md` is the rule set. This is the backlog *against* it — what the medium is still
owed, ordered by what a player would notice first.

Seeded from [saint11's pixel art tutorials](https://saint11.art/blog/pixel-art-tutorials/) (Pedro
Medeiros, 80+ cards, CC-BY) read against this codebase. Reading them turned up something worth
recording as a method rather than a one-off: **three of the seven items below are not "add polish",
they are places the spec already states a rule the code does not follow.** The spec was written first
and the enforcement lagged it, which is exactly the drift `ART_SPEC.md`'s own opening paragraph says
it exists to prevent.

Each item names the tutorial that backs it, so the technique can be checked rather than
re-derived.

---

## 1. Sprite frame animation — **shipped**

*(characterIdle, Attack, Death, Impact, Subpixel)*

The 36 sourced enemy tiles plus the player animate by frame swap: `idle`, `windup`, `hit`, `death`,
`escape`, derived by `artgen animate` (`tools/artgen/src/anim.rs`) with integer pixel operations
only, played by `scripts/ui/SpriteAnimator.cs`.

What it replaced was a live §2 violation: `scale → 1.04 / 1.08 / 1.15` and `rotation_degrees → 6` on
`EnemyView`'s sprite, `scale → 0.7` + `rotation → 10` on death, a `(0.15, 0.85)` squeeze on escape, a
1.5x pop on the intent icon, and a 6px player bob at a 5x render scale. All of it under "any
non-integer scale is a bug, not a judgement call", for three phases, with 23 green suites — because
the only assertion looked at the *static* `CustomMinimumSize`.

`ART_SPEC.md` §9 now states the rule and `PixelSpecSmokeTest.TestNoTweenTransformsAPixelSprite`
enforces it.

---

## 2. Chrome 9-slices

*(9-Slice UI, Outline, Shading)*

**§6 already claims this is done, and it is not.** It says frames, bezels, buttons and panels "are
`StyleBoxTexture` 9-slices over pixel border art" and that "`ChromeStyles.EndTurnButtonStyle` already
uses this pattern — follow it". Both halves are false: `ApplyEmphasisButtonStyle` deliberately
*dropped* the sourced ornate frame when the project committed to one medium, and every box in
`ChromeStyles.cs` is a `StyleBoxFlat` with a 1–3px border. There is no chrome art in `assets/` and no
`chrome` category in `artgen`.

The groundwork is already laid: §1 reserves the 16x16/24x24 grids, and `validate.rs`'s
`expected_grid` already enforces them for anything under `/theme/`.

This is also the item that would recover what §7's "Known cost, accepted" gave up — the
illuminated-manuscript character lost with Cinzel and IM Fell English is meant to come back through
ornament, and ornament is what 9-slice border art is.

Fix the false claim in §6 whether or not the work is picked up.

## 3. The animated glow ring

*(Shine)*

§6's other unfulfilled promise: "glow" for rare cards, boss nodes and the target lock is specified as
a 1px pixel border cycling `G3 → G4 → G5`. `ChromeStyles.cs` calls it "the eventual treatment" in a
comment and paints a static bright border instead. Small, self-contained, and the most visible
per-hour item on this list.

## 4. One light direction

*(Shading, Illumination Techniques)*

`tools/artgen/src/icons/shapes.rs` has a shared form vocabulary — one `blade`, one `shield` — which
is what stops 192 icons drifting into 192 different tapers. What it does not have is a **global light
direction**: each shape picks its own lit edge (`blade`'s `edge` parameter, `scale`'s `lit`,
`gem`'s facets), so the set is internally consistent in form and inconsistent in lighting.

§5 already commits to hue-shifted ramps, which is the harder half. Pinning a light direction is
mostly a convention plus an audit.

## 5. Combat effect frames

*(Impact, Explosion, Smoke, Electric, Wind)*

`CombatScreen` carries hit feedback as `modulate` flashes (`:1242`, `:538`) and one procedural
particle burst (`:472`). Now that `artgen animate` and `SpriteAnimator` exist, a generated effect
frame set is the pixel-native version and reuses both. The `hit` clip's flash frame is a first taste
of what this buys.

## 6. An easing vocabulary

*(Easings)*

~35 `CreateTween` sites pick `TransitionType` and duration ad hoc. saint11's Easings card is the
reference for which curve reads as what; the codebase would benefit from a small named set
(`Snap`, `Settle`, `Drift`) rather than per-site choices.

Pairs naturally with `ROADMAP.md` Phase 11's animation-speed setting, which needs one place to scale
durations from.

## 7. Backgrounds

*(Parallax, Clouds, Darkness, Illumination Techniques)*

Eight 64x64 tiles, drawn static under `ScreenBackground`'s vignette and ground-plane gradient. Depth
here is cheap and per-act atmosphere is the payoff.

---

## Out of scope, so it is not re-argued

The tutorial set is written for platformers and top-down action games, and a good half of it does not
apply:

- **Walk / run cycles, 4-leg walk, jump, wall-slide, slide-roll.** The game has no locomotion.
  Combatants stand in place and the map is a node graph, not traversable space.
- **Tiles, top-down houses, isometric, level progression.** No tilemap, no traversable world.
- **Portraits.** The 32x32 creature sprite is the whole character presentation; a portrait layer
  would be a second art medium for the same subject.
- **Resizing.** §2 forbids non-integer scaling outright, which is the problem that tutorial solves.
