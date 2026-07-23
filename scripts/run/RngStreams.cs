using System;

namespace Hollowdeck.Run;

// Separate seeded streams per hollowdeck.md risk #2, so combat shuffles and
// enemy AI rolls don't desync from each other. Map-gen and cosmetic-only
// streams aren't needed until later phases actually have those systems.
public static class RngStreams
{
    public static Random Combat { get; private set; } = new(0);
    public static Random EnemyAI { get; private set; } = new(1);

    public static void Init(int runSeed)
    {
        Combat = new Random(runSeed);
        EnemyAI = new Random(unchecked(runSeed * 397 + 1));
    }
}
