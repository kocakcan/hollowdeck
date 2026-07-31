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

`combat` `combatfull` `combat2` `combat3` `reward` `shop` `map` `map2` `map3` `rest` `restupgrade`
`treasure` `event` `unlocks` `runend` `mainmenu` `settings` `deckpopup`

`map2`/`map3` and `combat2`/`combat3` are the later acts — each has its own backdrop tint, title,
boss sprites and floor count, none of which act 1's shots show. `map3` is also the longest map (10
floors), which is where node layout runs out of horizontal room first.

`combatfull` is the combat HUD's worst case: 3 enemies, 8 relics, 3 potions. Plain `combat` is 2
enemies and 1 relic, so it cannot show top-left chrome colliding with the enemy row — which is a
real bug that shipped, the relic bar growing rightward across the leftmost enemy and painting over
its target-lock glow. Reach for this one for any HUD or enemy-row layout change.

`deckpopup` opens the pile popup over the map with a 13-card deck, since the popup is spawned on
demand by `DeckViewButtons` rather than being a screen of its own. `restupgrade` is the same idea
for the rest site's second view — it presses Smith first, and seeds seven un-upgraded cards so the
shot shows the picker's grid wrapping rather than one tidy row.

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
- Shots are at the project's **1152x648 design size**. The user's real window is much larger and
  the project stretches (`canvas_items` / `expand`), so content clipped at the bottom of a shot
  may not clip in their game. Pass `--resolution 1920x1080` before the scene path to check a
  larger window.

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
