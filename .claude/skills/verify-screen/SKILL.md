---
name: verify-screen
description: Screenshot any Hollowdeck screen and look at it. Use whenever a change touches UI, a .tscn, layout, colours, or card/relic rendering, and ALWAYS when a bug is described visually - "looks dimmed", "greyed out", "overlaps", "cut off", "wrong colour", "doesn't show up", "see what X looks like". Renders the real scene with realistic data seeded and saves a PNG you can read directly.
---

# Verify a screen visually

Do not guess what a screen looks like, and do not ask the user for a screenshot. Render it.

```bash
dotnet build
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . \
    scenes/debug/ScreenShot.tscn -- shop reward unlocks
```

Then read the PNGs with the Read tool:

```
~/Library/Application Support/Godot/app_userdata/Hollowdeck/shot_<name>.png
```

With no screen names it shoots all of them. Unknown names exit 1 and list the valid ones.

## Screens

`combat` `combatfull` `combatintents` `combatsummon` `combattarget` `combatvocab` `combat2`
`combat3` `reward` `rewardactclear` `rewardpotion` `rewardpotionfull` `rewardcards`
`rewardbossrelic` `rewardskip` `shop` `shopfull` `shopremove` `map` `map2` `map3` `mapfull` `rest`
`restupgrade` `treasure` `event` `eventpicker` `unlocks` `library` `libraryrelics`
`libraryinspectcard` `libraryinspectrelic` `runend` `mainmenu` `settings` `deckpopup` `fade`

`map2`/`map3` and `combat2`/`combat3` are the later acts — each has its own backdrop tint, title,
boss sprites and floor count, none of which act 1's shots show. `map3` is also the longest map (10
floors), which is where node layout runs out of horizontal room first.

`mapfull` is the map's worst case and the one none of the other three can show: 13 relics, which
wraps the run-status block's icon grid to three rows. That block grows downward out of the top-left
corner, and the node band used to start at a fixed `y=116` regardless — one row's worth of
clearance — so from the seventh relic on it was drawn straight over the top-left nodes. Reach for
it for anything touching `MapScreen` layout or `ScreenChrome`'s block.

`combatfull` is the combat HUD's worst case: 3 enemies, 8 relics, 3 potions. Plain `combat` is 2
enemies and 1 relic, so it cannot show top-left chrome colliding with the enemy row — which is a
real bug that shipped, the relic bar growing rightward across the leftmost enemy and painting over
its target-lock glow. Reach for this one for any HUD or enemy-row layout change.

`shopfull` is the same idea for the shop: 13 relics against a card row that starts at x=194. The
relic grid ran to x=280 at `ScreenChrome`'s six-column default and painted over the first card's
name banner, which is why `ShopScreen` asks for three columns. Reach for it for anything touching
that screen's layout or `ScreenChrome`'s block.

`combattarget` is the only shot in `AwaitingTarget`, the one state `TargetHintLabel` is visible in.
The hint used to sit inside the enemy row, writing its instructions across the name and HP bar of
the enemy being aimed at; its band between the enemy row and the top of the fan is narrow enough
that a second line does not fit, so shoot this after touching either boundary.

`combatintents` is the only shot carrying an enemy's intent hover panel: it pins three telegraph
shapes plain `combat` can't roll and target-locks the last enemy, which is how a keyboard player
raises that panel. Reach for it after any `HoverTooltip` or `EnemyView` change — the panel used to
place itself over the hand, which no assertion about its text could see. `rewardactclear` is the
boss-reward variant of `reward`, carrying the longest line the title block ever holds.

The reward screen is a list with a modal over it, so it takes four shots rather than one.
`rewardpotion` is the list at its fullest (all four row kinds at once); `rewardpotionfull` is the
same list against a full belt, which is the refused-row state no assertion can look at.
`rewardcards` and `rewardbossrelic` are the modal's two views — the card fan and the boss relic
picker — each reachable only by pressing its row, so both use the `AfterReady` hook. Shoot
`rewardbossrelic` for anything touching that overlay: the three tiles share an 800px band, and a
relic name is the one thing in there that can grow.

`rewardskip` is the card row's second line carrying a skip streak at the cap — the only rung that
also prints "(max)", so the longest form that line takes. Plain `reward` is the same row at rung 0,
holding the other string it can ever show; between them the two cover both. The odds in it are
computed from `CardPool.WeightOf` rather than authored, so this shot moves when the ladder is
retuned — which is the point of having it.

`deckpopup` opens the pile popup over the map with a 13-card deck, since the popup is spawned on
demand by `DeckViewButtons` rather than being a screen of its own. `restupgrade` is the same idea
for the rest site's second view — it presses Smith first, and seeds seven un-upgraded cards so the
shot shows the picker's grid wrapping rather than one tidy row.

`eventpicker` is the event screen's card grid, which the two `ICardPickerOutcome` outcomes open.
It searches for an RNG seed that rolls an event carrying a picker (a search, not a magic number:
authoring one more event shifts every index) and then presses that choice. Worth shooting for any
`CardPicker` change — the first version of this screen let the event's own text show through the
gaps in the grid, which no assertion caught and the shot showed instantly.

`fade` holds the cross-screen transition's cover at a fixed 62% over the map. Deliberately not the
live tween — 60 settle frames is longer than the whole fade, so a real `Play()` would always be
captured already finished. `TransitionSmokeTest` proves the alpha ramps; this shot is for what a
test can't see, that the cover spans the viewport and sits above the screen's own chrome. The cover
lives on the `RunManager` autoload and survives between shots, so `ResetRunState` puts it back down
before every screen.

Each one instantiates the real `.tscn` the way `RunManager.ChangeScreen` would, with the global
statics that screen's `_Ready` reads already seeded (`RunState`, `RewardContext`,
`CombatContext`, `RunEndContext`, `RngStreams`). That seeding is the whole point — an
un-seeded screen renders empty or throws mid-`_Ready`, and the screenshot is worthless.

## Rules

- **`dotnet build` first.** C# is compiled ahead of time; without it you screenshot the previous
  build and "confirm" a fix that isn't in the binary.
- **Windowed, never `--headless`.** `--headless` forces a dummy renderer and
  `GetViewport().GetTexture()` comes back empty. Same constraint `ArtScreenshot.cs`,
  `AnimationScreenshot.cs` and `StyleReferenceScreen.cs` already document.
- **Godot is not on `PATH`** — use the full path above. `timeout(1)` does not exist on this
  machine either; don't wrap the command in it.
- Shots are at the project's **1152x648 design size, and that is exactly what a player sees** at
  any window size. The project stretches `canvas_items` with aspect **`keep`** (ART_SPEC §4), so
  the canvas is letterboxed to 1152x648 rather than grown — a shot is the whole picture, and
  anything clipped in one is clipped in the game. This used to be `expand`, where a larger window
  really did reveal more canvas and a `--resolution 1920x1080` run was worth doing to check; that
  caveat is gone, and so is the reason to pass the flag.

## Adding a screen

One entry in the `Fixtures` dictionary in `scripts/debug/ScreenShot.cs`: scene path, a seed method,
and optionally an `Action<Node>` that runs after the screen's `_Ready` and receives the
instantiated screen. Use that third arg for anything that only exists once the screen has built
itself — `AfterCombatReady` plays real cards through `TryPlayCard` to get live Strength/Vulnerable
into the card text, and `deckpopup` uses it to open a popup that no screen owns.

Keep the seed realistic (a mid-run deck, believable gold) — a screenshot with empty fixtures hides
exactly the layout bugs this exists to catch. Seed the *worst* case, not the average one, when the
screen has a growable region: `combat` looked fine for three phases while `combatfull`'s relic
count was the thing that broke it.

## Two things the harness handles for you

- **Saves are protected.** `RunEndScreen._Ready` banks a run result and deletes
  `run_save.json`; `TreasureScreen` grants a relic; the `MetaProgressionManager` autoload can
  migrate the meta save just by launching. `ScreenShot` copies `meta_progression.json` and
  `run_save.json` aside and restores them in a `finally`. Verified: shooting `runend` leaves both
  files byte-identical. Never bypass this by hand-rolling a one-off harness.
- **Shots are reproducible.** `RngStreams.Init` is re-seeded with a fixed value before every
  screen, so shop stock, the treasure relic and the rolled event are the same every run. Most
  screens are byte-identical across runs; the ones with looping idle animations (`combat*`, `map*`,
  `reward` — card sway, enemy bob, node pulse) differ slightly because the tween phase moves. The
  *content* is still deterministic. Don't chase that as a bug.

## Worth knowing

This exists because three bugs in one session — cards rendering dimmed in the shop and reward
screens, damage numbers inflated by a finished fight's Strength, and overlapping rows on the
unlocks screen — were only found because the user screenshotted them by hand. All three are
visible instantly in a shot.
