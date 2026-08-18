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

§4 has since shipped too, and it inverts that pattern in a way worth noticing: the code landed
*first* and `ART_SPEC.md` §10 was written to describe it. So §10 is the first section of that
document that has never been ahead of its own enforcement — which is precisely the drift the three
items above each spent three phases inside.

§5 has shipped as well, and it turned up a fourth instance of the same shape from the other
direction: §9 said frame animation *derives* its frames from sourced art, which was true while
creatures were the only thing animating and silently stopped being true the moment an authored
effect set existed. That sentence has been widened rather than quietly restated, since a document
describing a rule the code has outgrown reads exactly like one describing a rule it never met.

§7 closes the file. It is the one entry whose first, complete, tested implementation was simply the
wrong thing — nine better tiles for seven worse ones, changing nothing, because the problem was
composition and a tile cannot have any. Worth carrying with the three below: **the failures this
document keeps recording are failures of knowing what is broken, not of building it.**

§6 has shipped, and it is the same drift arriving through a third door — not a spec ahead of its
enforcement, but a *helper* ahead of its adoption. `UiTheme.Motion` existed for three phases and was
used by two tween sites out of thirty-four; a constants bag nobody reaches for is indistinguishable
from one nobody needs, and neither `ART_SPEC.md` nor any suite could tell the difference. The lesson
generalises past this document: **shipping the abstraction is not the same as landing it, and only a
sweep over the call sites can say which happened.**

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
  CI's "generated art is up to date" step covering it — **and that was necessary, not sufficient,
  which this bullet got wrong at the time.** The step has to diff the directory too, and it listed
  `assets/icons` alone, so every slice was regenerated by CI and then ignored for as long as chrome
  existed. Fixed since; the general form is that a new output directory costs a line in
  `main::output_dir` and a line in that step.

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

## 4. One light direction — **shipped**

*(Shading, Illumination Techniques)*

One lamp, up and to the left, at 45°, for all 192 icons. `tools/artgen/src/icons/light.rs` owns the
direction and answers which of two faces it falls on; `shapes.rs` derives every highlight *position*
from it. `ART_SPEC.md` §10 is the rule.

What it replaced was a set with one **form** vocabulary and no shared **light**: each shape picked
its own lit edge out of a literal, so the icons were consistent in form and contradictory in
lighting. `blade` painted its bright edge on the side that was "leading" relative to the blade's own
rotation — a different *screen* side at every angle — and two thirds of the authored blades happened
to point somewhere that made that right. The three that did not were `pommel_strike`, `cataclysm`,
and `annihilate`, whose crossed pair carried one blade lit from the upper left and the other from
the lower right: two suns inside one 32x32 square. `shield`, at twenty-six call sites, had no light
direction at all.

Six things are worth knowing before touching it:

- **The direction was ratified, not chosen.** `strike` — the icon the whole Attack half quotes — was
  already lit upper-left, and the derived rule reproduces its bytes exactly. So `strike.png` staying
  out of `git status` after a regeneration is the sharpest check this change has: if it moves, the
  sign is wrong somewhere. `flask`, `droplet`, `raised_fist` and most hand-placed highlights were
  already up-left too, which is why the item cost three blade flips rather than thirty-six.
- **The rule is that position stops being a parameter.** Colours stay the caller's — pigment is a
  content decision — but `blade` no longer has a literal through which a wrong side *could* be
  authored. That is the `IsPlayable`-derived-from-`CardType` move, one language over; the alternative
  was documenting the convention and hoping, which is what the previous three items all found rots.
- **`light.rs` owns which side; the shape owns how much.** `shield` reveals a rim brighter than its
  face, so its wide band goes on the lit side; `gem` reveals a body darker than its face and wants
  the wide band in shadow. Same `by_face` call, arguments swapped. Folding that choice into `light.rs`
  would make one of the two wrong.
- **Two exemptions are by budget rather than by physics, and they look like oversights.** All four
  of `raised_fist`'s knuckles keep their `R4`; dropping the furthest to the body colour is what the
  light says and it was tried, and at 1x the fourth finger vanished into the hand. §1's legibility
  budget outranks §10 when they disagree. `clothesline` could make the opposite trade — its `N0`
  finger grooves already separate the fingers — which is why the two hands are lit differently and
  each says so.
- **The report that would rank the hand-drawn half was prototyped and deliberately not shipped.** It
  cannot separate a highlight from a *material* use of the same ramp entry: `BLADE_EDGE` is `N8`, a
  whole shield rim is `B4`, `sparkle` is nothing but its top entry — so it ranked the emissive shapes
  worst precisely because they were correct, and roughly half its top ten were false positives when
  checked by eye. A metric that noisy cannot gate, and a report that does not gate is one nobody runs.
  The identical centroid measurement is *exact* over one shape drawn alone with colours the test
  picks, which is why it lives in the test instead.
- **`tools/artgen` has tests now, and it had none before.** `cargo test` ran nowhere in this
  repository — not in `run-smoke-tests.sh`, not in CI. The blade sweep covers 32 angles rather than
  the authored call sites, because the call sites only cover the angles somebody already thought of.

Four things this cost that are worth recording, and the first two are the ones to read:

- **A "stability" deadband silently rebuilt the exact bug the feature exists to prevent.** `lit_offset`
  shipped for one commit with `GRAZING = 0.05`, on the reasoning that a band around the terminator
  stops a highlight flipping from a one-pixel change in a tip coordinate. That reasoning is
  backwards. 0.05 is a ~3° *wedge of real answers*, and inside it the tiebreak overrides the light:
  `wild_swing` (incidence +0.0499) and `map/elite`'s left sword (+0.0303) had their bright edges
  moved onto the **shadow** face, while `map/fight` — the same crossed-sword icon in a different
  colour, and 0.005 the other side of the threshold — kept its lit edge. So the two icons that are
  meant to be the same weapon twice lit the same blade on opposite faces: `annihilate`'s two suns,
  rebuilt across a matched pair, by the mechanism written to prevent them. `GRAZING` is a float
  epsilon now (`1e-3`), and a test asserts it stays under 0.01 rather than trusting the comment.
  The general form: **a tolerance that can contain a real answer is not a tolerance.**
- **The count in four documents was the tell, and it was legible before anyone looked at a pixel.**
  All four said the derived rule flips "three blades"; with the deadband in, reverting `blade` moved
  *five* icons. That gap was the whole bug, visible from a number. Fixing `GRAZING` made the
  documented three true again — which is the direction that repair should run: the docs were right
  and the code was wrong, rather than the docs being quietly restated to match.
- **The first version of the sweep failed on a blade that was lit correctly.** It compared the edge
  and body centroids directly; `edge` runs hilt to shoulder while `body` includes the tip, so on an
  up-left blade the edge's centroid sits *behind* the body's along the blade's own axis, and that
  along-axis term swamped the one-pixel lateral one the rule actually decides. Projecting onto
  `across` first removes a term the rule never touches. The test was wrong, not the shape — and it
  is the kind of wrong that would have been "fixed" by loosening the assertion.
- **The audit's finding was not the forecast.** This entry predicted "mostly a convention plus an
  audit" and expected the ~186 hand-placed highlights to be the bulk of the work. Reading all of them
  turned up that the hand-drawn half was already overwhelmingly up-left, and that the large majority
  of highlight-*coloured* draw calls are emblems, marks and materials rather than shading — a crown,
  a cross, a page block, a pale mask deliberately high on the ramp because it sits behind light bars.
  Eight sites genuinely contradicted the lamp. The expensive half was the vocabulary, which is the
  half that could be made unrepresentable.
- **Four of the six shape assertions were green on the code they replaced**, and every one failed
  for the same reason: it measured a colour the shape had *already* been placing, rather than the
  pixels this change's light pass writes. `flask`'s row read the pre-existing authored glint instead
  of the new lit wall, so deleting the wall outright left it passing; `skull`'s read the jaw's
  contact band, which is correct under any light; `raised_fist`'s `R2` centroid was dominated by a
  wrist block sitting fourteen rows below the knuckles, so the new flank could be deleted *or moved
  to the lit side* with the suite green. Each now measures inside a row band or against a colour only
  the new pass writes, and all eight shapes fail under both deletion and inversion. **A whole-colour
  centroid over a shape that already had shading measures the shading it already had.**

**What is deferred, and stated so it is not mistaken for finished:** those ~186 hand-placed
highlights are audited but *unheld*. Nothing asserts them, `artgen validate` is structurally blind to
them, and a new icon may put a `G5` pixel in its bottom-right corner with the whole suite green.

## 5. Combat effect frames — **shipped**

*(Impact, Explosion, Smoke, Electric, Wind)*

Four authored four-frame bursts — `impact`, `ward`, `bloom`, `venom` — in
`tools/artgen/src/icons/fx.rs`, spawned by `scripts/ui/CombatFx.cs` and played by
`SpriteAnimator.AttachOneShot`. One shape in four pigments, on one beat: flash, ring, arms, motes.

What it replaced was `CombatScreen.SpawnHitSpark`, a `CpuParticles2D` whose texture was a smooth
24x24 radial `GradientTexture2D` drawn at `ScaleAmountMin 0.4 / Max 0.9`. That is three violations
in one node — §5's ramp, §3's hard alpha, §2's integer scale — and **not one of them was visible to
anything in the project**: `PixelSpecSmokeTest`'s transform scan only knows identifiers declared
`TextureRect`, and `artgen validate` only reads files under `assets/`. It was the last
smooth-gradient art on the combat screen and it outlived four pixel passes by sitting in the gap
between two checks.

The `modulate` flashes the old entry named beside it were **left alone deliberately**: §9 permits
them, since `modulate` resamples nothing. Only the burst was outside the medium, and that is the
whole scope line.

Six things are worth knowing before touching it:

- **The art is in `assets/icons/fx/`, and the name reads backwards on purpose.** These are frame
  runs, and frame runs live under `assets/sprites/`. But `artgen`'s `output_dir` already routes an
  unknown category to `assets/icons/<category>/` with no arm, and CI already diffs `assets/icons`
  for drift — so this cost *zero* infrastructure lines, where a new output directory costs one in
  `output_dir` and one in that CI step. The second of those two is exactly the line §2 above records
  being missed, which left every chrome slice regenerated by CI and then ignored. The price paid
  instead is that `assets/icons/` now holds one directory whose files are not one per definition id,
  so `AssertIconsMatch` does not apply to it and it carries a coverage check of its own.
- **A tint is not available, so four effects are four frame sets.** `ModulateColor` multiplies, so a
  blue `impact` lands off the §5 ramp — the same argument that keeps `CardFrameStyle` flat while the
  chrome around it is 9-sliced, one asset class over.
- **Reduce Motion reached these for free, and that was the design.** `AttachOneShot` registers under
  one synthetic clip name and that name went into `SpriteAnimator.FlashOpeningClips`, so every burst
  inherits the decline of its opening flash frame rather than each needing a gate. Every burst opens
  on a solid disc with an `N8` core, which is the same frame the `hit` clip opens on — one rule, not
  a second answer to the same question.
- **`venom` fires when Poison *lands*, not when it ticks**, and that is forced rather than chosen.
  `PopupDelta` is a state diff with no cause channel, so a Poison tick arrives indistinguishable
  from a sword and plays `impact`. Giving it a cause is a `CombatManager` change; and the beat that
  was actually missing is the arrival, since a tick already moves a number the player is watching
  and an application moved nothing at all.
- **The Rust tests are about the *sequence*, which is the half `validate` is structurally blind to.**
  A burst that repeats a frame, contracts instead of expanding, or thickens as it dies is on the
  ramp, on the grid and hard-alpha. Four rules: consecutive frames differ, extent never shrinks, mass
  falls after the ring, and every frame is centred — the last because the call site positions by
  centre and art that is quietly off-centre reads as landing beside the creature.
- **`AnimationScreenshot` grew a fourth shot, because a frame-counted wait could not catch one.** A
  burst is 0.24s and that scene does enough per frame that its waits are not a reliable clock: the
  first attempt shot the beat *after* the burst had finished and freed itself, with the effect
  working perfectly. It spawns the four, ticks the i-th one i times, stops the drivers so the
  engine's own tick cannot race them, and pins Reduce Motion off through a scratch settings path. A
  shot that only sometimes contains its subject is worse than none, because a green-looking one
  proves nothing.

Two things this cost that are worth recording, both found by mutation testing rather than by
anything going red:

- **The "is anything spawning this?" check could not fail.** It scanned `scripts/ui` for
  `CombatFx.<Name>` and counted the *comment* above the call site — the paragraph explaining when
  `venom` fires names `CombatFx.Venom`, and prose about a call site reads to a regex exactly like
  one. Measured: repointing that spawn at another effect left all 1113 checks green. Skipping
  comment lines fixes it, which is what `ScanSourceForSpriteTransforms` beside it already did.
- **A test that steps by the constant it is testing cannot observe that constant.** The advance
  check ticked the animator by `CombatFx.FrameSeconds * 2`, so setting `FrameSeconds` to 600 — a
  burst that never ends — stayed green: the tick grew with it. It ticks at a real 1/60 against a
  fixed 30-frame budget now, which turns "does it advance" into "does it finish", and the second is
  the question worth asking.

**What is deferred, and stated so it is not mistaken for finished:** `PlaySlashTrail` is still a
white `Line2D`. A streak spans player→enemy at an arbitrary angle and §2 forbids rotating a pixel
asset, so replacing it needs an authored 8-direction set plus angle-bucketing at the call site —
a different rule from the one every burst here follows, and one none of them needed.

## 6. An easing vocabulary — **shipped**

*(Easings)*

Eight named curves in `scripts/ui/Motion.cs` — `Jolt`, `Flash`, `Snap`, `Pop`, `Settle`, `Land`,
`Fade`, `Drift` — each a duration, a transition and an ease held together, applied through the only
two builders any tween in the game now uses (`Tween.TweenTo`, `Tween.TweenPingPong`).
`ART_SPEC.md` §11 is the rule. All 34 tween sites were migrated; nothing new was animated.

What it replaced was a `UiTheme.Motion` that already half-existed and was **used by two sites out of
thirty-four**. It offered `Fast`/`Normal`/`Slow` beside `EaseStandard`/`EaseOvershoot` as two
independent lists, so a caller had to pair them itself and thirty-two callers didn't bother — `Fast`
was re-spelled as the literal `0.12` four times, `Slow` as `0.35` three times. This is the §2 shape
inverted: not a spec ahead of its enforcement, but a *helper* ahead of its adoption, which fails
quietly in the same way. A constants bag nobody reaches for looks identical to one nobody needs.

Five things are worth knowing before touching it:

- **The single biggest change was an ease that did not exist.** There was no `SetEase` call anywhere
  in the codebase, so all thirty-four sites ran Godot's default `InOut` — including seven `Back`
  tweens, which therefore overshot on *both* ends. `CardView.SnapHome` pulled the card visibly
  *away* from home before starting toward it. That was never a decision anyone made; it was the
  default of a parameter nobody knew was there, which is the strongest argument in this item's
  favour and was invisible until all thirty-four were listed side by side.
- **The period stayed negotiable on purpose, and the shape did not.** `MotionCurve.Over(seconds)` is
  the whole escape hatch. Ambient loop half-periods really are per-site (14s of fog against 0.7s of
  map ring), and `CardView`'s exhaust branch really is faster *because* it is an exhaust — those are
  content. Which curve they run on is not. A blanket "no literal durations" rule would have been
  enforceable and wrong; it would have pushed six real facts into six invented constant names.
- **`Fade` and `Drift` are the two `InOut` curves, and the ambient loops were already correct.**
  Loops ping-pong, and an `Out` ease at both turns reads as a stutter rather than as breathing, so
  the six looping sites came through *identical in feel* — Sine/InOut before and after. Worth stating
  because "we changed the easing on every tween in the game" is true and would be the wrong thing to
  go looking for in a playtest.

  **`Fade` shipped as `Out` and was corrected in review, which is the one wrong call in this item.**
  A disappearance is not an arrival: `sine::out` on a fade to zero spends most of its opacity in the
  first third. `EnemyView`'s death clip is three frames at 0.12 against a 0.35s fade, so `Out` put
  frame 1 at alpha 0.49 where the clip was authored against 0.74, and the escape clip — the one that
  exists so a fleeing enemy finally *travels* — would have travelled behind a sprite already 72%
  gone. The tell was in the table the whole time: "long enough to read, in either direction" is not
  something an asymmetric ease can be, and every `Fade` site had been Sine/InOut before the change.
- **`Back` never lands on alpha, and three sites had to be split to keep that true.** Back overshoots
  past its destination; past `modulate:a` of 0 or 1 the value clamps, so the property arrives early
  and sits. `PlayDrawTween`, `PlayDiscardTween`, `PlayResolveTween` and
  `TreasureScreen.PlayEntrance` each animate a transform and an alpha together and now pair
  `Land`/`Pop` with `Settle`/`Fade`.
- **`Motion.Seconds` is a function, not a `Scale` field.** ROADMAP Phase 11's animation-speed setting
  needs one place to multiply, and the tempting shape is a `public static float Scale = 1f`. That is
  dead state no assertion can observe. A funnel with one caller is a place to put the multiply
  without pretending the feature is half-built. (`Engine.TimeScale` is not available for it either —
  `CombatScreen`'s hit-stop comment already argued that out, since the same dial rescales
  `CombatManager`'s turn pacing.)

Two things this cost that are worth recording:

- **The rename silently disarmed §1's guard, and 23 green suites said nothing.**
  `TestNoTweenTransformsAPixelSprite` was keyed to the literal `TweenProperty(`, which was every
  tween in the project until this item renamed all thirty-four to `TweenTo(`. Measured on the first
  commit: the scan went from 9 matching lines in `CardView.cs` alone to **0**, with all 9 call sites
  still there. A `TweenTo(this, "scale", …)` added to `EnemyView.PlayDeath` — the exact violation §1
  says that guard was rewritten to catch — passed. This is the second time this one assertion has
  been found broken while three documents claimed it covered everything, and both times the failure
  was that it named things rather than describing them. **A rename is a change to every regex holding
  the old spelling, and nothing in the toolchain will say so.**
- **The forward check cannot fail on the failure that actually matters.** Scanning for a hand-built
  `TweenProperty` catches a site going around the vocabulary and is *completely green* over a
  vocabulary of thirty curves nobody calls — which is the direction this feature rots in, since the
  cost of a ninth curve is paid by the next reader rather than by the author. So two reflection
  sweeps run the other way: every declared curve must be in `Motion.All`, and something under
  `scripts/ui` or `scripts/run` must use it. Same third-set argument the combat bursts needed, and
  each of the three was mutation-tested before this was called done — a raw `TweenProperty`
  reintroduced into `StatusRow`, an unused curve added, and `Drift` dropped from `All`, each red.
- **The exemption is a property and not a file, and the first draft had that backwards.**
  `volume_db` is not motion; nothing on screen moves, and the speed setting this vocabulary exists to
  host must not cut a music crossfade short. That argument is entirely about the *property*, so
  exempting `AudioManager.cs` wholesale was a rule wider than its own reason — a mute-indicator flash
  added to that file later would have been visible motion, outside the vocabulary and outside the
  future setting, with the suite green. The scan roots at all of `scripts/` too, since §11's opening
  sentence is unqualified and two of seven directories does not back it. `FloatingText` and
  `CardView` are covered: their §9 transform exceptions are a different axis and confer nothing here.
- **`TweenPingPong` had a latent do-nothing mode.** `SetParallel` is sticky on a `Tween` and five
  callers elsewhere turn it on; under it the helper's two steps would blend simultaneously into one
  crossfade, i.e. a `SetLoops()` loop running forever and visibly doing nothing. No current caller
  does it. It sets `SetParallel(false)` itself now, which is the "compiles, passes, no motion" shape
  closed for one line rather than left for whoever hits it.

**What is deferred, and stated so it is not mistaken for finished:** *which* curve a site picked is
unheld. The scan sees that a curve was used, not that `Land` was the right one where `Settle`
belonged. That is §10's unheld-highlights situation exactly, and it is the honest boundary of what a
source scan can say.

## 7. Backgrounds — **shipped**

*(Parallax, Clouds, Darkness, Illumination Techniques)*

Eighteen seamless 64x64 tiles in `tools/artgen/src/icons/backgrounds.rs` — six per act: three
floors, a wall, a plinth and a pillar — composed by `scripts/ui/ScreenBackground.cs` into four
bands, with two haze layers drifting at two rates in front of them. `ART_SPEC.md` §12 is the rule.

**This item was built twice, and the first version is the whole lesson.** The entry above described
the state as "eight 64x64 tiles, drawn static", so the obvious reading was that the tiles were the
problem — they were seven sourced CC0 Dungeon Crawl dungeon floors, palette-clamped and tinted per
act, and they read as generic wallpaper. So the first pass generated better ones: nine tiles on the
43-colour ramp under §10's lamp, mortared flagstone and cut slabs and a carved inlay, with Rust
tests for the seam and the tiling. They were, by any measure of a tile, much better art. **They
changed almost nothing**, and the reason is worth stating flatly:

> One tile filling 1152x648 is wallpaper by construction. Repeated 9x5 it has no horizon, so
> nothing drawn in front of it acquires a position, and no amount of detail inside the tile
> survives that.

What was missing was never fidelity, it was **composition**. The second version keeps the floors
and adds the three pieces that make a room — a wall behind, a plinth where the wall meets the
ground, a colonnade standing in front of it — and that is what turned three colours of the same
flat sheet into the Sunken Ward, the Ember Reach and the Hollow Throne. The generalisable form, and
the reason this sits beside §6's: **an asset-quality problem and a composition problem look
identical in a single screenshot, and only one of them is fixed by drawing better.**

Six things are worth knowing before touching it:

- **Which axes a piece must close is per band, and the test knows the difference.** A floor and a
  wall tile both ways; a plinth is one row, so only x; a pillar repeats up the wall, so only y.
  Asking a piece for a seam it never meets is a rule with no failure behind it.
- **The seam test's first version could not pass.** It compared column 63 against column 0 and
  failed every tile in the set — on the premise that a seamless tile's two edges look alike.
  Adjacent columns of a mortared floor are *supposed* to differ; one can be a lit block face and its
  neighbour the joint. Similarity is a property of a gradient, not of a pattern. What actually
  separates a wrapped tile from a cut one is that its seam is an **ordinary** boundary — inside the
  spread of every other column pair rather than above all of them. Mutation-tested: swapping `put`'s
  `rem_euclid` for a `clamp` fails it.
- **A pillar is the one piece with transparent pixels**, so its flanks show the wall behind rather
  than carrying a copy of it, and it needs its own opaque cast shadow on the shaded flank — without
  one it renders as a stripe painted on the masonry. `only_a_pillar_sees_through_itself` asserts
  both directions, because a pillar that lost its transparency and a floor that gained some are the
  same size of bug pointing opposite ways.
- **`sheen` exists because the first composition was invisible.** Wall, plinth, floor and pillar all
  drawn inside `shade`/`face`/`lit` produced a room whose horizon and colonnade you had to be told
  were there. Two ramp steps above `face`, on exactly the two surfaces the lamp hits square, is what
  made them read — and it is why those two bands carry a wider contrast budget than the floors do.
- **Per-act identity moved out of `Modulate` and into the art.** The tints in `acts.json` are
  neutral greys now; each act's set is drawn in its own ramp family. A hue in the tint as well would
  be tinting twice, and the second one lands off the ramp — the same argument that keeps
  `CardFrameStyle` flat and the combat bursts four sets rather than one tinted four ways.
- **A new output directory is still a two-line change, and this is the third time that comment has
  been load-bearing.** `main::output_dir` gained a `backgrounds` arm and CI's drift diff gained the
  path. `validate.rs` needed nothing — its `/backgrounds/` 64x64 arm predates the category by six
  phases.

Two things this closed that were not part of the item, both found by opening the file:

- **`ScreenBackground.AddDustMotes` was `SpawnHitSpark` again** — a `CpuParticles2D` drawing a
  smooth radial `GradientTexture2D` at `ScaleAmountMin 0.4 / Max 1.1`, i.e. §5's ramp, §3's hard
  alpha and §2's integer scale, in one node, on the combat screen. Item 5 above retired the hit
  spark while calling it "the last smooth-gradient art on the combat screen"; **it was not**, and
  the survivor was invisible for the identical two reasons. Min and max are one integer now (Godot
  samples *continuously* between them, so any min ≠ max is a fractional-scale generator rather than
  a range of sizes) and the texture is two opaque ramp pixels.
- **`PixelSpec.TileScale = 2` had no readers.** §2's table said `128x128 tiled` and
  `StretchMode.Tile` repeats a texture at its native size, so tiles drew at 1x for six phases. The
  tile is upscaled once at attach time now, which is what makes the constant mean something —
  scaling the `TextureRect` instead would be both a fractional-scale hazard and a transform on a
  pixel holder.

**The focal feature is the seventh piece and the one that finished the item.** A drowned gate, a
furnace mouth and a throne, 256x128 each, placed once behind the action rather than tiled. Everything
else back there is a *surface*; this is the only thing that is a *subject*, and the difference is
what the first composition was still missing — four bands in three ramp families is one room
recoloured three times, and it looked like it. All three are the same archway with different things
inside it, for the reason the four combat bursts are one shape in four pigments: the arch is
architecture the backdrop already has, in the plinth's step and the pillar's drum, and three
unrelated silhouettes would read as three games.

It needed a second grid in `assets/backgrounds/` (ART_SPEC §1), which is the first time that
directory has held two asset classes. That cost one arm in `validate::expected_grid`, one entry in
`PixelSpec.IsLegalGrid`, and turning `PixelSpecSmokeTest`'s hardcoded 64x64 check into a call to it.
It also forced the horizon to become one number for every screen instead of two: a gate is 256
design pixels tall and stands *on* the plinth, so the higher horizon the map and room screens used
cropped its crown off.

**The focal feature is per room, not per act, and that took a second pass too.** It shipped as one
per act, which left a shop, a rest site and a boss fight in the Ember Reach all standing in front of
the same furnace: the act had a place and the rooms inside it did not. The split is that **the arch
is the act's and what stands in it is the room's** — six interiors (`Monument`, `Doorway`, `Hearth`,
`Stall`, `Shrine`, `Strongroom`) across three acts, eighteen pieces from six drawing functions plus
three bespoke monuments. Splitting it the other way would have been the same mistake mirrored: the
frame is where the act's stone, ramp family and lamp live.

Three things about it:

- **A screen names the kind of room it is, never a tile.** `ScreenBackground.BackdropRoom` is an
  enum argument on `AttachRoom`, which is deliberately *not* a violation of the call-site sweep: a
  screen knowing it is a shop is a fact about that screen, where a tile name is a fact about the art,
  and only the second can drift. `ActSmokeTest` drives the enum rather than a list beside it, so a
  seventh room kind fails until its art exists in all three acts.
- **Fire and gold keep their own pigments in every act.** A fire is the same fire in a flooded ward
  and a throne room — these are the two places where a colour is a material rather than a place, and
  the only exception the per-act rule has.
- **Where it sits is per surface, and that was measured rather than guessed.** Centred is right on
  the map and in combat and wrong on all six room screens, every one of which centres its own
  content: the first render showed the arch's crown and none of its interior, i.e. all of the frame
  and none of the half that says which room it is. They place it off-centre now.

**What is deferred, and stated so it is not mistaken for finished:** the shop is the one room screen
with no free canvas — its card row and relic grid together span nearly the full width, so its stall
is still mostly behind them. Fixing that is a shop-layout change, not a backdrop one. Depth is also
still two haze layers rather than anything that moves with the content, because nothing in this game
has a camera to move against.

## Out of scope, so it is not re-argued

The tutorial set is written for platformers and top-down action games, and a good half of it does not
apply:

- **Walk / run cycles, 4-leg walk, jump, wall-slide, slide-roll.** The game has no locomotion.
  Combatants stand in place and the map is a node graph, not traversable space.
- **Tiles, top-down houses, isometric, level progression.** No tilemap, no traversable world.
- **Portraits.** The 32x32 creature sprite is the whole character presentation; a portrait layer
  would be a second art medium for the same subject.
- **Resizing.** §2 forbids non-integer scaling outright, which is the problem that tutorial solves.
