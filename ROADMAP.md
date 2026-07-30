# Hollowdeck Roadmap

## Where things stand

The core loop is complete and playable end-to-end: a branching three-act map, combat with
telegraphed intents, relics, potions, events, a shop and gold economy, mid-run save/resume, and a
score-driven unlock track that persists across runs. All 13 screens are wired to real data. See
`CLAUDE.md` for the architecture and the current content counts.

**The gating problem is no longer content — it is visual coherence.** A previous version of this
roadmap led with card count (30 against a target of 80–120) and treated visuals as a single
"Polish" bullet about scene transitions. Rendering all 13 screens through
`scenes/debug/ScreenShot.tscn` and actually looking at them says otherwise: the game reads as
unfinished for visual reasons, and shipping 90 more cards into the current presentation would not
change that. Content work is still real, and it is still here — it is just correctly sequenced
now, in Phase 6.

### The diagnosis

**Two themes are fighting, and the generic one is winning.** `project.godot` sets
`gui/theme/custom` to `hollowdeck_theme.tres` — a slate-blue, *fontless* generic dark-UI theme
(`bg_color 0.165, 0.184, 0.243`). The atmospheric `combat_theme.tres` is attached to only **3 of 13
scenes** and defines fonts but no button styleboxes. `UiTheme.Palette` is a third palette, warm
bronze (`BgPanel 0.08, 0.06, 0.05`). So Shop, Rest, Event, Treasure, RunEnd, Settings, Unlocks and
Map all render in stock Godot sans-serif with grey-blue buttons, and combat's enemy panels are
slate-blue boxes sitting inches from bronze bezels. `ScreenBackground.Attach` is called from all 11
screens — backgrounds got the art pass, chrome and typography never did.

**Combat doesn't follow the genre's compositional grammar.** Cards are 224x308 on a 1152x648
canvas — 47% of screen height — so the hand physically covers the enemies. In the act-3 boss
screenshot, a five-card hand occludes nearly all of The Hollow Throne. Enemies sit inside
rectangular panels instead of standing free on the backdrop. Energy is three small pips plus "3/3"
in text. The four pile buttons consume the entire top-right corner. The top-left HP panel renders
as a bordered box containing a heart icon and no value.

**The assets are three media stapled together** — flat white vector icons, pixel-art sprites, and
high-res serif type, none of which share a palette, a density, or an outline treatment.

## The decision: pixel art as the single medium

This is not a new art direction. It is **making the existing majority medium win**, and the
foundation for it is already in place and already uniform:

| Asset class | Count | Status |
| --- | --- | --- |
| Enemy + player sprites | 25 | uniform 32x32 (one boss 32x48) |
| Background tiles | 7 | uniform 64x64 |
| Icons (cards, relics, potions, map, status, intents) | 78 | **SVG — wrong medium** |
| Fonts (Cinzel, IM Fell English) | 2 | **high-res serif — wrong medium** |
| `ChromeStyles` chrome | — | **anti-aliased rounded rects, soft shadows — wrong medium** |

Only three things contradict the medium, and all three are fixable without commissioning
illustration — which is the practical argument for this direction over chasing a painted look the
project has no art budget to reach.

The deeper argument is that it converts consistency from a matter of taste into an enforceable
**spec** — fixed palette, integer scaling, nearest filter, power-of-two grids. Rules can be
smoke-tested. Taste cannot, which is exactly how the project accumulated three palettes in the
first place.

It also makes tooling viable. Palette-clamping, outlining, dithering and 9-slice generation are
trivial batch operations on pixel art and effectively impossible on painted art — see Phase 3.

## Phase 0 — Write the pixel-art spec ✅ done

Everything downstream enforces this, so it lands first. `docs/ART_SPEC.md` plus a `PixelSpec` class
for the values code needs.

- **Grids:** creatures 32x32 (bosses may be 32x48), background tiles 64x64, icons 32x32, chrome
  9-slices 16x16 or 24x24.
- **Scaling:** integer factors only — creatures render at 5x (160px), which is what
  `EnemyView.tscn` already used. The player sprite was found at 180px (5.625x) and corrected.
- **Filtering:** `TextureFilter = Nearest` on every pixel asset, everywhere. `ScreenBackground.cs`
  already does this for tiles and documents why; generalize the rule.
- **Palette:** one shared ramp — 43 colours in seven hue-shifted families, anchored on the
  existing `UiTheme.Palette` values so the warm bronze/oxblood mood survives the medium
  change. `UiTheme.Palette` is now an alias layer over `PixelSpec.Ramp`, so a colour is
  defined in exactly one place.
- **Explicitly do NOT move to `stretch/mode="viewport"`.** A low-res viewport (480x270) is the
  conventional pixel-art choice and is wrong for a *card* game — cards are text-dense and would be
  illegible at that scale. Stay on `canvas_items` at 1152x648 and get crispness from integer
  discipline instead.

## Phase 1 — One theme, one medium ✅ done

The highest value per hour in the whole roadmap, and none of it was blocked on producing a
single new asset. Outcome: the slate blue is gone from all 13 screens, everything shares one
font pair and one ramp, and `PixelSpecSmokeTest` (41 checks) now enforces the spec.

- **Merge the themes** into one `hollowdeck_theme.tres` restyled to the warm palette. Point
  `gui/theme/custom` at it, delete `combat_theme.tres`, drop the three per-scene `theme =`
  overrides. Keep the `CombatDisplayLabel` type-variation — it works. This single change fixes the
  enemy panel colour, every stock grey button, the unlocks progress bar and the shop's raw
  `ItemList` at once.
- **Swap to a bitmap font.** Landed as **Silkscreen Bold** (display) + **Jersey 15** (body),
  both OFL. Pixelify Sans was tried first and rejected on evidence — at 16px its 2/3/5/8 are
  mutually ambiguous, so "HP: 21/50" read as "81/50". See `docs/ART_SPEC.md` §7.
- **Rewrite `ChromeStyles`.** Every corner radius and `ShadowSize` blur removed; emphasis is
  carried by border weight and ramp brightness instead. `CardFrameStyle`'s two-channel design
  (fill = `CardType`, border = `Rarity`) survives intact. The sourced CC0 ornate wooden button
  frames were dropped — smooth anti-aliased art beside bitmap type was the exact seam this
  closes — and replaced by a procedural double-weight gold bezel.
  *Outstanding:* Rare's animated pixel border (G3→G4→G5) still needs doing in `CardView`
  alongside the other tweens; today Rare reads as a heavier, brighter static border.
- **Enforce integer scaling and nearest filter.** The real bug was project-wide: the `.tscn`
  sprites set `texture_filter=1` individually, which hid that every icon *built in code* was
  inheriting Linear and being bilinearly blurred. Fixed by setting
  `default_texture_filter=0` in `project.godot`, with the four smooth procedural gradients in
  `ScreenBackground` explicitly opted back to Linear.

## Phase 2 — Rebuild the combat composition ✅ done

Restructures `CardView.tscn` and `CombatScreen.tscn`, which **will break smoke tests asserting on
`GetNode` paths**. Per `CLAUDE.md` that is the alarm working — update the assertions, never delete
the check.

- ✅ **Cards 224x308 → 176x240.** Art window 84→96px (27%→40% of the card, and exactly 3x the
  32px icon grid so Phase 3's pixel icons land at an integer scale). Description centred via
  `size_flags_vertical = 4` — which needs `fit_content = true` on the `RichTextLabel` or the
  label collapses to zero height and the rules text vanishes entirely.
  Card titles moved to the *body* face: the longest name in the data ("Reckless Charge+", 16
  chars) does not fit a 176px card in Silkscreen at any legal size. `HandLayoutSmokeTest` now
  asserts name fit as well as description fit — the name never had a check, and it broke twice.
- ✅ **Enemies out of their panels.** `EnemyView` is a `Button` (for click-to-target) and so was
  picking up the theme's Button stylebox and drawing a filled box around every enemy. All four
  states are now `StyleBoxEmpty`; only target-lock paints a background. Note `SetTargetLocked`
  must *restore* the empty box rather than call `RemoveThemeStyleboxOverride`, or the panel
  comes back.
- ✅ **Energy orb.** `max` small pips replaced by one 80px orb carrying the number. The pips
  encoded the same value twice (pip count *and* the "1/3" label under them) while reading as
  neither. Drawn as a 16x16 pixel octagon — a diamond was tried first and has too little
  interior for the number to sit inside it.
- ✅ **HUD density pass.** The three leftovers above were all the same defect — chrome sized for
  content that isn't there — and were fixed together.
  *Gold, relics, potions:* three fixed 280px-wide panels each framing one or two ~34px items.
  Gold and the relic/status rows now share one shrink-to-fit column, and the framing moved down a
  level to `ChromeStyles.SlotStyle` — one bordered slot per relic, so the row is exactly as wide as
  the relics you own. The potion belt draws all `RunState.MaxPotionSlots` slots, empty ones
  included: the same width now reads as capacity, and it is the only thing in combat that tells you
  how many more potions you can carry.
  *Pile buttons → counters:* the vertical stack of four full-width text buttons became
  `PileCounterBar`, a 160x44 strip of 40px cells (a ramp-tinted card-stack glyph over a live count).
  That deleted `PileCountsLabel` — "Draw N · Discard N · Exhaust N" was the same value-encoded-twice
  problem the energy pips had — and with it the three hardcoded `*PileAnchor` nodes, since drawn and
  discarded cards now fly to the counter that actually changed.
  *Enemy HP bars:* pinned to 160px (`SpriteScale × CreatureGrid`, the sprite's rendered width) and
  shrink-centred, instead of stretching the full 220px of a panel that no longer exists.
  The 40px cell width is a hard budget — the strip has to clear `EnemyRow`'s `offset_right=976` or
  it paints over the rightmost enemy's intent icon, which `DeckViewSmokeTest` asserts.
- ✅ **Hand no longer covers the enemies** — the headline fix. `FanBaseY` -140 → -72 puts a
  card's top at y=388 against `EnemyRow`'s bottom at y=330 (worst case y=352 once the fan arc
  lifts the outer cards). `HandLayoutSmokeTest` asserts the clearance.

Combat *animation* is deliberately out of scope — idle bob, attack lunge, hit shake, screen shake,
turn banner, hit sparks, floating text, ghost HP bars, energy pip pop and block flash already exist
and are good. The gap is layout, not juice.

## Phase 3 — Convert the icons, and build `tools/artgen`

The only large asset job: 30 card, 22 relic, 12 potion, 7 map, 4 status and 3 intent icons.

**Do not downscale the SVGs.** Vector art resampled onto a 32x32 grid produces mush, not pixel art.
Source a CC-licensed pixel set and palette-clamp it for breadth; hand-pixel the 30 card icons that
carry the most identity. `ArtAssets.cs` resolves by convention, so they land incrementally with no
code change.

**`tools/artgen`, in Rust.** This does not relitigate the engine decision in `CLAUDE.md` — that
rejection was about Rust as the *engine* (weak `bevy_ui`, weak tweening, ECS stacked on top of
engine-learning). A tool that emits PNGs has none of those problems, and the game never needs Rust
at build time. It lives beside `tools/run-smoke-tests.sh`; it earns its own repo only if it ever
becomes generally useful, since a second repo means syncing generated assets across two build
systems from day one.

What it does — all batch image work, all easy on pixel art:

- **Palette-clamp every asset to the shared ramp.** The single biggest cohesion lever available,
  and roughly a 200-line program. It is what makes 25 DCSS sprites contributed over ~20 years read
  as one game.
- Batch outline / rim-light sprites so they separate from dark backdrops.
- Generate 9-slice frames, bezels and card frames for Phase 1.
- Generate background tiles, replacing runtime `FastNoiseLite` (smooth noise violates the spec).
- **Validate the spec** — dimensions and palette conformance — wired into the smoke-test script so
  a non-conforming asset fails the build.

Art as code: change one palette constant, re-run, every asset regenerates consistently.

## Phase 4 — The remaining screens

These inherit correct chrome from Phase 1, but several are near-empty and need real layout:

- **Treasure** prints `"You found: Vampire Fang"` as text while `vampire_fang.svg` sits unused.
- **Rest** is ~90% empty — three buttons, no campfire.
- **Event** has no illustration at all.
- **Map** uses only the top half (~40% dead space), and `Event` nodes render as *text* while every
  other node type uses an icon — `event.svg` exists and isn't wired up.
- **Shop** puts relics in a raw `ItemList` with a visible scrollbar, and wastes its right half.
- **RunEnd** has no victory treatment for a won run.

## Phase 5 — Transitions

`RunManager.ChangeScreen` is still a hard `ChangeSceneToFile` cut — the last instant snap in a game
that otherwise tweens everything. One fade there covers all 13 screens. Gate on
`SettingsManager.ReduceMotion`, as `ScreenBackground.AddDustMotes` already does.

## Phase 6 — Content and ship readiness

Demoted, not dropped. Still genuinely open:

- **Card scaffolding before card volume.** `CardType` has no `Power`. `Rarity` exists on
  `CardDefinition` and already drives border colour, but no card in `cards.json` declares one and
  neither shop nor reward weighting reads it — see `RunScore.cs:17`, which can't award a rare-card
  bonus for exactly this reason. Add `Power`, populate `rarity`, wire it into weighting *before*
  bulk-authoring, so new content lands against the final shape.
- **Cards 30 → 80–120**, and more enemies per act (24 today is ~4 normal encounters' variety each).
- **Balance the three-act curve.** Authored and smoke-tested but never actually played: enemies
  scale ~1.4x per act against a 50 HP start with +8 max HP and 30% heal per act cleared. Act III
  against a deck with only ~20 card rewards is the open question. Unlock-track thresholds were
  scaled to one-act runs and now fill three times faster.
- **Wider status roster** — only `Vulnerable`, `Weak`, `Strength`, `Poison` today. Widen it
  alongside cards that use the new statuses, not speculatively.
- **Close the relic-hook gap.** `SimpleHookEffectRelic` covers 2 of the 7 hooks `RelicBehavior`
  defines. Extending it to the other five makes future simple relics data rows instead of classes.
- **Input actions.** There is no `[input]` section in `project.godot`, so rebinding isn't missing
  UI — there's no `InputMap` layer to rebind. Resolution/windowed-size options are also missing.
- **CI.** `tools/run-smoke-tests.sh` runs 15 suites and 373 checks from one command and exits
  nonzero on failure. What's missing is the trigger: there is no `.github/` at all. Coverage is the
  deeper gap — drag/targeting, the project's own stated highest-risk area, has only the target-lock
  glow asserted.
- **Packaged export pass** on Windows/Mac/Linux, and a **balance and bug-bash pass** once content
  lands, hunting the input-during-animation bugs the state machine was designed to prevent.

## Sequencing notes

- **Phase 1 before everything.** It needs no new art, and it fixes eight screens with one edit. The
  game should look dramatically better within days, long before the icon conversion starts.
- **Phase 0 before Phase 1**, because Phase 1's chrome rewrite has to target a written spec, not a
  vibe — otherwise it becomes the fourth palette.
- **Phases 2 and 3 can run in parallel.** Layout works on node trees and themes; `artgen` works on
  PNGs landing at paths `ArtAssets.cs` already resolves by convention. Neither blocks the other.
- **Phase 6's internal order still holds** — scaffolding before volume. Authoring 50 cards and then
  retrofitting `Power` and rarity is the expensive order. But content is no longer the gate on the
  project as a whole.

## Known cost, accepted

Moving to a bitmap font gives up the Cinzel / IM Fell English illuminated-manuscript character that
`UiTheme` and `ChromeStyles` were built around — currently the most distinctive thing about the
game's identity. Pixel type will not reproduce it. The alternative (pixel sprites, high-res serif
UI) is a defensible hybrid some games ship deliberately, but it keeps a visible seam between art
and text, which is the exact problem this roadmap exists to close.

The plan is to take the loss and recover the gothic identity through **palette and ornament** —
a heavy oxblood/bronze ramp, ornate pixel borders, drop caps — rather than through typeface.
