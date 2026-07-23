# Hollowdeck Roadmap

## Where things stand

Phases 0–4 from `CLAUDE.md`/`hollowdeck.md` are genuinely built, not stubbed: the
data-driven effect system, the explicit `CombatManager` state machine, separated
RNG streams, tolerant meta-progression save/load, and a branching map generator
all work as designed and are solid to build on. Every one of the 10 screens is
wired to real data, not a placeholder.

But "the planned phases are done" isn't the same as "this is a game" — an audit
of the current code turned up gaps that matter more to a player than anything
left in the original phase list. This roadmap picks up from there. It's ordered
by what most affects whether a playtester would call this a real game, not by
what's easiest.

Grepping for `TODO`/`FIXME` mostly comes up empty — the gaps below are
*omissions* (things never started), not flagged half-finished work, so they
don't show up by searching the code. They were found by comparing the running
game against the stated design (`CLAUDE.md`, `hollowdeck.md`) and against genre
expectations.

## Phase 5 — Core loop completeness

The loop is playable end-to-end but has holes a player will hit almost
immediately. Close these before adding more content on top.

- **Mid-run save/resume.** `RunState` (deck, HP, map position, relics, gold) is
  a `static class` living only in memory — quitting the app loses the run.
  Only `MetaProgressionManager` and `SettingsManager` persist to disk. Add
  `RunState` serialization (reuse the tolerant-JSON pattern already used for
  meta saves) plus a "Continue Run" option on `MainMenu`, and clear the save on
  victory/defeat.
- **Event nodes.** Named in `CLAUDE.md`'s and `hollowdeck.md`'s screen-flow spec
  but entirely absent — not in `MapNodeType`, not in `RunManager.ScenePaths`.
  Add the node type, map-generator weighting, and a minimal Event screen
  (start with simple choice-based text events with effect-spec outcomes so no
  new content pipeline is needed).
- **Turn/combat pacing.** Multi-enemy turns resolve synchronously in one frame
  with no delay between actors; turn transitions are an instant label flip.
  Add short delays/tweens between enemy actions in `ResolveNextEnemyTurn` and a
  brief transition beat between player/enemy turns — this is likely the single
  biggest "feels unfinished" cue in actual play.
- **Unify or justify the split targeting model.** Cards target via
  drag-and-drop hit-testing; potions target via the `AwaitingTarget` state and
  click-to-select. Pick one model (recommend: keep drag for cards, but add a
  visual target-lock affordance — highlight/glow the enemy under the cursor
  during a drag) rather than leaving two silently different interaction
  patterns.
- **Hand overflow handling.** `CombatScreen.RefreshHand` computes fixed-width
  card positions by index with no wrap/scale-down — a hand of 8+ cards (very
  possible with draw effects/relics) will run off-screen. Add a shrink-to-fit
  or fan layout once hand size crosses a threshold.

## Phase 6 — Content breadth

Only after Phase 5's holes are closed — more content just means more surface
area hitting the same bugs.

- **Cards toward the stated target.** Currently 30 of the planned 80–120, and
  entirely Attack/Skill — no `Power` card type exists in `CardDefinition` at
  all, no rarity tier, no upgrade mechanic (`RestScreen` explicitly defers
  this), no card-removal service. Add `Power` as a third `CardType`, add a
  `Rarity` field (Common/Uncommon/Rare) actually used by shop/reward weighting,
  and add card upgrades (`CardInstance`-level, doubling down on the existing
  instance-vs-definition split already used for save safety).
  Expand card count incrementally against this scaffolding rather than
  bulk-authoring 50 cards against the current no-Power/no-rarity/no-upgrade
  shape.
- **Enemy variety.** 5 enemies total (2 normal, 2 elite, 1 boss), single act.
  Add more normal/elite variety before considering a second act — the map
  generator and RNG streams already support it, this is purely an authoring
  gap.
- **Wider status roster.** Only 4 statuses (`Vulnerable`, `Weak`, `Strength`,
  `Poison`) — no Frail/Dexterity/Thorns/Artifact. Widening this expands card
  design space, but only do it alongside new cards/enemies that actually use
  the new statuses, not speculatively.
- **Close the relic-hook gap.** `SimpleHookEffectRelic` (data-driven, no code
  needed) only supports 2 of the 7 hooks `RelicBehavior` defines
  (`OnCombatStart`, `OnTurnStart`). Extending it to cover `OnTurnEnd`,
  `OnCardPlayed`, `OnDamageDealt`, `OnDamageTaken`, `OnCombatEnd` means future
  simple relics on those hooks are data rows instead of new C# classes —
  worth doing before authoring many more relics.

## Phase 7 — Audio & visual polish

Currently zero audio (no `AudioStreamPlayer` nodes, no sound assets, no music —
the only audio-adjacent code is the volume slider with nothing plugged into
it) and minimal juice (6 tween call sites total, all in
`CombatScreen`/`CardView`/`FloatingText`; every other screen and event —
scene transitions, HP-bar changes, card draw, victory/defeat, shop purchase —
is an instant state snap with no default Godot theme applied anywhere).

- Add music (menu/map/combat/boss/victory-defeat) and core SFX (card play,
  damage, block, potion use, enemy death, UI clicks) via a small
  `AudioManager` autoload alongside the existing three.
- Wire the settings audio bus properly — split Master into Music/SFX buses so
  the two-slider settings surface (Phase 8) has something real to control.
- Apply a Godot `Theme` resource across all screens instead of default gray UI;
  add map-node icons (replace the emoji-as-icon placeholder in `EnemyView`);
  add scene-transition fades in `RunManager.ChangeScreen` instead of the hard
  `ChangeSceneToFile` cut; tween HP bars instead of snapping label text.

## Phase 8 — Settings, meta depth, and stats

- **Settings surface.** Currently 2 options (master volume, fullscreen toggle)
  and no `[input]` section exists in `project.godot` at all, so keybind
  remapping isn't just missing UI — there's no `InputMap` action layer to
  remap. Add resolution/windowed-size options, split volume sliders (depends
  on Phase 7's bus work), and define real input actions before promising
  rebinding.
- **Deepen the unlock system.** Only 4 of 22 relics are ever locked; all
  cards/potions are unlocked from a fresh save, so a player runs out of things
  to unlock after ~4 shard grants. Either gate more content behind Shards as
  content grows in Phase 6, or add a different progression axis (e.g.
  ascension-style modifiers) so the meta-loop has legs.
- **Run-end stats.** `RunEndScreen` currently shows only win/lose + seed. Track
  and display basic run stats (floors reached, damage dealt, cards played,
  turns taken) — cheap to add, meaningfully improves the "did that run matter"
  feeling.

## Phase 9 — Ship readiness

- **Promote smoke tests to real CI.** The 7 existing `scripts/debug/*SmokeTest`
  files are solid but manual (`godot --headless scenes/debug/<Name>.tscn`,
  read stdout by hand) and only 3 of 10 screens have UI-level coverage
  (Reward/Shop/Treasure) — Combat's drag/targeting, the project's own stated
  highest-risk area, has zero UI-layer coverage. At minimum, wire the existing
  headless smoke tests into a CI script that runs on every push and fails the
  build on nonzero exit; consider GUT only if the custom harness starts
  showing real limits.
- **Packaged export pass.** Verify a real exported build (not just editor
  play) on Windows/Mac/Linux per `CLAUDE.md`'s verification standard — no
  console errors, all autoloads/paths resolve outside the editor.
- **Balance/bug-bash pass** once Phase 6's content lands — playtest full runs
  specifically hunting for the input-during-animation and mid-combat-crash
  bugs the state-machine design was meant to prevent.

## Sequencing notes

- Phases 5 and 6 can partially overlap once Phase 5's save/resume and Event
  work stabilizes the loop, but don't start bulk content authoring (Phase 6)
  while the core loop still has known holes (Phase 5) — new content just
  multiplies surface area on top of bugs that are cheaper to fix now.
- Phase 7 (audio/visual) is here rather than earlier because juice depends on
  the *set* of events being animated (turn pacing, card draw, etc.) — a lot of
  Phase 7 depends on default behavior added in Phase 5.
- Phase 8 and 9 are largely independent of each other and can run in
  parallel with the tail of Phase 6/7.
