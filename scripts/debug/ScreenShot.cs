using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Screenshots any screen in the game, on demand, with realistic data already
// seeded into the global statics that screen's _Ready reads. This is the
// "look at what I just changed" loop: every screen here is a real .tscn
// instantiated exactly the way RunManager.ChangeScreen would, so what lands
// in the PNG is what a player would see.
//
// Run WINDOWED (not --headless) - the dummy renderer --headless forces makes
// GetViewport().GetTexture() come back empty, the same constraint
// ArtScreenshot/AnimationScreenshot/StyleReferenceScreen already document:
//
//   dotnet build
//   /Applications/Godot_mono.app/Contents/MacOS/Godot --path . \
//       scenes/debug/ScreenShot.tscn -- shop reward unlocks
//
// -> user://shot_<name>.png each (~/Library/Application Support/Godot/
// app_userdata/Hollowdeck/ on macOS). No screen names = shoot all of them.
//
// Adding a screen is one arm in Fixtures below plus its scene path.
public partial class ScreenShot : Node
{
    // Deliberately fixed rather than time- or run-seeded: the shop's stock,
    // the treasure relic and the rolled event all come from RngStreams.Shop,
    // so without a pinned seed two runs of the same command produce
    // different screenshots and a visual diff can't tell a real change from
    // a different roll.
    private const int FixtureSeed = 4242;

    // Frames to wait before capturing. Higher than ArtScreenshot's 20
    // because that isn't enough for CombatScreen: the opening hand's draw
    // tween is 0.28s per card plus a per-card stagger, so a 20-frame wait
    // catches the fan still flying in and the cards overlapping. ~1s covers
    // the whole cascade on every screen.
    private const int SettleFrames = 60;

    // Screens that write to the player's real save just by being loaded.
    // RunEndScreen banks a run result and deletes the in-progress save;
    // MetaProgressionManager's own autoload _Ready can rewrite the meta save
    // (a version migration) before any of this code runs. BackupSaves/
    // RestoreSaves below wrap the whole session so shooting a screen can
    // never cost the player progress.
    private static readonly string[] ProtectedSaves =
    {
        "user://meta_progression.json",
        "user://run_save.json",
    };

    private record Fixture(string ScenePath, Action Seed);

    private static readonly Dictionary<string, Fixture> Fixtures = new()
    {
        ["combat"] = new("res://scenes/CombatScreen.tscn", SeedCombat),
        ["reward"] = new("res://scenes/RewardScreen.tscn", SeedReward),
        ["shop"] = new("res://scenes/ShopScreen.tscn", SeedShop),
        ["map"] = new("res://scenes/MapScreen.tscn", SeedMap),
        ["rest"] = new("res://scenes/RestScreen.tscn", SeedRest),
        ["treasure"] = new("res://scenes/TreasureScreen.tscn", SeedTreasure),
        ["event"] = new("res://scenes/EventScreen.tscn", SeedEvent),
        ["unlocks"] = new("res://scenes/MetaProgressionScreen.tscn", SeedUnlocks),
        ["runend"] = new("res://scenes/RunEndScreen.tscn", SeedRunEnd),
        ["mainmenu"] = new("res://scenes/MainMenu.tscn", SeedNothing),
        ["settings"] = new("res://scenes/SettingsScreen.tscn", SeedNothing),
    };

    public override async void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        EventDatabase.LoadAll();

        var requested = OS.GetCmdlineUserArgs().Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        var unknown = requested.Where(name => !Fixtures.ContainsKey(name)).ToList();
        if (unknown.Count > 0)
        {
            GD.PrintErr($"ScreenShot: unknown screen(s) {string.Join(", ", unknown)}. " +
                        $"Known: {string.Join(", ", Fixtures.Keys)}");
            GetTree().Quit(1);
            return;
        }

        var names = requested.Count > 0 ? requested : Fixtures.Keys.ToList();

        BackupSaves();
        try
        {
            foreach (var name in names)
            {
                await Shoot(name);
            }
        }
        finally
        {
            // finally, not just a trailing call: a screen that throws
            // mid-_Ready must still hand the player their save back.
            RestoreSaves();
        }

        GetTree().Quit();
    }

    private async Task Shoot(string name)
    {
        var fixture = Fixtures[name];

        // Re-seeded per screen, not once for the whole session, so shooting
        // "shop" alone and shooting "shop" as part of a full sweep produce
        // the same image - otherwise an earlier screen's rolls would shift
        // every later screen's.
        RngStreams.Init(FixtureSeed);
        ResetRunState();
        fixture.Seed();

        var screen = GD.Load<PackedScene>(fixture.ScenePath).Instantiate();
        AddChild(screen);

        for (int i = 0; i < SettleFrames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        string path = $"user://shot_{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");

        RemoveChild(screen);
        screen.QueueFree();
    }

    // A believable mid-run baseline every fixture starts from and then
    // overrides only what it cares about. Deliberately not RunState
    // .InitNewRun(), which would also regenerate the map off RngStreams.Map
    // and reset the stats block each fixture is trying to set.
    private static void ResetRunState()
    {
        RunState.Gold = 129;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 34;
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("strike"), CardDatabase.Get("strike"),
            CardDatabase.Get("defend"), CardDatabase.Get("defend"), CardDatabase.Get("bash"),
            CardDatabase.Get("cleave"), CardDatabase.Get("flex"),
        };
        RunState.Relics = new List<RelicInstance> { new(RelicDatabase.Get("second_wind")) };
        RunState.Potions = new List<PotionInstance> { new(PotionDatabase.Get("healing_potion")) };
        RunState.Stats = new RunStats();
        RunState.MapNodes = new List<MapNode>();
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();
    }

    private static void SeedNothing() { }

    private static void SeedCombat()
    {
        CombatContext.EnemyDefinitionIds = new List<string> { "cultist", "slime" };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = false;
        CombatContext.GoldReward = 30;
    }

    private static void SeedReward()
    {
        RewardContext.GoldAwarded = 45;
        RewardContext.GuaranteedRelic = RelicDatabase.Get("anchor_stone");
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("twin_strike"), CardDatabase.Get("shrug_it_off"), CardDatabase.Get("clothesline"),
        };
    }

    // Gold is set just above two card prices (50g each) and below a relic's
    // (150g) so the shot also exercises the affordability greying - the exact
    // state that was misread as a bug.
    private static void SeedShop() => RunState.Gold = 129;

    private static void SeedMap()
    {
        RunState.MapNodes = MapGenerator.Generate(new Random(7));
        var start = RunState.MapNodes.First(n => n.Floor == 0);
        RunState.CurrentNodeId = start.Id;
        RunState.VisitedNodeIds = new HashSet<string> { start.Id };
    }

    private static void SeedRest() => RunState.PlayerCurrentHp = 21;

    private static void SeedTreasure() => RunState.Relics = new List<RelicInstance>();

    private static void SeedEvent() { }

    private static void SeedUnlocks() { }

    private static void SeedRunEnd()
    {
        RunEndContext.Outcome = RunEndOutcome.Win;
        RunState.Gold = 260;
        RunState.Stats = new RunStats
        {
            MaxFloorReached = 12,
            EnemiesSlain = 18,
            ElitesSlain = 2,
            BossesSlain = 1,
            PerfectElites = 1,
            EventRoomsVisited = 5,
            ComboEarned = true,
        };
    }

    private static string BackupPath(string path) => path + ".shotbak";

    private static void BackupSaves()
    {
        foreach (var path in ProtectedSaves)
        {
            if (!FileAccess.FileExists(path)) continue;
            Copy(path, BackupPath(path));
        }
    }

    // Restores each protected save to exactly what it was, including the
    // "didn't exist" case - a screen that CREATES a save the player didn't
    // have must not leave it behind either.
    private static void RestoreSaves()
    {
        foreach (var path in ProtectedSaves)
        {
            var backup = BackupPath(path);
            if (FileAccess.FileExists(backup))
            {
                Copy(backup, path);
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(backup));
            }
            else if (FileAccess.FileExists(path))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
            }
        }
    }

    private static void Copy(string from, string to)
    {
        using var source = FileAccess.Open(from, FileAccess.ModeFlags.Read);
        var bytes = source.GetBuffer((long)source.GetLength());
        using var destination = FileAccess.Open(to, FileAccess.ModeFlags.Write);
        destination.StoreBuffer(bytes);
    }
}
