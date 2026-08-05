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
| Icons (cards, relics, potions, map, status, intents) | 78 | ~~**SVG — wrong medium**~~ → 79 generated PNGs (Phase 3), plus 5 event illustrations (Phase 4) |
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

## Phase 3 — Convert the icons, and build `tools/artgen` ✅ done

The only large asset job: 30 card, 22 relic, 12 potion, 7 map, 4 status and 3 intent icons.
Outcome: zero SVGs remain under `assets/`, every asset in the repo sits on the 43-colour ramp, and
`artgen validate` runs ahead of the engine suites in `tools/run-smoke-tests.sh`.

**Do not downscale the SVGs.** Vector art resampled onto a 32x32 grid produces mush, not pixel art.
The plan here was to source a CC-licensed pixel set for breadth and hand-pixel the card icons;
what actually shipped is **all 79 generated by `artgen`** — a `fn` per icon composing a shared
shape vocabulary (`icons/shapes.rs`: blade, shield, flask, droplet, flame, fist, skull) onto the
32x32 grid. That was the better trade for the same reason the spec exists: seventy-eight
hand-drawn icons are seventy-eight chances to draw a blade at a different width, which is the
vector set's exact failure restated in a new medium. It also removed the CC BY attribution
obligation entirely.

The 79th is `map/event`. `ArtAssets.MapIcon` had always looked for it and the SVG set never had
one, which is why Event was the only node type rendering as a *word* on the map — listed under
Phase 4 below as a layout problem, actually a missing file.

**`tools/artgen`, in Rust.** This does not relitigate the engine decision in `CLAUDE.md` — that
rejection was about Rust as the *engine* (weak `bevy_ui`, weak tweening, ECS stacked on top of
engine-learning). A tool that emits PNGs has none of those problems, and the game never needs Rust
at build time. It lives beside `tools/run-smoke-tests.sh`; it earns its own repo only if it ever
becomes generally useful, since a second repo means syncing generated assets across two build
systems from day one.

- ✅ **Palette-clamp every asset to the shared ramp.** The single biggest cohesion lever available.
  The 25 DCSS sprites carried 7–379 colours each (`hollow_shade` 379, `void_leech` 296,
  `molten_sentinel` 270 — contributed by different artists over ~20 years); they now carry 5–19
  each, all shared. Two idempotent passes, colour snap plus alpha hardening, since a soft mask edge
  is anti-aliasing by another name under Nearest. Deliberately no dithering: at 32x32 shown at 1x a
  dither pattern has no room to resolve and just reads as dirt.
- ✅ **Outlining.** Every generated icon ends with a hard `N0` outline (`shapes::finish`), which is
  what separates it from both a lit card frame and a near-black HUD. Done at authoring time rather
  than as a batch pass over finished art — the pass would have to guess where the outline belongs.
- ✅ **Validate the spec** — grid, ramp, hard alpha, no SVG — wired into `tools/run-smoke-tests.sh`
  ahead of the engine suites, and degrading to a warning if `cargo` is absent so the suite still
  runs without a Rust toolchain. It reads raw PNG bytes, which the engine-side suites cannot:
  `GD.Load` hands back an already-imported texture.
- ⏳ **Generate 9-slice frames and background tiles.** Not done, and no longer clearly worth doing.
  `ChromeStyles` builds every frame procedurally from `StyleBoxFlat` and has since Phase 1, so
  there is nothing for a 9-slice PNG to replace. The tile case is real but narrow — `ScreenBackground`
  still uses `FastNoiseLite` for its fog layer, which is smooth noise and violates the spec, though
  it is composited at 16% alpha over a vignette where nobody will ever see the banding.

Two invariants the tool now depends on, both asserted by `PixelSpecSmokeTest`: `artgen`'s
`palette.rs` must match `PixelSpec.Ramp` entry-for-entry (a validator clamping to a different
palette than the game draws with would pass assets that look wrong), and every icon filename must
be a live definition id in both directions (`ArtAssets` resolves by convention, so a renamed card
loses its art silently rather than failing).

Art as code: change one palette constant, re-run, every asset regenerates consistently.

## Phase 4 — The remaining screens ✅ done

These inherited correct chrome from Phase 1 but several were near-empty. The common defect turned
out to be one thing repeated six times: a `CenterContainer` of unstyled labels floating on a tiled
backdrop, with the run's HP/gold printed as a bare `Label` at `(24, 16)` in the body face. So the
fix is one new file plus six much smaller edits — `scripts/ui/ScreenChrome.cs`, attached from
`_Ready` the way `ScreenBackground` and `DeckViewButtons` already are, supplying a screen title, a
framed HP/gold/relic status block, a panel frame and an art plinth. The vocabulary is deliberately
combat's own (`ChromeStyles.PanelStyle` + one `SlotStyle` per relic) rather than a second one.

- ✅ **Treasure** shows the relic. `vampire_fang.png` at `SpriteScale` on a plinth, name in the
  display face, description under it, with a short rise-and-settle on arrival. It used to print
  `"You found: Vampire Fang"` as body text while the icon `ArtAssets` already resolves by id went
  unused.
- ✅ **Rest** has its campfire — `assets/icons/map/rest.png` at 5x, with a slow brightness flicker
  (gated on `ReduceMotion`; a modulate multiply, so it only ever darkens and the plinth bezel goes
  with it). The Smith picker also stopped being three stacked text rows per card and became real
  `CardView`s of the *upgraded* result with a "was:" delta beneath — the one place outside combat
  the player is asked to compare cards, so "one card component everywhere" applies.
- ✅ **Event** has per-event illustrations, five new `artgen` icons keyed to the event ids
  (`ArtAssets.EventIcon`, falling back to the map's scroll so an unillustrated event still gets a
  subject). This is the one icon category never drawn below 5x, and says so in its own module
  header. The screen deliberately does *not* take a `ScreenChrome` title: the event's own name is
  the title.
- ✅ **Map** fills the canvas. Column spacing is now derived from the widest floor exactly as floor
  spacing was already derived from the act length, and each floor is centred on the band rather
  than stacked downward from a fixed `y=60` — which is what left the bottom 45% empty. Node icons
  also moved off `Button.Icon` + `ExpandIcon`, which was resampling every 32px icon to a 56px
  button (1.75x, a fractional scale ART_SPEC section 2 forbids); they are centred child
  `TextureRect`s at an exact 2x on a 64px node, 3x on a 96px boss.
- ✅ **Shop** uses its full width: four cards on top, the relic/potion stock below as four framed
  tiles carrying their own icon, name, kind, rules text and price. The stock used to be a 476px
  `ScrollContainer` with a visible scrollbar pinned bottom-left while the right half sat empty.
- ✅ **RunEnd** has a victory treatment. Two columns — outcome and next actions on the left, the
  itemized score on the right — with the player sprite on a plinth, gold-titled and breathing on a
  win, drained toward the dark end of the ramp and red-titled on a loss. Winning used to be
  announced by one line of 16px body text set identically to the losing one.

Two things found on the way and fixed here rather than filed: `MapScreen._Draw` indexed
`_nodeCenters` by whatever is currently in `RunState.MapNodes`, so a freed-but-not-yet-collected
map instance threw a `KeyNotFoundException` out of `_Draw` on every suite run once the next act's
graph had replaced it; and `tools/run-smoke-tests.sh` now runs each suite under a watchdog,
because a test that throws inside `_Ready` never reaches `GetTree().Quit()` and hangs the sweep
instead of failing it.

## Phase 5 — Transitions ✅ done

`RunManager.ChangeScreen` was a hard `ChangeSceneToFile` cut — the last instant snap in a game that
otherwise tweens everything. One fade there now covers all 13 screens, gated on
`SettingsManager.ReduceMotion` exactly as `ScreenBackground.AddDustMotes` already was.

`scripts/run/ScreenFade.cs` is a `CanvasLayer` parented to the **RunManager autoload**, which is
the whole trick: `ChangeSceneToFile` frees the current scene, so anything that has to be visible on
both sides of the swap cannot live inside one. There is nothing to add to a `.tscn` and nothing a
new screen has to remember to do. It fades out over `Motion.Fast`, runs the swap, holds full black
for 0.05s — `ChangeSceneToFile` is itself deferred to end-of-frame, so without the hold the first
frames of "arriving" still show the screen being left — then fades in over `Motion.Normal`.

Three decisions worth keeping:

- **`Play` takes an `Action`, not a scene path.** A fade has no business knowing what a scene is,
  and it means `TransitionSmokeTest` can hand it a recorder instead of something that swaps the
  test's own scene out from under it.
- **The cover is `MouseFilter.Stop`.** Swallowing clicks for the third of a second the swap takes
  closes the window where a double-click lands on the outgoing screen's button and then the
  incoming one's — the input-during-animation class of bug risk 4 exists for.
- **It is `Ramp.N0`, not `#000`.** A pure-black wash would be the single off-ramp colour on screen,
  and it is the one that covers the whole screen.

The gate had a consequence worth recording: it reads the *developer's* `user://settings.json`, so
the three suites that drive a button into `ChangeScreen` would have behaved differently depending
on a file with nothing to do with them (two of them document an expected engine error that only
appears on the synchronous path). `HardCutGuard` pins them, using Reduce Motion itself as the seam
rather than a test-only flag — so the tests exercise a path a player can actually reach.

## Phase 6 — Content and ship readiness

Demoted, not dropped. Still genuinely open:

- ✅ **Card scaffolding before card volume.** Done, and it was the right order: every card authored
  from here lands against the final shape instead of being retrofitted.
  - `CardType.Power` exists and means something. A played Power goes to `PileManager.Powers` —
    deliberately not Discard (it would cycle back and be re-playable) and not Exhaust, which is
    *a cost* and which the HUD renders as one, ember tint and counter cell included. It picks up
    `UiTheme.Palette.PowerFill`, the third fill claimed back in Phase 1 precisely so it would not
    have to be chosen under deadline.
  - **Rarity is assigned across the whole pool** — 12 Common / 13 Uncommon / 6 Rare. Common is the
    basic curve you build out of, Uncommon a stronger or conditional version of something Common
    already does, Rare run-defining (every one is an exhaust card or an energy swing).
  - **`CardPool` weights every draw** 60/37/3, and reward picks, shop stock and the random-card
    event outcome all go through it. All three previously shuffled the unlocked pool uniformly and
    took the first N, which made a Rare exactly as likely as a Strike. It draws a *tier* first and
    then a card within it, so authoring more Uncommons doesn't silently re-tune the odds of every
    other tier.
  - **`RunScore` gained Pauper** (100 points, cleared with no Rare in the deck) — the category the
    file used to carry a comment apologising for, since it would have awarded unconditionally while
    every card was Common.
  - One Power ships, `Inflame` (2 energy, Rare, gain 4 Strength), so the type is live end-to-end
    rather than untested scaffolding — the `reward` screenshot fixture now shows one card of each
    type so both frame channels are visible in a single shot.

- ✅ **The per-turn Power hook.** Closed the gap the bullet above left open: Powers could only be
  one-shot permanent buffs, which made them Skills that don't come back — a real distinction, but
  too thin to author a dozen against.

  Landed as two non-decaying statuses granted at turn start — `Metallicize` (Block) and `Ritual`
  (Strength, compounding) — rather than the `RelicBehavior.OnTurnStart`-style hook the other option
  would have been. A hook means one C# class per Power, which is the one-class-per-card pattern the
  effect system exists to prevent (risk 1); a status keeps a Power an ordinary data row
  (`apply_status`, scope `Self`) and lets enemies carry them for free, which a player-only hook
  could not. That brings the status roster to six.

  The ordering is the load-bearing part and is commented at all three sites: both combatants clear
  `Block` on their own turn, so a grant landing before that clear is wiped the instant it is given.
  The player's clear is in `EndEnemyTurn` just before `BeginPlayerTurn`; the enemy's is mid-loop,
  *after* its poison tick — which is why the grants are a separate pass rather than folded in with
  poison, whose position is fixed by a death check.

  Two Powers ship against it: `Metallicize` (1 energy, Uncommon, 3 Block a turn) and `Demon Form`
  (2 energy, Rare, 2 Strength a turn, compounding). `EffectDescriptionFormatter` also stopped
  special-casing Strength by name for its "Gain" wording — it now reads scope, so any self-status
  says "Gain N X" instead of the "Apply 3 Metallicize" a self-targeted card would have printed.
- ✅ **A first content pass: cards 33 → 58, events 5 → 15.** Vocabulary landed first, because 33
  cards out of 7 effect actions was already near-saturated and another batch built from only those
  would have been numerically distinct and mechanically identical to what shipped.
  - **Three statuses**, each with cards that use them rather than speculatively (the pattern the
    bullet below asks for). `Dexterity`/`Frail` are `Strength`/`Weak` applied to Block, via a new
    `BlockMath` mirroring `DamageMath`; `Regen` joins `Metallicize`/`Ritual` as the third
    turn-start grant, which is what makes it authorable as a Power. Roster is now nine.
  - **Two effects**, `discard_cards` and `exhaust_hand` — the cost and the payoff that let a card
    overshoot its energy value. Nine actions now.
  - **Seven event outcome keys** (15 total), and `EventChoice` gained a compound `outcomes` list
    plus a `gamble` with `alternatives`. Compound choices are what let one option be "gain a
    relic, lose 10 max HP" — a cost attached to a reward, which is most of what makes an event a
    decision rather than a free pick.
  - **Events can ask which card.** `ICardPickerOutcome` + a shared `scripts/ui/CardPicker.cs`
    extracted from the rest site's Smith picker, so `remove_chosen_card` is the only deck-thinning
    in the game and the rest site's grid and the event's are one component.
  - Caught by screenshotting rather than by a test, then pinned as one: the picker and the event's
    own column are both full-rect transparent `CenterContainer`s, so showing one without hiding
    the other interleaves the event's text through the gaps in the card grid.
- ✅ **Enemies 24 → 36, and the intent vocabulary that made them worth authoring.** The bullet asked
  for volume; reading the 24 movesets side by side said volume was not the problem. **Every enemy in
  the game was the same three moves** — a `deal_damage`, a `deal_damage` plus a debuff, and either a
  `gain_block` or `+N Strength` — with only the numbers differing. Three things in the code held
  that ceiling: `IntentType` was `{Attack, Defend, Buff}`, so a move that only debuffs had to
  telegraph `0`; `EnemyView.FormatIntent` hardcoded Buff as `"+N Str"`, which made Metallicize,
  Ritual, Regen and Dexterity unusable by enemies (five of nine statuses player-only by accident of
  a format string); and one `DisplayAmount` meant a multi-hit attack could not be telegraphed
  truthfully. Twelve more enemies against that would have been numerically distinct and mechanically
  identical — the exact thing the card pass above landed statuses and effect actions to avoid.

  So the vocabulary went first, and it is *derived* rather than authored, which is the part worth
  keeping: hit count comes from a run of identical `deal_damage` specs (through
  `EffectDescriptionFormatter.SameEffect`, so a card's "twice" and an intent's `x2` cannot disagree),
  and a Buff's status name off the move's first `Self` spec. The single authored number is now pinned
  against the effects behind it for all 36 enemies by one sweep — a telegraph that lies is the
  canonical bad bug in this genre, and it was previously prevented only by review.

  Twelve enemies followed, three normals and one elite per act, each shipping to *use* the new
  vocabulary rather than to pad a count: Mire Leech opens with a damageless Frail curse and heals
  itself, Gaol Rat and Gilded Husk carry Metallicize, the Drowned Matron and the Emberforge Smith
  ramp on Ritual, and four enemies across the three acts attack twice or three times. Every act now
  offers 7 distinct normals and 3 distinct elites, up from 4 and 2, and `ActSmokeTest` asserts that
  floor rather than leaving it to this document.

  Two holes in the suite got closed on the way, both found by doing the work rather than by reading
  the tests. Nothing asserted that an **enemy has a sprite** — every coverage check lived under
  `assets/icons`, so an enemy shipped with no art passed all 17 suites and rendered as an empty
  rectangle mid-fight. And a newly added PNG with no `.import` sidecar makes `PixelSpecSmokeTest`
  throw inside `_Ready`, which the watchdog reports as a `TIMEOUT` rather than a missing asset;
  that is now written down where the next person will hit it.

- ✅ **Cards 58 → 84**, into the 80–120 band. Same order as the enemy pass above, and the analysis
  came out differently: the Common curve was genuinely close to full at 24 cards, but the **Power**
  roster was six cards that all granted a flat number. Every Power in the game was Block, Strength
  or HP per turn, because those were the only three statuses that paid out at turn start — so the
  card type that is supposed to define a deck could not define one.

  Two statuses fixed that, both authored as ordinary rows the way the roadmap asks: `Fervor`
  (+Energy each turn) and `Foresight` (+cards each turn) — the two resources a turn *assigns*
  rather than accumulates, which is exactly why they could not go in `ApplyTurnStartGrants` with
  the other three. Energy and hand size are set outright in `BeginPlayerTurn`, so a grant applied
  in that pass is overwritten a line later; they are folded into the assignments themselves
  (`MaxEnergy + Fervor`, `BaseHandSize + Foresight`), which is the Block ordering trap running the
  other way and the reason it can't happen. Four cards ship on them, laddered Uncommon→Rare exactly
  as Metallicize→Demon Form already was. Powers went 6 → 10.

  The other 22 fill measurable gaps rather than adding rows: the pool had **no card above cost 2**
  (three now, all Rare, all spending a whole turn), no card using `gain_gold` (Tithe), and one card
  on `Ritual` (Stoke is its Uncommon rung). Four drafts were cut or rewritten during the pass for
  the reason the enemy pass cut two names — Reprisal was Iron Wave with bigger numbers, Windfall
  was Sift one rarity up, and "Cinder Storm"/"Sharpen" could not be drawn without contradicting the
  set's own rules (an ember-named card that applies green Poison, a Skill whose name demands a
  weapon). Final pool: 34 Common / 34 Uncommon / 16 Rare.

  One bug found by writing the test rather than by playing: `gain_gold` was excluded from
  `CardUpgrade`'s scaled actions, correctly while it was relic-only — relics don't upgrade — and
  silently wrong the moment a card used it, so `Tithe+` read and played exactly like `Tithe`.
  `EffectSmokeTest.TestEveryCardUpgradeChangesSomething` now fails any card whose `+` moves no
  number, which is the general form of the failure `CardUpgrade.ShouldScale` had only warned about
  in a comment.
- ✅ **Balance tooling.** The bullet below used to open by saying there was none, and every figure
  in it had been computed by hand in a session nobody could reproduce. There is now
  `tools/balance-report.sh`, and the numbers are read off the content databases.

  Three pieces. `scripts/debug/BalanceModel.cs` is the analyser — pure, no `Node`, no scene tree —
  computing per-enemy DPT, encounter and act curves, player throughput, and what a path through a
  generated map actually contains. `BalanceReport` prints it and is deliberately *not* named
  `*SmokeTest`, so the sweep's glob skips it the way it already skips the three visual scenes; a
  report has no pass/fail. `BalanceSmokeTest` is the pass/fail half, 32 checks, in the sweep.

  **It is a static analyser, not a simulator**, and that was the load-bearing decision. The obvious
  design — a greedy auto-player driving N real runs, which is what this bullet used to ask for —
  cannot work here: `CombatManager` paces the enemy turn on wall-clock timers (0.35s per enemy
  action), so the 90s suite watchdog caps a sweep at a couple of hundred enemy actions. That is not
  enough fights to say anything about a curve, and the alternative was making production combat
  timings configurable for a test's convenience. Reading the numbers off `enemies.json`/`acts.json`
  is instant, exact, and reproduces the hand-computed figures well enough to validate itself
  against them.

  Two things the analyser has to get right and does, because approximating either would quietly
  wreck the output: multi-hit damage comes from counting `deal_damage` specs rather than reading
  `intent.DisplayAmount` (which is damage *per hit*), and `weighted_random`'s move distribution is
  the stationary distribution of `WeightedRandomIntentPicker`'s anti-repeat rule rather than the
  raw weights — that picker excludes the last move played for 3+-move enemies, which makes it a
  Markov chain. That correction alone moves elite DPT by about 2%.

  **Three of the four "anomalies" below survived contact with measurement. Two figures did not**,
  and both had been quoted as evidence:
  - The act I spread was cited as `rot_hound/rot_hound/ward_acolyte` at 16.1 DPT against
    `possessed_armor`'s 3.0, a 5.4x gap. That group is not in act I's normal pool at all, and the
    3.0 was an unweighted mean where every other figure used weights. The real spread is **3.4x**
    (`slime x3` at 12.6 against `ward_acolyte` at 3.7), and it is ~3.4x in all three acts.
  - A starter deck does **16.2** damage a turn, not ~18 — measured by dealing real hands out of
    `PileManager` and spending energy greedily, rather than assumed.

- **Balance the three-act curve.** Authored and smoke-tested but never actually played. Everything
  below is `tools/balance-report.sh` output; re-run it rather than trusting this snapshot.

  **The core tension, quantified.** Encounter HP scales **1.49x then 1.44x** per act (66 → 98 → 141
  average for a normal group) and incoming damage **1.61x then 1.30x** (8.6 → 13.9 → 18.1 per turn),
  while the player's defensive side scales only ~**1.15x per act** (50 → 58 → 66). Deck power has to
  cover the remaining **1.59x**, drawn from **16.6 three-card rewards** on an average path. At
  starter throughput an act I group takes 4.0 turns to clear and kills the player in 5.8; by act III
  that is 8.7 against 3.7 — the fight goes from winnable-by-default to lost-by-default, and closing
  it is what the deck is for.

  ✅ **The encounter retune.** The bullet asked for two things — elites that hit softer than normal
  fights, and a flat boss enrage curve. Measuring properly found the same symptoms with a different
  and more useful cause, and fixing the cause fixed both.

  **The elite pool was not soft, it was incoherent.** In every act, three of the four elite groups
  were singletons and the fourth stacked 2–3 enemies, so within one act's pool an Elite node cost
  anywhere from **0.58x to 2.55x** the damage of an average normal fight. Elite nodes are identical
  on the map, so the player had no way to tell a pushover from the hardest fight in the act. The
  cause is structural rather than numeric: a singleton acts once a turn against a group's two or
  three, and no amount of raising its numbers fixes a cadence problem — the auto-tuner asked for
  x2.6–x2.9 damage multipliers, which would have meant single hits over half the player's max HP.

  So the fix was mostly structural, and the numbers followed:
  - **The defensive move became an opening stance rather than a recurring rest.** Four sequential
    elites had a Defend move inside their loop, costing them a third of their turns; reordering the
    moves and moving `loopFromIndex` past it keeps the move, keeps the telegraph, and stops it
    recurring. That is the mechanism `emberforge_smith` already used, applied to the rest.
  - `sable_inquisitor`'s `inquest` became an attack that also applies Vulnerable — the shape
    `ward_acolyte`/`hex`, `crown_reaver`/`sunder` and `silent_judge`/`condemn` already use — instead
    of a damageless turn on an elite that only attacked once every three.
  - The three over-tuned groups lost their third enemy or were rebuilt as a matched pair.
  - Moderate HP and damage raises on the nine elite-only enemies, kept under the constraint that no
    single move exceeds ~45% of that act's player max HP.

  **The boss curve had the same shape, and the same fix.** Act I's bosses take their Buff turn in
  their *normal* set and enrage into an all-attack phase; act III's did the reverse, spending one
  enrage turn in three on `dominion`/`ascend`, which is why the climax of the game was its safest
  fight (1.12x an average act-III fight, against act I's 3.59x). Moving those two moves into the
  normal set — deleting nothing — mirrors act I exactly and carries the Strength ramp *into* the
  spike instead of spending the spike on it.

  Result, from `tools/balance-report.sh`: elites now span **1.13x–1.85x** and bosses **2.44x–3.16x**,
  against 0.58–2.55x and 1.12–3.59x before. Enrage escalation now *rises* across the game
  (1.8x → 1.9x → 2.7x over the boss's own normal phase) rather than sagging in act III.
  `BalanceSmokeTest` asserts both bands plus the tier ordering (no boss cheaper than the act's
  costliest elite), so a content edit that breaks a tier fails a build.

  **The analyser had to be fixed first, and that changed the answer twice.** Damage per turn ignores
  Poison — authored on six enemies, and `corrosive_tide`'s Poison 5 is 15 damage against the 13 the
  move telegraphs — ignores that an enemy applying Vulnerable amplifies its *own* later hits, and
  ignores Strength accumulating through an enrage phase. `BalanceModel.EncounterCost` now walks the
  fight turn by turn and accounts for all three. On the old measure `sable_inquisitor` looked like
  0.30x and `drowned_matron` 0.54x; both were nearly twice that. Tuning against the old numbers would
  have overshot badly on exactly the enemies whose threat is a status rather than a hit.

  **What still wants fixing**, in order:
  - **`Deep Focus+` draws 8 cards a turn, not 7**, and `Bloodpact+` is 5 energy every turn.
    `CardUpgrade.Apply`'s `max(amount + 1, round(amount * 1.4))` floor beats the multiplier below
    amount 3, so a grant of 2 upgrades to 3. The comment claiming otherwise (and calling `Deep Focus`
    a cost-3 Rare, which it is not) is fixed and the amounts are pinned by a test; **whether they
    should be this large is still open**, and they remain the largest deltas in the pool by a
    distance.
  - **The unlock track.** A winning three-act run banks ~400–600 against a 5500-point track, so it
    completes in ~10–14 wins. Also still true: 74 of the 84 cards are unlocked from the first run,
    so the track gates a tenth of the pool rather than a fifth.

  ✅ **`RunScore`'s unreachable categories**, which is where measuring changed the answer. The worry
  was Mystery Machine at 5 event rooms against an average of 1.6 — real, but it turned out to be
  *reachable on 42% of maps* by a player routing for it, i.e. a lottery on the map roll rather than
  dead points. The genuinely dead category was one nobody had flagged: **Encyclopedian's 50-card deck
  was unreachable on every one of 500 seeds** (ceiling 47, median 41, counting fight rewards *and*
  everything ~1000 gold buys at 50g a card). Both are now set from the measurement — Mystery Machine
  3 (83% of maps), Encyclopedian 43 (23%) — and `BalanceSmokeTest` fails any threshold no seed can
  reach, which is the general form of the bug. `RunScore.cs`'s stale premise ("a 30-card pool, 22
  relics", against 84 and 27) is corrected, and its point *values* are deliberately untouched: how
  hard a category should be is a design call, zero is always wrong.

- ✅ **Close the drag/targeting test gap** — risk 5, the project's own stated highest-risk area, and
  previously the thinnest coverage in the repo. `CombatTargetingSmokeTest` never instantiated a
  `CardView`: its target-lock checks drove `EnemyView.SetTargetLocked` directly and its other two
  groups were layout assertions, while **every combat test in the suite called
  `CombatManager.TryPlayCard` directly** — precisely the layer *below* the one carrying the risk.
  `TryPlayFromHand`, `FindEnemyViewUnderMouse`, `SnapHome`, `_leavingHand` and
  `UpdateTargetHighlight` appeared in zero tests.

  All nine listed checks landed, 64 → 112 in the same suite, no new machinery: `TryPlayFromHand` is
  public, `EnemyView.OnPressed` is reachable by emitting `Button.Pressed` (which is what a click
  does), and reflection into privates was already an accepted idiom (`HandLayoutSmokeTest`).

  Two things worth keeping, both found by writing the tests rather than by planning them:

  - **A headless Godot pins the mouse at `(0, 0)` and ignores `Viewport.WarpMouse` *and*
    `Input.WarpMouse`.** So the two checks that hang off `GetGlobalMousePosition()` cannot move the
    cursor to the enemies; they build `EnemyView`s standalone and place them over the origin
    instead. That turned out to be the better test anyway: going around `CombatScreen` puts the
    order of `EnemyView.Instances` under the test's control, and the corpse has to be *first* in
    that list for the skip-dead-enemies check to mean anything — which is exactly the shape of the
    bug that shipped once.
  - **The rejected-drop round trip is the one that would have hurt.** `TryPlayFromHand` reparents
    out of the hand area *before* asking whether the play is legal, so a refused play has to undo
    it. Deleting that undo leaves the card under `CurrentScene`, where `RefreshHand` — which only
    tears down what is still parented under `HandArea` — can never see it again, while it stays in
    `Piles.Hand`. The player is left holding a card they cannot see or play for the rest of the
    fight, with no error anywhere.

  Every new check was verified to fail: deleting the reparent-undo and the `IsDead` guard in
  `FindEnemyViewUnderMouse` turns exactly four checks red, each naming the consequence rather than
  the symptom.

- **Play a packaged build end to end.** The last unchecked item on the phase bar in `CLAUDE.md`.
  `tools/build-export.sh` proves a build boots and quits clean; nobody has played a run inside one.
- **Wider status roster** — eleven today: `Vulnerable`, `Weak`, `Strength`, `Poison`,
  `Metallicize`, `Ritual`, `Dexterity`, `Frail`, `Regen`, plus `Fervor` and `Foresight` from the
  card pass above. Keep widening it alongside cards that use the new statuses, not speculatively —
  every one of those landed with the cards that grant it, which is the pattern. Five of the eleven
  now pay out at turn start, which is what makes `Power` a card type rather than a frame colour.
- ✅ **Close the relic-hook gap.** Went further than the bullet asked, because reading the eleven
  bespoke relic classes showed why the partial version wasn't worth doing: **every one of them
  decomposed** into the same five parts — a hook, a target selector, a condition, a firing limit
  and an `EffectSpec`. Even the three holding per-instance state (`_triggeredThisTurn`,
  `_cardsThisTurn`, `_usedThisCombat`) were only the firing-limit part wearing a class. Opening the
  other five hooks and stopping there would have left `relics.json` split down the middle exactly
  as before, with the vocabulary sitting unused beside eleven classes that needed it.

  So all 27 relics are now data rows, `RelicRegistry` has one factory, and the eleven classes are
  deleted. `RelicBehavior` stays as the escape hatch with nothing using it — the same standing
  `IScriptedEffect` has had since Phase 1, and the same argument: the seam is worth proving, not
  populating.

  The vocabulary is in `scripts/data/RelicTrigger.cs` and every key in it is demanded by a relic
  that shipped with it (`target`: Self/Attacker/FirstEnemy/RandomEnemy/AllEnemies; `condition`:
  cardType/outcome/minEnergy/minHpPercent/targetKilled; `limit`: oncePerTurn/oncePerCombat/
  everyNth). Two decisions worth keeping:

  - **`condition.outcome` is a string, not the `CombatOutcome` enum**, because that enum lives in
    `Hollowdeck.Combat` and `Hollowdeck.Data` references it nowhere. `hook` was already a string
    for that reason; `cardType` stays a real enum because `CardType` is already in `Data`.
  - **The per-turn limits reset in `OnTurnStart`, which only fires on the player's turn**, so a hit
    taken during the enemy turn shares a bucket with the player turn after it. That is what the
    bespoke classes did, and preserving it exactly is why the conversion is behaviour-neutral —
    `RelicSmokeTest`'s eight original checks drive all seven hooks through relics this change
    converted, and none of them was touched.

  One new effect action, `gain_gold` (ten now): Scavenger's Charm reached into `RunState.Gold`
  directly and was the single relic no `EffectSpec` could express. It is the first effect that
  ignores its targets entirely.

  Five new relics, 22 → 27, one per newly-opened hook or selector so the vocabulary ships live
  rather than as scaffolding — the argument that shipped `Inflame` with the `Power` type. Ossuary
  Bell (OnTurnEnd, `AllEnemies`), Conduit Sigil (OnCardPlayed, `cardType: Power`), Rusted
  Portcullis (OnDamageTaken, `oncePerCombat`), Palsy Shackle (OnDamageTaken, `Attacker`), Reaper's
  Tally (OnDamageDealt, `targetKilled`). Two were renamed off their first drafts by the art rather
  than the data: "Cracked Aegis" could only be drawn as a cracked shield, which is already the
  `vulnerable` status icon, and "Sablewood Charm" as a pendant on a cord, which is already
  `scavengers_charm`. A relic whose icon can't be distinct from an existing one is a relic with the
  wrong name.

  `RelicSmokeTest` gained five checks for the keys the original eight don't reach. The two limits
  are measured as a **difference against an unrelicked control** rather than against hardcoded HP —
  the Cultist's damage is data, and a balance tweak to `enemies.json` should not be able to break a
  test about firing limits.
- ✅ **Input actions, and full keyboard support.** `project.godot` now has an `[input]` section:
  every binding is a named `hd_*` action and all three input handlers check `IsActionPressed`
  rather than switching on a raw keycode, so there is finally an `InputMap` layer to rebind.

  The prompt for it was the seam, not the layer. Combat was *already* keyboard-driven — arrows,
  Space, number keys, `D`/`Q`/`W`/`E` — and then the victory panel's Continue was mouse-only,
  because `Enter` mapped to `OnEndTurnRequested`, which early-returns at `CombatState.CombatEnd`.
  Playing a whole fight on the keyboard and then reaching for the mouse for one button is what
  made the gap obvious.

  Outside combat it was the reverse. Every screen is stock `Button`s already sitting at Godot's
  default `FocusModeEnum.All`, so Tab and arrow navigation worked — but **nothing in the repo ever
  called `GrabFocus`**, so no screen had a focus owner and the first key press went nowhere. The
  fix is `ScreenKeyboardNav.Attach`, one line per screen. Godot skipping `Disabled` controls means
  the map needed no navigation code at all: unreachable nodes were already disabled.

  Deliberately *not* unified: combat keeps its own `_UnhandledInput` and its widgets stay
  `FocusModeEnum.None`. Cards are fanned `Panel`s and targeting is a `CombatState` sub-state; focus
  navigation there would fight the arrow-key cycling it exists to protect.

  Three things the work turned up that were not on the list. Potion targeting (`AwaitingTarget`)
  had **no** keyboard path at all — only Escape, because both cycle and confirm gated on
  `PlayerTurn`. A card played with Space skipped `CardView.PlayResolveTween` and flew to the
  discard counter instead, because the keyboard called `TryPlayCard` directly and left the node
  parented under the hand area; both paths now share `CardView.TryPlayFromHand`. And the theme's
  focus stylebox was G4 at 2px — the same gold `ChromeStyles.EmphasisState` paints on hover, drawn
  inside a 4px bezel, so focus was invisible where it wasn't ambiguous. It's G5 at 4px now, with
  focus styles added for `CheckButton` and (in code, since `Slider` has no focus stylebox) the
  volume sliders.

  *Outstanding:* the rebinding **UI** itself, now that there's a layer under it. Gamepad support is
  also much cheaper than it was. Resolution/windowed-size options are still missing.
- ✅ **CI.** `.github/workflows/ci.yml` runs the whole sweep on every push to `main` and every PR:
  every suite, plus `artgen validate` over the asset tree. Godot is pinned to the same
  4.7.1-stable mono build the csproj SDK and `project.godot` declare, and cached.

  Two things were worth getting right. **Importing assets first** — `.godot/` is gitignored, so a
  fresh checkout has no imported resources and every `ResourceLoader.Exists()` returns false; that
  is the same failure a newly generated icon caused locally, and without the import step CI would
  fail on assets sitting right there in the tree. And **an artgen drift check** — its output is
  committed and generation is pure, so regenerating has to be a no-op; editing an icon's `fn` and
  forgetting to re-run would otherwise ship art that no longer matches the code claiming to produce
  it.

  Verified in both directions: green on the first run with counts identical to local (so nothing
  silently skipped or platform-dependent), then deliberately broken with an un-regenerated icon
  edit to confirm it actually goes red and names the offending file.

  This bullet used to end by naming coverage as the deeper gap, with drag/targeting — the project's
  own stated highest-risk area — carrying only the target-lock glow. That gap is closed (see the
  drag/targeting bullet in Phase 6); the sweep is 19 suites and 974 checks now.
- ✅ **Packaged export.** `export_presets.cfg` for Windows Desktop / macOS / Linux,
  `tools/build-export.sh` as the one-command entry point, and a CI job that exports Linux and boots
  the result.

  **The premise it started from was wrong, and finding that out was most of the value.** The plan
  was built on a plausible reading of Godot's exporter: the six content JSONs are read as raw text
  through `FileAccess.Open`, they carry no `.import` sidecar, so `export_filter="all_resources"`
  would never see them and a default preset would ship a deckbuilder with no cards. Exporting and
  then blanking `include_filter` to prove it produced a **byte-identical `.pck`**. Godot 4 loads
  `.json` through a built-in resource loader; the files were always going to pack. The
  `include_filter="data/*.json"` that survives on all three presets is deliberate insurance rather
  than the mechanism — a `data/foo.txt` *is* verifiably dropped without a filter that matches it,
  and the line states the intent instead of depending on how Godot classifies an extension.

  What the exercise did establish is the failure *shape*, and it is worse than the guess. Forcing
  the case with an `exclude_filter`: the export exits 0, and the resulting build boots to a menu
  with no cards, no enemies and no acts — and **also exits 0**. Godot's .NET layer logs an unhandled
  exception through `GD.PushError` and lets the main loop carry on rather than aborting. So a
  content-less build is not a crash; it is a silent success. That is why the boot check greps the
  log and not `$?`, and it is the only check in the repo running against a `.pck` rather than a
  source tree.

  Two real bugs came out of actually running the thing, neither of them the one the plan predicted.
  Godot's built-in ad-hoc macOS signer (`codesign/codesign=1`) emits a signature `codesign -vvv`
  calls valid and that AMFI rejects — `failed parsing DER entitlements` — so the kernel `SIGKILL`s
  the app at launch with no stdout, no crash report, exit 137. **The Mac build could not start at
  all**, and the boot check is what caught it; Apple's own `codesign` (`3`) fixes it, at the cost of
  needing a Mac to build for Mac. And a `universal`/`arm64` macOS export is refused outright unless
  `textures/vram_compression/import_etc2_astc=true` is set project-wide — the alternative being an
  x86_64-only build putting every Apple Silicon player on Rosetta.

  `scripts/data/DataFile.cs` stayed regardless. Its null check turns a bare `NullReferenceException`
  into a line naming the file and the preset, and it lives in one place instead of six copies of the
  same eight lines — six copies of a guard is six places to forget it, and the seventh database gets
  written by copy-pasting whichever of the six was open.

  `project.godot` gained exactly one line, `config/version`, which Godot feeds into the Windows
  `.exe` version fields and the macOS `Info.plist` on its own, and which MainMenu now reads back out
  of `ProjectSettings` and displays so it can reach a bug report. Window size and mode were
  considered and deliberately left out: 1152x648 *is* the engine default and `ProjectSettings` drops
  any setting equal to its default on save, so writing it would have produced a load-bearing-looking
  line that silently deletes itself — and window mode already has an owner in `SettingsManager`,
  which applies it from `user://settings.json` on every boot.

  *Outstanding:* the macOS build is ad-hoc signed and **not notarized**. Downloaded from anywhere it
  carries `com.apple.quarantine`, and macOS 15 removed the Control-click bypass, so a playtester
  needs System Settings → Privacy & Security → Open Anyway or an `xattr -dr`. The trap is that a
  locally built `.app` has no quarantine attribute, so it never reproduces on the machine that made
  it. Fixing it is an Apple Developer Program membership, a Developer ID certificate and
  `notarytool` credentials as CI secrets — a launch decision, not a build-script one. Also open: CI
  exports Linux only (and cannot export macOS at all now that the preset needs Apple's `codesign`),
  and `CREDITS.md` ships as a file beside the binary because there is still no in-game credits
  screen.

  One thing found on the way and filed rather than fixed, because it belongs to the bullet below:
  `window/stretch/aspect="expand"` grows the canvas past 1152 units on a non-16:9 window, while
  `ScreenChrome` still centres a fixed 1152-wide panel and `MapScreen` lays out against `1152f`.
  Identical at any 16:9 size, so it is invisible on the developer's display and appears the first
  time someone maximises on a 16:10 laptop.

- A **balance and bug-bash pass** once content lands, hunting the input-during-animation bugs the
  state machine was designed to prevent.

## Sequencing notes

- **Phase 1 before everything.** It needs no new art, and it fixes eight screens with one edit. The
  game should look dramatically better within days, long before the icon conversion starts.
- **Phase 0 before Phase 1**, because Phase 1's chrome rewrite has to target a written spec, not a
  vibe — otherwise it becomes the fourth palette.
- **Phases 2 and 3 can run in parallel.** Layout works on node trees and themes; `artgen` works on
  PNGs landing at paths `ArtAssets.cs` already resolves by convention. Neither blocks the other.
- **Phase 6's internal order still holds** — scaffolding before volume, and the scaffolding half is
  now done: `Power`, rarity across the pool, and rarity-weighted offers all landed before a single
  bulk-authored card. The same rule applies one level down: the per-turn Power hook comes before
  authoring Powers, or the first dozen get retrofitted the way 50 cards would have.

## Known cost, accepted

Moving to a bitmap font gives up the Cinzel / IM Fell English illuminated-manuscript character that
`UiTheme` and `ChromeStyles` were built around — currently the most distinctive thing about the
game's identity. Pixel type will not reproduce it. The alternative (pixel sprites, high-res serif
UI) is a defensible hybrid some games ship deliberately, but it keeps a visible seam between art
and text, which is the exact problem this roadmap exists to close.

The plan is to take the loss and recover the gothic identity through **palette and ornament** —
a heavy oxblood/bronze ramp, ornate pixel borders, drop caps — rather than through typeface.
