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
- **Cards 58 → 80–120**, and more enemies per act (24 today is ~4 normal encounters' variety each).
- **Balance the three-act curve.** Authored and smoke-tested but never actually played: enemies
  scale ~1.4x per act against a 50 HP start with +8 max HP and 30% heal per act cleared. Act III
  against a deck with only ~20 card rewards is the open question. Unlock-track thresholds were
  scaled to one-act runs and now fill three times faster.
- **Wider status roster** — nine today: `Vulnerable`, `Weak`, `Strength`, `Poison`, `Metallicize`,
  `Ritual`, plus `Dexterity`, `Frail` and `Regen` from the content pass above. Keep widening it
  alongside cards that use the new statuses, not speculatively — every one of those landed with
  the cards that grant it, which is the pattern.
- **Close the relic-hook gap.** `SimpleHookEffectRelic` covers 2 of the 7 hooks `RelicBehavior`
  defines. Extending it to the other five makes future simple relics data rows instead of classes.
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
  17 suites, 661 checks, plus `artgen validate` over 121 assets. Godot is pinned to the same
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

  *Outstanding:* coverage is the deeper gap — drag/targeting, the project's own stated
  highest-risk area, still has only the target-lock glow asserted. CI makes that gap cheaper to
  close, it does not close it.
- **Packaged export pass** on Windows/Mac/Linux, and a **balance and bug-bash pass** once content
  lands, hunting the input-during-animation bugs the state machine was designed to prevent.

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
