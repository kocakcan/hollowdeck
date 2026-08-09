using System;

namespace Hollowdeck.Run;

// Separate seeded streams per CLAUDE.md risk #2, so combat shuffles, enemy
// AI rolls, shop stock, map shape and post-fight drops can't desync each other
// - drawing one extra card must not change what the next shop offers. A new
// system that needs randomness gets its own stream here rather than borrowing
// one.
//
// The derivation is per-stream rather than sequential, which is what makes
// appending one cost nothing: adding Drops at +4 left every seed's map, shop
// and shuffles exactly where they were.
public static class RngStreams
{
    public static Random Combat { get; private set; } = new(0);
    public static Random EnemyAI { get; private set; } = new(1);
    public static Random Shop { get; private set; } = new(2);
    public static Random Map { get; private set; } = new(3);

    // Post-fight reward rolls: whether a won fight drops a potion, and which.
    // Named for the system rather than for RewardScreen, so a later non-combat
    // drop has somewhere to go instead of looking like it needs a sixth
    // stream. Deliberately NOT Shop: whether the player got a potion after a
    // fight must not shift what the next shop stocks, which is risk #2 stated
    // exactly. Deliberately not Combat either - a drop is settled after the
    // fight, and folding it in would let an extra shuffle move it.
    public static Random Drops { get; private set; } = new(4);

    public static void Init(int runSeed)
    {
        Combat = new Random(runSeed);
        EnemyAI = new Random(unchecked(runSeed * 397 + 1));
        Shop = new Random(unchecked(runSeed * 397 + 2));
        Map = new Random(unchecked(runSeed * 397 + 3));
        Drops = new Random(unchecked(runSeed * 397 + 4));
    }
}
