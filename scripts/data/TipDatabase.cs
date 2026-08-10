using System.Collections.Generic;

namespace Hollowdeck.Data;

public static class TipDatabase
{
    private static readonly List<TipDefinition> Tips = new();

    public static void LoadAll()
    {
        Tips.Clear();
        Tips.AddRange(DataFile.LoadList<TipDefinition>("res://data/tips/tips.json"));
    }

    public static IReadOnlyList<TipDefinition> All => Tips;

    /// The tip for the nth reward screen of a run - a *rotation*, not a roll.
    ///
    /// Deliberately not an RngStreams draw, and appending a sixth stream would
    /// have been free, so the reasons are worth stating:
    ///
    ///  - A stream's position is not serialized and RngStreams.Init runs again
    ///    on load, so anything drawn outside the deterministic run pipeline
    ///    replays differently after a resume. That is risk 2 approached from
    ///    the wrong side.
    ///  - Five ScreenShot fixtures re-render the reward screen. A rolled tip
    ///    makes all five non-reproducible, which is exactly what the fixtures
    ///    exist to stop.
    ///  - A rotation is better behaviour anyway: every tip is seen once before
    ///    any repeats, and the same tip can never land twice running - which a
    ///    uniform roll over this many rows does several percent of the time.
    ///
    /// The seed offset is what stops every run opening on the same tip, and
    /// `visit` advances it, so two players on the same seed see the same
    /// sequence. Returns null when nothing is authored rather than throwing:
    /// a suite that skips the bootstrap should render no tip, not fall over.
    public static TipDefinition? ForVisit(int seed, int visit)
    {
        if (Tips.Count == 0) return null;
        int index = (int)(((long)seed % Tips.Count + visit) % Tips.Count);
        if (index < 0) index += Tips.Count;
        return Tips[index];
    }
}
