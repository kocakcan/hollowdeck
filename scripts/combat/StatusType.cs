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
public enum StatusType { Vulnerable, Weak, Strength, Poison, Metallicize, Ritual }
