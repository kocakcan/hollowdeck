using Godot;

namespace Hollowdeck.Run;

// Autoload singleton for cross-run meta-progression (unlocks, currency).
// Deliberately separate from RunManager/run state - different lifetime and
// versioning needs. Phase 3 adds real persistence.
public partial class MetaProgressionManager : Node
{
    public static MetaProgressionManager Instance { get; private set; }

    public override void _Ready() => Instance = this;
}
