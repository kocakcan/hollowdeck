using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Visual check for the art assets: boots a populated CombatScreen (3 enemies,
// relics, potions) and a generated MapScreen, saving viewport screenshots to
// user://art_screenshot.png and user://art_screenshot_map.png, then quits.
// Run windowed (not --headless): `godot --path . scenes/debug/ArtScreenshot.tscn`.
public partial class ArtScreenshot : Node
{
    public override async void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        RunState.PlayerMaxHp = 70;
        RunState.PlayerCurrentHp = 58;
        RunState.Deck = new List<CardDefinition>(CardDatabase.All);
        RunState.Relics = new List<RelicInstance>
        {
            new(RelicDatabase.Get("anchor_stone")),
            new(RelicDatabase.Get("vampire_fang")),
            new(RelicDatabase.Get("cracked_hourglass")),
        };
        RunState.Potions = new List<PotionInstance>
        {
            new(PotionDatabase.Get("fire_potion")),
            new(PotionDatabase.Get("healing_potion")),
        };
        CombatContext.EnemyDefinitionIds = new List<string> { "cultist", "slime", "bog_troll" };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = false;

        var combat = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(combat);

        // Seed some statuses so the status rows are visible in the shot,
        // then poke the screen's private Refresh to redraw them.
        var manager = Hollowdeck.Combat.CombatManager.Instance;
        manager.Player.AddStatus(StatusType.Strength, 2);
        manager.Player.AddStatus(StatusType.Poison, 3);
        manager.Enemies[0].AddStatus(StatusType.Weak, 2);
        manager.Enemies[1].AddStatus(StatusType.Vulnerable, 2);
        manager.Enemies[1].AddStatus(StatusType.Poison, 4);
        typeof(UI.CombatScreen)
            .GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(combat, null);

        await Snapshot("user://art_screenshot.png");
        RemoveChild(combat);
        combat.QueueFree();

        RunState.MapNodes = Hollowdeck.Map.MapGenerator.Generate(new System.Random(7));
        // A real (not empty) CurrentNodeId so Phase 4's current-node ring
        // and "choosable path" highlighting actually show up in the shot -
        // an empty CurrentNodeId (the pre-run state) never draws a ring.
        RunState.CurrentNodeId = RunState.MapNodes.Find(n => n.Floor == 0)!.Id;
        var map = GD.Load<PackedScene>("res://scenes/MapScreen.tscn").Instantiate();
        AddChild(map);
        await Snapshot("user://art_screenshot_map.png");
        RemoveChild(map);
        map.QueueFree();

        RewardContext.GoldAwarded = 35;
        RewardContext.GuaranteedRelic = null;
        RewardContext.CardChoices = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("bash"), CardDatabase.Get("shrug_it_off"),
        };
        AddChild(GD.Load<PackedScene>("res://scenes/RewardScreen.tscn").Instantiate());
        await Snapshot("user://art_screenshot_reward.png");

        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Snapshot(string path)
    {
        for (int i = 0; i < 20; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }
}
