using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Run;

namespace Hollowdeck.Debug;

// Headless check for the start-of-run blessing pool: the database loads, every
// authored outcome key is one EventOutcomeRegistry actually has, the two schema
// rules a compound choice has to obey (a picker is the last spec, a gamble
// holds no pickers), the offer draw, and - the highest-value one here - that
// every row actually *does something* to RunState when it resolves.
//
// That last one is the check this content type most needs. A blessing is prose
// plus a list of string keys; a row whose keys are all registered, all authored,
// and collectively change nothing is a tile the player can pick that silently
// costs them their one opening decision, and nothing else in the repo can see
// it. Run via `godot --headless scenes/debug/BlessingSmokeTest.tscn`.
public partial class BlessingSmokeTest : Node
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
        BlessingDatabase.LoadAll();

        // Resolving a row grants relics and potions, which RelicPool filters
        // against what the player already owns and MetaProgressionManager
        // against what is unlocked - both read live state, so the whole
        // sequence needs the save guard the screen suites use.
        using var saveGuard = RunSaveGuard.Protect();

        TestDatabaseLoads();
        TestIdsAreUniqueAndAscii();
        TestEveryOutcomeKeyIsRegistered();
        TestPickerOutcomesAreAlwaysLast();
        TestGamblesContainNoPickers();
        TestEveryAddCardNamesARealCard();
        TestDescriptionsAreAuthored();
        TestOfferDrawsDistinctRows();
        TestOfferIsAFunctionOfTheSeed();
        TestEveryBlessingChangesTheRun();
        TestPickerBlessingsComeBackPending();

        GD.Print($"BlessingSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    // An exact count, the EventDatabase rule: a malformed row drops out of the
    // list without throwing, and a floor would not notice.
    private void TestDatabaseLoads()
    {
        Check("blessing_database_loads_every_authored_row", BlessingDatabase.All.Count == 10,
            $"count={BlessingDatabase.All.Count}");
    }

    private void TestIdsAreUniqueAndAscii()
    {
        var ids = BlessingDatabase.All.Select(b => b.Id).ToList();
        Check("blessing_ids_are_unique", ids.Distinct().Count() == ids.Count,
            string.Join(", ", ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key)));

        var nonAscii = BlessingDatabase.All
            .Where(b => !IsAscii(b.Label) || !IsAscii(b.Description) || !IsAscii(b.ResultText))
            .Select(b => b.Id).ToList();
        Check("blessing_prose_is_ascii", nonAscii.Count == 0, string.Join(", ", nonAscii));
    }

    private void TestEveryOutcomeKeyIsRegistered()
    {
        var unregistered = BlessingDatabase.All
            .SelectMany(b => b.Outcomes.SelectMany(AllSpecs).Select(s => (b.Id, s.Outcome)))
            .Where(pair => !EventOutcomeRegistry.IsRegistered(pair.Outcome))
            .Select(pair => $"{pair.Id}: '{pair.Outcome}'")
            .ToList();
        Check("every_blessing_outcome_is_registered", unregistered.Count == 0,
            string.Join(", ", unregistered));

        // The other half of the same seam, and the one nothing else covers:
        // BalanceModel prices these keys, and its switch ends in a default arm
        // that returns zero. A key that resolves correctly in the game but is
        // unknown to the model prints as a blessing that changes nothing, with
        // every table green.
        var unpriced = BlessingDatabase.All
            .SelectMany(b => b.Outcomes.SelectMany(AllSpecs).Select(s => (b.Id, s.Outcome)))
            .Where(pair => !BalanceModel.PricesOutcome(pair.Outcome))
            .Select(pair => $"{pair.Id}: '{pair.Outcome}'")
            .ToList();
        Check("every_blessing_outcome_is_priced_by_the_balance_model", unpriced.Count == 0,
            string.Join(", ", unpriced));
    }

    // Begin() returns the moment it hits a picker, so anything authored after
    // one would never resolve.
    private void TestPickerOutcomesAreAlwaysLast()
    {
        var offenders = new List<string>();
        foreach (var b in BlessingDatabase.All)
        {
            for (int i = 0; i < b.Outcomes.Count - 1; i++)
            {
                if (EventOutcomeRegistry.PickerFor(b.Outcomes[i]) is not null)
                {
                    offenders.Add($"{b.Id}: '{b.Outcomes[i].Outcome}' at {i} of {b.Outcomes.Count}");
                }
            }
        }
        Check("blessing_picker_outcomes_are_always_the_last_spec", offenders.Count == 0,
            string.Join(", ", offenders));
    }

    private void TestGamblesContainNoPickers()
    {
        var offenders = BlessingDatabase.All
            .SelectMany(b => b.Outcomes.SelectMany(s => s.Alternatives).Select(a => (b.Id, a)))
            .Where(pair => EventOutcomeRegistry.PickerFor(pair.a) is not null)
            .Select(pair => $"{pair.Id}: gamble -> '{pair.a.Outcome}'")
            .ToList();
        Check("blessing_gamble_alternatives_contain_no_pickers", offenders.Count == 0,
            string.Join(", ", offenders));

        var empty = BlessingDatabase.All
            .SelectMany(b => b.Outcomes.Select(s => (b.Id, s)))
            .Where(pair => pair.s.Outcome == "gamble" && pair.s.Alternatives.Count == 0)
            .Select(pair => pair.Id)
            .ToList();
        Check("every_blessing_gamble_has_alternatives", empty.Count == 0, string.Join(", ", empty));
    }

    // add_card names its card and takes a copy count; an unauthored count of 0
    // makes the whole outcome a silent no-op, which is what put this check in
    // EventSmokeTest and puts it here.
    private void TestEveryAddCardNamesARealCard()
    {
        var offenders = BlessingDatabase.All
            .SelectMany(b => b.Outcomes.SelectMany(AllSpecs).Select(s => (b.Id, s)))
            .Where(pair => pair.s.Outcome == "add_card"
                           && (CardDatabase.Find(pair.s.CardId) is null || pair.s.Amount <= 0))
            .Select(pair => $"{pair.Id}: '{pair.s.CardId}' x{pair.s.Amount}")
            .ToList();
        Check("every_blessing_add_card_names_a_real_card_and_a_count", offenders.Count == 0,
            string.Join(", ", offenders));
    }

    // The tile's body is the only place a blessing's effect is stated, so an
    // empty one is an offer the player cannot evaluate.
    private void TestDescriptionsAreAuthored()
    {
        var offenders = BlessingDatabase.All
            .Where(b => b.Label.Length == 0 || b.Description.Length == 0 || b.Outcomes.Count == 0)
            .Select(b => b.Id).ToList();
        Check("every_blessing_has_a_label_a_description_and_outcomes", offenders.Count == 0,
            string.Join(", ", offenders));

        // And the number in the prose has to be the number in the data. The
        // description restates amounts ("Gain 8 max HP."), which is prose
        // duplicating a field - moving the 8 to 12 leaves the tile lying to the
        // player with nothing else in the repo able to see it.
        //
        // Scoped to top-level specs with an amount above 1: `add_card` x1 reads
        // as "a Pain" rather than "1 Pain", and a gamble's alternatives are
        // deliberately not enumerated on the tile, which is what makes it a
        // gamble. ResultText is not checked here at all - see the resolution
        // check below, which is the honest version of that question.
        var drifted = BlessingDatabase.All
            .SelectMany(b => b.Outcomes.Select(s => (b, s)))
            .Where(pair => pair.s.Outcome != "gamble" && pair.s.Amount > 1
                           && !ContainsNumber(pair.b.Description, pair.s.Amount))
            .Select(pair => $"{pair.b.Id}: '{pair.s.Outcome} {pair.s.Amount}' not in \"{pair.b.Description}\"")
            .ToList();
        Check("every_blessing_description_states_its_own_amounts", drifted.Count == 0,
            string.Join("; ", drifted));
    }

    // Whole-number match, so "Lose 6 max HP" is not satisfied by a stray 6 in
    // "16" - the substring version of this passes on exactly the drift it is
    // meant to catch.
    private static bool ContainsNumber(string text, int value) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<!\d){value}(?!\d)");

    private void TestOfferDrawsDistinctRows()
    {
        // Three is what RunSetupScreen asks for; the pool has to be able to
        // answer it on every draw rather than most of them.
        for (int seed = 0; seed < 200; seed++)
        {
            var offer = BlessingDatabase.Offer(3, new Random(seed));
            if (offer.Count == 3 && offer.Select(b => b.Id).Distinct().Count() == 3) continue;

            Check("blessing_offer_is_three_distinct_rows", false,
                $"seed {seed} gave [{string.Join(", ", offer.Select(b => b.Id))}]");
            return;
        }
        Check("blessing_offer_is_three_distinct_rows", true, "");

        // Asking for more than the pool holds comes back short rather than
        // looping forever or repeating a row - the arm no caller reaches today.
        var everything = BlessingDatabase.Offer(BlessingDatabase.All.Count + 5, new Random(1));
        Check("blessing_offer_cannot_exceed_the_pool",
            everything.Count == BlessingDatabase.All.Count
            && everything.Select(b => b.Id).Distinct().Count() == everything.Count,
            $"count={everything.Count} of {BlessingDatabase.All.Count}");
    }

    // The whole point of typed seed entry: two players on one seed have to see
    // the same three tiles. RunSetupScreen guarantees this by re-Initing the
    // stream rather than drawing from wherever it had got to, so this drives
    // the same path - Init, then draw.
    private void TestOfferIsAFunctionOfTheSeed()
    {
        RngStreams.Init(4242);
        var first = BlessingDatabase.Offer(3, RngStreams.Shop).Select(b => b.Id).ToList();
        RngStreams.Init(4242);
        var second = BlessingDatabase.Offer(3, RngStreams.Shop).Select(b => b.Id).ToList();
        Check("blessing_offer_repeats_for_a_seed", first.SequenceEqual(second),
            $"[{string.Join(",", first)}] vs [{string.Join(",", second)}]");

        RngStreams.Init(99);
        var other = BlessingDatabase.Offer(3, RngStreams.Shop).Select(b => b.Id).ToList();
        Check("blessing_offer_differs_across_seeds", !first.SequenceEqual(other),
            $"seed 4242 and seed 99 both gave [{string.Join(",", first)}]");
    }

    // The one that matters. A row that resolves to no change at all is a tile
    // that eats the run's only opening decision, and every other check in this
    // file would pass through it: the keys are registered, the schema is legal,
    // the prose is authored.
    private void TestEveryBlessingChangesTheRun()
    {
        var inert = new List<string>();
        var silent = new List<string>();
        foreach (var blessing in BlessingDatabase.All)
        {
            RngStreams.Init(7);
            RunState.InitNewRun();
            var before = Snapshot();

            string text = Resolved(blessing);

            if (Snapshot() == before) inert.Add(blessing.Id);
            if (text.Length == 0) silent.Add(blessing.Id);
        }
        Check("every_blessing_changes_the_run", inert.Count == 0, string.Join(", ", inert));

        // And says so. The screen shows whatever Begin hands back, which is the
        // authored ResultText for most rows, a picker's own message for two and
        // a gamble's readback for one - so the question is not "is ResultText
        // authored" (two rows correctly leave it empty) but "does anything come
        // out the other end". Blanking a plain row's ResultText otherwise ships
        // an empty result band under a claimed blessing with every suite green.
        Check("every_blessing_says_what_it_did", silent.Count == 0, string.Join(", ", silent));
    }

    // The text the screen would actually print, and the state it would leave
    // behind. A picker has changed nothing when Begin returns - it is pending -
    // so its first selectable index is applied here, which is what the screen's
    // grid does and what makes the row measurable at all.
    private static string Resolved(BlessingDefinition blessing)
    {
        var resolution = EventOutcomeRegistry.Begin(blessing.Outcomes, blessing.ResultText);
        if (resolution.Pending is not { } picker) return resolution.Text;

        string message = picker.Apply(picker.Selectable().First());
        return resolution.Text.Length == 0 ? message : $"{resolution.Text} {message}";
    }

    // remove_chosen_card and upgrade_chosen_card have to come back as Pending
    // against the *starting deck* specifically. Both gate on Selectable(), and
    // an empty one degrades silently to a message instead: upgrade_chosen_card
    // excludes already-upgraded and unplayable cards, so a starter deck that
    // ever ships pre-upgraded would turn that blessing into prose.
    private void TestPickerBlessingsComeBackPending()
    {
        var missed = new List<string>();
        foreach (var blessing in BlessingDatabase.All)
        {
            bool authoredAPicker = blessing.Outcomes.Any(s => EventOutcomeRegistry.PickerFor(s) is not null);
            if (!authoredAPicker) continue;

            RngStreams.Init(7);
            RunState.InitNewRun();
            if (EventOutcomeRegistry.Begin(blessing.Outcomes, blessing.ResultText).Pending is null)
            {
                missed.Add(blessing.Id);
            }
        }
        Check("picker_blessings_are_pending_against_the_starting_deck", missed.Count == 0,
            string.Join(", ", missed));

        // And that there is at least one, so the check above cannot pass by
        // finding nothing to test.
        int pickers = BlessingDatabase.All
            .Count(b => b.Outcomes.Any(s => EventOutcomeRegistry.PickerFor(s) is not null));
        Check("the_pool_authors_at_least_one_picker_blessing", pickers > 0, "none authored");
    }

    // Everything a blessing can move, as one comparable value. Deck and relics
    // by content rather than by count, so "remove a card" and "add a card"
    // cannot cancel out and read as inert.
    private static string Snapshot() =>
        $"hp={RunState.PlayerCurrentHp}/{RunState.PlayerMaxHp} gold={RunState.Gold} "
        + $"deck=[{string.Join(",", RunState.Deck.Select(c => c.Id))}] "
        + $"relics=[{string.Join(",", RunState.Relics.Select(r => r.Definition.Id))}] "
        + $"potions=[{string.Join(",", RunState.Potions.Select(p => p.Definition.Id))}]";

    private static IEnumerable<EventOutcomeSpec> AllSpecs(EventOutcomeSpec spec) =>
        new[] { spec }.Concat(spec.Alternatives);

    private static bool IsAscii(string text) => text.All(c => c < 128);
}
