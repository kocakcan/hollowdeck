# Hollowdeck Roadmap

## Where things stand

The core loop is complete and playable end-to-end: a branching map, combat with telegraphed
intents, relics, potions, events, a shop and gold economy, mid-run save/resume, a score-driven
unlock track that persists across runs, and a full art/typography/audio pass. Every one of the
11 screens is wired to real data. See `CLAUDE.md` for the architecture and the current
content counts.

What's left is listed below. Grepping for `TODO`/`FIXME` mostly comes up empty — these are
*omissions* (things never started), not flagged half-finished work, so they don't surface by
searching the code. They come from comparing the running game against the stated design and
against genre expectations, and they're ordered by what most affects whether a playtester would
call this a finished game.

## Content breadth

The single biggest gap is now cards: 30 against a stated target of 80–120. Acts and enemies have
landed — three acts, 24 enemies, six bosses (two per act, picked from the act's pool by the run
seed).

- **Card scaffolding before card volume.** `CardType` is still `{Attack, Skill}` — no `Power`
  type exists. `Rarity` exists on `CardDefinition` and already drives card border colour in
  `ChromeStyles.CardFrameStyle`, but no card in `cards.json` declares one (everything defaults to
  Common) and neither shop nor reward weighting reads it — see the note at `RunScore.cs:17`,
  which can't award a rare-card bonus for exactly this reason. Add `Power`, populate `rarity` in
  the data, and wire rarity into shop/reward weighting *before* bulk-authoring cards, so new
  content lands against the final shape rather than needing a retrofit.
  Card upgrades are done (`CardUpgrade.cs` + the Rest-screen smith, formula-driven `<id>+`).
- **Balance the three-act curve.** The act content is authored but only smoke-tested, not played:
  enemy numbers scale ~1.4x per act and each cleared act grants `clearMaxHpBonus` (+8) and
  `clearHealPercent` (30%) against a 50 HP start. Those dials live in `data/acts/acts.json`; they
  need a real playthrough, particularly act III against a deck that only had ~20 card rewards to
  grow with. The unlock track's thresholds (up to 5,500) were also scaled to a one-act run and now
  fill faster, since a full run scores three bosses instead of one.
- **More enemies per act.** 24 enemies across three acts (12 normal, 6 elite, 6 boss) means ~4
  normal encounters' worth of variety per act; more rows in `enemies.json` plus entries in that
  act's `normalEncounters` is all it takes.
- **Wider status roster.** Only 4 statuses (`Vulnerable`, `Weak`, `Strength`, `Poison`) — no
  Frail/Dexterity/Thorns/Artifact. Widening this expands card design space, but only do it
  alongside cards and enemies that actually use the new statuses, not speculatively.
- **Close the relic-hook gap.** `SimpleHookEffectRelic` (data-driven, no code needed) still
  covers only 2 of the 7 hooks `RelicBehavior` defines — `OnCombatStart` and `OnTurnStart`.
  Extending it to `OnTurnEnd`, `OnCardPlayed`, `OnDamageDealt`, `OnDamageTaken` and
  `OnCombatEnd` makes future simple relics on those hooks data rows instead of new C# classes.
  Worth doing before authoring many more relics.

## Settings and meta depth

- **Input actions.** There is no `[input]` section in `project.godot` at all, so keybind
  remapping isn't just missing UI — there's no `InputMap` action layer to remap. Define real
  input actions before promising rebinding. Resolution/windowed-size options are also still
  missing (volume sliders and the Music/SFX split are done).
- **Deepen the unlock track.** The track's 14 rungs unlock 10 cards and 4 relics; a strong player
  exhausts it. As content lands, either gate more of it behind the track or add a second
  progression axis (e.g. ascension-style run modifiers) so the meta-loop has legs.

## Polish

- **Scene transitions.** `RunManager.ChangeScreen` is still a hard `ChangeSceneToFile` cut with
  no fade. Combat, map, reward and the card/status/background layers all have tweens now; the
  screen-to-screen seam is the remaining instant snap.

## Ship readiness

- **CI.** The 14 `scripts/debug/*SmokeTest` suites (332 checks) run from one command that fails
  on nonzero exit — `tools/run-smoke-tests.sh`, catalogued in the `smoke-test` skill. What's
  missing is the trigger: there is no `.github/` at all. Point a workflow at that script so it
  runs on every push instead of being remembered by hand. Coverage is the deeper gap — combat's
  drag/targeting, the project's own stated highest-risk area, has only the target-lock glow
  asserted, and no screen has visual-regression coverage (`scenes/debug/ScreenShot.tscn` renders
  them, but a human still has to look). Consider GUT only if the custom harness starts showing
  real limits.
- **Packaged export pass.** Verify a real exported build (not just editor play) on
  Windows/Mac/Linux — no console errors, all autoloads and `res://` paths resolving outside the
  editor.
- **Balance and bug-bash pass** once the content work lands — playtest full runs specifically
  hunting the input-during-animation and mid-combat-crash bugs the state-machine design was
  meant to prevent.

## Sequencing notes

- Content breadth is the gating item for everything else: the unlock track can't be deepened
  meaningfully without more to unlock. The three-act balance pass is the exception - the acts
  exist now, so playing them is the only way to find out whether the curve works.
- Within content breadth, do the scaffolding (Power type, rarity in data, remaining relic hooks)
  before volume. Authoring 50 cards against the current shape and then retrofitting rarity and
  Power is the expensive order.
- Ship-readiness items are independent of the rest and can run in parallel — CI in particular is
  cheap now and gets more valuable with every card added.
