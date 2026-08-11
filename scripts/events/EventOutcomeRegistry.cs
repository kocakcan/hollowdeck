using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Events;

// What a choice did, and whether it still owes the player a question.
// Pending is non-null only when a card-picker outcome found something to pick
// from; EventScreen opens the grid and finishes it there.
public readonly record struct EventResolution(string Text, ICardPickerOutcome? Pending);

// EffectRegistry doesn't fit here: EffectContext's Source/Targets/Combat
// fields are required and typed to Combatant/CombatManager, none of which
// exist outside a fight. This is a small, self-contained parallel system
// for the non-combat outcomes Event choices need.
public static class EventOutcomeRegistry
{
    private static readonly Dictionary<string, IEventOutcome> Outcomes = new()
    {
        ["gain_gold"] = new GainGoldOutcome(),
        ["lose_gold"] = new LoseGoldOutcome(),
        ["heal"] = new HealOutcome(),
        ["lose_hp"] = new LoseHpOutcome(),
        ["gain_random_card"] = new GainRandomCardOutcome(),
        // Same key name as the EffectRegistry action, and the same meaning.
        // The two registries stay separate because an EffectContext requires a
        // Combatant and a CombatManager, neither of which exists on an event
        // screen - see this file's header.
        ["add_card"] = new AddCardOutcome(),
        ["gain_relic"] = new GainRelicOutcome(),
        ["lose_relic"] = new LoseRelicOutcome(),
        ["none"] = new NoneOutcome(),
        ["gain_max_hp"] = new GainMaxHpOutcome(),
        ["lose_max_hp"] = new LoseMaxHpOutcome(),
        ["gain_potion"] = new GainPotionOutcome(),
        ["upgrade_random_card"] = new UpgradeRandomCardOutcome(),
        ["gamble"] = new GambleOutcome(),
        ["remove_chosen_card"] = new RemoveChosenCardOutcome(),
        ["upgrade_chosen_card"] = new UpgradeChosenCardOutcome(),
    };

    /// Resolves a choice's whole spec list, front to back.
    ///
    /// Named Begin rather than Resolve because a choice ending in a card
    /// picker is not finished when this returns - it stops there and hands
    /// the picker back as Pending for EventScreen to open. A picker with
    /// nothing selectable is *not* pending: it resolves through Execute to
    /// its empty-deck message like any other outcome.
    public static EventResolution Begin(EventChoice choice) => Begin(choice.Specs, choice.ResultText);

    /// The same resolution, for a caller that has specs and prose but is not an
    /// EventChoice - today the start-of-run blessing (BlessingDefinition).
    ///
    /// An overload rather than a BlessingDefinition.AsChoice() adapter, so one
    /// content type never has to impersonate another to be resolved. What
    /// matters is that there is exactly one implementation: the picker-is-
    /// pending contract, the override-joining below and the "a picker must be
    /// the last spec" rule are properties of this function, and a second copy
    /// of it is a second place for them to stop being true.
    public static EventResolution Begin(IReadOnlyList<EventOutcomeSpec> specs, string resultText)
    {
        var overrides = new List<string>();

        foreach (var spec in specs)
        {
            if (PickerFor(spec) is { } picker && picker.Selectable().Any())
            {
                // A picker must be the *last* spec, so nothing is left
                // unresolved behind the grid it opens. EventSmokeTest and
                // BlessingSmokeTest assert that against the authored data
                // rather than trusting it.
                return new EventResolution(Join(overrides, resultText), picker);
            }

            if (Resolve(spec) is { } message) overrides.Add(message);
        }

        return new EventResolution(Join(overrides, resultText), null);
    }

    /// One spec, with no picker handling. Public because GambleOutcome
    /// resolves whichever alternative it rolled back through here.
    public static string? Resolve(EventOutcomeSpec spec)
    {
        if (!Outcomes.TryGetValue(spec.Outcome, out var outcome))
        {
            GD.PushError($"EventOutcomeRegistry: unknown outcome '{spec.Outcome}'");
            return null;
        }
        return outcome.Execute(spec);
    }

    public static ICardPickerOutcome? PickerFor(EventOutcomeSpec spec) =>
        Outcomes.TryGetValue(spec.Outcome, out var outcome) ? outcome as ICardPickerOutcome : null;

    public static bool IsRegistered(string outcome) => Outcomes.ContainsKey(outcome);

    // The authored ResultText is the default; any outcome that couldn't do
    // what its label promised replaces it. Several overrides join rather than
    // the last one winning - on a compound choice they are different failures
    // and the player should see both.
    private static string Join(List<string> overrides, string resultText) =>
        overrides.Count == 0 ? resultText : string.Join(" ", overrides);
}
