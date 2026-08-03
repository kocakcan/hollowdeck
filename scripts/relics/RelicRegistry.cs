using System;
using System.Collections.Generic;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Relics;

// One entry, on purpose. Every relic in data/relics/relics.json declares
// behaviorId "simple_hook_effect" and is authored entirely as data; the
// dictionary stays because a bespoke RelicBehavior subclass is still a legal
// escape hatch, and this is where one would register.
public static class RelicRegistry
{
    private static readonly Dictionary<string, Func<RelicDefinition, RelicBehavior>> Factories = new()
    {
        ["simple_hook_effect"] = def => new SimpleHookEffectRelic(def),
    };

    public static RelicBehavior Create(RelicDefinition definition)
    {
        if (Factories.TryGetValue(definition.BehaviorId, out var factory)) return factory(definition);
        GD.PushError($"RelicRegistry: unknown behaviorId '{definition.BehaviorId}', using no-op relic.");
        return new SimpleHookEffectRelic(definition);
    }
}
