namespace Hollowdeck.Run;

// Everything a finished run needs to be scored (see RunScore). Accumulated
// across the whole run rather than recomputed at the end, because most of it
// - kills, flawless fights, the biggest hit landed - is only observable while
// a fight is actually happening (CombatManager tracks it per-fight and
// CombatScreen folds it in here when the fight ends).
//
// Public fields, not properties: this is serialized straight into
// RunSaveData, whose MapNode list already forces IncludeFields on the
// JsonSerializerOptions there.
public class RunStats
{
    public int EnemiesSlain;
    public int ElitesSlain;
    public int BossesSlain;

    // Elites/bosses killed without the player losing a single HP in that
    // fight - Slay the Spire's Champion/Perfect bonuses.
    public int PerfectElites;
    public int PerfectBosses;

    public int MaxFloorReached;
    public int EventRoomsVisited;

    // One-shot achievements: once earned anywhere in the run they stay
    // earned, so they're flags rather than counters.
    public bool OverkillEarned;
    public bool ComboEarned;
}
