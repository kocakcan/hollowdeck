using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

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
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
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
        var manager = Hollowdeck.Combat.CombatManager.Instance!;

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

        await SnapshotCombatEffects(combat);

        GetTree().Quit();
    }

    // The four combat effect bursts, one per beat, held still.
    //
    // They cannot be caught by waiting frames the way everything above is. A
    // burst is 4 x CombatFx.FrameSeconds = 0.24s, and this scene does enough
    // real work per frame that a frame-counted wait is not a reliable clock -
    // shooting 10 frames after the card resolved caught the beat *after* the
    // burst had already finished and freed itself, with the effect working
    // perfectly. A shot that only sometimes contains its subject is worse than
    // no shot, because a green-looking one proves nothing.
    //
    // So this drives the animators directly instead: spawn four bursts across
    // the board and tick the i-th one i times, which puts flash / ring / arms /
    // motes on screen at once, deterministically, over the real backdrop at the
    // real 5x. That is the only view in the project of what these look like in
    // place rather than as files - `artgen validate` reads pixels, the Rust
    // tests read shape, and PixelSpecSmokeTest reads the driver. None of them
    // can see whether a burst reads.
    private async System.Threading.Tasks.Task SnapshotCombatEffects(Node combat)
    {
        // Reduce Motion is pinned off, through a scratch settings path so the
        // real file is untouched - the same trick PixelSpecSmokeTest uses. It
        // matters here because that setting *declines the opening flash frame*,
        // so with it on the sheet silently starts a beat late and the fourth
        // burst has already freed itself by the time the shot is taken. This
        // shot's job is to show the full-strength effect; the declined variant
        // is asserted rather than photographed.
        bool reduceMotion = SettingsManager.Instance.ReduceMotion;
        const string scratch = "user://settings_animshot.json";
        SettingsManager.Instance.SetReduceMotion(false, scratch);

        // Placed either side of the enemy column (roughly x 500-660) and in the
        // band between the enemy row and the top of the fan, so the bursts sit
        // over the backdrop rather than over the furniture they would obscure.
        var beats = new[] { CombatFx.Impact, CombatFx.Ward, CombatFx.Bloom, CombatFx.Venom };
        var spots = new[] { new Vector2(280, 270), new Vector2(470, 270),
                            new Vector2(760, 270), new Vector2(950, 270) };

        for (int i = 0; i < beats.Length; i++)
        {
            CombatFx.Play(combat, spots[i], beats[i]);

            // The last-spawned rect is the one just added, and its animator is
            // its only SpriteAnimator child.
            var rect = combat.GetChildren().OfType<TextureRect>().LastOrDefault();
            var animator = rect?.GetChildren().OfType<SpriteAnimator>().FirstOrDefault();
            for (int frame = 0; frame < i; frame++) animator?._Process(CombatFx.FrameSeconds);

            // Then stop the driver, because Snapshot awaits a ProcessFrame and
            // the engine would tick every one of these again on the way to it.
            // Without this the shot is off by one beat and the last burst has
            // already freed itself - which is what the first version did, and
            // it looked plausible enough to keep.
            animator?.SetProcess(false);
        }

        await Snapshot("user://anim_03_combat_effects.png");

        foreach (var stale in combat.GetChildren().OfType<TextureRect>().Where(r => r.GetChildren().OfType<SpriteAnimator>().Any()))
        {
            stale.QueueFree();
        }

        await SnapshotTheSwipeInFlight(combat);

        SettingsManager.Instance.SetReduceMotion(reduceMotion, scratch);
        if (Godot.FileAccess.FileExists(scratch)) DirAccess.RemoveAbsolute(scratch);
    }

    // The swipe, laid out along the path it actually takes.
    //
    // It is the one effect whose direction comes from *motion* rather than from
    // the art (CombatFx.PlayTravelling), which makes it the one effect a
    // single still cannot show at all: a frame of it is a bar on a diagonal,
    // and what the player reads is that bar crossing the board. A tween also
    // cannot be stepped the way _Process can, so waiting for one would be the
    // same unreliable clock the sheet above exists to avoid.
    //
    // So this spawns four travelling swipes whose *origins* are the four points
    // the real tween passes through, and ticks the i-th to its i-th frame. The
    // line is the live geometry - CombatScreen pins PlayerSprite at canvas
    // (120, 350) and an EnemyView centre sits in the row above it - so what is
    // photographed is the path the game takes, not an arrangement chosen to
    // look good.
    private async System.Threading.Tasks.Task SnapshotTheSwipeInFlight(Node combat)
    {
        var from = new Vector2(120, 350);
        var to = new Vector2(576, 170);

        for (int i = 0; i < 4; i++)
        {
            var at = from.Lerp(to, i / 3f);
            CombatFx.PlayTravelling(combat, at, to, CombatFx.Swipe);

            var rect = combat.GetChildren().OfType<TextureRect>().LastOrDefault();
            var animator = rect?.GetChildren().OfType<SpriteAnimator>().FirstOrDefault();
            for (int frame = 0; frame < i; frame++) animator?._Process(CombatFx.TravelFrameSeconds);
            animator?.SetProcess(false);

        }

        // Every live tween, killed before the shot. The travel tweens would
        // otherwise carry all four rects to the target and stack them on one
        // point - which is exactly the "it worked perfectly and photographed
        // nothing" failure the sheet above records, arriving through the one
        // channel that sheet does not have. Killing the lot rather than
        // filtering is deliberate: this is the last shot in the run, and an
        // enemy's idle pulse frozen mid-cycle is one less thing making the
        // image non-deterministic.
        foreach (var tween in GetTree().GetProcessedTweens()) tween.Kill();

        await Snapshot("user://anim_04_swipe_in_flight.png");
    }

    private async System.Threading.Tasks.Task Snapshot(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }
}
