using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Relics;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Throwaway headless check for pile/effect logic - run via
// `godot --headless scenes/debug/EffectSmokeTest.tscn` and read stdout.
// Not part of the shipped game; safe to delete once Phase 1 stabilizes or
// real GUT coverage replaces it.
public partial class EffectSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestRelicAndPotionDatabasesLoad();
        TestPileShuffleAndDraw();
        TestDamageWithVulnerableWeakStrength();
        TestBlockAbsorption();
        TestGainBlockAndDraw();
        TestHeal();
        TestGainEnergy();
        TestEffectDescriptionFormatter();

        GD.Print($"EffectSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _pass++;
            GD.Print($"PASS {name}");
        }
        else
        {
            _fail++;
            GD.Print($"FAIL {name}: {detail}");
        }
    }

    private void TestRelicAndPotionDatabasesLoad()
    {
        Check("relics_loaded", RelicDatabase.All.Count == 14, $"count={RelicDatabase.All.Count}");
        Check("potions_loaded", PotionDatabase.All.Count == 7, $"count={PotionDatabase.All.Count}");

        int created = 0;
        foreach (var def in RelicDatabase.All)
        {
            var behavior = RelicRegistry.Create(def);
            if (behavior is not null) created++;
        }
        Check("every_relic_behavior_id_resolves", created == RelicDatabase.All.Count,
            $"created={created} expected={RelicDatabase.All.Count}");
    }

    private void TestPileShuffleAndDraw()
    {
        var piles = new PileManager(CardDatabase.All);
        int total = piles.DrawPile.Count;
        piles.DrawHand(5);
        Check("draw_hand_moves_five", piles.Hand.Count == 5 && piles.DrawPile.Count == total - 5,
            $"hand={piles.Hand.Count} draw={piles.DrawPile.Count}");

        piles.DiscardHand();
        Check("discard_hand_empties_hand", piles.Hand.Count == 0 && piles.Discard.Count == 5,
            $"hand={piles.Hand.Count} discard={piles.Discard.Count}");

        // Drain the draw pile, forcing a reshuffle-from-discard.
        piles.DrawHand(piles.DrawPile.Count);
        piles.DiscardHand();
        piles.DrawHand(3);
        Check("reshuffle_from_discard_when_draw_empty", piles.Hand.Count == 3,
            $"hand={piles.Hand.Count}");
    }

    private void TestDamageWithVulnerableWeakStrength()
    {
        var attacker = new EnemyCombatant { Name = "Attacker", MaxHp = 50, CurrentHp = 50 };
        attacker.AddStatus(StatusType.Strength, 2);
        attacker.AddStatus(StatusType.Weak, 1);

        var target = new EnemyCombatant { Name = "Target", MaxHp = 50, CurrentHp = 50 };
        target.AddStatus(StatusType.Vulnerable, 1);

        // base 6 + 2 strength = 8, *0.75 weak = 6 (int trunc), *1.5 vulnerable = 9
        var ctx = new EffectContext
        {
            Source = attacker,
            Targets = new List<Combatant> { target },
            Combat = null!,
        };
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 6 });

        Check("damage_strength_weak_vulnerable_stacking", target.CurrentHp == 41,
            $"expected 41, got {target.CurrentHp}");
    }

    private void TestBlockAbsorption()
    {
        var attacker = new EnemyCombatant { Name = "Attacker", MaxHp = 50, CurrentHp = 50 };
        var target = new EnemyCombatant { Name = "Target", MaxHp = 50, CurrentHp = 50, Block = 4 };

        var ctx = new EffectContext
        {
            Source = attacker,
            Targets = new List<Combatant> { target },
            Combat = null!,
        };
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 6 });

        Check("block_absorbs_before_hp", target.Block == 0 && target.CurrentHp == 48,
            $"block={target.Block} hp={target.CurrentHp}");
    }

    private void TestGainBlockAndDraw()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50,
            Piles = new PileManager(CardDatabase.All),
        };

        var ctx = new EffectContext
        {
            Source = player,
            Targets = new List<Combatant> { player },
            Combat = null!,
        };
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_block", Amount = 5 });
        Check("gain_block", player.Block == 5, $"block={player.Block}");

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "draw_cards", Amount = 2 });
        Check("draw_cards", player.Piles.Hand.Count == 2, $"hand={player.Piles.Hand.Count}");
    }

    private void TestHeal()
    {
        var player = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 30 };
        var ctx = new EffectContext { Source = player, Targets = new List<Combatant> { player }, Combat = null! };

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "heal", Amount = 10 });
        Check("heal_below_max", player.CurrentHp == 40, $"hp={player.CurrentHp}");

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "heal", Amount = 100 });
        Check("heal_clamps_to_max", player.CurrentHp == 50, $"hp={player.CurrentHp}");
    }

    private void TestGainEnergy()
    {
        var player = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50, MaxEnergy = 3, CurrentEnergy = 1 };
        var ctx = new EffectContext { Source = player, Targets = new List<Combatant> { player }, Combat = null! };

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_energy", Amount = 2 });
        Check("gain_energy", player.CurrentEnergy == 3, $"energy={player.CurrentEnergy}");
    }

    private void TestEffectDescriptionFormatter()
    {
        var strike = CardDatabase.Get("strike");

        var noContext = EffectDescriptionFormatter.Describe(strike.Effects);
        Check("description_base_damage_with_no_player_context", noContext.Contains("Deal 6 damage"),
            $"text='{noContext}'");
        Check("description_shows_vulnerable_preview", noContext.Contains("~9 vs Vulnerable"),
            $"text='{noContext}'");

        var strongPlayer = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        strongPlayer.AddStatus(StatusType.Strength, 2);
        var withStrength = EffectDescriptionFormatter.Describe(strike.Effects, strongPlayer);
        Check("description_reflects_live_strength", withStrength.Contains("Deal 8 damage"),
            $"text='{withStrength}' (expected 6 base + 2 strength = 8)");

        var flex = CardDatabase.Get("flex");
        var flexText = EffectDescriptionFormatter.Describe(flex.Effects);
        Check("description_self_strength_reads_as_gain", flexText == "Gain 2 Strength.", $"text='{flexText}'");
    }
}
