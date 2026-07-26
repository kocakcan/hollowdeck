using System;

namespace Hollowdeck.Run;

// Separate seeded streams per CLAUDE.md risk #2, so combat shuffles, enemy
// AI rolls, shop stock and map shape can't desync each other - drawing one
// extra card must not change what the next shop offers. A new system that
// needs randomness gets its own stream here rather than borrowing one.
public static class RngStreams
{
    public static Random Combat { get; private set; } = new(0);
    public static Random EnemyAI { get; private set; } = new(1);
    public static Random Shop { get; private set; } = new(2);
    public static Random Map { get; private set; } = new(3);

    public static void Init(int runSeed)
    {
        Combat = new Random(runSeed);
        EnemyAI = new Random(unchecked(runSeed * 397 + 1));
        Shop = new Random(unchecked(runSeed * 397 + 2));
        Map = new Random(unchecked(runSeed * 397 + 3));
    }
}
