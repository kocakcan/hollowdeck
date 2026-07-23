using System;
using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

public static class RelicRegistry
{
    private static readonly Dictionary<string, Func<RelicDefinition, RelicBehavior>> Factories = new()
    {
        ["simple_hook_effect"] = def => new SimpleHookEffectRelic(def),
        ["frugal_satchel"] = def => new FrugalSatchelRelic(def),
        ["thorned_carapace"] = def => new ThornedCarapaceRelic(def),
        ["bulwark_charm"] = def => new BulwarkCharmRelic(def),
        ["momentum_token"] = def => new MomentumTokenRelic(def),
        ["skirmishers_sash"] = def => new SkirmishersSashRelic(def),
        ["ledger_of_ruin"] = def => new LedgerOfRuinRelic(def),
        ["scavengers_charm"] = def => new ScavengersCharmRelic(def),
        ["second_wind"] = def => new SecondWindRelic(def),
        ["vampire_fang"] = def => new VampireFangRelic(def),
        ["toxic_fang"] = def => new ToxicFangRelic(def),
        ["vengeful_spirit"] = def => new VengefulSpiritRelic(def),
    };

    public static RelicBehavior Create(RelicDefinition definition)
    {
        if (Factories.TryGetValue(definition.BehaviorId, out var factory)) return factory(definition);
        GD.PushError($"RelicRegistry: unknown behaviorId '{definition.BehaviorId}', using no-op relic.");
        return new SimpleHookEffectRelic(definition);
    }
}
