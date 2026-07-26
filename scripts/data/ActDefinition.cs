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
    public string MapBackground { get; set; } = "black_cobalt";
    public string MapTint { get; set; } = "d9d9e6";
    public string CombatBackground { get; set; } = "crypt";
    public string CombatTint { get; set; } = "99949e";

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

    // Applied by RunState.AdvanceAct when this act is cleared. A run that has
    // to survive three acts on the 50 HP one act was tuned for would be
    // hopeless, so clearing an act raises the ceiling and heals some of the
    // damage - kept in data because it's the main dial for pacing a longer run.
    public int ClearMaxHpBonus { get; set; }
    public int ClearHealPercent { get; set; }
}
