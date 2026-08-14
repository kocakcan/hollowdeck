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
| Background tiles | **64x64** | Must tile seamlessly |
| Icons (card, relic, potion, map, status, intent) | **32x32** | |
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
applies to sprites, icons, chrome and particles alike. Use `PixelSpec.ApplyPixelFilter()`.

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
- "Glow" (rare cards, boss nodes, target lock) should be an **animated pixel border** — a 1px ring
  cycling `G3 → G4 → G5` — not a blur. **Not built**: `ChromeStyles.CardFrameStyle` steps the border
  to the brightest gold statically and its comment calls the ring "the eventual treatment".
  `PIXEL_ART_ROADMAP.md` §3. These three surfaces stay flat for a second reason on top of the
  fixed-colour rule above: a glow wants a driver, not a still.
- Drop shadows are a hard 1px or 2px offset in `N0`, never a gradient.

The remaining unbuilt bullet is stated as unbuilt on purpose. A spec that describes an intention in
the present tense is indistinguishable from one describing the code, and the next reader inherits a
claim they have no reason to check — which is how the animation rule in §9 went three phases without
enforcement, and how the first bullet above spent three phases pointing at
`ChromeStyles.EndTurnButtonStyle` as an example to follow after that function had been deleted.

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

Events are the one category where the *missing* direction is survivable: `ArtAssets.EventIcon`
falls back to the map's scroll, so an event authored without art still renders a screen with a
subject. The check is there for the orphan direction, where a renamed id would drop back to the
generic art silently and forever with a stale file sitting beside it. Event icons are also the
only ones never drawn below 5x — `EventScreen` shows them at `SpriteScale` as its focal art —
which is why they carry more structure than the 1x-budgeted HUD set.

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

- **Frames come from `artgen animate`** (`tools/artgen/src/anim.rs`), which derives them from the
  sourced 32x32 art with integer pixel moves and palette substitutions only, and writes one PNG per
  frame into `assets/sprites/anim/<id>/<clip>_<n>.png`. `SpriteAnimator` plays them by setting
  `TextureRect.Texture`.
- **The only node property a pixel asset may be tweened on is alpha.** `modulate` resamples nothing.
  `scale`, `rotation`, `rotation_degrees` and `skew` all do.
- **A translation must land on a whole source pixel** — a multiple of the asset's render factor from
  §2. A lunge that genuinely travels is allowed to interpolate between snapped endpoints, because the
  motion is fast and the alternative is no travel at all; a slow idle loop sitting on a fractional
  offset is not, and that is what the player bob was.
- **Deliberately not `AnimatedSprite2D`.** The creature sprites are `TextureRect`s whose
  `CustomMinimumSize` is what §2's assertion reads, and `EnemyView`'s is inside a `VBoxContainer`. A
  `Node2D` there breaks Control layout and that assertion together.

The container point is worth keeping, because it is what produced the original bug: a `Container`
owns its children's position and size, so the old code reached for `Scale` as "the one transform a
container does not touch". A frame swap is not a transform at all, so the question never arises —
which is also what lets an escaping enemy finally *travel* rather than being squeezed sideways.
