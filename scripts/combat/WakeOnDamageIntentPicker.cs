using Hollowdeck.Data;

namespace Hollowdeck.Combat;

// PhaseThresholdIntentPicker inverted: loops Definition.Moves - the dormant
// list - until the enemy has actually lost HP, then permanently loops
// Definition.EnrageMoves instead. Opt in via aiType "wake_on_damage".
//
// It is a separate file rather than a flag on the enrage picker for the same
// reason BlockMath is a copy of DamageMath's shape rather than four more
// methods on it: the two transitions answer to different things (an HP
// *threshold* against a damage *event*), and one class holding both would be
// one place to reach for the wrong condition.
//
// Definition.EnrageMoves carries the awake phase rather than a list of its own.
// Ten sweeps across the debug suites already walk `Moves.Concat(EnrageMoves)` -
// telegraph honesty, summon termination, act crossing, the enemy-voice
// descriptions - and a third list would be ten places to remember, which is
// exactly the silent data/code seam this project keeps losing content to.
public class WakeOnDamageIntentPicker : IIntentPicker
{
    private int _index;
    private bool _awake;

    public EnemyMove PickNext(EnemyCombatant self)
    {
        Wake(self);

        var moves = _awake ? self.Definition.EnrageMoves : self.Definition.Moves;
        var move = moves[_index];
        _index = _index + 1 >= moves.Count ? 0 : _index + 1;
        return move;
    }

    // The player's turn asking whether the telegraph on screen has gone stale.
    // True at most once, because Wake latches - see below.
    public bool TryAdvancePhase(EnemyCombatant self) => Wake(self);

    // The wake condition is HP actually lost, read off the combatant the way
    // PhaseThresholdIntentPicker reads its threshold. Three things follow from
    // that, and all three are the point rather than side effects:
    //
    //  - Every route in wakes it. Poison ticking, Thorns pricking, a relic
    //    retaliating and a card connecting are one condition here, where a hook
    //    inside DealDamageEffect would have been four to remember.
    //  - A hit the enemy's own Block eats entirely does NOT wake it. Chipping a
    //    sleeper through its guard is a real question rather than a formality,
    //    and it is why no dormant move may grant Block: a sleeper whose Block
    //    outgrows the player's per-hit damage could never be woken, never be
    //    killed, and the fight would have no exit. Phase4ContentSmokeTest
    //    refuses the authoring rather than this method defending against it.
    //  - The latch is what stops a healed sleeper going back to sleep. Waking
    //    is an event; CurrentHp < MaxHp is only the evidence for it.
    //
    // The index reset lives here rather than at either call site, and that is
    // load-bearing: the transition is normally *reported* through
    // TryAdvancePhase and only then acted on by a PickNext one call later, so a
    // reset owned by PickNext never runs on the path the player actually takes.
    // The awake list would then be indexed with the dormant list's cursor -
    // silently skipping the first awake move, or reading off the end of a
    // shorter list. gilded_husk's single dormant move hides both (the cursor
    // never leaves 0), so this would have been a trap set for the second
    // sleeper rather than a bug in this one.
    private bool Wake(EnemyCombatant self)
    {
        if (_awake || self.CurrentHp >= self.MaxHp) return false;
        _awake = true;
        _index = 0;
        return true;
    }
}
