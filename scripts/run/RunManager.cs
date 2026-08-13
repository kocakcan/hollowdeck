using Godot;
using System;
using System.Collections.Generic;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Autoload singleton. Owns top-level run/screen state and drives scene
// transitions. ScreenState documents the full intended flow from
// CLAUDE.md; ScenePaths only maps states that have a real scene so far.
public partial class RunManager : Node
{
    public static RunManager Instance { get; private set; } = null!;

    public enum ScreenState
    {
        MainMenu, RunSetup, Map, Combat, Event, Rest, Shop,
        Treasure, Reward, Victory, Defeat, MetaProgression, Library, Settings,
    }

    private static readonly Dictionary<ScreenState, string> ScenePaths = new()
    {
        { ScreenState.MainMenu, "res://scenes/MainMenu.tscn" },
        { ScreenState.Map, "res://scenes/MapScreen.tscn" },
        { ScreenState.Combat, "res://scenes/CombatScreen.tscn" },
        { ScreenState.Rest, "res://scenes/RestScreen.tscn" },
        { ScreenState.Shop, "res://scenes/ShopScreen.tscn" },
        { ScreenState.Treasure, "res://scenes/TreasureScreen.tscn" },
        { ScreenState.Reward, "res://scenes/RewardScreen.tscn" },
        { ScreenState.Victory, "res://scenes/RunEndScreen.tscn" },
        { ScreenState.Defeat, "res://scenes/RunEndScreen.tscn" },
        { ScreenState.MetaProgression, "res://scenes/MetaProgressionScreen.tscn" },
        { ScreenState.Library, "res://scenes/LibraryScreen.tscn" },
        { ScreenState.Settings, "res://scenes/SettingsScreen.tscn" },
        { ScreenState.Event, "res://scenes/EventScreen.tscn" },
        { ScreenState.RunSetup, "res://scenes/RunSetupScreen.tscn" },
    };

    // Screens whose arrival marks a safe checkpoint to persist RunState -
    // deliberately excludes Combat, since PileManager's hand/draw/discard/
    // exhaust piles are rebuilt fresh from RunState.Deck at the start of
    // every fight and have no serializable shape today. Quitting mid-fight
    // resumes at the pre-fight map state (that node offered again).
    //
    // RunSetup is excluded for a different reason, and it is the load-bearing
    // one: BeginRun has already built a full RunState by the time that screen
    // opens, so a save here would be perfectly valid - and Continue jumps
    // straight to Map. A player who opened the setup screen and quit would come
    // back to a run whose blessing was never offered, with nothing to say so.
    private static readonly HashSet<ScreenState> AutoSaveScreens = new()
    {
        ScreenState.Map, ScreenState.Rest, ScreenState.Shop,
        ScreenState.Treasure, ScreenState.Reward, ScreenState.Event,
    };

    public ScreenState CurrentScreen { get; private set; } = ScreenState.MainMenu;
    public int RunSeed { get; private set; }

    // The cross-screen fade. Parented here rather than to any screen because
    // ChangeSceneToFile frees the current scene out from under whatever is
    // mid-transition - see ScreenFade.
    public ScreenFade Fade { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        Fade = new ScreenFade { Name = "ScreenFade" };
        AddChild(Fade);
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        EventDatabase.LoadAll();
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        TipDatabase.LoadAll();
        BlessingDatabase.LoadAll();
    }

    public void ChangeScreen(ScreenState next)
    {
        if (!ScenePaths.TryGetValue(next, out var path))
        {
            GD.PushError($"RunManager: no scene registered for {next} yet (Phase 2+ TODO).");
            return;
        }
        if (AutoSaveScreens.Contains(next)) RunSaveManager.Save(RunSeed);
        CurrentScreen = next;
        // Music leads the visual deliberately. The track is chosen for where
        // you are going, and starting it under the fade-out means arriving on
        // the new screen with its music already established rather than
        // switching a beat late.
        AudioManager.Instance?.PlayMusicForState(next);

        // Fade.Play returns false when it declined - Reduce Motion, or the
        // overlay not built - so the hard cut stays reachable on exactly one
        // path instead of being duplicated at the call site.
        if (!Fade.Play(() => GetTree().ChangeSceneToFile(path))) GetTree().ChangeSceneToFile(path);
    }

    // Applies a seed and builds a fresh RunState from it. Deliberately does
    // *not* change screen: RunSetup calls this again on every reroll and on
    // every seed the player types, because the three blessings it offers are
    // drawn from RngStreams.Shop and the map from RngStreams.Map - so a run is
    // a pure function of its seed only if re-seeding rebuilds both.
    //
    // The corollary is the ordering trap on that screen: InitNewRun resets
    // Deck/Relics/HP, so re-seeding *after* a blessing has resolved silently
    // erases it. RunSetupScreen closes that by taking the seed controls out of
    // play the moment a blessing is claimed.
    //
    // The ascension rung arrives the same way and for the same reason. It is
    // *not* preserved across the rebuild - it is assigned from the argument
    // every time, because InitNewRun reads it (starting max HP, the imposed
    // cards) and MapGenerator reads it (elite frequency). A level held on
    // RunState and left alone here would make a re-seed at rung 12 rebuild the
    // map at rung 12 and the deck at whatever the field last happened to hold.
    // Same argument as the seed: a run is a pure function of (seed, rung) only
    // if changing either rebuilds from both.
    public void BeginRun(int seed, int ascension = 0)
    {
        RunSeed = seed;
        RngStreams.Init(RunSeed);
        RunState.AscensionLevel = ascension;
        RunState.InitNewRun();
        GD.Print($"Run seed: {RunSeed}"
                 + (ascension > 0 ? $", ascension {ascension}" : ""));
    }

    // The single entry point into a run. Goes to RunSetup rather than Map -
    // one path in, so a blessing cannot be skipped by a caller that forgot the
    // screen exists.
    public void StartNewRun()
    {
        BeginRun(new Random().Next());
        ChangeScreen(ScreenState.RunSetup);
    }

    // Loads a persisted run (if any) and jumps straight to Map, skipping
    // StartNewRun()/InitNewRun() entirely. Returns false (no-op) if there's
    // no save or it's unreadable, so the caller can fall back to StartNewRun.
    public bool TryContinueRun()
    {
        var seed = RunSaveManager.TryLoad();
        if (seed is null) return false;

        RunSeed = seed.Value;
        // Resets all RNG streams to this seed's start - shops/fights right
        // after a resume can echo an earlier-in-run roll (e.g. the next
        // shop after resuming might repeat the very first shop's offers).
        // Cosmetic only: no crash, no exploit, map shape is unaffected
        // since MapNodes is restored directly from the save.
        RngStreams.Init(RunSeed);
        GD.Print($"Resumed run seed: {RunSeed}");
        ChangeScreen(ScreenState.Map);
        return true;
    }
}
