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
public enum StatusType
{
    Vulnerable, Weak, Strength, Poison, Metallicize, Ritual, Dexterity, Frail, Regen,
    Fervor, Foresight,
}
