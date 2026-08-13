using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public static class EnemyFactory
{
    // The only place an EnemyCombatant's HP is established, which is what lets
    // the ascension ladder's HP rungs be one edit: normals, elites, bosses and
    // mid-fight summons all arrive through here.
    //
    // isBoss comes from ActDatabase.BossIds rather than a flag on the
    // definition, because that list is already the one place the game decides
    // what a boss is. EnrageHpPercent needs no help - it is a percentage of
    // MaxHp evaluated against the live pool, so it scales with it.
    public static EnemyCombatant Create(EnemyDefinition definition)
    {
        int maxHp = RunState.Ascension.EnemyHp(definition.MaxHp, ActDatabase.IsBoss(definition.Id));

        var enemy = new EnemyCombatant
        {
            Name = definition.Name,
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Definition = definition,
            IntentPicker = CreatePicker(definition.AiType),
        };
        return enemy;
    }

    private static IIntentPicker CreatePicker(string aiType) => aiType switch
    {
        "sequential" => new SequentialLoopingIntentPicker(),
        "weighted_random" => new WeightedRandomIntentPicker(),
        "phase_threshold" => new PhaseThresholdIntentPicker(),
        "wake_on_damage" => new WakeOnDamageIntentPicker(),
        _ => LogUnknownAndFallback(aiType),
    };

    private static IIntentPicker LogUnknownAndFallback(string aiType)
    {
        GD.PushError($"EnemyFactory: unknown aiType '{aiType}', defaulting to sequential.");
        return new SequentialLoopingIntentPicker();
    }
}
