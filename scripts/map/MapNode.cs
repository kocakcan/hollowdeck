using System.Collections.Generic;

namespace Hollowdeck.Map;

// Generated once per run by MapGenerator and stored in RunState.MapNodes -
// not loaded from JSON like CardDefinition/EnemyDefinition, since the graph
// shape itself is randomized per-run rather than authored content.
public class MapNode
{
    public string Id = "";
    public int Floor;
    public float Column;
    public MapNodeType Type;
    public List<string> NextNodeIds = new();

    // Only populated for Combat/Elite/Boss nodes.
    public List<string> EnemyIds = new();

    // True while the map still draws this node as a "?" instead of its type.
    //
    // Type is always the truth - the roll happens in MapGenerator like every
    // other node's, and concealment only withholds it from the player until
    // they walk in. Rolling at *visit* time instead would break two things
    // quietly: BalanceModel reads Type in a dozen places and would count an
    // as-yet-unrolled node as nothing at all (no fight, no gold, no reward
    // card), and since Combat is not in RunManager.AutoSaveScreens a "?" that
    // turned out to be an Elite would never be persisted before the fight, so
    // quitting mid-fight would re-roll it.
    //
    // Cleared by MapScreen.EnterNode, so the trail behind the player shows
    // what each "?" turned out to be.
    public bool Concealed;
}
