using System.Collections.Generic;

namespace Hollowdeck.Data;

// One act (chapter) of a run: how long its map is, which enemies it draws
// from, which boss can end it, and what it looks like. Authored in
// data/acts/acts.json, in play order - MapGenerator used to hardcode all of
// this for a single act, which meant a second act could only exist as more C#.
//
// Encounters are lists of enemy ids, one list per possible group ("two slimes"
// is one encounter, not two). Ids are resolved against EnemyDatabase at
// generation time; ActSmokeTest checks every id here actually exists so a typo
// fails a test rather than crashing a run mid-map.
public class ActDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // Includes the forced Rest floor and the Boss floor at the end (see
    // MapGenerator.BuildFloor), so the smallest sane value is 3.
    public int FloorCount { get; set; } = 8;

    // Backdrop tile names under assets/backgrounds/ plus their tint, so each
    // act reads as a different place. Tints are hex strings parsed with
    // Color.FromString, which falls back to a default rather than throwing on
    // a malformed value - same tolerance the rest of the data layer has.
    //
    // The tints are neutral greys now and that is a change worth knowing about:
    // the tiles used to be seven sourced dungeon floors shared between the
    // acts, so the *tint* was the only thing that made act II warm. They are
    // generated per act now (tools/artgen/src/icons/backgrounds.rs), authored
    // in that act's own ramp family - so a tint carrying a hue as well would be
    // tinting twice, and the second one lands off the ramp. What is left for
    // this field is brightness per surface, which is a real dial: combat is
    // dimmer than the map because ten cards and a HUD sit on top of it.
    // The act's backdrop set. Its wall, plinth and pillar are derived from
    // this prefix (`ward_wall`, `ward_plinth`, `ward_pillar`) rather than
    // authored one field each: those three are one room, and an act that could
    // author act I's wall against act III's plinth is expressing something
    // nobody wants. The floors stay explicit below because they genuinely vary
    // per surface.
    public string Backdrop { get; set; } = "ward";

    public string MapBackground { get; set; } = "ward_flags";
    public string MapTint { get; set; } = "e0e0e0";
    public string CombatBackground { get; set; } = "ward_drowned";
    public string CombatTint { get; set; } = "b4b4b4";

    // The floor for the six room screens - Reward, Rest, Shop, Event, Treasure
    // and RunEnd. One pair rather than one per screen: those screens differ in
    // what they offer, not in where they are, and six fields would be six
    // chances for act III's shop to stay in act I.
    public string RoomBackground { get; set; } = "ward_cistern";
    public string RoomTint { get; set; } = "cccccc";

    // The colour of the air, and the colour the edges fall off to. Both are
    // read by ScreenBackground on every screen, which is why they are one pair
    // for the act rather than a pair per surface: a place has one atmosphere.
    // VignetteTint was black everywhere before acts authored it, and it is the
    // single largest per-act lever there is - the corners are most of the
    // screen and they cost nothing to paint.
    public string HazeTint { get; set; } = "d9d9e6";
    public string VignetteTint { get; set; } = "07060a";

    public List<List<string>> NormalEncounters { get; set; } = new();
    public List<List<string>> EliteEncounters { get; set; } = new();

    // The act's possible bosses. One is picked per run from RngStreams.Map, so
    // which boss ends an act is part of the seed like the map shape is.
    public List<string> BossIds { get; set; } = new();

    // Combat rewards for this act's nodes. Boss gold is 0 in the final act
    // (nothing left to spend it on) and nonzero earlier, where the next act's
    // shops still matter.
    public int NormalGoldBase { get; set; } = 20;
    public int GoldPerEnemy { get; set; } = 5;
    public int EliteGold { get; set; } = 45;
    public int BossGold { get; set; }

    // Chance out of 100 that a won fight offers a potion, rolled off
    // RngStreams.Drops in CombatScreen.RollPotionDrop. Per act rather than one
    // constant for the same reason the gold above is: how much a room pays is
    // a pacing dial, and act I is where the belt is emptiest. There is no Boss
    // field because a boss never rolls - it already guarantees a relic and an
    // act clear.
    //
    // The defaults are 0, and that is the wrong reading of an act that forgot
    // the key - unlike every other absent-is-zero field in the data layer, a
    // silent 0 here disables the whole feature for that act with nothing
    // thrown and every suite green. ActSmokeTest asserts both are authored
    // above zero for exactly that reason.
    public int PotionDropPercent { get; set; }
    public int ElitePotionDropPercent { get; set; }

    // Applied by RunState.AdvanceAct when this act is cleared. A run that has
    // to survive three acts on the 50 HP one act was tuned for would be
    // hopeless, so clearing an act raises the ceiling and heals - currently to
    // full, the genre's default for a boss kill. Kept as a percentage in data
    // rather than a hard-coded full heal because it is the main dial for pacing
    // a longer run, and the rung an ascension ladder would turn back down.
    public int ClearMaxHpBonus { get; set; }
    public int ClearHealPercent { get; set; }
}
