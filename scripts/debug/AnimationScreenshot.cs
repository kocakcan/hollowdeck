using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Visual check for Phase 3 combat feel (hit-stop, damage-scaled shake,
// ghost HP bar, crit floating text, turn banner): unlike ArtScreenshot
// (static, at-rest), this drives real gameplay actions and screenshots
// mid-animation, since none of Phase 3's additions are visible in a static
// snapshot. Run windowed (not --headless, same GetViewport().GetTexture()
// constraint ArtScreenshot/StyleReferenceScreen document):
// `godot --path . scenes/debug/AnimationScreenshot.tscn`.
public partial class AnimationScreenshot : Node
{
    public override async void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        RunState.PlayerMaxHp = 70;
        RunState.PlayerCurrentHp = 70;
        // A big Strike-only deck so the opening hand is guaranteed a lethal-
        // looking multi-hit big hit against a low-HP target, reliably
        // crossing the bigHit threshold (>= max(10, MaxHp*0.2)) for the
        // crit-text/hit-stop/shake path instead of leaving it to chance.
        RunState.Deck = Enumerable.Repeat(CardDatabase.Get("bash"), 10).ToList();
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        CombatContext.EnemyDefinitionIds = new List<string> { "slime" };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = false;

        var combat = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(combat);
        var manager = Hollowdeck.Combat.CombatManager.Instance;

        for (int i = 0; i < 15; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Snapshot("user://anim_00_turn_banner.png");

        // Play the first playable card at the enemy - Bash (8 damage) vs a
        // 32-HP slime is >= 20% MaxHp, so this should cross the bigHit
        // threshold and trigger hit-stop + crit-sized floating text + a
        // visibly draining ghost bar.
        var playable = manager.Player.Piles.Hand.FirstOrDefault(c => c.Definition.Cost <= manager.Player.CurrentEnergy);
        if (playable is not null)
        {
            var enemy = manager.Enemies[0];
            manager.TryPlayCard(playable, enemy);
        }

        // ~60ms hit-stop + a few frames into the shake/damage-number punch,
        // before the 0.6s floating text finishes and before the ghost bar's
        // 0.15s hold + drain completes - should land mid-animation for all
        // three at once.
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Snapshot("user://anim_01_hit_reaction.png");

        // Let the ghost bar fully catch up and the floating text finish.
        for (int i = 0; i < 40; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Snapshot("user://anim_02_settled.png");

        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Snapshot(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }
}
