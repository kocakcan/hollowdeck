# Hollowdeck Pixel Art Roadmap

`docs/ART_SPEC.md` is the rule set. This is the backlog *against* it — what the medium is still
owed, ordered by what a player would notice first.

Seeded from [saint11's pixel art tutorials](https://saint11.art/blog/pixel-art-tutorials/) (Pedro
Medeiros, 80+ cards, CC-BY) read against this codebase. Reading them turned up something worth
recording as a method rather than a one-off: **three of the seven items below were not "add polish",
they were places the spec already stated a rule the code did not follow.** The spec was written first
and the enforcement lagged it, which is exactly the drift `ART_SPEC.md`'s own opening paragraph says
it exists to prevent. **All three (§1, §2, §3) have now shipped, and each landed an assertion with
it** — which closes that category and leaves §4 onward as ordinary backlog: things the spec never
claimed and the code never had.

Worth carrying forward, because it is the pattern rather than three incidents: in every one of the
three, the expensive part was not building the thing. It was that the spec had described it in the
present tense for three phases, so nobody reading either document had a reason to check.

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

---

## 3. The animated glow ring — **shipped**

*(Shine)*

§6's other unfulfilled promise, and the last of the three. `scripts/ui/GlowRing.cs` is a `Node`
parented to the Control it drives, stepping that box's `BorderColor` through a ramp triple on a
`_Process` timer — the `SpriteAnimator` shape one property over. Three surfaces: a Rare
`CardFrameStyle`, `BossNodeGlowStyle`, and the `TargetLockStyle` that moved out of `EnemyView` to
join the other two.

What it replaced was `ChromeStyles.cs` calling the ring "the eventual treatment" in a comment while
painting a static bright border, and §6 specifying it in the present tense beside it.

Five things are worth knowing before touching it:

- **It steps rather than tweening, and that is correctness rather than style.** The obvious
  implementation is `TweenProperty` on `border_color`; it passes through every colour between `G3`
  and `G4`, and §5 admits 43. The guard is `TestEveryGlowFrameIsOnTheRamp`, which reads the colour
  back off the *installed* `StyleBox` rather than off the cycle array — the array is trivially
  on-ramp, and what can go wrong is the builder in between, where `CardFrameStyle` is already
  lerping two lines from where the glow lands.
- **The triple is a parameter, so §6 got reworded rather than obeyed literally.** It said
  `G3 → G4 → G5` for all three surfaces; the boss node takes `R3 → R4 → R5` instead, because
  `BossNodeGlowStyle` is keyed to the Damage semantic and gold there would say "rare" on the one node
  whose meaning is danger. A spec sentence written before two of its three subjects existed is worth
  re-reading rather than following.
- **`Attach` opens on the peak of its cycle**, which is exactly the colour each ring replaced. That
  is what makes the rest state identity rather than a change, and what keeps the six `ScreenShot`
  fixtures that render these screens deterministic. There is deliberately no `ScatterIdlePhase`
  analogue for the same reason — and because two Rare cards pulsing in unison read as one rule,
  where two enemies breathing in unison read as one creature drawn twice.
- **Ungated on `ReduceMotion`**, unlike the reflex. §1's driver already established the rule — gate
  the photosensitive flash, not the gentle ambient loop, since gating the idle breathe was reported
  from a playthrough as "sprites don't animate" — and `MapScreen.BuildCurrentNodeRing` loops an
  ungated alpha pulse on the very screen the boss ring lives on.
- **Three of the new assertions are about the *seam*, not the driver.** Every check on `GlowRing`
  itself stays green while nothing attaches one, so `MapSmokeTest`, `CombatTargetingSmokeTest` and
  `PixelSpecSmokeTest` each assert their surface actually carries a ring — and the card one asserts
  the reverse too, since `CardView`s are pooled and a ring that attaches without detaching puts a
  gold pulse on a Common.

Two things this cost that are worth recording, both caught by mutation testing rather than by the
suite going red:

- **The first teardown assertion could not fail.** It waited three frames after unlocking a target
  and re-read `IsTargetLocked` — 48ms against a 220ms frame time, so the ring would not have
  repainted whether or not it was stopped. It asserts the structural fact instead (no `GlowRing`
  still parented to an unlocked view), which is what actually separates `GlowRing.Stop` from a bare
  `QueueFree`.
- **The hazard that teardown closes is intermittent, which is worse than constant.** Godot runs a
  parent before its children and frees a queued node at frame end, so a ring released with
  `QueueFree` gets one more tick *after* the caller installed its own box — repainting over it only
  when that tick crosses the frame time, roughly one unlock in fourteen at 60fps.

---

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
