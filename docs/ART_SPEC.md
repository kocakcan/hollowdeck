# Hollowdeck Art Spec

Hollowdeck is **pixel art, one medium, no exceptions**. This file is the rule set;
`scripts/ui/PixelSpec.cs` holds the same values in code, and `tools/artgen` validates assets
against them. If a rule here and the code disagree, the code is the bug.

The point of writing this down is that the project previously accumulated *three* palettes
(`hollowdeck_theme.tres` slate-blue, `combat_theme.tres` parchment, `UiTheme.Palette` bronze) with
nothing to stop a fourth. Consistency as taste drifts. Consistency as a spec can be asserted in a
smoke test.

## 1. Grids

Every asset is authored on one of these grids. No in-between sizes.

| Asset class | Grid | Notes |
| --- | --- | --- |
| Creatures (enemies, player) | **32x32** | Bosses may use 32x48 when the silhouette needs the height |
| Background tiles | **64x64** | Must tile seamlessly. Six per act: three floors, a wall, a plinth, a pillar |
| Backdrop features | **256x128** | In `assets/backgrounds/`. Placed once, never tiled — one per act |
| Icons (card, relic, potion, map, status, intent) | **32x32** | |
| Combat effect frames | **32x32** | In `assets/icons/fx/`. A creature's grid, because an effect lands *on* a creature |
| Chrome 9-slices (frames, bezels, buttons) | **16x16** or **24x24** | In `assets/theme/`. Corners must be ≤ 1/3 of the slice |

`emberlord_vashk.png` is the only current 32x48 sprite; everything else is already 32x32 and
conforms.

Which of the two chrome sizes a slice takes is decided by the *smallest box that draws it*, not by
how much room the art wants: a `StyleBoxTexture` whose box is shorter than twice its texture margin
folds its own corners into each other. Panels and HUD slots are 16 because `ScreenChrome`'s HP and
gold panels sit at a 4px vertical content margin; buttons and the art plinth are 24 because none of
them is under 28px tall.

## 2. Scaling — integer factors only

A pixel asset drawn at a non-integer scale produces uneven pixel widths, which is the single most
obvious way pixel art looks broken. **Any non-integer scale is a bug**, not a judgement call.

| Context | Factor | Result |
| --- | --- | --- |
| Enemy / player sprite in combat | **5x** | 160x160 (tall bosses 160x240) |
| Card art icon | **2x** | 64x64 |
| Relic / potion / status / intent icon in HUD | **1x** | 32x32 |
| Map node icon | **1x** | 32x32 |
| Background tile | **2x** | 128x128 tiled |

These factors are what the game actually renders, not an aspiration — 5x for
creatures is what `EnemyView.tscn` already used, and the player sprite was
corrected from 180px (5.625x, a real violation that made the player subtly
softer than everything it fought) to match.

Use `PixelSpec.SnapScale()` rather than writing a float scale anywhere. Never size a pixel
`TextureRect` by anchors or `SizeFlags.Expand` — that produces whatever fractional scale the layout
happens to land on. Set an explicit `CustomMinimumSize` of `grid × factor`.

## 3. Filtering

`TextureFilter = Nearest` on **every** pixel asset, everywhere. The default (`Linear`) blurs pixel
art into mush at any scale.

`ScreenBackground.cs` already does this for background tiles and documents why; that rule now
applies to sprites, icons, chrome and particles alike — **including the dust motes**, which were a
`CpuParticles2D` drawing a smooth 8x8 radial `GradientTexture2D` at `ScaleAmountMin 0.4 / Max 1.1`
until §12 landed. That is the same node type and the same three violations as the hit spark §5's
combat frames retired, and it survived the same way: a `CpuParticles2D` is not a `TextureRect`, so
the transform scan skips it, and its texture is not a file under `assets/`, so `artgen validate`
never reads it. Use `PixelSpec.ApplyPixelFilter()`.

The one deliberate exception: smooth procedural gradients that are *not* pixel art — the vignette
and ground-plane in `ScreenBackground` — stay on `Linear`, because `Nearest` would band them. They
are lighting, not art.

## 4. Resolution architecture

**Stay on `canvas_items` stretch at 1152x648. Do not move to `stretch/mode="viewport"`.**

The conventional pixel-art setup is a low base resolution (480x270) integer-scaled to the window.
That is the right choice for most pixel games and the wrong one here: Hollowdeck is a *card* game.
Cards carry two to three lines of rules text each, and a hand holds up to ten of them. At 480x270 a
single card would occupy roughly a third of the screen and its text would be illegible.

So crispness comes from **discipline, not from dropping the canvas**: pixel assets at integer
scales with nearest filtering, on a high-resolution canvas, with a bitmap font at integer sizes.
This is the "HD pixel art" arrangement — pixel-perfect art, legible UI.

Practical consequence: the UI layer works in 1152x648 space and may use any position, but anything
*textured with a pixel asset* obeys §2 and §3.

**And `stretch/aspect="keep"`, so that space is 1152x648 at every window size.** `expand` was the
setting until the playtest that produced this paragraph: it grows the canvas along the window's long
axis (a 1470x956 window yields 1152x749), and since every screen positions against the design size —
`MapScreen`'s `DesignHeight`, `CombatScreen.tscn`'s `HandArea`/`EnemyRow` offsets, `ScreenChrome`'s
`DesignWidth` — the extra 101px was simply dead. It is invisible at any 16:9 size, which is why it
survived to a playthrough. Letterboxing trades bars on an odd-shaped window for every fixed offset
in the codebase being right by construction; making the screens genuinely responsive is the
alternative, and a much larger change. `PixelSpecSmokeTest.TestCanvasIsLetterboxedNotExpanded` pins
both halves.

## 5. Palette

One shared ramp, 43 colours, seven hue families. Every asset must reduce to these colours;
`tools/artgen` clamps and validates. Ramps are hue-shifted (shadows toward blue/violet, highlights
toward yellow) rather than straight lightness ramps, which is what keeps pixel art from looking
flat.

The ramp is anchored on the existing `UiTheme.Palette` values so the game's warm bronze/oxblood
identity survives the medium change — the marked entries are the current colours, kept exactly.

### Neutral — stone, bone, parchment (9)

```
N0 #07060a   N1 #12100f   N2 #1e1a18   N3 #2e2724   N4 #453b35
N5 #665950   N6 #8c7d70   N7 #c4b5a4   N8 #ede4d4
```

### Bronze / gold — chrome, rare, currency (6)

```
G0 #3d2a12   G1 #6b4a1f   G2 #9c6f2e   G3 #c79e47*  G4 #ebc766*  G5 #fbeaa8
```
`*` `AccentGold`, `AccentGoldBright`

### Oxblood / red — attack, damage, danger (6)

```
R0 #240d10   R1 #3f1418   R2 #522121*  R3 #8c2b2b   R4 #c93f3f   R5 #ff5959*
```
`*` `AttackFill`, `Damage`

### Verdigris / green — skill, heal, upgrade (6)

```
V0 #0d1f1a   V1 #1f4238*  V2 #2f6b52   V3 #4a9c68   V4 #73d973*  V5 #a8f0a0
```
`*` `SkillFill`, `UpgradeAccent`

### Steel / blue — block, uncommon (6)

```
B0 #0f1626   B1 #1e2c47   B2 #35496e   B3 #598cd9*  B4 #8cbfff*  B5 #c4dfff
```
`*` `RarityUncommon`, `Block`

### Ember / orange — exhaust, act II (5)

```
E0 #3d1a08   E1 #6b2f0f   E2 #a85420   E3 #f2a64d*  E4 #ffd9a0
```
`*` `ExhaustAccent`

### Void / violet — act III, arcane (5)

```
P0 #180d24   P1 #2e1a42   P2 #4d2e6b   P3 #7a52a3   P4 #b08cd9
```

### Semantic assignments

| Meaning | Colour |
| --- | --- |
| Attack card fill / border | `R2` / rarity |
| Skill card fill | `V1` |
| Power card fill *(when added)* | `P1` |
| Rarity Common / Uncommon / Rare | `N6` / `B3` / `G3` |
| Upgraded card accent | `V4` |
| Exhaust accent | `E3` |
| Damage / heal / block numbers | `R5` / `V4` / `B4` |
| Buff / debuff status framing | `V3` / `R4` |
| Panel background / bezel | `N1` / `G2` |
| Act I / II / III tint | `B1` / `E1` / `P1` |

## 6. Chrome

No `corner_radius` and no `ShadowSize` blur anywhere. Both are anti-aliased effects that cannot be
expressed in pixels.

- Frames, bezels, buttons and panels are `StyleBoxTexture` 9-slices over pixel border art, generated
  by `artgen`'s `chrome` category into `assets/theme/`. Two properties of every such box are
  correctness rather than taste, and both are wrong by default:

  - **Edges tile, they never stretch.** `AxisStretchMode.Stretch` is what a `StyleBoxTexture` is born
    with, and it resamples the edge strip to whatever fractional width the box happens to be — §2's
    "a bug, not a judgement call", reached by doing nothing at all. `TileFit` is the same violation
    from the other side, since it rescales tiles to fit a whole number of them. Only `Tile` keeps
    every edge pixel at 1:1. The art is drawn so each edge pixel is a function of one axis alone, so
    the tile seam is invisible at any size.
  - **The corner is at most a third of the slice** — §1's rule, which nothing checked until this
    landed. It is a property of the StyleBox rather than of the PNG, so `artgen validate` cannot see
    it: it reads pixels, not texture margins.

  `PixelSpecSmokeTest` asserts both over every chrome box the game builds, walking the `ChromeStyles`
  producers *and* the theme resource, since those are two independent consumers of the same art.

  **A box gets a 9-slice iff its colours are fixed at author time.** A `StyleBoxTexture` has no
  `BorderColor`, and its one runtime colour knob (`ModulateColor`) multiplies, which lands off the §5
  ramp. So `PanelStyle`, `SlotStyle`, `PlinthStyle`, the emphasis button and the theme's ordinary
  buttons are art; `CardFrameStyle` — whose border is a rarity lerped with upgraded and again with
  hover — stays a `StyleBoxFlat`, along with the badges, the HP bars and the slider. That is a rule,
  not an unfinished migration.

  The three glow surfaces below are the sharpest case of it: their border colour is decided *per
  frame*, so they are the furthest thing there is from fixed at author time, and art was never
  available to them.
- "Glow" (rare cards, boss nodes, target lock) is an **animated pixel border**, not a blur:
  `scripts/ui/GlowRing.cs`, a `Node` parented to the Control it drives, stepping that box's
  `BorderColor` through a ramp triple on a `_Process` timer. Five things about it are rules rather
  than settings:

  - **It steps, it never interpolates**, and that is the whole reason it is a frame timer rather
    than the obvious `TweenProperty` on `border_color`. A tween passes through every colour between
    `G3` and `G4`; §5 admits 43. Stepping between authored entries is on-ramp by construction — the
    same argument §9 makes for frame swaps over scale tweens, one property over.
  - **The triple is the caller's, because a ring is a mechanism and not a colour.** `GlowRing.Gold`
    (`G3 → G4 → G5 → G4`) is the instance this bullet used to name, and it drives rare cards and the
    target lock. The boss node takes `GlowRing.Danger` (`R3 → R4 → R5 → R4`): `BossNodeGlowStyle` is
    keyed to the Damage semantic on purpose, and gold there would say "rare" on the one node whose
    meaning is danger.
  - **Ping-pong, not sawtooth.** Coming back down through the middle entry reads as a pulse; jumping
    two ramp steps in one frame reads as a flicker.
  - **`Attach` opens on the peak.** The brightest entry of each triple is exactly the static colour
    that ring replaced (`G5`, `G5`, `R5`), so the rest state is byte-identical to what shipped
    before the feature and the ring never opens dimmer than the still it took over. Six `ScreenShot`
    fixtures render these screens and capture immediately, which is the other half of why the
    opening frame is fixed rather than scattered.
  - **Not gated on `ReduceMotion`**, per the rule §9's driver states: gate the photosensitive thing,
    not the gentle ambient loop. A two-step walk up a hue-shifted ramp is gentler than
    `MapScreen.BuildCurrentNodeRing`'s ungated `modulate:a` pulse sitting on the same screen, and
    gating one of those two and not the other is what would read as broken.

  Note the width is unchanged. This bullet said "a 1px ring" when the thing it was arguing against
  was a 6px blur; all three boxes keep `BorderWidth.Thick`, and on a card that weight is a
  deliberate second emphasis channel beside the colour.

  `PixelSpecSmokeTest` drives the ring's `_Process` directly and reads the colour back off the
  installed `StyleBox` — never off the cycle array, since what can go wrong is the builder in
  between. It also asserts each of the three surfaces actually *attaches* one, because every check
  on the driver stays green while the driver is connected to nothing.
- Drop shadows are a hard 1px or 2px offset in `N0`, never a gradient.

There is no unbuilt bullet left in this section, and the discipline that got it there is worth more
than the section. A spec that describes an intention in the present tense is indistinguishable from
one describing the code, and the next reader inherits a claim they have no reason to check — which
is how the animation rule in §9 went three phases without enforcement, how the first bullet above
spent three phases pointing at `ChromeStyles.EndTurnButtonStyle` as an example to follow after that
function had been deleted, and how the glow spent three phases as a still.

## 7. Type

Two bitmap faces, split by job. Sizes are tied to each face's **design em**, because a bitmap
glyph rendered away from it resamples and grows uneven stems.

| Face | Job | Design em | Legal sizes |
| --- | --- | --- | --- |
| **Silkscreen Bold** | Display — titles, buttons, card/enemy names, HP, energy, damage numbers | 8px | 8, 16, 24, 32 |
| **Tiny5** | Body — card rules text, descriptions, general UI | 8px | 16 everywhere, 24 for hover tooltips (8 is unreadable; 24 is too big for a card, but a tooltip is not in a 152x88 box) |

Both faces share an 8px em, so the legal set is one list: **exact multiples of 8, no exceptions
and no fallback band.** Silkscreen has no lowercase and is very wide — right for short strings,
wrong for sentences. Tiny5 has proper lowercase and descenders and unambiguous digits.

"Design em" means the number Godot's `font_size` sets, which is the em box in pixels — **not** the
cap height, and not whatever number is in the font's name. Get it with:

```bash
tools/font-grid.py assets/fonts/*.ttf
```

Run that before adopting any face. If `design em` comes back as `none`, it is not a pixel font.

Import settings for both: `antialiasing=0`, `hinting=0`, `subpixel_positioning=0`,
`keep_rounding_remainders=false`, `oversampling=1.0`. Any of those left at the engine default
resamples the glyph off the pixel grid.

### Rejected candidates, so this isn't re-tried

- **Pixelify Sans** — at 16px its `2`, `3`, `5` and `8` are mutually ambiguous. `HP: 21/50` read as
  `81/50`; `Deal 12 damage` read as `Deal 13`. Disqualifying for a game made of numbers.
- **Jersey 15** — shipped as the body face for three phases and had the *same* disease, which this
  document caused by recording its design size as 15px. That 15 is its **cap height**; its em is
  **27px**. Rendered at 16 it got 0.59 device pixels per design pixel and the rasterizer dropped
  ~40% of every stem, so `Deal 6 damage` rendered as `Deal 8 damage`. Its smallest crisp size has a
  15px cap height, far too big for a 176x240 card, so the face cannot be rescued by re-sizing. The
  rest of the family doesn't help either — measured, Jersey 10/20/25 are 56/34/41px ems.
- **Micro 5** — an 11px em, so it is only crisp at 11, 22, 33. Nothing in the UI wants those.

The "narrow band" of 16 → 14 → 12 that used to live here was a workaround for Jersey 15 having no
usable legal size at all. It is gone: **every card in the data fits its 152x88 description box at
16**, asserted by `HandLayoutSmokeTest`. Content that overflows should be shortened or given a
bigger box — collapsing repeated effect text ("Deal 4 damage twice.", "ALL enemies:" hoisted to a
prefix) is what bought the room, and is the move to reach for first.

### The cost

This replaces Cinzel and IM Fell English. That is a real loss — the illuminated-manuscript feel was
the most distinctive thing about the game's look — and the plan is to recover that identity through
palette and ornament (heavy oxblood/bronze, ornate pixel borders, drop caps) rather than typeface.
See `ROADMAP.md`, "Known cost, accepted".

The ornate-pixel-borders half of that is now paid: §6's 9-slices are the ornament, and the emphasis
button is where it shows most, having carried the loss as bare border weight for three phases. Drop
caps are still owed.

## 8. What gets enforced automatically

`tools/artgen validate` runs from `tools/run-smoke-tests.sh` ahead of the engine suites, and fails
the build on:

- an asset whose dimensions are not a legal grid from §1
- a colour outside the §5 ramp
- a partially-transparent pixel — a soft mask edge is anti-aliasing by another name, and under
  Nearest it shows as a halo of half-lit pixels around the silhouette (§3)
- an SVG anywhere under `assets/`

It reads the raw PNG bytes, which is why this half lives outside the engine: `GD.Load` hands back
an already-imported texture, not the file.

`tools/run-smoke-tests.sh` also runs **`cargo test`** over `tools/artgen` immediately before
`validate` — the generator's own rules, checked before the output is. Those are §10's light
direction, and they are separate from `validate` for a structural reason rather than a tidiness one:
a highlight on the wrong side of a blade is on the ramp, on the grid and hard-alpha, so every rule
`validate` has passes it. Some rules can only be asserted about the code that draws, not about what
it drew.

`PixelSpecSmokeTest` asserts the runtime half — that every sprite and icon site sets `Nearest` and
an integer scale, that every asset is on a legal grid, that no SVG survives, that the fonts in use
are the bitmap pair, the two §6 chrome rules above, and two things that keep the two halves honest:
that `artgen`'s `palette.rs` still matches `PixelSpec.Ramp` entry-for-entry, and that every icon
filename is a live definition id (in both directions) — cards, relics, potions and events.

Chrome gets that same bidirectional treatment, and it needs it more than the icons do. Three sets
have to agree: the PNGs in `assets/theme/`, the names in `ChromeStyles.Slices`, and the textures
something actually draws. A missing icon degrades a view to text; a missing chrome slice makes a
`StyleBoxTexture` draw **nothing at all**, so the panel leaves the interface rather than looking
unfinished. The "something actually draws" set is collected by driving the real producers and reading
the theme back, never from a list of textures — a list only knows the names someone already thought
of, which is the lesson `TestNoTweenTransformsAPixelSprite` was rewritten over.

The combat effect bursts get the same bidirectional treatment plus a third set, for the reason
chrome needs one: `CombatFx.All`, the frame runs in `assets/icons/fx/`, and the effects some call
site actually *spawns*. The third is the one that earns its keep — an effect authored, drawn,
generated and never played is four files of dead art that `artgen validate` reports as conforming.
It is collected by scanning `scripts/ui` for the constant, **skipping comment lines**, because the
first version counted the paragraph explaining when an effect fires as a call site and could not
fail. Beside it, `TestACombatEffectAdvancesAndFrees` drives the animator directly: frames on disk
and a driver that never advances them look identical to every other check here, and the burst also
has to *end* — its budget is asserted against a real 60fps tick rather than against its own frame
time, since a check that steps by the constant it is testing cannot observe that constant at all.

Events are the one category where the *missing* direction is survivable: `ArtAssets.EventIcon`
falls back to the map's scroll, so an event authored without art still renders a screen with a
subject. The check is there for the orphan direction, where a renamed id would drop back to the
generic art silently and forever with a stale file sitting beside it. Event icons are also the
only ones never drawn below 5x — `EventScreen` shows them at `SpriteScale` as its focal art —
which is why they carry more structure than the 1x-budgeted HUD set.

§11's motion vocabulary is checked the same three ways, and the third is the one that matters: a
forward scan for a hand-built tween, plus two reflection sweeps asserting that every declared curve
is in `Motion.All` and that something actually calls it. The forward scan alone stays green over a
vocabulary nobody uses, which is the same orphan direction the event icons and the combat bursts
each needed a separate set for.

It also scans the theme, every `.tscn` and every `scripts/ui/*.cs` for a rendered font size and
fails any that is not a multiple of §7's 8px em. That check exists because the sizes that drifted
off-grid were all local `AddThemeFontSizeOverride` calls and `.tscn` overrides — none of which
went anywhere near a constant a test was watching, which is how the body face rendered mangled
text for three phases with the whole suite green.

Fixing a violation is usually `artgen clamp`, which snaps colours onto the ramp and hardens alpha.
Both of its passes are idempotent, so it is safe to re-run over the whole tree after a palette
edit.

## 9. Animation

**A pixel sprite animates by swapping frames, never by transforming the node.** §2 already forbids a
non-integer scale; a tween that *passes through* one is the same violation spread over time, and it
is the form that actually shipped. `EnemyView` tweened its sprite's `Scale` to 1.04, 1.08 and 1.15
and its `RotationDegrees` to 6, `CombatScreen` slid the player 6px at a 5x render scale, and the
intent icon popped from 1.5x — all of it under this document's own "any non-integer scale is a bug,
not a judgement call", for three phases, with every suite green. `PixelSpecSmokeTest` only ever read
the *static* `CustomMinimumSize` out of the `.tscn`, so the rule was enforced at rest and broken in
motion.

The rules:

- **Frames come from `artgen`, and there are two kinds.** *Derived* frames come from
  `artgen animate` (`tools/artgen/src/anim.rs`), which takes the sourced 32x32 creature art and
  applies integer pixel moves and palette substitutions only, writing one PNG per frame into
  `assets/sprites/anim/<id>/<clip>_<n>.png`. *Authored* frames come from plain `artgen generate` —
  today the combat effect bursts in `tools/artgen/src/icons/fx.rs`, drawn from nothing and written
  to `assets/icons/fx/<effect>_<n>.png`. Both are played by `SpriteAnimator` setting
  `TextureRect.Texture`, and the distinction is only about where the pixels come from: an effect has
  no source tile to displace, so there is nothing for `animate` to derive it from.

  This bullet said "derives" flat for as long as frame animation has existed, which was true when
  creatures were the only thing animating. It is recorded here rather than quietly rewritten because
  the same drift is what §1, §2 and §3 of `PIXEL_ART_ROADMAP.md` each cost three phases: a document
  describing in the present tense a rule the code has since outgrown reads exactly like a document
  describing one it never met.
- **The only node property a pixel asset may be tweened on is alpha.** `modulate` resamples nothing.
  `scale`, `rotation`, `rotation_degrees` and `skew` all do.
- **A translation must land on a whole source pixel** — a multiple of the asset's render factor from
  §2, through `PixelSpec.SnapTranslation`. A lunge that genuinely travels is allowed to interpolate
  between snapped endpoints, because the motion is fast and the alternative is no travel at all; a
  slow idle loop sitting on a fractional offset is not, and that is what the player bob was.

  It applies to a node *placed* as much as to one tweened, which is easy to read past because the
  rule arrived from a tween. A combat effect is positioned from a `Control`'s `GlobalPosition` plus
  half its `Size` — fractional far more often than not — and sits there for four frames, which is a
  longer look at an off-grid pixel asset than any lunge gives.

  **And it applies to a node's parents, which snapping the node cannot reach.** `CombatScreen`'s
  screen shake moves the scene root, so every pixel asset on the screen rides its offset; a burst
  whose own position is snapped still resamples if the screen under it is at x = 1.68. The shake
  therefore snaps its waypoints too, at scale **1** rather than at the asset's factor — a
  Nearest-filtered texture translated by a whole number of canvas pixels samples exactly at any
  integer scale. Worth stating because no assertion can see it: a check on a burst parents it to a
  Control that never moves, and there is nowhere the accumulated transform of an arbitrary ancestor
  chain is available to assert about.
- **Deliberately not `AnimatedSprite2D`.** The creature sprites are `TextureRect`s whose
  `CustomMinimumSize` is what §2's assertion reads, and `EnemyView`'s is inside a `VBoxContainer`. A
  `Node2D` there breaks Control layout and that assertion together.

The container point is worth keeping, because it is what produced the original bug: a `Container`
owns its children's position and size, so the old code reached for `Scale` as "the one transform a
container does not touch". A frame swap is not a transform at all, so the question never arises —
which is also what lets an escaping enemy finally *travel* rather than being squeezed sideways.

## 10. Light

**One lamp, up and to the left, at 45°, for every icon in the set.**
`tools/artgen/src/icons/light.rs` is where that becomes a number. Canvas y grows downward, so the
direction the light *travels* is `(+1/√2, +1/√2)` and a face is lit when its outward normal turns
back against it. Getting that sign backwards is the one mistake here that nothing else can see:
every shape would stay internally consistent while the whole set lit from the lower right at once.

§5 already committed to hue-shifted ramps, which is the harder half of making pixel art read as
lit. This is the half that was missing, and it was missing rather than wrong: `shapes.rs` gave the
set one *form* vocabulary — one `blade`, one `shield` — and no shared light, so each shape picked
its own lit edge out of a literal and the set came out **consistent in form and contradictory in
lighting**.

What that cost, concretely. `blade` painted its bright edge on the side that was "leading" relative
to the blade's own rotation, which is a different *screen* side at every angle; two thirds of the
authored blades happened to point somewhere that made it right. The three that did not were
`pommel_strike`, `cataclysm`, and `annihilate` — whose crossed pair came out with one blade lit
from the upper left and the other from the lower right, **two suns inside one 32x32 square**. And
`shield`, the second most-quoted form in the game at twenty-six call sites, drew its rim in one flat
colour all the way round: not lit wrongly, but not lit at all.

Upper-left is ratified rather than chosen. `strike` — the icon the whole Attack half of the set
quotes — was already lit that way, and its bytes are **unchanged** by the derived rule; so were
`flask`, `droplet`, `raised_fist` and most of the hand-placed highlights across the category
modules. A rule that agrees with the existing majority costs three blade flips instead of thirty-six.

### The division of labour

**`light.rs` owns which side is lit; the shape owns how much material that side shows.** A bright
rim wants to be wide where the light lands (`shield`); a dark body wants to be wide where it does
not (`gem`). Those are the same rule with different amounts, they go through the same `by_face`
call with the arguments swapped, and only the amounts are art.

Colour parameters — `edge`, `lit`, `highlight`, `shade` — stay the caller's, because a material's
pigment is a content decision. **Where those colours land is not a parameter and must never become
one.** That is the point of the whole item: a wrong-side highlight is now *unrepresentable* in the
vocabulary rather than discouraged, the same move the game makes deriving `IsPlayable` from
`CardType` instead of authoring a sixth bool.

Nothing here lightens or darkens arithmetically; §5's ramp is why. The tempting generalisation — a
pass that walks a finished silhouette and brightens its up-left boundary one ramp step — was
considered and declined in `light.rs`'s header, because it needs a "next entry in this family"
relation `palette.rs` does not have and its failure mode is silent at the top of every family.

### Named exceptions

Three classes, and every shape in `shapes.rs` states its own in its doc comment:

- **Directional** — `blade`, `sword`, `shield`, `droplet`, `flask`, `gem`, `scale`, `skull`,
  `raised_fist`.
- **Emissive** — `flame`, `orb`, `sparkle`, and the whole of `icons/fx.rs`. They *are* the light,
  locally; giving one a lit side would say it is lit by something else.
- **Symmetric** — `eye` (an aperture is a hole, and lighting one side of a hole is a mistake),
  `arrow` and `crack` (marks at 1–3px, no volume), `barb` (the tip is bright because it is thin, and
  `misc.rs` mirrors barbs in pairs).

**Chrome is exempt as a class.** The fourteen 9-slices stay 4-fold symmetric. A frame is inlay
rather than lit form, and bevelling it would trade §7's hard-won ornament for a raised-panel look;
`assets/theme/` showing no diff after a regeneration is the check that the exemption held.

Two smaller exemptions are by **budget** rather than by physics, and they are worth knowing because
they look like oversights: `sword`'s guard and pommel, and all four knuckles of `raised_fist`.
Dropping the furthest knuckle to the body colour is what the light says, and it was tried — at 1x
the fourth finger vanished and the fist read as having three. §1's legibility budget outranks this
section when they disagree.

### What is enforced, and what is not

Enforced: `cargo test --manifest-path tools/artgen/Cargo.toml`, run from `tools/run-smoke-tests.sh`
ahead of the engine suites and again as its own CI step. It sweeps `blade` over 32 angles rather
than checking the authored call sites — the call sites only cover the angles somebody already
thought of — and drives every other directional shape alone on a blank canvas.

Two things about those assertions are load-bearing and were both wrong once:

- **`light.rs`'s `GRAZING` is a float-equality epsilon, not a tolerance.** At a true tie both faces
  are edge-on and the answer is arbitrary; a band wide enough to contain a *real* answer lets the
  tiebreak override the light. At `0.05` — a ~3° wedge — it moved `wild_swing`'s and `map/elite`'s
  bright edges onto their shadow face while `map/fight`, the same crossed swords in another colour,
  fell outside the band and kept its lit one. A test pins the epsilon under 0.01 rather than
  trusting that sentence.
- **Each shape's assertion measures the pixels its light pass writes**, not the shape's bright mass.
  Four of the six were green on the previous code because a whole-colour centroid over a shape that
  already had shading measures the shading it already had.

**Not enforced, and this paragraph is the point of the section:** every hand-placed highlight in
`cards.rs`, `relics.rs`, `misc.rs`, `events.rs` and `potions.rs`. Those were audited once, by
reading all ~186 of them and looking at the output; nothing holds them there. A new icon can put a
`G5` pixel in its bottom-right corner and the whole suite stays green.

`artgen validate` is *structurally* blind here and no amount of work on it would help: a highlight
on the wrong side is still on the ramp, still 32x32 and still hard-alpha, so it passes every rule
that command has. A metric over finished icons was prototyped and not shipped — it cannot separate a
highlight from a *material* use of the same ramp entry (`BLADE_EDGE` is `N8`, an entire shield rim
is `B4`, `sparkle` is nothing but its top entry), so it ranked the emissive shapes worst precisely
because they were correct. The same centroid measurement is exact over one shape drawn alone with
colours the test picks, which is why it lives in the test and not in a command.

The audit's honest finding is worth recording, because it is not what the roadmap forecast: the
hand-drawn half was **already overwhelmingly up-left**, and the large majority of highlight-coloured
draw calls turned out to be emblems, marks and materials rather than shading — a crown, a cross, a
page block, a pale mask behind bars. The real offenders were few.

## 11. Motion

**A tween takes its duration, its transition and its ease from one named curve, and picks none of
the three itself.** `scripts/ui/Motion.cs` holds eight of them; `Tween.TweenTo` and
`Tween.TweenPingPong` are the only two ways a property is animated, and `Tween.Wait` the only way
a delay is held. §9 already governs *what* a pixel asset may be tweened on — this section governs
*how* anything is.

The eight, in period order:

| Curve | Period | Shape | What it is for |
| --- | --- | --- | --- |
| `Jolt` | 0.03s | Linear / Out | one step of a shake; a jitter that eases is a wobble |
| `Flash` | 0.06s | Sine / Out | the in-half of an impact tint |
| `Snap` | 0.12s | Sine / Out | an immediate answer to input — hover, the out-half of a flash |
| `Pop` | 0.18s | Back / Out | a small overshoot: something leaving with a kick, a number punching in |
| `Settle` | 0.20s | Sine / Out | a value arriving at a new resting place; the default |
| `Land` | 0.28s | Back / Out | something arriving *with weight* — a dealt card, a relic onto its plinth |
| `Fade` | 0.35s | Sine / **InOut** | an alpha ramp long enough to read, in either direction |
| `Drift` | 1.40s | Sine / **InOut** | the half-period of an ambient loop |

Five things about it are load-bearing:

- **The period is negotiable and the shape is not.** `MotionCurve.Over(seconds)` returns the same
  curve over a different period, and that is the whole escape hatch. It exists because a loop's
  half-period is genuinely a per-site fact — a fog bank wanders for 14 seconds, a map ring pulses for
  0.7, an exhausting card is faster than a discarded one *because* it is exhausting — while the shape
  those run on is not. A site that needs its own number still cannot invent its own easing.
- **`Fade` and `Drift` are the two symmetric curves, and neither is a style choice.** Every other
  curve is `Out`, because a thing *arriving* decelerates into its rest. Those two are the cases where
  "arriving" is the wrong verb. A loop does not arrive anywhere, and an `Out` ease on a ping-pong
  snaps at both turns — a stutter rather than breathing; `TweenPingPong` exists so the six ambient
  loops cannot disagree about the period of their halves. And a thing *disappearing* does not arrive
  either: `sine::out` on a fade to zero spends most of its opacity in the first third, so the subject
  is largely gone before the beat it is meant to occupy. That one is measured rather than argued —
  `EnemyView`'s death clip is three frames at 0.12 against a 0.35s fade, and `Out` puts frame 1 at
  alpha 0.49 where the clip was authored against 0.74. **This is the one curve in the table that
  shipped wrong and was corrected in review**, and the tell was the table's own words: an `Out` ease
  cannot be "long enough to read, in either direction", because it is asymmetric by construction.
- **`Back` never lands on alpha.** Back overshoots *past* its destination, and past `modulate:a = 0`
  or `1` there is nowhere to overshoot to — the value clamps, so the property simply arrives early and
  waits. Four sites animate a transform and an alpha together and each pairs `Land`/`Pop` with
  `Settle`/`Fade` for exactly this reason.
- **`Motion.Seconds` is the one place a period becomes a number**, which is where ROADMAP Phase 11's
  animation-speed setting multiplies. It is a function rather than a `Scale` field sitting at 1f,
  because a field nothing writes is dead state no assertion can see. Note that the setting cannot be
  `Engine.TimeScale` — `CombatScreen`'s hit-stop comment argued that out already, since the same dial
  rescales `CombatManager`'s turn pacing.

  **A bare delay goes through it too, and that is not tidiness.** `TweenInterval` takes a raw double,
  so a hold, a stagger or a lag would sit *inside* an animated sequence and not scale with it. At
  0.5x, a `ChromeStyles` ghost bar whose 0.4s drain scaled and whose 0.15s lag did not would begin
  draining after the real bar had already finished — inverting the readout the lag exists to create.
  `Tween.Wait` is the only sanctioned `TweenInterval` for that reason.
- **The one exemption is a *property*, not a file.** `volume_db` is not motion: nothing on screen
  moves, and an animation-speed dial must not cut a music crossfade short. Exempting `AudioManager.cs`
  wholesale is the version of that sentence which stops being true the moment someone adds a
  mute-indicator flash to it — visible motion, outside the vocabulary and outside the future setting,
  with the exemption's stated reason not applying. The predicate is the reason.

### What is enforced

`PixelSpecSmokeTest` scans **all of `scripts/`** for a bare `TweenProperty`, `TweenInterval`,
`SetTrans` or `SetEase`, and fails any it finds outside `Motion.cs` and any line touching
`volume_db`. Rooted at the whole tree rather than at `ui` and `run`, because the opening sentence of
this section is unqualified and a scan covering two of seven source directories does not back it. A
source scan for the same reason §7's font-size scan and §9's transform scan are ones: nearly every
tween here sits behind a combat event or a `ReduceMotion` branch, so instantiating the screens
reaches almost none of them. Comment lines are skipped, per the rule §8 records — `GlowRing`'s
comment explaining why it is a frame timer *instead* of a `TweenProperty` reads to a regex exactly
like a `TweenProperty`.

**§9's transform scan had to be widened in the same commit, and that is the sharpest thing this
section has to teach.** That guard was keyed to the literal string `TweenProperty(` — every tween in
the project, until this section renamed all thirty-four to `TweenTo(`. Measured: the scan went from
9 matching lines in `CardView.cs` alone to **0**, with all 9 call sites still present and all 23
suites green. A guard keyed to a spelling dies the day the spelling changes, and it dies *silently*,
because a scan that finds nothing is indistinguishable from a codebase with nothing to find. It
matches both spellings now. The general form: **a rename is a change to every regex that names the
old spelling, and no compiler will say so.**

Two sweeps run the other direction, and they are what stop the vocabulary rotting. Reflection over
`Motion`'s own fields asserts that each declared curve is in `Motion.All` (a curve missing from the
registry is invisible to every sweep driven by it — the seam `CombatFx.All` has one asset class
over), that no two curves share all three values (two names for one curve distinguishes nothing),
and that something under `scripts/` actually *uses* each one. That last is the
half that earns its keep: the forward scan stays perfectly green over a vocabulary of thirty curves
nobody calls, and eight is only a useful number while eight is the true one.

**Not enforced:** which curve a site picked. `Land` where `Settle` belonged is a judgement call, and
the check can only see that a curve was used at all — the same shape as §10's unheld hand-placed
highlights, and stated here so it is not mistaken for covered.

## 12. Backdrops

**A backdrop is a room, not a texture.** Four bands, back to front: a wall, a colonnade standing in
front of it, the plinth where the wall meets the ground, and the floor. `scripts/ui/ScreenBackground.cs`
composes them; `tools/artgen/src/icons/backgrounds.rs` draws them.

The rule exists because the alternative was tried twice and failed identically both times. One tile
filling all of 1152x648 is wallpaper *by construction*: repeated 9x5 it has no horizon, so nothing
drawn in front of it acquires a position, and every screen in the game reads as the same flat sheet
in a different colour. That was true of the seven sourced Dungeon Crawl floors this replaced, and it
stayed true when they were swapped one-for-one for generated ones on the same ramp under the same
lamp. **The tiles were never the problem.**

The fifth piece is the one that makes an act a *place* rather than a palette. A **focal feature** —
act I's drowned gate, act II's furnace mouth, act III's throne — is placed once, centred, standing
on the plinth, and it is the only thing back there that is a subject rather than a surface. Depth is
not a subject: three rooms built from the same four bands in three ramp families still read as one
room recoloured, which is exactly how this looked before the gate went in. All three are the same
archway with different things inside it, for the reason the four combat bursts are one shape in four
pigments — three unrelated silhouettes would read as three games.

It is the one asset here on a different grid, and that is the point: 256x128 is placed rather than
repeated, so seamlessness is meaningless for it and size is the whole argument. It is also exempt
from the contrast budget below — it is the subject, it is drawn once, and nothing sits on top of it.

Four things follow, and none is visible from a single tile:

- **Which axes a piece must close is per band.** A floor and a wall tile both ways, a plinth is one
  row so only x, a pillar repeats only up the wall so only y. Asking a piece for a seam it never
  meets is a rule with no failure behind it; not asking is a line down the screen.
- **A pillar is the one piece with transparent pixels, and that is what it is for.** Its flanks read
  through to the wall behind, so a colonnade is a rhythm laid over the masonry rather than a second
  wall with columns painted on it. §3 forbids *partial* alpha, not alpha — these are 0 or 255 like
  everything else. It carries its own cast shadow, opaque, on the flank away from the lamp; without
  one it renders as a stripe rather than as something standing in front of something.
- **Which axes a piece must close is per band, and a focal feature closes none.** Asking a placed
  piece for a seam is a rule with no failure behind it — the same reason a plinth is not asked for a
  vertical one.
- **`sheen` is two ramp steps above `face` and only two pieces may use it** — the plinth's top
  surface and a pillar's lit band, the two surfaces the lamp hits closest to square. Being *found*
  is their whole job. Everything else stays inside `shade`/`face`/`lit`, which is the contrast
  budget a surface with ten cards drawn on top of it gets, and `no_tile_spends_more_of_the_ramp_than_its_band_allows`
  holds both budgets.

Per-act identity lives in the art rather than in a `Modulate`. Each act authors one backdrop set
(`ward`, `reach`, `throne`) drawn in its own ramp family, so the surface tints in `acts.json` are
neutral greys carrying brightness alone — a tint with a hue in it would be tinting twice, and the
second one lands off the ramp.
