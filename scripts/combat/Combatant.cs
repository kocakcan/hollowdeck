using System.Collections.Generic;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Combat;

public abstract class Combatant
{
    public string Name = "";
    public int MaxHp;
    public int CurrentHp;
    public int Block;
    public Dictionary<StatusType, int> Statuses = new();
    public bool IsDead => CurrentHp <= 0;

    // How many hits this combatant's Block has eaten, ever, this fight. The one
    // *cause* the view layer gets, and it exists because a state diff cannot
    // recover one: CombatScreen.PopupDelta sees Block fall and has no way to
    // tell "a hit was absorbed" from "the turn ended and Block expired", which
    // are the same two numbers moving the same way.
    //
    // Both combatants clear Block on their own turn, so without this the
    // "Blocked!" beat fired every single turn either side had leftover Block
    // and nothing had attacked. Monotonic on purpose - a counter cannot be
    // confused by the clear, where a bool would need someone to reset it.
    //
    // Not serialized, because no Combatant is: Combat is deliberately absent
    // from RunManager.AutoSaveScreens, so a fight never outlives its session.
    public int HitsAbsorbed;

    // How many hits have reached this combatant's HP, ever, this fight, and who
    // dealt the last one. HitsAbsorbed's sibling, and it exists for the
    // identical reason one level over: a falling HP bar is not a cause either,
    // and CombatScreen.PopupDelta cannot tell an attack from a Poison tick,
    // from a card that costs HP, or from Thorns billing the attacker.
    //
    // The pair is what makes it a cause. LastAttacker on its own goes stale the
    // moment anything else takes HP - it would still name the last enemy that
    // swung while the player was ticking down from Poison on their own turn -
    // and the counter on its own says a hit landed without saying from where.
    // Read together they answer "is this HP loss the hit that enemy dealt",
    // which is the question CombatScreen.AttackerOf asks.
    //
    // Set in exactly one place, DealDamageEffect, gated on damage that got past
    // Block. Nothing else in combat may write them: a hit Block ate whole is
    // the ward burst's beat and not a blade's, and the other three in-combat HP
    // losses (the Poison tick, LoseHpEffect, Thorns' direct subtraction) are
    // deliberately none of this feature's business.
    //
    // Not serialized, because no Combatant is: Combat is deliberately absent
    // from RunManager.AutoSaveScreens, so a fight never outlives its session.
    public int HitsTaken;
    public Combatant? LastAttacker;

    public int GetStatus(StatusType status) => Statuses.GetValueOrDefault(status, 0);

    public void AddStatus(StatusType status, int amount)
    {
        Statuses[status] = GetStatus(status) + amount;
    }

    public void DecayStatus(StatusType status)
    {
        var current = GetStatus(status);
        if (current <= 0) return;
        Statuses[status] = current - 1;
    }
}

public class PlayerCombatant : Combatant
{
    public int MaxEnergy = 3;
    public int CurrentEnergy;
    public PileManager Piles = null!;
}

public class EnemyCombatant : Combatant
{
    public EnemyDefinition Definition = null!;
    public IIntentPicker IntentPicker = null!;
    public EnemyMove? CurrentMove;

    // Left the fight alive, via an escape move. Set by EscapeEffect and acted
    // on one pass later in CombatManager.ResolveDeathsAndSettle, so an enemy
    // cannot vanish part-way through the remaining specs of its own move.
    public bool HasEscaped;

    // Recursion guard for Definition.OnDeath: an onDeath can kill another
    // enemy (or summon one that immediately dies), so ResolveDeaths loops
    // until nothing new is dying, and this is what terminates it.
    public bool OnDeathFired;

    // The one predicate every "is this still in the fight" site reads - the
    // combat loop's skips, the drag hit test, the keyboard target. There are
    // two ways out of a fight now and a third would otherwise be four call
    // sites to remember rather than one.
    public bool IsGone => IsDead || HasEscaped;
}
