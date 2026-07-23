using System.Collections.Generic;

namespace Hollowdeck.Combat;

// Data mailbox for the next encounter. Not an autoload Node - it doesn't
// need _Ready/_Process, just needs to survive the scene destruction that
// RunManager.ChangeScreen(Combat) causes. Plain C# statics persist across
// Godot scene changes (only the node tree is torn down, not the CLR).
public static class CombatContext
{
    public static List<string> EnemyDefinitionIds { get; set; } = new();
    public static bool IsElite { get; set; }
    public static bool IsBoss { get; set; }
    public static int GoldReward { get; set; }
}
