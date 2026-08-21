using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Relics;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Throwaway headless check for pile/effect logic - run via
// `godot --headless scenes/debug/EffectSmokeTest.tscn` and read stdout.
// Not part of the shipped game; safe to delete once Phase 1 stabilizes or
// real GUT coverage replaces it.
public partial class EffectSmokeTest : Node
{
    private int _pass;
    private int _fail;

    // async because the turn-start-grant test has to drive a real combat
    // round, and the enemy turn paces itself with awaited delays - there is no
    // synchronous way to step one.
    public override async void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();
        TipDatabase.LoadAll();

        TestRelicAndPotionDatabasesLoad();
        TestPileShuffleAndDraw();
        TestDamageWithVulnerableWeakStrength();
        TestBlockAbsorption();
        TestGainBlockAndDraw();
        TestHeal();
        TestGainEnergy();
        TestGainGold();
        TestEnemyOnlyActionsStayOffCardsPotionsAndRelics();
        TestEffectDescriptionFormatter();
        TestAllEnemiesWording();
        TestLiveTargetDamage();
        TestEveryCardDeclaresARarity();
        TestCardPoolIsRarityWeighted();
        TestSkipStreakLadder();
        TestTierPoolAlgorithm();
        TestEveryPotionDeclaresARarity();
        TestPotionPoolIsRarityWeighted();
        TestEveryRelicDeclaresATier();
        TestRelicSitesDrawFromTheirOwnPools();
        TestTipsAreAuthoredAndFit();
        TestPowerCardsLeavePlay();
        TestDexterityAndFrailBlock();
        TestDiscardAndExhaustHand();
        TestExhaustHandCardDoesNotEatItself();
        TestNewStatusDescriptions();
        TestEveryCardUpgradeChangesSomething();
        TestEveryStatusHasAKeywordBlurb();
        TestEnemyVoiceDescriptions();
        TestArtifactRefusesExactlyTheDebuffs();
        TestTheCombatEngineReadsTheRung();
        TestThornsBillsTheAttackerOnlyForAnAttack();
        TestIntangibleFloorsDamagePastVulnerable();
        await TestTurnStartGrantingStatuses();
        await TestRegenHealsEachTurn();
        await TestIntangibleDecaysForBothSides();
        await TestPlatingGrantsBlockAndErodesOnlyOnUnblockedDamage();

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
        Check("relics_loaded", RelicDatabase.All.Count == 33, $"count={RelicDatabase.All.Count}");
        Check("potions_loaded", PotionDatabase.All.Count == 12, $"count={PotionDatabase.All.Count}");

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
        // Deliberately NOT CardDatabase.All any more. Phase 7's keywords made
        // that fixture a lie: a Retain card left in hand by DiscardHand and an
        // Ethereal one routed to Exhaust both change these counts, and whether
        // either was in the five drawn depended on the shuffle - so this test
        // started failing intermittently for a reason that had nothing to do
        // with draw, discard or reshuffle. A keyword-free deck is what this
        // test was always measuring; CardKeywordSmokeTest owns the rest.
        var plain = CardDatabase.All
            .Where(c => !c.Retain && !c.Ethereal && !c.Innate)
            .ToList();
        var piles = new PileManager(plain);
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

        TestAbsorbingAHitIsDistinguishableFromLosingBlock();
        TestALandedHitIsDistinguishableFromEveryOtherWayHpFalls();
    }

    // Combatant.HitsAbsorbed is a *cause*, and it exists because falling Block
    // is not one: both combatants clear their own Block at the top of their
    // turn, so CombatScreen.PopupDelta saw the identical two numbers move the
    // identical way whether a hit had been absorbed or the turn had simply
    // ended. It gated the "Blocked!" beat on the wrong one and fired it every
    // turn either side had leftover Block and nothing had attacked - which was
    // survivable as a small text pop and stopped being survivable when it
    // became a full-creature-sized ward burst.
    //
    // So the property worth asserting is the *difference*, not the increment:
    // an absorbed hit moves the counter and an expiry does not. Checking only
    // that damage increments it would stay green under the original bug.
    private void TestAbsorbingAHitIsDistinguishableFromLosingBlock()
    {
        var attacker = new EnemyCombatant { Name = "Attacker", MaxHp = 50, CurrentHp = 50 };
        var target = new EnemyCombatant { Name = "Target", MaxHp = 50, CurrentHp = 50, Block = 10 };
        var ctx = new EffectContext
        {
            Source = attacker,
            Targets = new List<Combatant> { target },
            Combat = null!,
        };

        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 4 });
        Check("absorbing_a_hit_counts_it", target.HitsAbsorbed == 1,
            $"Block ate 4 damage and HitsAbsorbed is {target.HitsAbsorbed}, expected 1");

        // A hit Block cannot reach must not count - otherwise the beat fires on
        // damage that went straight through, which is the opposite mistake.
        target.Block = 0;
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 4 });
        Check("an_unblocked_hit_does_not_count", target.HitsAbsorbed == 1,
            $"a hit against 0 Block moved HitsAbsorbed to {target.HitsAbsorbed}");

        // And the turn boundary, which is the case that was actually wrong.
        target.Block = 12;
        int before = target.HitsAbsorbed;
        target.Block = 0;
        Check("expiring_block_does_not_count", target.HitsAbsorbed == before,
            $"clearing Block moved HitsAbsorbed from {before} to {target.HitsAbsorbed} - the " +
            "view layer cannot then tell an absorbed hit from an expired one");
    }

    // Combatant.HitsTaken/LastAttacker are the same argument as HitsAbsorbed
    // above, one number over, and they exist because a falling HP bar is not a
    // cause either. Four things take HP in a fight - an attack, a Poison tick,
    // a card that costs it, and Thorns billing the attacker - and only the
    // first is a weapon crossing the gap that CombatScreen can draw a blade
    // along. Without the pair, CombatScreen.AttackerOf would name whichever
    // enemy last swung, so the player ticking down from Poison on their own
    // turn would take a blade from an enemy that did nothing.
    //
    // So the property worth asserting is again the *difference*: an attack that
    // reaches HP moves the counter, and the other three ways HP falls do not.
    // Asserting only that damage increments it would stay green under every
    // misfire this is here to stop.
    private void TestALandedHitIsDistinguishableFromEveryOtherWayHpFalls()
    {
        var attacker = new EnemyCombatant { Name = "Attacker", MaxHp = 50, CurrentHp = 50 };
        var target = new PlayerCombatant { Name = "Target", MaxHp = 50, CurrentHp = 50, Block = 10 };
        var ctx = new EffectContext
        {
            Source = attacker,
            Targets = new List<Combatant> { target },
            Combat = null!,
        };

        // A hit Block eats whole is not a blade's beat, it is the ward burst's -
        // and it is the case the `unblocked` gate exists for. First, so a
        // counter that moved here would fail before anything sets it legitimately.
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 4 });
        Check("a_fully_blocked_hit_is_not_a_landed_one",
            target.HitsTaken == 0 && target.LastAttacker is null,
            $"Block ate all 4 damage and HitsTaken is {target.HitsTaken}, LastAttacker is " +
            $"{target.LastAttacker?.Name ?? "null"} - a hit that never reached HP has no blade");

        target.Block = 0;
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 4 });
        Check("a_landed_hit_counts_it", target.HitsTaken == 1,
            $"a hit against 0 Block left HitsTaken at {target.HitsTaken}, expected 1");

        // Identity, not non-null. A LastAttacker wrongly set to the *target*
        // passes every null check and draws the blade out of the thing it is
        // supposed to be arriving at.
        Check("a_landed_hit_names_who_dealt_it", ReferenceEquals(target.LastAttacker, attacker),
            $"LastAttacker is {target.LastAttacker?.Name ?? "null"}, expected {attacker.Name}");

        // The three losses that are not attacks. HP subtracted directly stands
        // in for all of them - a Poison tick, LoseHpEffect and Thorns each do
        // exactly this and none goes near the counter - the way the Block check
        // above stands in for the turn boundary.
        int before = target.HitsTaken;
        target.CurrentHp -= 5;
        Check("hp_lost_without_an_attack_does_not_count", target.HitsTaken == before,
            $"HP falling on its own moved HitsTaken from {before} to {target.HitsTaken} - the " +
            "view layer cannot then tell a sword from a Poison tick");

        // And the rule that reads them, driven over the shapes the diff
        // actually produces. This is the whole of the gate, and PopupDelta
        // needs a live fight and a built screen to reach.
        Check("attacker_of_a_landed_hit_is_the_attacker",
            ReferenceEquals(CombatScreen.AttackerOf(-4, 1, attacker), attacker),
            "a landed hit did not resolve to its attacker");
        Check("attacker_of_a_poison_tick_is_nobody",
            CombatScreen.AttackerOf(-5, 0, attacker) is null,
            "HP fell with the counter still - a stale attacker would draw a blade out of an " +
            "enemy that did nothing");
        Check("attacker_of_a_heal_is_nobody",
            CombatScreen.AttackerOf(3, 0, attacker) is null,
            "HP rose and something resolved to an attacker");
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

    // gain_gold is the one effect that ignores ctx.Targets entirely - gold is
    // run state, not a combatant property - so the targets list here is
    // deliberately empty and the amount still has to land.
    private void TestGainGold()
    {
        var player = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        var ctx = new EffectContext { Source = player, Targets = new List<Combatant>(), Combat = null! };

        int before = RunState.Gold;
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_gold", Amount = 7 });
        Check("gain_gold_ignores_targets", RunState.Gold == before + 7,
            $"before={before} after={RunState.Gold}");

        // A negative amount is theft, and it is authored content now: an
        // escaping enemy's parting move. Two things about it are worth pinning
        // rather than trusting - that the sign works at all, and that it
        // cannot drive the purse below zero, where the next reward would be
        // silently swallowed paying the debt off.
        RunState.Gold = 10;
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_gold", Amount = -4 });
        Check("negative_gain_gold_steals", RunState.Gold == 6, $"gold={RunState.Gold}");
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_gold", Amount = -99 });
        Check("gain_gold_clamps_at_zero_not_negative", RunState.Gold == 0, $"gold={RunState.Gold}");

        RunState.Gold = before;
    }

    // summon_enemy and escape resolve coherently from anywhere - a summon joins
    // the enemy side whoever fired it, and EscapeEffect refuses a non-enemy
    // source with a logged error rather than a throw. So a card authoring
    // either is not a crash; it is a card that does nothing, or one that spawns
    // reinforcements for the other side. Nothing in the game would report it.
    //
    // The mirror of Phase4ContentSmokeTest's card-only-scope guard, running the
    // other way: that one keeps card vocabulary off enemy moves, this one keeps
    // enemy vocabulary off cards.
    private void TestEnemyOnlyActionsStayOffCardsPotionsAndRelics()
    {
        var enemyOnly = new[] { "summon_enemy", "escape" };
        var problems = new List<string>();

        foreach (var card in CardDatabase.All)
        {
            foreach (var spec in card.Effects.Where(e => enemyOnly.Contains(e.Action)))
            {
                problems.Add($"card {card.Id}: {spec.Action}");
            }
        }
        foreach (var potion in PotionDatabase.All)
        {
            foreach (var spec in potion.Effects.Where(e => enemyOnly.Contains(e.Action)))
            {
                problems.Add($"potion {potion.Id}: {spec.Action}");
            }
        }
        foreach (var relic in RelicDatabase.All)
        {
            // A relic carries at most one spec, not a list.
            if (relic.Effect is { } effect && enemyOnly.Contains(effect.Action))
            {
                problems.Add($"relic {relic.Id}: {effect.Action}");
            }
        }

        Check("enemy_only_actions_stay_off_cards_potions_and_relics", problems.Count == 0,
            string.Join("; ", problems));
    }

    private void TestEffectDescriptionFormatter()
    {
        var strike = CardDatabase.Get("strike");

        var noContext = EffectDescriptionFormatter.Describe(strike.Effects);
        Check("description_base_damage_with_no_player_context", noContext.Contains("Deal 6 damage"),
            $"text='{noContext}'");
        // Inverted deliberately. This used to assert the "(~9 vs Vulnerable)"
        // parenthetical was appended; the hint is gone (it was a hypothetical
        // the player could not act on, and it vanished the moment the card was
        // aimed), and the check stays as the thing that catches it coming back.
        Check("description_carries_no_hypothetical_vulnerable_hint", !noContext.Contains("vs Vulnerable"),
            $"text='{noContext}'");

        var strongPlayer = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        strongPlayer.AddStatus(StatusType.Strength, 2);
        var withStrength = EffectDescriptionFormatter.Describe(strike.Effects, strongPlayer);
        Check("description_reflects_live_strength", withStrength.Contains("Deal 8 damage"),
            $"text='{withStrength}' (expected 6 base + 2 strength = 8)");

        var flex = CardDatabase.Get("flex");
        var flexText = EffectDescriptionFormatter.Describe(flex.Effects);
        Check("description_self_strength_reads_as_gain", flexText == "Gain 2 Strength.", $"text='{flexText}'");

        var weakPlayer = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        weakPlayer.AddStatus(StatusType.Weak, 1);
        var weakened = EffectDescriptionFormatter.DescribeDetailed(strike.Effects, new DescribeContext(weakPlayer));
        Check("description_reflects_live_weak", weakened.Text.Contains("Deal 4 damage"),
            $"text='{weakened.Text}' (expected 6 base * 0.75 = 4)");
        Check("description_reports_weakened_number", weakened.Weakened.Contains(4) && weakened.Buffed.Count == 0,
            $"buffed=[{string.Join(",", weakened.Buffed)}] weakened=[{string.Join(",", weakened.Weakened)}]");

        var strengthDetailed = EffectDescriptionFormatter.DescribeDetailed(strike.Effects, new DescribeContext(strongPlayer));
        Check("description_reports_buffed_number", strengthDetailed.Buffed.Contains(8) && strengthDetailed.Weakened.Count == 0,
            $"buffed=[{string.Join(",", strengthDetailed.Buffed)}] weakened=[{string.Join(",", strengthDetailed.Weakened)}]");
    }

    // The reported bug: an AllEnemies card's text was identical to a
    // single-target one's, so Cleave/Whirlwind never said they hit everything.
    private void TestAllEnemiesWording()
    {
        var cleave = CardDatabase.Get("cleave");
        var cleaveText = EffectDescriptionFormatter.Describe(cleave.Effects, new DescribeContext(TargetType: cleave.Target));
        Check("description_all_enemies_damage_says_so", cleaveText.Contains("Deal 8 damage to ALL enemies"),
            $"text='{cleaveText}'");

        var toxicCloud = CardDatabase.Get("toxic_cloud");
        var cloudText = EffectDescriptionFormatter.Describe(toxicCloud.Effects, new DescribeContext(TargetType: toxicCloud.Target));
        Check("description_all_enemies_status_says_so", cloudText.Contains("Apply 3 Poison to ALL enemies"),
            $"text='{cloudText}'");

        // A single-target card must NOT pick up the suffix, and neither must
        // the self-scoped half of a mixed card (Iron Wave's block).
        var ironWave = CardDatabase.Get("iron_wave");
        var ironWaveText = EffectDescriptionFormatter.Describe(ironWave.Effects, new DescribeContext(TargetType: ironWave.Target));
        Check("description_single_target_has_no_suffix", !ironWaveText.Contains("ALL enemies"),
            $"text='{ironWaveText}'");

        var thunderclap = CardDatabase.Get("thunderclap");
        var clapText = EffectDescriptionFormatter.Describe(thunderclap.Effects, new DescribeContext(TargetType: thunderclap.Target));
        // Two effects would each carry the suffix, so it is hoisted to a
        // single prefix instead of being repeated - the phrasing that let
        // Thunderclap fit its description box at one on-grid font size.
        Check("description_all_enemies_hoists_shared_suffix",
            clapText == "ALL enemies: Deal 4 damage. Apply 1 Vulnerable.",
            $"text='{clapText}'");
    }

    // Damage against the enemy actually being targeted, rather than the base
    // number an un-aimed card shows. This is the whole of what the player is
    // told about Vulnerable now that the hypothetical hint is gone, so it is
    // the check that matters.
    private void TestLiveTargetDamage()
    {
        var strike = CardDatabase.Get("strike");
        var plain = MakeEnemy();
        var vulnerable = MakeEnemy();
        vulnerable.AddStatus(StatusType.Vulnerable, 2);

        var vsPlain = EffectDescriptionFormatter.Describe(strike.Effects,
            new DescribeContext(Targets: new List<Combatant> { plain }));
        Check("description_live_plain_target_is_base", vsPlain == "Deal 6 damage.", $"text='{vsPlain}'");

        var vsVulnerable = EffectDescriptionFormatter.DescribeDetailed(strike.Effects,
            new DescribeContext(Targets: new List<Combatant> { vulnerable }));
        Check("description_live_vulnerable_target_shows_real_number",
            vsVulnerable.Text == "Deal 9 damage." && vsVulnerable.Buffed.Contains(9), $"text='{vsVulnerable.Text}'");

        // Mixed vulnerability across an AllEnemies card can't be one number.
        var cleave = CardDatabase.Get("cleave");
        var mixed = EffectDescriptionFormatter.Describe(cleave.Effects,
            new DescribeContext(null, cleave.Target, new List<Combatant> { plain, vulnerable }));
        Check("description_mixed_targets_show_a_range", mixed == "Deal 8-12 damage to ALL enemies.",
            $"text='{mixed}'");

        // A multi-hit card un-aimed is its two base hits collapsed into one
        // sentence and nothing else. The exact string is what pins that: this
        // card carried the vs-Vulnerable parenthetical longer than any other
        // (two deal_damage specs, one hint), so it is the one that would show a
        // reintroduction first.
        var twinStrike = CardDatabase.Get("twin_strike");
        var twinText = EffectDescriptionFormatter.Describe(twinStrike.Effects);
        Check("description_unaimed_multi_hit_is_base_numbers_only",
            twinText == "Deal 4 damage twice.", $"text='{twinText}'");
    }

    // Rarity is a real content field now, not decoration. Every card declaring
    // one explicitly is what stops the enum's Common default from quietly
    // absorbing a card someone forgot to tier - which is exactly the state the
    // whole pool was in before Phase 6, and why RunScore could not have a
    // Pauper category.
    private void TestEveryCardDeclaresARarity()
    {
        // The *offerable* pool, not the whole database. Curses and Status
        // cards carry a Rarity because the field has no null, but it is inert
        // on them - CardPool never offers one - so counting them here would
        // inflate the denominator below and let the Rare ceiling keep passing
        // for a reason that has nothing to do with how many Rares there are.
        var offerable = CardDatabase.All.Where(c => c.IsPlayable).ToList();
        var byRarity = offerable.GroupBy(c => c.Rarity)
            .ToDictionary(g => g.Key, g => g.Count());

        // Every tier populated: a weighting table that can roll a tier with no
        // cards in it would just re-roll forever or silently skew.
        foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare })
        {
            Check($"pool_has_{rarity.ToString().ToLowerInvariant()}_cards",
                byRarity.GetValueOrDefault(rarity) > 0, $"none authored at {rarity}");
        }

        // Rare has to stay genuinely rare. Not an exact count - content will
        // grow - but a ceiling, because a pool that drifts to a third Rares
        // makes both the weighting and the Pauper category meaningless.
        int rare = byRarity.GetValueOrDefault(Rarity.Rare);
        Check("rares_are_at_most_a_quarter_of_the_pool", rare * 4 <= offerable.Count,
            $"{rare} rare of {offerable.Count}");

        // Both unplayable types are authored, and neither can ever be handed
        // to the player as a reward. The second half is the assertion that
        // matters: CardPool.Sample is the only gate, and it is one Where().
        foreach (var type in new[] { CardType.Status, CardType.Curse })
        {
            Check($"pool_has_{type.ToString().ToLowerInvariant()}_cards",
                CardDatabase.All.Any(c => c.Type == type), $"none authored at {type}");
        }

        var offered = CardPool.Sample(CardDatabase.All, CardDatabase.All.Count, new System.Random(7));
        Check("no_unplayable_card_is_ever_offered", offered.All(c => c.IsPlayable),
            string.Join(", ", offered.Where(c => !c.IsPlayable).Select(c => c.Id)));

        // The starting deck can never contain a Rare, or Pauper is unearnable
        // by construction and silently dead.
        var starters = CardDatabase.All.Where(c => RunState.StarterCardIds.Contains(c.Id)).ToList();
        Check("starter_cards_are_not_rare", starters.All(c => c.Rarity != Rarity.Rare),
            string.Join(", ", starters.Where(c => c.Rarity == Rarity.Rare).Select(c => c.Id)));
    }

    // The behaviour the tier exists for. Sampling uniformly (which is what
    // every call site did before CardPool) would put Rares at roughly their
    // share of the pool - about 19% here - rather than the ~3% the weights
    // ask for.
    private void TestCardPoolIsRarityWeighted()
    {
        var rng = new System.Random(1234);
        int rares = 0, draws = 0;
        for (int trial = 0; trial < 400; trial++)
        {
            foreach (var card in CardPool.Sample(CardDatabase.All, 3, rng))
            {
                draws++;
                if (card.Rarity == Rarity.Rare) rares++;
            }
        }

        // Wide band on purpose: this asserts the weighting is *applied*, not
        // that a seeded RNG hits a precise ratio. Uniform sampling would land
        // near 19%, so anything under 10% proves the weights are doing work.
        double share = (double)rares / draws;
        Check("card_pool_keeps_rares_rare", share is > 0.001 and < 0.10,
            $"rare share={share:P1} over {draws} draws");

        // Without replacement: a reward screen must never offer the same card
        // twice.
        var single = CardPool.Sample(CardDatabase.All, 3, rng);
        Check("card_pool_samples_without_replacement",
            single.Select(c => c.Id).Distinct().Count() == single.Count,
            string.Join(", ", single.Select(c => c.Id)));

        // Asking for more than exists returns the pool, not an infinite loop.
        // "The pool" is the offerable half: Sample filters unplayable cards
        // out before it groups, so the ceiling is 91-of-95 rather than 95, and
        // the gap between those two numbers is the exclusion working.
        int offerableCount = CardDatabase.All.Count(c => c.IsPlayable);
        var everything = CardPool.Sample(CardDatabase.All, CardDatabase.All.Count + 5, rng);
        Check("card_pool_caps_at_offerable_pool_size", everything.Count == offerableCount,
            $"got {everything.Count} of {offerableCount}");
    }

    // The skip streak: declining a card reward shifts the *next* one's weights
    // out of Common. Four properties of the ladder, then the one check that the
    // ladder is actually wired into a draw - which is the half that can silently
    // no-op, since a weight function nobody passes to TierPool compiles, prints
    // a perfectly good balance report, and changes no card the player ever sees.
    private void TestSkipStreakLadder()
    {
        var tiers = System.Enum.GetValues<Rarity>();

        // The weights sum to the same total at every rung, which is what lets
        // the table be read as percentages - in CardPool's own comment, in the
        // balance report, and on the reward row. A step set that did not cancel
        // would leave all three of those quietly describing a different ladder.
        int baseTotal = tiers.Sum(r => CardPool.WeightOf(r, 0));
        for (int rung = 0; rung <= CardPool.MaxSkipStreak; rung++)
        {
            int total = tiers.Sum(r => CardPool.WeightOf(r, rung));
            Check($"skip_streak_rung_{rung}_keeps_the_total",
                total == baseTotal, $"rung {rung} totals {total}, rung 0 totals {baseTotal}");

            // Every tier still drawable at every rung. This is the one that
            // guards MaxSkipStreak: a rung further up drives Common to 0, and
            // PickTier would leave it in the pool and never roll it - a tier
            // that exists and cannot come back, with nothing thrown.
            foreach (var rarity in tiers)
            {
                Check($"skip_streak_rung_{rung}_keeps_{rarity.ToString().ToLowerInvariant()}_drawable",
                    CardPool.WeightOf(rarity, rung) > 0,
                    $"{rarity} weight is {CardPool.WeightOf(rarity, rung)} at rung {rung}");
            }

            // Uncommon carries the ladder. Rare passing Common at the cap is
            // intended; Rare passing *Uncommon* would make the top rung a Rare
            // dispenser rather than a richer pool, which is a different feature.
            Check($"skip_streak_rung_{rung}_is_led_by_uncommon",
                CardPool.WeightOf(Rarity.Uncommon, rung) > CardPool.WeightOf(Rarity.Rare, rung),
                $"rung {rung}: uncommon {CardPool.WeightOf(Rarity.Uncommon, rung)}, "
                + $"rare {CardPool.WeightOf(Rarity.Rare, rung)}");

            if (rung == 0) continue;
            Check($"skip_streak_rung_{rung}_beats_the_one_below",
                CardPool.WeightOf(Rarity.Rare, rung) > CardPool.WeightOf(Rarity.Rare, rung - 1),
                $"rare {CardPool.WeightOf(Rarity.Rare, rung - 1)} -> "
                + $"{CardPool.WeightOf(Rarity.Rare, rung)}");
        }

        // Clamped at both ends. The counter is capped where it is built, so
        // neither of these should be reachable in a live run - but WeightOf
        // reads a number off a save file, and a hand-edited or corrupt one must
        // land on a rung of the ladder rather than off the end of it.
        Check("skip_streak_clamps_above_the_cap",
            CardPool.WeightOf(Rarity.Rare, 99) == CardPool.WeightOf(Rarity.Rare, CardPool.MaxSkipStreak),
            $"rung 99 gave {CardPool.WeightOf(Rarity.Rare, 99)}");
        Check("skip_streak_clamps_below_zero",
            CardPool.WeightOf(Rarity.Rare, -5) == CardPool.WeightOf(Rarity.Rare, 0),
            $"rung -5 gave {CardPool.WeightOf(Rarity.Rare, -5)}");

        // And the ladder actually reaches a draw. Same 400-trial shape
        // TestCardPoolIsRarityWeighted uses, run at the cap against the same
        // seed - if the streak never made it into TierPool's weightOf, these
        // two shares are identical and every other check above still passes.
        double flat = RareShareOverTrials(0);
        double capped = RareShareOverTrials(CardPool.MaxSkipStreak);
        Check("skip_streak_actually_moves_the_draw", capped > flat * 2,
            $"rung 0 share={flat:P1}, rung {CardPool.MaxSkipStreak} share={capped:P1}");

        // The other direction, and the one that pins the streak as reward-only:
        // the overload the shop and the random-card event call is the rung-0
        // draw, unchanged. Same seed, so this is an exact match rather than a
        // band - a streak leaking into the shared overload would move it.
        var viaShared = CardPool.Sample(CardDatabase.All, 8, new System.Random(99));
        var viaRungZero = CardPool.Sample(CardDatabase.All, 8, new System.Random(99), 0);
        Check("unboosted_sample_is_the_rung_zero_draw",
            viaShared.Select(c => c.Id).SequenceEqual(viaRungZero.Select(c => c.Id)),
            $"{string.Join(",", viaShared.Select(c => c.Id))} vs "
            + $"{string.Join(",", viaRungZero.Select(c => c.Id))}");
    }

    // The Rare share of a reward-sized draw repeated 400 times at one rung.
    // Seeded identically per rung so the two calls above differ only by the
    // streak, which is what makes their comparison mean anything.
    private static double RareShareOverTrials(int streak)
    {
        var rng = new System.Random(1234);
        int rares = 0, draws = 0;
        for (int trial = 0; trial < 400; trial++)
        {
            foreach (var card in CardPool.Sample(CardDatabase.All, 3, rng, streak))
            {
                draws++;
                if (card.Rarity == Rarity.Rare) rares++;
            }
        }
        return (double)rares / draws;
    }

    // The potion half of TestEveryCardDeclaresARarity, plus one check cards do
    // not have and should: that the key is actually *authored*.
    private void TestEveryPotionDeclaresARarity()
    {
        // Read the file as text rather than trusting the deserialized objects.
        // Rarity has no null and defaults to Common, so a row that simply
        // forgot the key is indistinguishable from a row deliberately tiered
        // Common once DataFile has run - which is the single silent seam in
        // this whole feature. Same source-scan shape PixelSpecSmokeTest uses
        // for literal font-size calls, and for the same reason: the fact being
        // asserted is about the source, not about the loaded object.
        string json = Godot.FileAccess.GetFileAsString("res://data/potions/potions.json");
        int authored = System.Text.RegularExpressions.Regex.Matches(json, "\"rarity\"").Count;
        Check("every_potion_row_authors_a_rarity", authored == PotionDatabase.All.Count,
            $"{authored} rarity keys for {PotionDatabase.All.Count} potions");

        var byRarity = PotionDatabase.All.GroupBy(p => p.Rarity).ToDictionary(g => g.Key, g => g.Count());
        foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare })
        {
            Check($"potion_pool_has_{rarity.ToString().ToLowerInvariant()}_potions",
                byRarity.GetValueOrDefault(rarity) > 0, $"none authored at {rarity}");
        }

        // The tiers have to stay monotone *per row*, not per tier. A tier's
        // weight is split among its members, so authoring rows into one tier
        // and not the others silently re-orders how likely a named potion is -
        // and at these numbers it does not take much: two more Uncommons puts
        // an Uncommon potion below a Rare one. Nothing else in the repo would
        // notice, because every other assertion here is about a tier's share.
        double PerRow(Rarity r) => (double)PotionPool.WeightOf(r) / byRarity.GetValueOrDefault(r, 1);
        double common = PerRow(Rarity.Common), uncommon = PerRow(Rarity.Uncommon), rare = PerRow(Rarity.Rare);
        Check("potion_tiers_stay_monotone_by_row", common > uncommon && uncommon > rare,
            $"per-row odds C={common:F2} U={uncommon:F2} R={rare:F2} "
            + $"over {byRarity.GetValueOrDefault(Rarity.Common)}/"
            + $"{byRarity.GetValueOrDefault(Rarity.Uncommon)}/{byRarity.GetValueOrDefault(Rarity.Rare)} rows");
    }

    // The relic half of the two above, plus the one assertion that is actually
    // about relic tiers rather than about tiering in general: which pool each
    // grant site draws from. That is the whole feature - the tier field exists
    // so a boss cannot hand over what 150 gold would have - and it is the part
    // no other suite looks at.
    private void TestEveryRelicDeclaresATier()
    {
        // Text, not the loaded objects, for the reason the potion version
        // spells out: RelicTier has no null and defaults to Common, so a
        // forgotten key and an authored Common are the same object afterwards.
        string json = Godot.FileAccess.GetFileAsString("res://data/relics/relics.json");
        int authored = System.Text.RegularExpressions.Regex.Matches(json, "\"tier\"").Count;
        Check("every_relic_row_authors_a_tier", authored == RelicDatabase.All.Count,
            $"{authored} tier keys for {RelicDatabase.All.Count} relics");

        var byTier = RelicDatabase.All.GroupBy(r => r.Tier).ToDictionary(g => g.Key, g => g.Count());

        // Driven over every enum member rather than a hand-written list. A
        // seventh RelicTier added later and authored on nothing is a pool that
        // silently never yields, and the shape of test that would miss it is
        // exactly the hardcoded three-element Rarity arrays elsewhere in this
        // file - which is also why RelicTier is not Rarity.
        foreach (RelicTier tier in System.Enum.GetValues<RelicTier>())
        {
            Check($"relic_pool_has_{tier.ToString().ToLowerInvariant()}_relics",
                byTier.GetValueOrDefault(tier) > 0, $"none authored at {tier}");
        }

        // Monotone per *row*, over the Common/Uncommon/Rare ladder only. Boss,
        // Shop and Event are deliberately excluded: they are sources rather
        // than power levels, they are never weighed against the ladder except
        // at one site each, and Boss is never weighed against anything at all.
        double PerRow(RelicTier t) => (double)RelicPool.WeightOf(t) / byTier.GetValueOrDefault(t, 1);
        double common = PerRow(RelicTier.Common), uncommon = PerRow(RelicTier.Uncommon);
        double rare = PerRow(RelicTier.Rare);
        Check("relic_tiers_stay_monotone_by_row", common > uncommon && uncommon > rare,
            $"per-row odds C={common:F2} U={uncommon:F2} R={rare:F2} "
            + $"over {byTier.GetValueOrDefault(RelicTier.Common)}/"
            + $"{byTier.GetValueOrDefault(RelicTier.Uncommon)}/{byTier.GetValueOrDefault(RelicTier.Rare)} rows");
    }

    // Which pool each site draws from, sampled rather than asserted about
    // RelicPool.TiersFor - reading the table back would pass against a Sample
    // that ignored it entirely.
    private void TestRelicSitesDrawFromTheirOwnPools()
    {
        RunState.Relics = new List<RelicInstance>();

        List<RelicTier> Drawn(RelicSite site, int draws)
        {
            var rng = new System.Random(90210);
            var seen = new List<RelicTier>();
            for (int i = 0; i < draws; i++)
            {
                if (RelicPool.SampleOne(site, rng) is { } relic) seen.Add(relic.Tier);
            }
            return seen;
        }

        var boss = Drawn(RelicSite.Boss, 400);
        Check("boss_reward_draws_only_boss_relics",
            boss.Count == 400 && boss.All(t => t == RelicTier.Boss),
            $"{boss.Count} draws, tiers seen: {string.Join(",", boss.Distinct())}");

        // The other half of the same claim, and the one a player would notice:
        // a Boss relic turning up in a chest is the feature not working.
        foreach (var site in new[] { RelicSite.Reward, RelicSite.Treasure, RelicSite.Shop, RelicSite.Event })
        {
            var drawn = Drawn(site, 400);
            var illegal = drawn.Distinct().Except(RelicPool.TiersFor(site)).ToList();
            Check($"{site.ToString().ToLowerInvariant()}_never_draws_outside_its_tiers",
                illegal.Count == 0, $"leaked: {string.Join(",", illegal)}");
        }

        // Each exclusive tier is reachable from its own site and from nowhere
        // else. Authoring a Shop relic that no shop ever stocks is the failure
        // this catches, and it is silent: every other assertion here passes.
        var shopDraws = Drawn(RelicSite.Shop, 400);
        var eventDraws = Drawn(RelicSite.Event, 400);
        Check("shop_actually_reaches_its_own_tier", shopDraws.Contains(RelicTier.Shop),
            "400 shop draws yielded no Shop-tier relic");
        Check("event_actually_reaches_its_own_tier", eventDraws.Contains(RelicTier.Event),
            "400 event draws yielded no Event-tier relic");

        // Own every Boss relic and a boss must still pay - from the ladder,
        // since its own tier is spent. Silence is the wrong failure here: it
        // would deny the reward to precisely the player who earned it most.
        RunState.Relics = RelicDatabase.All
            .Where(r => r.Tier == RelicTier.Boss)
            .Select(r => new RelicInstance(r))
            .ToList();
        var fallback = RelicPool.SampleOne(RelicSite.Boss, new System.Random(7));
        Check("boss_falls_back_to_the_ladder_when_its_tier_is_owned_out",
            fallback is not null && fallback.Tier != RelicTier.Boss,
            $"got {fallback?.Id ?? "null"} with every Boss relic already owned");
        RunState.Relics = new List<RelicInstance>();

        TestBossOffersAChoiceOfThree();
    }

    // A boss offers three relics rather than granting one, so "the pool can pay"
    // is now a question about a *count*. Every assertion above it draws one at a
    // time and would pass against a tier with a single row left.
    private void TestBossOffersAChoiceOfThree()
    {
        const int choices = 3;
        RunState.Relics = new List<RelicInstance>();

        var offered = RelicPool.Sample(RelicSite.Boss, choices, new System.Random(31));
        Check("boss_offers_three_distinct_relics",
            offered.Count == choices && offered.Select(r => r.Id).Distinct().Count() == choices,
            $"got {offered.Count}: {string.Join(",", offered.Select(r => r.Id))}");
        Check("boss_offers_three_boss_relics", offered.All(r => r.Tier == RelicTier.Boss),
            string.Join(",", offered.Select(r => $"{r.Id}:{r.Tier}")));

        // The content half, and the one that goes stale rather than breaking: a
        // run reaches two non-final bosses, so the tier has to carry a choice
        // plus what the first boss took. relic_pool_has_boss_relics above only
        // proves there is one. The top-up below would cover a shortfall, but
        // covering it with Rares is a fallback, not the design.
        int bossStock = RelicDatabase.All.Count(r => r.Tier == RelicTier.Boss);
        int rewardBosses = ActDatabase.Count - 1; // The final act's boss routes to Victory instead.
        Check("boss_tier_can_pay_every_boss_in_a_run", bossStock >= choices + rewardBosses - 1,
            $"{bossStock} Boss relics for {rewardBosses} boss rewards offering {choices} each");

        // Own all but two, and the draw still has to come back with three. This
        // is the arm the == 0 fallback could not reach: at two rows left the
        // pool was non-empty, so nothing topped it up and a boss silently
        // offered two tiles. Sampling one at a time - which is all any
        // assertion above does - never sees it.
        RunState.Relics = RelicDatabase.All
            .Where(r => r.Tier == RelicTier.Boss)
            .Skip(2)
            .Select(r => new RelicInstance(r))
            .ToList();

        var topped = RelicPool.Sample(RelicSite.Boss, choices, new System.Random(11));
        Check("boss_tops_up_from_the_ladder_when_its_tier_runs_short",
            topped.Count == choices && topped.Select(r => r.Id).Distinct().Count() == choices,
            $"got {topped.Count} with only 2 Boss relics left: {string.Join(",", topped.Select(r => r.Id))}");
        // Both remaining Boss relics are in the offer, and this is the assertion
        // that pins *how* the top-up works rather than merely that it happens.
        // Filling the shortfall by widening the first draw's pool would put the
        // ladder in the same tier roulette as Boss, where BossWeight is about
        // half the total - so a boss with two of its own left would offer
        // Commons instead about one tile in two while both Boss relics sat
        // unoffered, and every other check here would still pass. The ladder is
        // only asked for what the site's own tiers could not supply.
        Check("boss_top_up_exhausts_its_own_tier_before_the_ladder",
            topped.Count(r => r.Tier == RelicTier.Boss) == 2,
            $"expected both remaining Boss relics, got {string.Join(",", topped.Select(r => $"{r.Id}:{r.Tier}"))}");
        Check("boss_top_up_never_offers_one_already_owned",
            !topped.Any(t => RunState.Relics.Any(owned => owned.Definition.Id == t.Id)),
            string.Join(",", topped.Select(r => r.Id)));

        RunState.Relics = new List<RelicInstance>();
    }

    // The tip line's data, which has no other guard: RewardScreen hides the row
    // when nothing loads, so a broken tips.json is invisible in the game.
    private void TestTipsAreAuthoredAndFit()
    {
        Check("tips_loaded", TipDatabase.All.Count > 0, "no tips authored");

        var badId = TipDatabase.All.Where(t => t.Id.Length == 0 || t.Text.Length == 0).ToList();
        Check("every_tip_has_an_id_and_text", badId.Count == 0,
            $"{badId.Count} empty row(s)");

        var duplicated = TipDatabase.All.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check("tip_ids_are_unique", duplicated.Count == 0, string.Join(", ", duplicated));

        // ASCII only, like every other UI string: the pixel faces carry no
        // punctuation past it (ART_SPEC section 5), so a curly quote pasted in
        // from anywhere renders as a blank box.
        var nonAscii = TipDatabase.All.Where(t => t.Text.Any(c => c > 127)).Select(t => t.Id).ToList();
        Check("tips_are_ascii_only", nonAscii.Count == 0, string.Join(", ", nonAscii));

        // Every {hd_*} token names a real action. An unresolved one renders as
        // the literal "{hd_typo}" on the reward screen - deliberately visible
        // rather than silently blank, but this is what should catch it first.
        var unknown = TipDatabase.All
            .SelectMany(t => ScreenKeyboardNav.KeyHintTokens(t.Text).Select(a => (t.Id, Action: a)))
            .Where(x => !InputMap.HasAction(x.Action))
            .ToList();
        Check("tip_key_hints_name_real_actions", unknown.Count == 0,
            string.Join(", ", unknown.Select(x => $"{x.Id}:{x.Action}")));

        // A rotation, so a full lap must visit every tip exactly once and land
        // back where it started. This is the property that makes it worth not
        // being a roll, and it is one line to lose.
        int n = TipDatabase.All.Count;
        var lap = Enumerable.Range(0, n).Select(v => TipDatabase.ForVisit(1234, v)!.Id).ToList();
        Check("a_full_lap_of_tips_repeats_nothing", lap.Distinct().Count() == n,
            $"{lap.Distinct().Count()} distinct over {n} visits");
        Check("tips_wrap_around", TipDatabase.ForVisit(1234, n)!.Id == lap[0],
            $"visit {n} gave {TipDatabase.ForVisit(1234, n)!.Id}, visit 0 gave {lap[0]}");

        // A negative seed must not index backwards off the front. RunSeed comes
        // from Random.Next() so it is non-negative today, but a seed-entry
        // screen is on the roadmap and typing one in is the obvious way this
        // stops being true.
        var negative = TipDatabase.ForVisit(-99, 3);
        Check("a_negative_seed_still_picks_a_tip", negative is not null, "null for seed -99");
    }

    // The potion half of TestCardPoolIsRarityWeighted. Same argument: uniform
    // sampling - which is what the shop and the event outcome both did before
    // PotionPool - would put Rares at their share of the pool, 2 of 12 or
    // ~17%, rather than the 10% the weights ask for.
    private void TestPotionPoolIsRarityWeighted()
    {
        var rng = new System.Random(4321);
        int rares = 0, draws = 0;
        for (int trial = 0; trial < 600; trial++)
        {
            var picked = PotionPool.SampleOne(PotionDatabase.All, rng);
            draws++;
            if (picked!.Rarity == Rarity.Rare) rares++;
        }

        // Band, not a point: this asserts the weighting is applied, not that a
        // seeded RNG hits 10% exactly. Uniform would land near 17%.
        double share = (double)rares / draws;
        Check("potion_pool_keeps_rares_rare", share is > 0.03 and < 0.15,
            $"rare share={share:P1} over {draws} draws");

        // Without replacement: the shop must never stock the same potion twice.
        var stock = PotionPool.Sample(PotionDatabase.All, 2, rng);
        Check("potion_pool_samples_without_replacement",
            stock.Select(p => p.Id).Distinct().Count() == stock.Count,
            string.Join(", ", stock.Select(p => p.Id)));
    }

    // A tiered row for the synthetic pools below. Deliberately not a card or a
    // potion: TierPool is generic, and pinning its behaviour against real
    // content would produce an assertion that goes red every time someone
    // authors a row and therefore gets deleted the second time it does.
    private sealed record Tiered(string Id, Rarity Rarity);

    // TierPool is the draw CardPool and PotionPool share. Both of them assert
    // their *weights* elsewhere; what is pinned here is the algorithm, because
    // that is the half an extraction can break silently and the half neither
    // caller would notice - a band on a rare share still passes if the draw
    // stopped being without-replacement or started biasing an exhausted tier.
    private void TestTierPoolAlgorithm()
    {
        // Six rows, three tiers, uneven - the shape both real pools have.
        var pool = new List<Tiered>
        {
            new("c1", Rarity.Common), new("c2", Rarity.Common), new("c3", Rarity.Common),
            new("u1", Rarity.Uncommon), new("u2", Rarity.Uncommon),
            new("r1", Rarity.Rare),
        };
        static Rarity TierOf(Tiered t) => t.Rarity;
        static int Weight(Rarity r) => r switch { Rarity.Rare => 10, Rarity.Uncommon => 25, _ => 65 };

        // Asking for more than exists drains the pool exactly once each rather
        // than looping forever or repeating a row. This is the assertion that
        // covers both remove-on-pick and the tier being dropped when it empties.
        var drained = TierPool.Sample(pool, 20, new System.Random(5), TierOf, Weight);
        Check("tier_pool_drains_every_tier_exactly_once",
            drained.Count == pool.Count && drained.Select(t => t.Id).Distinct().Count() == pool.Count,
            $"got {drained.Count} of {pool.Count}: {string.Join(",", drained.Select(t => t.Id))}");

        // Same seed, same sequence. TierPool groups into a Dictionary, whose
        // key order is not guaranteed - PickTier's OrderBy is what makes this
        // hold, and it is the single line an extraction is most likely to drop.
        string first = string.Join(",", TierPool.Sample(pool, 6, new System.Random(11), TierOf, Weight)
            .Select(t => t.Id));
        string second = string.Join(",", TierPool.Sample(pool, 6, new System.Random(11), TierOf, Weight)
            .Select(t => t.Id));
        Check("tier_pool_draws_in_a_fixed_order", first == second, $"{first} then {second}");

        // An exhausted tier is *removed*, not re-rolled: with a 99:1 split, a
        // two-row pool must still hand back both rows on a two-draw. If the
        // tier stayed in the roulette after emptying, this would come back
        // short (Sample breaks when PickTier can find nothing to draw).
        var lopsided = new List<Tiered> { new("c", Rarity.Common), new("r", Rarity.Rare) };
        static int Lopsided(Rarity r) => r == Rarity.Rare ? 1 : 99;
        bool bothEveryTime = true;
        for (int seed = 0; seed < 50; seed++)
        {
            var drawn = TierPool.Sample(lopsided, 2, new System.Random(seed), TierOf, Lopsided);
            if (drawn.Count != 2) bothEveryTime = false;
        }
        Check("tier_pool_renormalises_an_exhausted_tier", bothEveryTime,
            "a two-draw on a two-row pool came back short");

        // The weight function is honoured rather than ignored: inverting it has
        // to invert which tier dominates. Without this, a sampler that quietly
        // drew uniformly would pass every other check here.
        static int Inverted(Rarity r) => r switch { Rarity.Rare => 65, Rarity.Uncommon => 25, _ => 10 };
        int raresNormal = 0, raresInverted = 0;
        var rng = new System.Random(77);
        for (int trial = 0; trial < 400; trial++)
        {
            if (TierPool.SampleOne(pool, rng, TierOf, Weight)!.Rarity == Rarity.Rare) raresNormal++;
            if (TierPool.SampleOne(pool, rng, TierOf, Inverted)!.Rarity == Rarity.Rare) raresInverted++;
        }
        Check("tier_pool_actually_reads_the_weight_table", raresInverted > raresNormal * 3,
            $"rare draws: {raresNormal} weighted vs {raresInverted} inverted of 400 each");
    }

    // A Power leaves the fight when played: not to Discard (it would cycle
    // back and be re-playable) and not to Exhaust (which is a cost, and the
    // HUD renders it as one).
    private void TestPowerCardsLeavePlay()
    {
        var powers = CardDatabase.All.Where(c => c.Type == CardType.Power).ToList();
        Check("pool_has_at_least_one_power", powers.Count > 0,
            "CardType.Power exists but no card in cards.json uses it");
        if (powers.Count == 0) return;

        var combat = new CombatManager();
        AddChild(combat);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50,
            MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(CardDatabase.All),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        var power = new CardInstance(powers[0]);
        player.Piles.Hand.Add(power);
        int discardBefore = player.Piles.Discard.Count;
        int exhaustBefore = player.Piles.Exhaust.Count;
        combat.TryPlayCard(power);

        Check("power_goes_to_the_powers_pile", player.Piles.Powers.Contains(power),
            $"powers={player.Piles.Powers.Count}");
        Check("power_does_not_go_to_discard", player.Piles.Discard.Count == discardBefore,
            $"discard {discardBefore} -> {player.Piles.Discard.Count}");
        Check("power_does_not_go_to_exhaust", player.Piles.Exhaust.Count == exhaustBefore,
            $"exhaust {exhaustBefore} -> {player.Piles.Exhaust.Count}");
        Check("power_left_the_hand", !player.Piles.Hand.Contains(power), "still in hand");

        // And its effect actually landed - a Power that leaves play without
        // doing anything is the failure mode this routing could hide.
        Check("power_effect_resolved", player.GetStatus(StatusType.Strength) > 0,
            $"strength={player.GetStatus(StatusType.Strength)}");

        // Reshuffling the discard back into the draw pile must not bring it
        // back: this is the whole difference between a Power and a Skill.
        player.Piles.DrawPile.Clear();
        player.Piles.DrawHand(5);
        Check("power_never_returns_to_the_draw_pile",
            !player.Piles.DrawPile.Contains(power) && !player.Piles.Hand.Contains(power),
            "the played Power came back around");

        combat.QueueFree();
    }

    // Metallicize and Ritual are what make Power a real card class: they pay
    // out every turn, which no recurring Skill can do. Driven through a live
    // CombatManager rather than by poking the tick directly, because the thing
    // most likely to break is *ordering* - both combatants clear Block on
    // their own turn, and a grant that lands before that clear is wiped the
    // instant it is given.
    //
    // All In carries exhaust_hand *and* deals damage, so the order matters:
    // if the card were still in hand when its own effect ran, it would exhaust
    // itself mid-resolution. It isn't - ResolveCard moves the played card to
    // its destination pile before touching its EffectSpecs - but that is an
    // ordering two files apart, so it gets a test rather than a comment.
    private void TestExhaustHandCardDoesNotEatItself()
    {
        var combat = new CombatManager();
        AddChild(combat);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50,
            MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(CardDatabase.All),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        player.Piles.Hand.Clear();
        player.Piles.Exhaust.Clear();
        var allIn = new CardInstance(CardDatabase.Get("all_in"));
        player.Piles.Hand.Add(allIn);
        foreach (var id in new[] { "strike", "defend", "cleave" })
        {
            player.Piles.Hand.Add(new CardInstance(CardDatabase.Get(id)));
        }

        int enemyHpBefore = enemy.CurrentHp;
        combat.TryPlayCard(allIn);

        Check("exhaust_hand_card_exhausts_the_other_three_and_itself",
            player.Piles.Hand.Count == 0 && player.Piles.Exhaust.Count == 4,
            $"hand={player.Piles.Hand.Count} exhaust={player.Piles.Exhaust.Count}");
        // The real assertion: the damage landed. If All In had exhausted
        // itself, resolution would have stopped and the enemy would be untouched.
        Check("exhaust_hand_card_still_resolves_its_own_damage",
            enemy.CurrentHp < enemyHpBefore,
            $"enemy hp {enemyHpBefore} -> {enemy.CurrentHp}");

        combat.QueueFree();
    }

    // Dexterity and Frail are Strength and Weak applied to Block, and the
    // thing worth pinning is that they are read off whoever *gains* the Block
    // rather than off ctx.Source - which is why the last case here hands the
    // block to a combatant other than the caster.
    private void TestDexterityAndFrailBlock()
    {
        var player = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        var ctx = new EffectContext { Source = player, Targets = new List<Combatant> { player }, Combat = null! };

        player.AddStatus(StatusType.Dexterity, 3);
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_block", Amount = 5 });
        Check("dexterity_adds_flat_block", player.Block == 8, $"block={player.Block}");

        // Frail multiplies *after* Dexterity, exactly as Weak applies after
        // Strength: (5 + 3) * 0.75 = 6.
        player.Block = 0;
        player.AddStatus(StatusType.Frail, 2);
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "gain_block", Amount = 5 });
        Check("frail_cuts_block_after_dexterity", player.Block == 6, $"block={player.Block}");

        var ally = MakeEnemy();
        var crossCtx = new EffectContext
        {
            Source = player,
            Targets = new List<Combatant> { ally },
            Combat = null!,
        };
        EffectRegistry.Execute(crossCtx, new EffectSpec { Action = "gain_block", Amount = 5 });
        Check("block_reads_the_receivers_statuses_not_the_casters", ally.Block == 5,
            $"block={ally.Block} - 8 means the caster's Dexterity leaked onto someone else");
    }

    private void TestDiscardAndExhaustHand()
    {
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 50, CurrentHp = 50,
            Piles = new PileManager(CardDatabase.All),
        };
        var ctx = new EffectContext { Source = player, Targets = new List<Combatant> { player }, Combat = null! };

        player.Piles.DrawHand(5);
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "discard_cards", Amount = 2 });
        Check("discard_cards_moves_hand_to_discard",
            player.Piles.Hand.Count == 3 && player.Piles.Discard.Count == 2,
            $"hand={player.Piles.Hand.Count} discard={player.Piles.Discard.Count}");

        // Asking for more than the hand holds must empty it rather than throw.
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "discard_cards", Amount = 99 });
        Check("discard_cards_stops_at_an_empty_hand", player.Piles.Hand.Count == 0,
            $"hand={player.Piles.Hand.Count}");

        player.Piles.DrawHand(4);
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "exhaust_hand", Amount = 0 });
        Check("exhaust_hand_empties_the_hand_into_exhaust",
            player.Piles.Hand.Count == 0 && player.Piles.Exhaust.Count == 4,
            $"hand={player.Piles.Hand.Count} exhaust={player.Piles.Exhaust.Count}");
    }

    // The formatter's "Gain N X" vs "Apply N X" split is by scope, not by
    // status name - it used to special-case Strength, which read correctly
    // right up until a second self-status shipped. These three are the ones
    // that would have broken it.
    // The general form of the failure CardUpgrade.ShouldScale warns about, and
    // the reason it is worth a sweep rather than a spot check: an upgrade that
    // scales nothing is not an error anywhere - it produces a valid card, with
    // a "+" on its name, that reads and plays exactly like the original. The
    // player pays a rest site for it.
    //
    // Every card must therefore have at least one effect the upgrade moves.
    // A card built only from costs (lose_hp, discard_cards, exhaust_hand) would
    // legitimately fail this; there are none, and if one is ever authored it
    // needs a benefit to scale rather than an exemption here.
    private void TestEveryCardUpgradeChangesSomething()
    {
        var unchanged = new List<string>();
        // Unplayable cards are skipped rather than exempted by name:
        // CardUpgrade.Apply refuses them outright (there is no Wound+), so
        // every one of them would fail this by construction and the failure
        // would say nothing.
        foreach (var card in CardDatabase.All.Where(c => c.IsPlayable))
        {
            var upgraded = CardUpgrade.Apply(card);
            bool moved = card.Effects
                .Zip(upgraded.Effects, (before, after) => after.Amount > before.Amount)
                .Any(x => x);
            if (!moved) unchanged.Add(card.Id);
        }

        Check("every_card_upgrade_changes_something", unchanged.Count == 0,
            $"upgrading these changes nothing: {string.Join(", ", unchanged)} - " +
            "the status or action is missing from CardUpgrade's scale lists");
    }

    // The same silent-omission failure CardUpgrade.ShouldScale has, one layer
    // up. Keywords.Blurb's default arm renders a status as bare "Weak." rather
    // than throwing, so a twelfth StatusType would ship with a hover panel that
    // explains nothing and nothing anywhere would go red. This is what goes red.
    //
    // Before Keywords existed there were two rosters - eleven statuses in
    // StatusRow's tooltips against five in CardView's hover panel - and the gap
    // had gone unnoticed through every status added since.
    private void TestEveryStatusHasAKeywordBlurb()
    {
        var missing = new List<string>();
        foreach (var status in System.Enum.GetValues<StatusType>())
        {
            string generic = Keywords.Blurb(status);
            string withAmount = Keywords.Blurb(status, 3);
            // The default arm's shape: the status name and nothing else.
            if (generic == $"{status}." || withAmount == $"{status} 3.") missing.Add(status.ToString());
        }

        Check("every_status_has_a_keyword_blurb", missing.Count == 0,
            $"no hover explanation for: {string.Join(", ", missing)} - add an arm to Keywords.Blurb");

        Check("every_status_is_in_the_keyword_roster",
            System.Enum.GetValues<StatusType>().All(s => Keywords.All.Any(e => e.Keyword == s.ToString())),
            "a status missing from Keywords.All never gets highlighted or explained in card text");

        // StatusRow's icon tooltips are the one caller that re-attaches the
        // name, and their wording is load-bearing enough to pin: this is the
        // text a player reads to learn what the number on the icon means.
        Check("status_tooltip_composes_name_amount_and_blurb",
            Keywords.StatusTooltip(StatusType.Strength, 3) == "Strength 3: attacks deal +3 damage.",
            $"got '{Keywords.StatusTooltip(StatusType.Strength, 3)}'");
    }

    // An enemy's telegraph is the same EffectSpecs as a card, described from the
    // other side. One formatter with a voice, rather than a second formatter -
    // so this pins that the voice actually changes the verb and that the
    // player-facing default is untouched by it.
    private void TestEnemyVoiceDescriptions()
    {
        // Both sides get a known target, which is what EnemyView does for real
        // (CombatManager.Instance.Player) - the numbers below are therefore the
        // ones that would actually land, not base amounts. The verb is what
        // this cares about either way.
        var victim = new PlayerCombatant { Name = "Player", MaxHp = 50, CurrentHp = 50 };
        var targets = new List<Combatant> { victim };

        string Enemy(params EffectSpec[] effects) =>
            EffectDescriptionFormatter.Describe(effects.ToList(),
                new DescribeContext(Targets: targets, Voice: DescribeVoice.Enemy));
        string Player(params EffectSpec[] effects) =>
            EffectDescriptionFormatter.Describe(effects.ToList(), new DescribeContext(Targets: targets));

        var attack = new EffectSpec { Action = "deal_damage", Amount = 12, Scope = EffectScope.Target };
        var block = new EffectSpec { Action = "gain_block", Amount = 8, Scope = EffectScope.Self };
        var debuff = new EffectSpec { Action = "apply_status", Status = "Weak", Amount = 2, Scope = EffectScope.Target };
        var buff = new EffectSpec { Action = "apply_status", Status = "Strength", Amount = 3, Scope = EffectScope.Self };

        Check("enemy_voice_uses_third_person_verbs",
            Enemy(attack) == "Deals 12 damage to you."
            && Enemy(block) == "Gains 8 Block."
            && Enemy(debuff) == "Applies 2 Weak to you."
            && Enemy(buff) == "Gains 3 Strength.",
            $"got '{Enemy(attack)}' / '{Enemy(block)}' / '{Enemy(debuff)}' / '{Enemy(buff)}'");

        Check("player_voice_is_unchanged_by_the_new_field",
            Player(attack) == "Deal 12 damage."
            && Player(block) == "Gain 8 Block."
            && Player(debuff) == "Apply 2 Weak."
            && Player(buff) == "Gain 3 Strength.",
            $"got '{Player(attack)}' / '{Player(block)}' / '{Player(debuff)}' / '{Player(buff)}'");

        // The multi-hit collapse is shared with the intent row's "12 x2", so a
        // tooltip and the number beside it can't disagree about what one hit is.
        Check("enemy_voice_collapses_multi_hits",
            Enemy(attack, attack) == "Deals 12 damage to you twice.",
            $"got '{Enemy(attack, attack)}'");
    }

    private void TestNewStatusDescriptions()
    {
        string Describe(params EffectSpec[] effects) =>
            EffectDescriptionFormatter.Describe(effects.ToList(), new DescribeContext());

        Check("dexterity_reads_as_gained",
            Describe(new EffectSpec { Action = "apply_status", Status = "Dexterity", Amount = 2, Scope = EffectScope.Self })
                == "Gain 2 Dexterity.", "self-scoped statuses must say Gain");

        Check("regen_reads_as_gained",
            Describe(new EffectSpec { Action = "apply_status", Status = "Regen", Amount = 3, Scope = EffectScope.Self })
                == "Gain 3 Regen.", "self-scoped statuses must say Gain");

        Check("frail_reads_as_applied",
            Describe(new EffectSpec { Action = "apply_status", Status = "Frail", Amount = 2, Scope = EffectScope.Target })
                == "Apply 2 Frail.", "target-scoped statuses must say Apply");

        Check("discard_and_exhaust_hand_have_description_text",
            Describe(new EffectSpec { Action = "discard_cards", Amount = 2, Scope = EffectScope.Self })
                == "Discard 2 cards at random."
            && Describe(new EffectSpec { Action = "exhaust_hand", Scope = EffectScope.Self })
                == "Exhaust your hand.",
            "a missing formatter arm renders the effect invisible on the card");

        // gain_gold shipped for a relic, and relics describe themselves from
        // their own JSON - so the arm was missing here until a card used it,
        // and Tithe rendered with no rules text at all.
        Check("gain_gold_has_description_text",
            Describe(new EffectSpec { Action = "gain_gold", Amount = 15, Scope = EffectScope.Self })
                == "Gain 15 Gold.",
            "a missing formatter arm renders the effect invisible on the card");

        Check("fervor_and_foresight_read_as_gained",
            Describe(new EffectSpec { Action = "apply_status", Status = "Fervor", Amount = 1, Scope = EffectScope.Self })
                == "Gain 1 Fervor."
            && Describe(new EffectSpec { Action = "apply_status", Status = "Foresight", Amount = 2, Scope = EffectScope.Self })
                == "Gain 2 Foresight.", "self-scoped statuses must say Gain");

        // The silent failure CardUpgrade.ShouldScale documents: a status left
        // out of its list upgrades to a "+" that reads and plays identically.
        // Checked on the two newest because they are the ones most recently
        // at risk of it, and on the whole roster's worth of Self statuses via
        // the cards themselves in TestEveryCardUpgradeChangesSomething.
        var deepFocus = CardUpgrade.Apply(CardDatabase.Get("deep_focus"));
        var bloodpact = CardUpgrade.Apply(CardDatabase.Get("bloodpact"));
        Check("new_power_statuses_scale_on_upgrade",
            deepFocus.Effects[0].Amount == 3 && bloodpact.Effects[0].Amount == 2,
            $"deep_focus+={deepFocus.Effects[0].Amount} (want 3), bloodpact+={bloodpact.Effects[0].Amount} (want 2)");
    }

    // Metallicize is tested on the enemy and Ritual on the player, which is
    // not arbitrary: nothing reduces the enemy's Block in this fixture (the
    // player never attacks), so its value is exact, whereas the player is
    // being hit every round and its Block is whatever survived. Between them
    // both call sites are covered - and the enemy's is the awkward one, since
    // its Block clear happens mid-loop rather than in BeginPlayerTurn.
    private async System.Threading.Tasks.Task TestTurnStartGrantingStatuses()
    {
        var combat = new CombatManager();
        AddChild(combat);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 200,
            MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(CardDatabase.All),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        player.AddStatus(StatusType.Ritual, 2);
        enemy.AddStatus(StatusType.Metallicize, 4);
        int strengthBefore = player.GetStatus(StatusType.Strength);

        await RunOneRound(combat);

        Check("metallicize_grants_block_after_the_block_clear", enemy.Block == 4,
            $"block={enemy.Block} - 0 means the grant landed before the clear and was wiped");
        Check("ritual_grants_strength_each_turn",
            player.GetStatus(StatusType.Strength) == strengthBefore + 2,
            $"strength={player.GetStatus(StatusType.Strength)}");
        Check("granting_statuses_do_not_decay",
            enemy.GetStatus(StatusType.Metallicize) == 4 && player.GetStatus(StatusType.Ritual) == 2,
            $"metallicize={enemy.GetStatus(StatusType.Metallicize)} ritual={player.GetStatus(StatusType.Ritual)}");

        await RunOneRound(combat);

        // Ritual compounds - that is the whole reason it is worth a card slot
        // it never returns from. Metallicize does not, because Block is
        // cleared and re-granted rather than added to.
        Check("ritual_compounds_over_turns",
            player.GetStatus(StatusType.Strength) == strengthBefore + 4,
            $"strength={player.GetStatus(StatusType.Strength)} after two rounds");
        Check("metallicize_does_not_accumulate_across_turns", enemy.Block == 4,
            $"block={enemy.Block} after two rounds");

        combat.QueueFree();
    }

    // Regen is the third turn-start grant, and the one that heals rather than
    // granting Block or Strength - so unlike Metallicize it is indifferent to
    // where the Block clear falls, and unlike Ritual it has a ceiling. Both
    // of those are what this checks.
    private async System.Threading.Tasks.Task TestRegenHealsEachTurn()
    {
        var combat = new CombatManager();
        AddChild(combat);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 100,
            MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(CardDatabase.All),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        player.AddStatus(StatusType.Regen, 5);
        int hpBefore = player.CurrentHp;

        await RunOneRound(combat);

        // The enemy hits back, so this asserts the delta rather than an exact
        // total: HP is higher than the damage taken alone would leave it.
        Check("regen_heals_at_turn_start",
            player.CurrentHp > hpBefore - 20 && player.GetStatus(StatusType.Regen) == 5,
            $"hp={player.CurrentHp} from {hpBefore}, regen={player.GetStatus(StatusType.Regen)}");

        // Never past MaxHp - Regen ticks every turn forever, so an unclamped
        // heal would run the bar off the end of the fight.
        player.CurrentHp = player.MaxHp - 1;
        await RunOneRound(combat);
        Check("regen_never_exceeds_max_hp", player.CurrentHp <= player.MaxHp,
            $"hp={player.CurrentHp}/{player.MaxHp}");

        combat.QueueFree();
    }

    // Artifact's gate is StatusRow.IsDebuff, which until Phase 8 only decided an
    // icon tint. Nothing asserted that list was right, and now a debuff missing
    // from it walks past Artifact in silence.
    //
    // So this drives every StatusType rather than a hand-picked few, and asserts
    // the *biconditional*: refused iff IsDebuff. A test that only checked "Weak
    // is refused" would pass with an IsDebuff that had drifted, and a test that
    // only checked the debuffs would pass with an IsDebuff that returned true
    // for everything - which would make Artifact eat Strength.
    private void TestArtifactRefusesExactlyTheDebuffs()
    {
        var wrongly = new List<string>();
        var stackNotSpent = new List<string>();

        foreach (var status in System.Enum.GetValues<StatusType>())
        {
            // Artifact refusing an incoming Artifact is a coherent question with
            // no interesting answer, and it would make the stack bookkeeping
            // below ambiguous. It is a buff, so it lands; that is covered by the
            // buff half of the sweep like any other.
            var target = new EnemyCombatant { Name = "Target", MaxHp = 50, CurrentHp = 50 };
            target.AddStatus(StatusType.Artifact, 1);

            var ctx = new EffectContext
            {
                Source = new EnemyCombatant { Name = "Source", MaxHp = 50, CurrentHp = 50 },
                Targets = new List<Combatant> { target },
                Combat = null!,
            };
            EffectRegistry.Execute(ctx, new EffectSpec
            {
                Action = "apply_status", Status = status.ToString(), Amount = 3,
            });

            bool shouldRefuse = StatusRow.IsDebuff(status);
            int landed = status == StatusType.Artifact
                ? target.GetStatus(status) - 1
                : target.GetStatus(status);

            if (shouldRefuse != (landed == 0)) wrongly.Add(status.ToString());

            // One stack per refused *application*, not per stack of the debuff:
            // the spec above applies 3 and must still cost exactly one Artifact.
            int artifactLeft = status == StatusType.Artifact
                ? target.GetStatus(StatusType.Artifact) - 3
                : target.GetStatus(StatusType.Artifact);
            if (artifactLeft != (shouldRefuse ? 0 : 1)) stackNotSpent.Add(status.ToString());
        }

        Check("artifact_refuses_exactly_the_debuffs", wrongly.Count == 0,
            $"wrong for: {string.Join(", ", wrongly)} - either ApplyStatusEffect's gate or "
            + "StatusRow.IsDebuff disagrees with the other about what a debuff is");
        Check("artifact_spends_one_stack_per_application", stackNotSpent.Count == 0,
            $"wrong stack cost for: {string.Join(", ", stackNotSpent)}");

        // The control: with no Artifact held, every debuff lands. Without this
        // the sweep above would pass if ApplyStatusEffect refused nothing and
        // IsDebuff returned false for everything.
        var unwarded = new EnemyCombatant { Name = "Unwarded", MaxHp = 50, CurrentHp = 50 };
        var plainCtx = new EffectContext
        {
            Source = unwarded, Targets = new List<Combatant> { unwarded }, Combat = null!,
        };
        EffectRegistry.Execute(plainCtx, new EffectSpec
        {
            Action = "apply_status", Status = "Weak", Amount = 3,
        });
        Check("a_debuff_lands_without_artifact", unwarded.GetStatus(StatusType.Weak) == 3,
            $"weak={unwarded.GetStatus(StatusType.Weak)} - the sweep above proves nothing if "
            + "debuffs never land in the first place");
    }

    // Thorns is the one status that damages a combatant who is not in
    // ctx.Targets, so the two things worth pinning are that it fires on an
    // attack that was fully blocked (it bills the attempt, not the damage) and
    // that it does not fire on lose_hp - which is HP loss with no attacker to
    // bill, and would otherwise have to invent one.
    // That the combat engine actually *reads* the ascension rung. Everything
    // else about the ladder can be green while this is false: the data layer
    // folds, the balance report prints a perfect table of what each rung would
    // do, the toggle flips and the save round-trips - and the fight is
    // identical, because the two call sites that matter are one line each and
    // nothing else in the repo touches them.
    //
    // That is the silent data/code-seam no-op this codebase produces, and it is
    // the one failure mode a ladder authored as data is most exposed to.
    private void TestTheCombatEngineReadsTheRung()
    {
        int top = AscensionDatabase.MaxLevel;
        var asc = AscensionDatabase.Effective(top);
        int saved = RunState.AscensionLevel;

        try
        {
            // A normal and a boss, so the boss-only knob is covered as well as
            // the general one - and so a scale applied to every enemy alike
            // fails rather than passing half of this.
            var normalDef = EnemyDatabase.All.First(e => !ActDatabase.IsBoss(e.Id));
            var bossDef = EnemyDatabase.All.First(e => ActDatabase.IsBoss(e.Id));

            RunState.AscensionLevel = 0;
            var flatNormal = EnemyFactory.Create(normalDef);
            var flatBoss = EnemyFactory.Create(bossDef);

            RunState.AscensionLevel = top;
            var hardNormal = EnemyFactory.Create(normalDef);
            var hardBoss = EnemyFactory.Create(bossDef);

            Check("rung_zero_builds_the_authored_enemy",
                flatNormal.MaxHp == normalDef.MaxHp && flatNormal.CurrentHp == normalDef.MaxHp,
                $"{flatNormal.MaxHp}/{flatNormal.CurrentHp} against {normalDef.MaxHp}");
            Check("the_rung_scales_enemy_hp",
                hardNormal.MaxHp == asc.EnemyHp(normalDef.MaxHp, false) && hardNormal.MaxHp > flatNormal.MaxHp,
                $"{flatNormal.MaxHp} -> {hardNormal.MaxHp}");
            Check("an_enemy_starts_at_its_scaled_maximum", hardNormal.CurrentHp == hardNormal.MaxHp,
                $"{hardNormal.CurrentHp}/{hardNormal.MaxHp}");
            // The boss knob stacks on top, so a boss must gain proportionally
            // more than a normal does - not merely "more", which the boss's
            // bigger HP pool would give for free.
            Check("the_rung_leans_harder_on_a_boss",
                hardBoss.MaxHp / (double)flatBoss.MaxHp > hardNormal.MaxHp / (double)flatNormal.MaxHp,
                $"boss {flatBoss.MaxHp}->{hardBoss.MaxHp}, normal {flatNormal.MaxHp}->{hardNormal.MaxHp}");

            // The damage half, through DamageMath - which is the function both
            // DealDamageEffect and EnemyView.LiveAttackAmount call, so the
            // telegraph and the hit scale together and cannot disagree.
            var enemy = new EnemyCombatant { Name = "Enemy", MaxHp = 50, CurrentHp = 50 };
            var player = new PlayerCombatant { MaxHp = 50, CurrentHp = 50 };

            RunState.AscensionLevel = 0;
            int flatHit = DamageMath.ComputeOutgoing(10, enemy);
            RunState.AscensionLevel = top;
            int hardHit = DamageMath.ComputeOutgoing(10, enemy);

            Check("the_rung_scales_enemy_damage",
                flatHit == 10 && hardHit == asc.EnemyDamage(10) && hardHit > flatHit,
                $"{flatHit} -> {hardHit}");

            // The gate. The player's cards go through the same function, and a
            // ladder that scaled them would hand back what every other knob is
            // taking - invisibly, since the player's own damage preview reads
            // this too and would agree with itself all the way down.
            Check("the_rung_leaves_the_players_damage_alone",
                DamageMath.ComputeOutgoing(10, player) == 10,
                $"player hit {DamageMath.ComputeOutgoing(10, player)} at rung {top}");

            // And end to end, so a scale applied to the preview but not to
            // resolution - or the reverse - fails here.
            var ctx = new EffectContext
            {
                Source = enemy, Targets = new List<Combatant> { player }, Combat = null!,
            };
            EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 10 });
            Check("a_scaled_hit_lands_scaled", player.CurrentHp == 50 - hardHit,
                $"player hp={player.CurrentHp}, expected {50 - hardHit}");
        }
        finally
        {
            RunState.AscensionLevel = saved;
        }
    }

    private void TestThornsBillsTheAttackerOnlyForAnAttack()
    {
        var attacker = new EnemyCombatant { Name = "Attacker", MaxHp = 50, CurrentHp = 50 };
        var target = new EnemyCombatant { Name = "Target", MaxHp = 50, CurrentHp = 50 };
        target.AddStatus(StatusType.Thorns, 3);

        var ctx = new EffectContext
        {
            Source = attacker, Targets = new List<Combatant> { target }, Combat = null!,
        };
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 6 });

        Check("thorns_bills_the_attacker", attacker.CurrentHp == 47,
            $"attacker hp={attacker.CurrentHp}, expected 47");

        // Fully blocked, and it still pricks: Thorns is an answer to a
        // multi-hit deck, which it would not be if Block turned it off.
        target.Block = 99;
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 6 });
        Check("thorns_fires_through_block", attacker.CurrentHp == 44,
            $"attacker hp={attacker.CurrentHp}, expected 44");

        // lose_hp has no attacker, so nothing is billed.
        int before = attacker.CurrentHp;
        var selfCtx = new EffectContext
        {
            Source = target, Targets = new List<Combatant> { target }, Combat = null!,
        };
        EffectRegistry.Execute(selfCtx, new EffectSpec { Action = "lose_hp", Amount = 5 });
        Check("thorns_does_not_fire_on_lose_hp", attacker.CurrentHp == before,
            $"attacker hp={attacker.CurrentHp}, expected {before}");

        // A Thorns holder attacking a Thorns holder resolves once and stops.
        // Direct HP subtraction rather than a nested deal_damage is what makes
        // that structural - see the comment in DealDamageEffect.
        var spiky = new EnemyCombatant { Name = "Spiky", MaxHp = 50, CurrentHp = 50 };
        spiky.AddStatus(StatusType.Thorns, 4);
        var other = new EnemyCombatant { Name = "Other", MaxHp = 50, CurrentHp = 50 };
        other.AddStatus(StatusType.Thorns, 4);
        var duelCtx = new EffectContext
        {
            Source = spiky, Targets = new List<Combatant> { other }, Combat = null!,
        };
        EffectRegistry.Execute(duelCtx, new EffectSpec { Action = "deal_damage", Amount = 5 });
        Check("thorns_does_not_retaliate_against_its_own_retaliation",
            spiky.CurrentHp == 46 && other.CurrentHp == 45,
            $"spiky={spiky.CurrentHp} (expected 46) other={other.CurrentHp} (expected 45)");
    }

    // Order inside DamageMath.ApplyIncoming, which is the rule rather than an
    // accident: Vulnerable amplifies and Intangible then floors, so a target
    // holding both takes 1. Flooring first would let Vulnerable multiply the
    // floor back up, and an Intangible target would take *more* from a
    // Vulnerable-stacking deck than from a plain one.
    private void TestIntangibleFloorsDamagePastVulnerable()
    {
        var attacker = new EnemyCombatant { Name = "Attacker", MaxHp = 99, CurrentHp = 99 };
        attacker.AddStatus(StatusType.Strength, 5);

        var target = new EnemyCombatant { Name = "Target", MaxHp = 99, CurrentHp = 99 };
        target.AddStatus(StatusType.Intangible, 1);
        target.AddStatus(StatusType.Vulnerable, 2);

        var ctx = new EffectContext
        {
            Source = attacker, Targets = new List<Combatant> { target }, Combat = null!,
        };
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 20 });

        Check("intangible_floors_damage_past_vulnerable",
            target.CurrentHp == 99 - DamageMath.IntangibleDamage,
            $"hp={target.CurrentHp}, expected {99 - DamageMath.IntangibleDamage}");

        // It floors attacks and nothing else. Poison and lose_hp bypass it on
        // purpose, which is what keeps Poison a live answer to it.
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "lose_hp", Amount = 7 });
        Check("intangible_does_not_floor_lose_hp",
            target.CurrentHp == 99 - DamageMath.IntangibleDamage - 7,
            $"hp={target.CurrentHp}");
    }

    // The two turn-end decay sites used to be two hand-written lists, and a
    // status added to one and not the other wears off for the player and not
    // the enemy while both sites keep compiling. They are one array now, and
    // this is what says so - it would have failed against the old shape.
    private async System.Threading.Tasks.Task TestIntangibleDecaysForBothSides()
    {
        var combat = new CombatManager();
        AddChild(combat);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 200,
            MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(CardDatabase.All),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        player.AddStatus(StatusType.Intangible, 3);
        enemy.AddStatus(StatusType.Intangible, 3);

        await RunOneRound(combat);

        Check("intangible_decays_for_the_player", player.GetStatus(StatusType.Intangible) == 2,
            $"player intangible={player.GetStatus(StatusType.Intangible)}, expected 2");
        Check("intangible_decays_for_the_enemy", enemy.GetStatus(StatusType.Intangible) == 2,
            $"enemy intangible={enemy.GetStatus(StatusType.Intangible)}, expected 2 - a status "
            + "that decays on one side only means the two decay sites have drifted apart");

        combat.QueueFree();
    }

    // Plating is Metallicize with a cost, and both halves are worth pinning:
    // the grant lands after the Block clear (the ordering trap ApplyTurnStartGrants
    // exists for) and the erosion is charged only for damage that gets through,
    // not for damage the Block it just granted absorbed. Charging on every hit
    // would make Plating strictly worse than the Metallicize it sits beside.
    private async System.Threading.Tasks.Task TestPlatingGrantsBlockAndErodesOnlyOnUnblockedDamage()
    {
        var combat = new CombatManager();
        AddChild(combat);
        var player = new PlayerCombatant
        {
            Name = "Player", MaxHp = 200, CurrentHp = 200,
            MaxEnergy = 3, CurrentEnergy = 3,
            Piles = new PileManager(CardDatabase.All),
        };
        var enemy = EnemyFactory.Create(EnemyDatabase.Get("cultist"));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());

        enemy.AddStatus(StatusType.Plating, 6);
        await RunOneRound(combat);

        Check("plating_grants_block_after_the_block_clear", enemy.Block == 6,
            $"block={enemy.Block} - 0 means the grant landed before the clear and was wiped");

        var ctx = new EffectContext
        {
            Source = player, Targets = new List<Combatant> { enemy }, Combat = combat,
        };

        // Eaten entirely by the 6 Block above: no stack spent.
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 4 });
        Check("plating_survives_a_blocked_hit", enemy.GetStatus(StatusType.Plating) == 6,
            $"plating={enemy.GetStatus(StatusType.Plating)} after a hit its Block absorbed");

        // Through the remaining 2 Block: one stack spent, and only one however
        // far past the Block the hit goes.
        EffectRegistry.Execute(ctx, new EffectSpec { Action = "deal_damage", Amount = 30 });
        Check("plating_erodes_on_an_unblocked_hit", enemy.GetStatus(StatusType.Plating) == 5,
            $"plating={enemy.GetStatus(StatusType.Plating)}, expected 5");

        combat.QueueFree();
    }

    // End the player's turn and wait until control comes back - the enemy turn
    // is async (it paces itself with real delays), so there is no synchronous
    // way to step a round.
    private async System.Threading.Tasks.Task RunOneRound(CombatManager combat)
    {
        combat.TryEndTurn();
        while (combat.State != CombatState.PlayerTurn && combat.State != CombatState.CombatEnd)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static EnemyCombatant MakeEnemy() => new()
    {
        Name = "Dummy",
        MaxHp = 40,
        CurrentHp = 40,
        Definition = EnemyDatabase.Get("cultist"),
        IntentPicker = new SequentialLoopingIntentPicker(),
    };
}
