namespace Hollowdeck.Combat;

// Metallicize and Ritual are the "grants something at the start of your turn"
// pair, and they are what makes CardType.Power a real card class rather than a
// Skill that doesn't come back (ROADMAP Phase 6).
//
// Deliberately statuses rather than a PowerBehavior class per Power. A hook
// interface would have meant one C# class per Power card, which is exactly the
// one-class-per-card pattern the effect system exists to avoid (genre risk 1) -
// whereas a status keeps a Power an ordinary data row: apply_status, scope
// Self, with a status that happens to tick. It also means enemies can carry
// them for free, which a player-only Power hook could not.
//
// Neither decays. That is the point: Vulnerable and Weak wear off, Poison
// decays as it ticks, and these two persist for the fight because a Power is a
// permanent investment.
//
// Dexterity/Frail are the Block half of the roster, and are deliberately exact
// mirrors of Strength/Weak rather than a new mechanic: Dexterity adds a flat
// amount to Block gained and never decays, Frail cuts it by a percentage and
// wears off by 1 a turn. The mirroring is what lets BlockMath be a copy of
// DamageMath's shape, and what lets a player read them without a tooltip.
//
// Regen joins Metallicize/Ritual as the third turn-start grant (heals instead
// of granting Block or Strength) and so is also non-decaying. That is what
// makes it authorable as a Power - a decaying Poison-mirror would have been a
// Skill's worth of value and would have needed its own tick site next to
// ApplyPoisonTick, where ordering is already load-bearing.
// Fervor and Foresight are the other two things a turn can hand you - energy
// and cards - and they exist because the first six Powers could only ever grant
// a number that already had a status behind it. They are the archetype anchors:
// a deck is built around drawing more or spending more, not around 3 Block a
// turn.
//
// Unlike the three above them they are player-only in effect. An enemy has no
// piles and no energy pool, so ApplyTurnStartGrants cannot pay them out for one
// - which is why they are granted in BeginPlayerTurn beside the two lines they
// add to, rather than with the other three. See the comment there: energy and
// hand size are *assigned* at turn start, so a grant applied before that
// assignment is overwritten, the same ordering trap Block has in the opposite
// direction.
//
// Artifact, Thorns, Intangible and Plating are the four that stop a debuff, a
// deck shape or a turn from being unconditionally correct.
//
// Artifact is the load-bearing one, and it is the only status in the roster
// that is *spent* rather than decayed: it consumes a stack to refuse an
// incoming debuff instead of wearing off on a clock. Before it, stacking
// Vulnerable was always right and there was no read to make. Its gate is
// StatusRow.IsDebuff, which is what turns that predicate from a rendering
// detail into a resolution rule - a debuff missing from that list would slip
// past Artifact silently, which is why EffectSmokeTest now drives Artifact over
// the whole enum rather than over a hand-picked few.
//
// Thorns and Plating are the two that read the *incoming hit* rather than the
// turn: Thorns bills the attacker, Plating spends a stack per unblocked hit.
// Both live in DealDamageEffect for that reason, and Plating's grant half sits
// with Metallicize in ApplyTurnStartGrants, inheriting the Block-clear ordering
// trap's solution rather than restating it.
//
// Intangible is the one new *decaying* status, and it is the reason the two
// hand-written decay lists in CombatManager were folded into DecayAtTurnEnd.
// Two lists is exactly how a status ends up wearing off for the player and not
// for the enemy. It floors incoming attack damage only - not lose_hp, not the
// poison tick - so Poison stays a live answer to it.
public enum StatusType
{
    Vulnerable, Weak, Strength, Poison, Metallicize, Ritual, Dexterity, Frail, Regen,
    Fervor, Foresight, Artifact, Thorns, Intangible, Plating,
}
