using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// The Phase 7 card vocabulary: the three pile keywords, the two unplayable
// card types, the add_card primitive, the two new effect scopes and X-cost.
//
// Its own suite rather than more methods on EffectSmokeTest, which is already
// 800 lines and twenty-odd tests. The dividing line is what the assertion is
// about: EffectSmokeTest asks "does an effect do its arithmetic", this asks
// "does a card behave like the keyword printed on it" - which mostly means
// driving a real CombatManager, because three of the six items here (Innate,
// the unplayable gate, X-cost) only exist on the path a card takes from a hand
// into a pile.
public partial class CardKeywordSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestRetainKeepsACardInHand();
        TestEtherealExhaustsInsteadOfDiscarding();
        TestEtherealBeatsRetain();
        TestNoAuthoredCardDeclaresBothRetainAndEthereal();
        TestInnateReachesTheOpeningHand();
        TestSurplusInnateArrivesTheFollowingTurn();
        TestRetainDoesNotReduceTheNextDraw();
        TestAddCardReachesEveryPile();
        TestAddCardRefusesAnUnknownId();
        TestUnplayableCardsAreRefusedAndLeaveTheHandUnchanged();
        TestUnplayableCardsAreNeverUpgraded();
        TestAllEnemiesScopeHitsEveryone();
        TestRandomEnemyScopeHitsExactlyOne();
        TestXCostSpendsEverythingAndScalesOnlyPerXSpecs();
        TestXCostIsRefusedAtZeroEnergy();
        TestEveryAuthoredAddCardNamesARealCardAndACount();
        TestKeywordTextIsGeneratedForEveryKeyword();

        GD.Print($"CardKeywordSmokeTest: {_pass} passed, {_fail} failed");
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

    // A hand built by id, so every count below is exact rather than whatever
    // the shuffle happened to deal.
    private static PileManager HandOf(params string[] ids)
    {
        var piles = new PileManager(new List<CardDefinition>());
        foreach (var id in ids) piles.Hand.Add(new CardInstance(CardDatabase.Get(id)));
        return piles;
    }

    // A live fight against one enemy, with the hand under the test's control.
    // Returns the manager so the caller can free it; CombatManager clears its
    // own static Instance in _ExitTree, so leaking one would make every later
    // test in the sweep see a finished fight.
    private CombatManager StartFight(PlayerCombatant player, out EnemyCombatant enemy, string enemyId = "cultist")
    {
        var combat = new CombatManager();
        AddChild(combat);
        enemy = EnemyFactory.Create(EnemyDatabase.Get(enemyId));
        combat.StartCombat(player, new List<EnemyCombatant> { enemy }, new List<RelicInstance>());
        return combat;
    }

    private static PlayerCombatant NewPlayer(IEnumerable<CardDefinition>? deck = null) => new()
    {
        Name = "Player", MaxHp = 50, CurrentHp = 50, MaxEnergy = 3, CurrentEnergy = 3,
        Piles = new PileManager(deck ?? new List<CardDefinition>()),
    };

    private void TestRetainKeepsACardInHand()
    {
        var piles = HandOf("hold_fast", "strike", "defend");
        piles.DiscardHand();

        Check("retain_card_stays_in_hand",
            piles.Hand.Count == 1 && piles.Hand[0].Definition.Id == "hold_fast",
            $"hand={string.Join(",", piles.Hand.Select(c => c.Definition.Id))}");
        Check("retain_does_not_hold_back_its_neighbours", piles.Discard.Count == 2,
            $"discard={piles.Discard.Count}");
    }

    private void TestEtherealExhaustsInsteadOfDiscarding()
    {
        var piles = HandOf("mirage", "strike");
        piles.DiscardHand();

        Check("ethereal_card_is_exhausted",
            piles.Exhaust.Count == 1 && piles.Exhaust[0].Definition.Id == "mirage",
            $"exhaust={string.Join(",", piles.Exhaust.Select(c => c.Definition.Id))}");
        // The half that would be silently wrong if Ethereal merely removed the
        // card: an exhausted card must not also be in the discard pile, or it
        // comes back on the next reshuffle.
        Check("ethereal_card_does_not_also_reach_the_discard",
            piles.Discard.All(c => c.Definition.Id != "mirage"),
            $"discard={string.Join(",", piles.Discard.Select(c => c.Definition.Id))}");
    }

    // The interaction PileManager.DiscardHand picks a winner for. Nothing
    // authors both today (the check below enforces that), so this drives a
    // synthetic card - the point is that the resolution is decided rather than
    // emergent.
    private void TestEtherealBeatsRetain()
    {
        var both = new CardDefinition
        {
            Id = "test_both", Name = "Both", Type = CardType.Skill,
            Retain = true, Ethereal = true,
        };
        var piles = new PileManager(new List<CardDefinition>());
        piles.Hand.Add(new CardInstance(both));
        piles.DiscardHand();

        Check("ethereal_beats_retain",
            piles.Hand.Count == 0 && piles.Exhaust.Count == 1,
            $"hand={piles.Hand.Count} exhaust={piles.Exhaust.Count}");
    }

    private void TestNoAuthoredCardDeclaresBothRetainAndEthereal()
    {
        var both = CardDatabase.All.Where(c => c.Retain && c.Ethereal).Select(c => c.Id).ToList();
        Check("no_authored_card_declares_both_retain_and_ethereal", both.Count == 0,
            $"{string.Join(", ", both)} - one keyword would silently cancel the other; "
            + "Ethereal wins, but a card should not need a reader to know that");
    }

    private void TestInnateReachesTheOpeningHand()
    {
        // A deck big enough that drawing the innate card by luck is not the
        // explanation: 5 of 40 is a 12.5% chance per card without promotion.
        var deck = new List<CardDefinition> { CardDatabase.Get("first_light") };
        for (int i = 0; i < 39; i++) deck.Add(CardDatabase.Get("strike"));

        var player = NewPlayer(deck);
        var combat = StartFight(player, out _);

        Check("innate_card_is_in_the_opening_hand",
            player.Piles.Hand.Any(c => c.Definition.Id == "first_light"),
            $"hand={string.Join(",", player.Piles.Hand.Select(c => c.Definition.Id))}");

        combat.QueueFree();
    }

    // Promotion is a partition, not a truncation - the surplus stays on top
    // rather than being dropped, which is the behaviour a player would
    // otherwise discover by losing a card.
    private void TestSurplusInnateArrivesTheFollowingTurn()
    {
        var deck = new List<CardDefinition>();
        for (int i = 0; i < 7; i++) deck.Add(CardDatabase.Get("first_light"));
        for (int i = 0; i < 20; i++) deck.Add(CardDatabase.Get("strike"));

        var piles = new PileManager(deck);
        piles.PromoteInnate();
        piles.DrawHand(CombatManager.BaseHandSize);

        int drawn = piles.Hand.Count(c => c.Definition.Innate);
        Check("hand_size_caps_how_many_innate_cards_arrive_at_once",
            drawn == CombatManager.BaseHandSize, $"drew {drawn} innate");
        // The other two are still on top, not lost and not shuffled back in.
        Check("surplus_innate_cards_stay_on_top_of_the_draw_pile",
            piles.DrawPile.TakeLast(2).All(c => c.Definition.Innate),
            $"top two = {string.Join(",", piles.DrawPile.TakeLast(2).Select(c => c.Definition.Id))}");
    }

    // Retain is a benefit, and it would stop being one if the retained card
    // came out of next turn's allowance. BeginPlayerTurn *assigns* a hand size
    // rather than topping up to one, which is what makes this true - the same
    // assign-vs-accumulate distinction Fervor and Foresight turn on.
    private void TestRetainDoesNotReduceTheNextDraw()
    {
        var deck = new List<CardDefinition>();
        for (int i = 0; i < 20; i++) deck.Add(CardDatabase.Get("strike"));

        var piles = new PileManager(deck);
        piles.Hand.Add(new CardInstance(CardDatabase.Get("hold_fast")));
        piles.DiscardHand();
        piles.DrawHand(CombatManager.BaseHandSize);

        Check("a_retained_card_makes_the_next_hand_one_larger",
            piles.Hand.Count == CombatManager.BaseHandSize + 1,
            $"hand={piles.Hand.Count}");
    }

    private void TestAddCardReachesEveryPile()
    {
        foreach (var (pile, name) in new[]
                 {
                     (CardPile.Hand, "hand"),
                     (CardPile.Draw, "draw"),
                     (CardPile.Discard, "discard"),
                 })
        {
            var player = NewPlayer(Enumerable.Repeat(CardDatabase.Get("strike"), 10));
            var combat = StartFight(player, out _);
            var before = (player.Piles.Hand.Count, player.Piles.DrawPile.Count, player.Piles.Discard.Count);

            EffectRegistry.Execute(
                new EffectContext { Source = player, Targets = new List<Combatant>(), Combat = combat },
                new EffectSpec { Action = "add_card", CardId = "wound", Amount = 2, Pile = pile });

            int landed = pile switch
            {
                CardPile.Hand => player.Piles.Hand.Count - before.Item1,
                CardPile.Draw => player.Piles.DrawPile.Count - before.Item2,
                _ => player.Piles.Discard.Count - before.Item3,
            };
            Check($"add_card_puts_two_copies_in_the_{name}_pile", landed == 2, $"landed={landed}");

            combat.QueueFree();
        }
    }

    private void TestAddCardRefusesAnUnknownId()
    {
        var player = NewPlayer();
        var combat = StartFight(player, out _);
        int before = player.Piles.Discard.Count;

        // Pushes an error rather than throwing - a typo in cards.json must not
        // take a combat screen down mid-resolution. The error is expected
        // output for this suite.
        EffectRegistry.Execute(
            new EffectContext { Source = player, Targets = new List<Combatant>(), Combat = combat },
            new EffectSpec { Action = "add_card", CardId = "no_such_card", Amount = 1 });

        Check("add_card_with_an_unknown_id_adds_nothing",
            player.Piles.Discard.Count == before, $"discard grew to {player.Piles.Discard.Count}");

        combat.QueueFree();
    }

    private void TestUnplayableCardsAreRefusedAndLeaveTheHandUnchanged()
    {
        var player = NewPlayer();
        var combat = StartFight(player, out var enemy);

        player.Piles.Hand.Clear();
        var wound = new CardInstance(CardDatabase.Get("wound"));
        player.Piles.Hand.Add(wound);
        int energyBefore = player.CurrentEnergy;
        int enemyHpBefore = enemy.CurrentHp;

        bool played = combat.TryPlayCard(wound, enemy);

        Check("unplayable_card_is_refused", !played, "TryPlayCard returned true");
        // The three things a rejected play must not have done, asserted
        // separately so a partial resolution names which half leaked.
        Check("refused_unplayable_card_stays_in_hand", player.Piles.Hand.Contains(wound),
            $"hand={player.Piles.Hand.Count}");
        Check("refused_unplayable_card_costs_no_energy", player.CurrentEnergy == energyBefore,
            $"energy {energyBefore} -> {player.CurrentEnergy}");
        Check("refused_unplayable_card_resolves_no_effects", enemy.CurrentHp == enemyHpBefore,
            $"enemy hp {enemyHpBefore} -> {enemy.CurrentHp}");

        combat.QueueFree();
    }

    private void TestUnplayableCardsAreNeverUpgraded()
    {
        var unplayable = CardDatabase.All.Where(c => !c.IsPlayable).ToList();
        var upgraded = unplayable.Where(c => !ReferenceEquals(CardUpgrade.Apply(c), c)).ToList();

        Check("card_upgrade_refuses_unplayable_cards", upgraded.Count == 0,
            $"{string.Join(", ", upgraded.Select(c => c.Id))} produced a '+' with no meaning");

        // The site a Curse would otherwise reach the player through: the rest
        // site's Smith and both upgrade event outcomes all read this list, so
        // a Curse in it is a picker column whose button does nothing.
        var offered = Events.UpgradeRandomCardOutcome.Upgradable().ToList();
        Check("upgrade_pickers_never_offer_an_unplayable_card",
            offered.All(i => RunState.Deck[i].IsPlayable), $"{offered.Count} offered");
    }

    private void TestAllEnemiesScopeHitsEveryone()
    {
        var player = NewPlayer();
        var combat = new CombatManager();
        AddChild(combat);
        var enemies = new List<EnemyCombatant>
        {
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
        };
        combat.StartCombat(player, enemies, new List<RelicInstance>());

        player.Piles.Hand.Clear();
        var sunder = new CardInstance(CardDatabase.Get("sunder"));
        player.Piles.Hand.Add(sunder);
        combat.TryPlayCard(sunder, enemies[0]);

        // Sunder is a SingleEnemy card: its damage lands on the one enemy it
        // was dragged onto, and only its AllEnemies-scoped Vulnerable spreads.
        // Both halves matter - if the scope were resolved at card level rather
        // than per effect, the damage would splash too.
        Check("all_enemies_scope_applies_to_every_enemy",
            enemies.All(e => e.GetStatus(StatusType.Vulnerable) > 0),
            string.Join(", ", enemies.Select(e => e.GetStatus(StatusType.Vulnerable))));
        Check("target_scope_on_the_same_card_still_hits_only_one",
            enemies[1].CurrentHp == enemies[1].MaxHp,
            $"second enemy hp {enemies[1].CurrentHp}/{enemies[1].MaxHp}");

        combat.QueueFree();
    }

    private void TestRandomEnemyScopeHitsExactlyOne()
    {
        var player = NewPlayer();
        var combat = new CombatManager();
        AddChild(combat);
        var enemies = new List<EnemyCombatant>
        {
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
            EnemyFactory.Create(EnemyDatabase.Get("cultist")),
        };
        combat.StartCombat(player, enemies, new List<RelicInstance>());

        player.Piles.Hand.Clear();
        // One spec at a time, so "exactly one enemy was hit" is a statement
        // about the scope rather than about Scattershot's three specs landing
        // on three different enemies (which they may or may not).
        var single = new CardDefinition
        {
            Id = "test_random", Name = "Test Random", Type = CardType.Attack,
            Target = CardTargetType.None,
            Effects = new List<EffectSpec>
            {
                new() { Action = "deal_damage", Amount = 4, Scope = EffectScope.RandomEnemy },
            },
        };
        var card = new CardInstance(single);
        player.Piles.Hand.Add(card);
        combat.TryPlayCard(card);

        int hit = enemies.Count(e => e.CurrentHp < e.MaxHp);
        Check("random_enemy_scope_hits_exactly_one_enemy", hit == 1, $"hit {hit} of 3");

        combat.QueueFree();
    }

    private void TestXCostSpendsEverythingAndScalesOnlyPerXSpecs()
    {
        var player = NewPlayer();
        player.CurrentEnergy = 3;
        var combat = StartFight(player, out var enemy);
        player.CurrentEnergy = 3;

        player.Piles.Hand.Clear();
        // deal_damage is PerX (5 per point, so 15 at three energy); the Block
        // is a plain spec on the same card and must NOT scale, which is the
        // whole reason PerX is per-spec rather than a blanket override.
        var mixed = new CardDefinition
        {
            Id = "test_x", Name = "Test X", Cost = -1, Type = CardType.Attack,
            Target = CardTargetType.SingleEnemy,
            Effects = new List<EffectSpec>
            {
                new() { Action = "deal_damage", Amount = 5, PerX = true, Scope = EffectScope.Target },
                new() { Action = "gain_block", Amount = 3, Scope = EffectScope.Self },
            },
        };
        var card = new CardInstance(mixed);
        player.Piles.Hand.Add(card);

        int hpBefore = enemy.CurrentHp;
        bool played = combat.TryPlayCard(card, enemy);

        Check("x_cost_card_is_playable", played, "TryPlayCard returned false");
        Check("x_cost_card_spends_all_remaining_energy", player.CurrentEnergy == 0,
            $"energy={player.CurrentEnergy}");
        Check("per_x_spec_scales_with_the_energy_spent", hpBefore - enemy.CurrentHp == 15,
            $"dealt {hpBefore - enemy.CurrentHp}, expected 15");
        Check("plain_spec_on_an_x_card_does_not_scale", player.Block == 3,
            $"block={player.Block}, expected 3");

        combat.QueueFree();
    }

    private void TestXCostIsRefusedAtZeroEnergy()
    {
        var player = NewPlayer();
        var combat = StartFight(player, out var enemy);
        player.CurrentEnergy = 0;

        player.Piles.Hand.Clear();
        var overload = new CardInstance(CardDatabase.Get("overload"));
        player.Piles.Hand.Add(overload);

        bool played = combat.TryPlayCard(overload, enemy);

        // An X card at zero resolves for nothing and is gone - a pure trap,
        // and the reason CardView dims it rather than leaving it bright.
        Check("x_cost_card_is_refused_at_zero_energy", !played, "TryPlayCard returned true");
        Check("refused_x_cost_card_stays_in_hand", player.Piles.Hand.Contains(overload),
            $"hand={player.Piles.Hand.Count}");

        combat.QueueFree();
    }

    // The authoring audit. AddCardEffect deliberately does not clamp a missing
    // count to 1, so an amount of 0 is a card that reads as a cost and is not
    // one - which is exactly the silent failure this codebase keeps closing
    // with a sweep rather than a comment.
    private void TestEveryAuthoredAddCardNamesARealCardAndACount()
    {
        var bad = new List<string>();
        foreach (var card in CardDatabase.All)
        {
            foreach (var spec in card.Effects.Where(e => e.Action == "add_card"))
            {
                if (CardDatabase.Find(spec.CardId ?? "") is null) bad.Add($"{card.Id}: id '{spec.CardId}'");
                else if (spec.Amount < 1) bad.Add($"{card.Id}: amount {spec.Amount}");
            }
        }

        Check("every_authored_add_card_names_a_real_card_and_a_count", bad.Count == 0,
            string.Join(", ", bad));
    }

    // A keyword the player cannot read is a keyword that does not exist. The
    // text also feeds Keywords.Find, which is what raises the hover blurb, so
    // this is both halves in one assertion.
    private void TestKeywordTextIsGeneratedForEveryKeyword()
    {
        foreach (var (id, word) in new[]
                 {
                     ("hold_fast", "Retain"),
                     ("first_light", "Innate"),
                     ("mirage", "Ethereal"),
                     ("wound", "Unplayable"),
                 })
        {
            var def = CardDatabase.Get(id);
            string text = EffectDescriptionFormatter.DescribeCard(def, new DescribeContext(TargetType: def.Target)).Text;
            Check($"{id}_description_says_{word.ToLowerInvariant()}", text.Contains(word), $"text='{text}'");
            Check($"{id}_keyword_has_a_hover_blurb",
                UI.Keywords.Find(text).Any(e => e.Keyword == word),
                $"Keywords.Find found none of {word} in '{text}'");
        }

        // The description box is the other half: an unplayable card with no
        // effects would otherwise render an empty panel.
        string woundText = EffectDescriptionFormatter
            .DescribeCard(CardDatabase.Get("wound"), new DescribeContext()).Text;
        Check("a_card_with_no_effects_still_has_rules_text", woundText.Length > 0, "empty");

        // add_card names the card it makes, which is the one formatter arm
        // that resolves an id against a database.
        string bloodPrice = EffectDescriptionFormatter.Describe(
            CardDatabase.Get("blood_price").Effects, new DescribeContext());
        Check("add_card_description_names_the_card_it_adds", bloodPrice.Contains("Wound"),
            $"text='{bloodPrice}'");

        // And the two new scopes say who they hit, since neither has a drag
        // gesture or a card-level target type to say it for them.
        string sunder = EffectDescriptionFormatter.Describe(
            CardDatabase.Get("sunder").Effects,
            new DescribeContext(TargetType: CardTargetType.SingleEnemy));
        Check("all_enemies_scope_says_so_on_a_single_target_card",
            sunder.Contains("ALL enemies"), $"text='{sunder}'");

        string scattershot = EffectDescriptionFormatter.Describe(
            CardDatabase.Get("scattershot").Effects, new DescribeContext());
        Check("random_enemy_scope_says_so", scattershot.Contains("random enemy"),
            $"text='{scattershot}'");

        string overload = EffectDescriptionFormatter.Describe(
            CardDatabase.Get("overload").Effects, new DescribeContext());
        Check("x_cost_damage_prints_as_x", overload.Contains("5X"), $"text='{overload}'");
    }
}
