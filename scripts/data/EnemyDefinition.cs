using System.Collections.Generic;

namespace Hollowdeck.Data;

public class EnemyDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxHp { get; set; }
    public string AiType { get; set; } = "sequential";
    public int LoopFromIndex { get; set; } = 0;
    public List<EnemyMove> Moves { get; set; } = new();

    // Only used by aiType "phase_threshold": once CurrentHp/MaxHp drops to
    // or below this percent (0-100), the enemy permanently switches from
    // looping Moves to looping EnrageMoves instead. 0 (default) means no
    // enrage phase - every enemy stays plain sequential unless authored
    // otherwise.
    public int EnrageHpPercent { get; set; } = 0;

    // The *second phase*, whatever brings it on, which is why this list has two
    // readers rather than one. "phase_threshold" enters it at the HP percent
    // above; "wake_on_damage" enters it the moment the enemy loses HP at all,
    // and for that one Moves is what it does while dormant.
    //
    // One list rather than one per trigger on purpose: ten sweeps across the
    // debug suites walk `Moves.Concat(EnrageMoves)` to check that a telegraph
    // matches its effects, that a summon terminates, that nothing crosses an
    // act. A third list would be ten places to remember and one silent gap per
    // forgotten site.
    public List<EnemyMove> EnrageMoves { get; set; } = new();

    // What this enemy does as it dies, resolved by CombatManager.ResolveDeaths
    // before the corpse leaves Enemies. Data rather than a C# class per enemy,
    // for the same reason relics and Powers are (risk 1) - and splitting falls
    // out of it rather than being a mechanic of its own, since a split is an
    // onDeath that summons.
    //
    // Untelegraphed by construction: a death is not scheduled, so there is no
    // intent to show it on. That is why the ordering in ResolveDeathsAndSettle
    // is Lose-before-Win rather than the other way round - see the comment
    // there - and why the authored content keeps to effects the player can
    // still answer (Poison, a summon), not a lethal parting blow.
    public List<EffectSpec> OnDeath { get; set; } = new();
}
