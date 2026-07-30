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
| Chrome 9-slices (frames, bezels, buttons) | **16x16** or **24x24** | Corners must be ≤ 1/3 of the slice |

`emberlord_vashk.png` is the only current 32x48 sprite; everything else is already 32x32 and
conforms.

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

- Frames, bezels, buttons and panels are `StyleBoxTexture` 9-slices over pixel border art.
  `ChromeStyles.EndTurnButtonStyle` already uses this pattern — follow it.
- "Glow" (rare cards, boss nodes, target lock) is an **animated pixel border** — a 1px ring that
  cycles through `G3 → G4 → G5` — not a blur.
- Drop shadows are a hard 1px or 2px offset in `N0`, never a gradient.

## 7. Type

Two bitmap faces, split by job. Sizes are tied to each face's design size, because a bitmap glyph
rendered away from its design size resamples and grows uneven stems.

| Face | Job | Design size | Legal sizes |
| --- | --- | --- | --- |
| **Silkscreen Bold** | Display — titles, buttons, card/enemy names, HP, energy, damage numbers | 8px | 8, 16, 24, 32 (exact multiples) |
| **Jersey 15** | Body — card rules text, descriptions, general UI | 15px | 16 primary; 14 and 12 as the card auto-fit fallback band |

Silkscreen has no lowercase and is very wide — right for short strings, wrong for sentences.
Jersey 15 has proper lowercase and descenders and is narrow enough for card text.

The display face gets exact multiples of 8. The body face gets a **narrow band** around its 15px
design size rather than exact multiples, because card rules text varies enough in length that a
single size cannot fit every card, and the alternative (dropping from 16 straight to 8) is
unreadable on a 1152x648 canvas. Three steps, 16 → 14 → 12, is the compromise; below 12 Jersey 15
loses stem consistency and the card should be re-laid-out instead.

Import settings for both: `antialiasing=0`, `hinting=0`, `subpixel_positioning=0`,
`keep_rounding_remainders=false`, `oversampling=1.0`. Any of those left at the engine default
resamples the glyph off the pixel grid.

### Rejected candidates, so this isn't re-tried

- **Pixelify Sans** — at 16px its `2`, `3`, `5` and `8` are mutually ambiguous. `HP: 21/50` read as
  `81/50`; `Deal 12 damage` read as `Deal 13`. Disqualifying for a game made of numbers.
- **Tiny5** — legible and unambiguous, but ~15% wider than Jersey 15 on the same string, which is
  real estate a card does not have.

### The cost

This replaces Cinzel and IM Fell English. That is a real loss — the illuminated-manuscript feel was
the most distinctive thing about the game's look — and the plan is to recover that identity through
palette and ornament (heavy oxblood/bronze, ornate pixel borders, drop caps) rather than typeface.
See `ROADMAP.md`, "Known cost, accepted".

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
are the bitmap pair, and two things that keep the two halves honest: that `artgen`'s `palette.rs`
still matches `PixelSpec.Ramp` entry-for-entry, and that every icon filename is a live definition
id (in both directions).

Fixing a violation is usually `artgen clamp`, which snaps colours onto the ramp and hardens alpha.
Both of its passes are idempotent, so it is safe to re-run over the whole tree after a palette
edit.
