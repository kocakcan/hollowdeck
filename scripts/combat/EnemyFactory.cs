using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Combat;

public static class EnemyFactory
{
    public static EnemyCombatant Create(EnemyDefinition definition)
    {
        var enemy = new EnemyCombatant
        {
            Name = definition.Name,
            MaxHp = definition.MaxHp,
            CurrentHp = definition.MaxHp,
            Definition = definition,
            IntentPicker = CreatePicker(definition.AiType),
        };
        return enemy;
    }

    private static IIntentPicker CreatePicker(string aiType) => aiType switch
    {
        "sequential" => new SequentialLoopingIntentPicker(),
        "weighted_random" => new WeightedRandomIntentPicker(),
        _ => LogUnknownAndFallback(aiType),
    };

    private static IIntentPicker LogUnknownAndFallback(string aiType)
    {
        GD.PushError($"EnemyFactory: unknown aiType '{aiType}', defaulting to sequential.");
        return new SequentialLoopingIntentPicker();
    }
}
