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

    // How many hits this combatant's Block has eaten, ever, this fight, and who
    // dealt the last one. One of the two *causes* the view layer gets, and it
    // exists because a state diff cannot recover one: CombatScreen.PopupDelta
    // sees Block fall and has no way to tell "a hit was absorbed" from "the
    // turn ended and Block expired", which are the same two numbers moving the
    // same way.
    //
    // Both combatants clear Block on their own turn, so without this the
    // "Blocked!" beat fired every single turn either side had leftover Block
    // and nothing had attacked. Monotonic on purpose - a counter cannot be
    // confused by the clear, where a bool would need someone to reset it.
    //
    // LastAbsorbedAttacker is the second half of that pair, and it arrived
    // later than the counter, which had been carrying the beat alone. Read
    // together they answer "is this absorption the hit that enemy dealt" -
    // which is what CombatScreen.AbsorbedAttackerOf asks, and what lets a swing
    // Block ate whole still draw a blade across the gap. Alone it goes stale
    // exactly as LastAttacker does below: it would keep naming the last enemy
    // that swung while Block quietly expired on a turn nothing attacked at all.
    //
    // Not serialized, because no Combatant is: Combat is deliberately absent
    // from RunManager.AutoSaveScreens, so a fight never outlives its session.
    public int HitsAbsorbed;
    public Combatant? LastAbsorbedAttacker;

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
    // All four fields are set in exactly one place, DealDamageEffect: this pair
    // gated on damage that got past Block, the pair above gated on damage Block
    // ate. Nothing else in combat may write them, and the reason is the same
    // for both - the other three in-combat HP losses (the Poison tick,
    // LoseHpEffect, Thorns' direct subtraction) have no attacker to draw a
    // blade from.
    //
    // This paragraph used to end "a hit Block ate whole is the ward burst's
    // beat and not a blade's". That was a decision rather than a fact, and it
    // was reversed deliberately: the ward burst says the Block held and says
    // nothing whatever about who swung, so the one swing in the game that could
    // not be seen coming was the one that got stopped.
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
