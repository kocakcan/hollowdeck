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
}
