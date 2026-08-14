# Hollowdeck Pixel Art Roadmap

`docs/ART_SPEC.md` is the rule set. This is the backlog *against* it — what the medium is still
owed, ordered by what a player would notice first.

Seeded from [saint11's pixel art tutorials](https://saint11.art/blog/pixel-art-tutorials/) (Pedro
Medeiros, 80+ cards, CC-BY) read against this codebase. Reading them turned up something worth
recording as a method rather than a one-off: **three of the seven items below were not "add polish",
they were places the spec already stated a rule the code did not follow.** The spec was written first
and the enforcement lagged it, which is exactly the drift `ART_SPEC.md`'s own opening paragraph says
it exists to prevent. Two of those three (§1, §2) have shipped and each landed an assertion with it;
§3 is the one left, and it is the one the spec still describes in the future tense on purpose.

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
enforces it — by *type*, over every `TextureRect`-typed identifier in `scripts/ui` plus `this` in a
view wrapping one. The first version of that guard was a hand-written list of three field names, and
it was green over two live violations while claiming full coverage in three documents; the review
that caught it is the reason the scan is type-driven.

**Two named exceptions remain, and they are the rest of this item:** `CardView.cs` (the 1.15x hover
bump and the play/exhaust pops) and `FloatingText.cs` (damage numbers punching in from 2.2x, which
is §7's design-em rule rather than §2's). Both carry a real affordance that needs replacing rather
than deleting — the first is half of ROADMAP Phase 11's "card inspect".

---

## 2. Chrome 9-slices — **shipped**

*(9-Slice UI, Outline, Shading)*

Fourteen slices from `artgen`'s `chrome` category (`tools/artgen/src/icons/chrome.rs`) into
`assets/theme/`, drawn by `ChromeStyles` and by `hollowdeck_theme.tres`: panels, HUD slots, the art
plinth, the emphasis button's four states and the theme's five ordinary button states. A 1px outer
rule, a face-coloured gutter, a 1px inner rule, and a stepped bracket in each corner. This is the
ornament §7's "Known cost, accepted" promised in exchange for Cinzel and IM Fell English, and the
emphasis button — which had carried that loss as bare border weight since the sourced wooden frame
was retired — is where it shows.

What it replaced was §6 describing this in the present tense while none of it existed: every box in
`ChromeStyles.cs` was a `StyleBoxFlat`, `assets/theme/` held no PNGs, and the entry pointed at
`ChromeStyles.EndTurnButtonStyle` as an example to follow — a function deleted in the same commit
that dropped the sourced frame. The groundwork had been laid and left standing on nothing:
`PixelSpec.ChromeSlice` was referenced only by `IsLegalGrid`, and `validate.rs`'s `/theme/` arm had
never seen a file.

Four things are worth knowing before touching it:

- **A box gets a 9-slice iff its colours are fixed at author time**, which is why half of
  `ChromeStyles.cs` is still flat. A `StyleBoxTexture` has no `BorderColor` and `ModulateColor`
  multiplies, so a rarity-tinted frame lands off the §5 ramp. `CardFrameStyle` would need one
  texture per `CardType` × `Rarity` × hover × upgraded, which is the per-card-class explosion the
  effect system exists to avoid one layer down.
- **The two properties that matter are both wrong by default.** `AxisStretchMode.Stretch` is the
  constructor's value and resamples every edge to a fractional width; the corner budget (§1's ≤ 1/3)
  is invisible to `artgen validate`, which reads pixels and cannot see a texture margin. Both are
  asserted now, over the `ChromeStyles` producers *and* the theme resource, because those are two
  independent consumers of the same art.
- **Slice size follows the shortest box, not the art.** Panels and slots are 16 because
  `ScreenChrome`'s HP and gold panels have a 4px vertical content margin and a `StyleBoxTexture`
  under twice its margin folds its own corners together; buttons and the plinth are 24.
- **`chrome` is the one category `generate` writes outside `assets/icons/`** (`main::output_dir`),
  which is what puts it under `validate.rs`'s `/theme/` rule — the trick `anim.rs` already plays with
  `/sprites/`. Keeping it inside plain `generate` rather than giving it a subcommand is what keeps
  CI's "generated art is up to date" step covering it.

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
