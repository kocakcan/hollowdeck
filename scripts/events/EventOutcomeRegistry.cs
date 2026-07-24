using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Events;

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
        ["gain_relic"] = new GainRelicOutcome(),
        ["lose_relic"] = new LoseRelicOutcome(),
        ["none"] = new NoneOutcome(),
    };

    public static string Resolve(EventChoice choice)
    {
        if (!Outcomes.TryGetValue(choice.Outcome, out var outcome))
        {
            GD.PushError($"EventOutcomeRegistry: unknown outcome '{choice.Outcome}'");
            return choice.ResultText;
        }
        return outcome.Execute(choice) ?? choice.ResultText;
    }
}
