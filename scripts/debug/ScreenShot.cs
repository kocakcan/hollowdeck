using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Map;
using Hollowdeck.Run;
using Hollowdeck.UI;

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

    // AfterReady runs once the screen's _Ready has finished building itself,
    // for state that only exists after that point - CombatScreen constructs
    // its PlayerCombatant/EnemyCombatants in _Ready, so a fixture can't seed
    // their statuses up front the way it seeds RunState.
    private record Fixture(string ScenePath, Action Seed, Action<Node>? AfterReady = null);

    private static readonly Dictionary<string, Fixture> Fixtures = new()
    {
        ["combat"] = new("res://scenes/CombatScreen.tscn", SeedCombat, AfterCombatReady),
        ["reward"] = new("res://scenes/RewardScreen.tscn", SeedReward),
        ["shop"] = new("res://scenes/ShopScreen.tscn", SeedShop),
        ["map"] = new("res://scenes/MapScreen.tscn", SeedMap),
        // One map + one boss fight per later act: each act has its own title,
        // backdrop tint, boss sprites and floor count, and none of that is
        // visible from act 1's shots. Act 3's map is also the longest (10
        // floors), which is where node layout runs out of horizontal room first.
        ["map2"] = new("res://scenes/MapScreen.tscn", () => SeedActMap(1)),
        ["map3"] = new("res://scenes/MapScreen.tscn", () => SeedActMap(2)),
        ["combat2"] = new("res://scenes/CombatScreen.tscn", () => SeedActBossCombat(1)),
        ["combat3"] = new("res://scenes/CombatScreen.tscn", () => SeedActBossCombat(2)),
        // The HUD's worst case, which "combat" (2 enemies, 1 relic) can't
        // show: the widest encounter beside a late-run relic collection. The
        // relic bar used to grow rightward across the enemy row from 5 relics
        // on, painting over the leftmost enemy - and with it the target-lock
        // glow, which is that enemy's own background.
        ["combatfull"] = new("res://scenes/CombatScreen.tscn", SeedCrowdedCombat, AfterCombatReady),
        // The pile popup is spawned on demand by DeckViewButtons rather than
        // being a screen of its own, so it needs a host screen plus the click
        // that opens it. Deck is deliberately larger than one row of the grid,
        // since the thing worth looking at is how the columns pack.
        ["deckpopup"] = new("res://scenes/MapScreen.tscn", SeedDeckPopup, OpenDeckPopup),
        // The cross-screen fade, held at a fixed alpha over a real screen.
        // Deliberately not the live tween: 60 settle frames is longer than the
        // whole transition, so a real Play() would always be captured already
        // finished. TransitionSmokeTest proves the alpha ramps; this shot is
        // for the thing a test cannot see - that the cover spans the viewport
        // and sits above the screen's own chrome, pile counters included.
        ["fade"] = new("res://scenes/MapScreen.tscn", SeedMap, HoldFadeMidway),
        ["rest"] = new("res://scenes/RestScreen.tscn", SeedRest),
        // The rest site's second view, reached by pressing Smith. It renders
        // real CardViews now rather than stacked text rows, so it is a layout
        // that can overflow and needs looking at - and it is unreachable
        // without the click, same as the deck popup above.
        ["restupgrade"] = new("res://scenes/RestScreen.tscn", SeedRestUpgrade, OpenRestUpgrade),
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
        ActDatabase.LoadAll();
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
        fixture.AfterReady?.Invoke(screen);

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
        // The fade overlay lives on the RunManager autoload, so unlike the
        // screen itself it survives from one shot to the next - without this
        // the "fade" fixture would tint every screen shot after it.
        var cover = RunManager.Instance.Fade.GetNode<ColorRect>("Cover");
        cover.Visible = false;
        cover.Color = new Color(cover.Color, 0f);

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
        RunState.ActIndex = 0;
        RunState.MapNodes = new List<MapNode>();
        RunState.CurrentNodeId = "";
        RunState.VisitedNodeIds = new HashSet<string>();
    }

    private static void SeedNothing() { }

    // Exactly five cards, so DrawHand(5) draws the whole deck and the opening
    // hand is guaranteed rather than a lucky roll: Flex and Bash for
    // AfterCombatReady to spend, and Cleave/Whirlwind/Thunderclap left
    // standing as the cards whose text is being verified. Thunderclap is
    // deliberately the longest description in the game - two effects that both
    // hit every enemy, so it is the card that forces the "ALL enemies:" prefix
    // and the one the description box's single 16px size is tightest against.
    // Five is the hand size the fan layout is tuned for, so this stays a
    // representative layout shot.
    private static void SeedCombat()
    {
        CombatContext.EnemyDefinitionIds = new List<string> { "cultist", "slime" };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = false;
        CombatContext.GoldReward = 30;
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("flex"), CardDatabase.Get("bash"), CardDatabase.Get("cleave"),
            CardDatabase.Get("whirlwind"), CardDatabase.Get("thunderclap"),
        };
    }

    private static void SeedDeckPopup()
    {
        SeedMap();
        RunState.Deck = CardDatabase.All.Take(13).ToList();
    }

    private static void OpenDeckPopup(Node screen) => DeckViewButtons.OpenDeck(screen);

    private static void HoldFadeMidway(Node screen)
    {
        var cover = RunManager.Instance.Fade.GetNode<ColorRect>("Cover");
        cover.Visible = true;
        cover.Color = new Color(cover.Color, 0.62f);
    }

    private static void SeedCrowdedCombat()
    {
        SeedCombat();
        CombatContext.EnemyDefinitionIds = new List<string> { "rot_hound", "rot_hound", "ward_acolyte" };
        RunState.Relics = RelicDatabase.All.Take(8).Select(r => new RelicInstance(r)).ToList();
        RunState.Potions = PotionDatabase.All.Take(3).Select(p => new PotionInstance(p)).ToList();
    }

    // Card text depends on live combat state that a turn-1 fight doesn't have:
    // the player's Strength, and Vulnerable on the enemy being hit. Reached by
    // actually playing Flex and Bash through TryPlayCard rather than poking
    // statuses in directly - that fires the same events gameplay does, so the
    // hand re-renders exactly as it would mid-fight, and the shot can't show a
    // state the real game can't produce.
    //
    // Leaves: Strength 2 (every damage number buffed), Vulnerable 2 on the
    // cultist but not the slime (so an AllEnemies card prints a range), and
    // 1 energy (so Cleave is affordable and Whirlwind is dimmed).
    private static void AfterCombatReady(Node screen)
    {
        var combat = CombatManager.Instance;
        if (combat is null) return;

        var hand = combat.Player.Piles.Hand;
        if (hand.FirstOrDefault(c => c.Definition.Id == "flex") is { } flex) combat.TryPlayCard(flex);
        if (hand.FirstOrDefault(c => c.Definition.Id == "bash") is { } bash)
        {
            combat.TryPlayCard(bash, combat.Enemies.FirstOrDefault());
        }

        // Ritual is granted directly (Demon Form costs 2 and the turn-1 hand
        // has 1 energy left after Flex and Bash), but Metallicize is actually
        // played - and it has to be, not just granted. The status row is
        // rebuilt from CombatantsChanged, which only a real resolution fires;
        // granting both and stopping there left the HUD showing the row it
        // built before either existed.
        combat.Player.AddStatus(StatusType.Ritual, 2);
        var metallicize = new CardInstance(CardDatabase.Get("metallicize"));
        combat.Player.Piles.Hand.Add(metallicize);
        combat.TryPlayCard(metallicize);
    }

    private static void SeedReward()
    {
        RewardContext.GoldAwarded = 45;
        RewardContext.GuaranteedRelic = RelicDatabase.Get("anchor_stone");
        // One card of each type and each rarity, on purpose: the card frame
        // carries two independent channels (fill = CardType, border = Rarity)
        // and this is the only shot where all three fills and all three border
        // treatments are visible side by side. Twin Strike is a Common Attack,
        // Shrug It Off a Common Skill, Inflame a Rare Power - so a regression
        // in either channel shows up here rather than needing a lucky roll on
        // the shop screen.
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("twin_strike"), CardDatabase.Get("shrug_it_off"), CardDatabase.Get("inflame"),
        };
    }

    // Gold is set just above two card prices (50g each) and below a relic's
    // (150g) so the shot also exercises the affordability greying - the exact
    // state that was misread as a bug.
    private static void SeedShop() => RunState.Gold = 129;

    private static void SeedMap()
    {
        RunState.MapNodes = MapGenerator.Generate(new Random(7), RunState.CurrentAct);
        var start = RunState.MapNodes.First(n => n.Floor == 0);
        RunState.CurrentNodeId = start.Id;
        RunState.VisitedNodeIds = new HashSet<string> { start.Id };
    }

    // Deep into a run rather than the start of one: a later act, and the
    // HP/max-HP and gold a run that had already cleared the acts before it
    // would plausibly have (RunState.AdvanceAct grants +8 max HP per act).
    private static void SeedActProgress(int actIndex)
    {
        RunState.ActIndex = actIndex;
        RunState.PlayerMaxHp = 50 + 8 * actIndex;
        RunState.PlayerCurrentHp = RunState.PlayerMaxHp - 19;
        RunState.Gold = 180 + 66 * actIndex;
    }

    private static void SeedActMap(int actIndex)
    {
        SeedActProgress(actIndex);
        RunState.MapNodes = MapGenerator.Generate(new Random(7), RunState.CurrentAct);
        var start = RunState.MapNodes.First(n => n.Floor == 0);
        RunState.CurrentNodeId = start.Id;
        RunState.VisitedNodeIds = new HashSet<string> { start.Id };
    }

    private static void SeedActBossCombat(int actIndex)
    {
        SeedActProgress(actIndex);
        CombatContext.EnemyDefinitionIds = new List<string> { RunState.CurrentAct.BossIds[0] };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = true;
        CombatContext.GoldReward = RunState.CurrentAct.BossGold;
    }

    private static void SeedRest() => RunState.PlayerCurrentHp = 21;

    // Seven un-upgraded cards: two more than the picker's five columns, so the
    // shot shows the wrap rather than a single tidy row.
    private static void SeedRestUpgrade()
    {
        SeedRest();
        RunState.Deck = CardDatabase.All.Take(7).ToList();
    }

    private static void OpenRestUpgrade(Node screen) =>
        screen.GetNode<Button>("CenterContainer/VBoxContainer/ChoiceColumn/SmithButton")
            .EmitSignal(Button.SignalName.Pressed);

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
