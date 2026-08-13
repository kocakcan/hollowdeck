using System.Collections.Generic;

namespace Hollowdeck.Data;

// The *resolved* state of the ascension ladder at one rung: every
// AscensionDefinition from 1 up to that rung, folded together, plus the methods
// that apply it. AscensionDatabase.Effective(level) hands one of these out.
//
// This type is the single place a modifier is turned into a number, and that is
// the whole reason it exists rather than the deltas being read directly at each
// site. Six of the eight knobs have two readers - the game and BalanceModel -
// and this project has twice shipped a mirror between those two that nothing
// asserted (BalanceModel's flat ShopRelicPrice = 150 under a "mirrors
// ShopScreen" comment, and PlayerMaxHpByAct's private copy of 50). Both were
// fixed by having the analyser *call* the game's function. The ladder starts
// that way instead of arriving there.
//
// Percent fields are totals where 100 is identity; the delta fields are 0 at
// identity. See AscensionDefinition for why the authored rows are deltas and
// the fold is a sum.
public sealed record AscensionModifiers(
    int Level,
    int EnemyHpPercent,
    int EnemyDamagePercent,
    int BossHpBonusPercent,
    int StartingMaxHpDelta,
    int ClearHealPercentDelta,
    int ShopPricePercent,
    int EliteWeightDelta,
    int PotionDropPercentDelta,
    IReadOnlyList<string> StartingCurseIds)
{
    // Rung 0, and every method below returns its argument unchanged under it.
    //
    // This is the load-bearing property of the whole feature, not a
    // convenience default: it is what lets every existing call site in
    // BalanceModel keep its meaning, and what makes "the balance report is
    // byte-identical against main" a check that means something. A ladder that
    // moved the rung-0 curve would invalidate every band, threshold and
    // encounter cost the last four phases measured.
    public static readonly AscensionModifiers None = new(
        Level: 0,
        EnemyHpPercent: 100,
        EnemyDamagePercent: 100,
        BossHpBonusPercent: 0,
        StartingMaxHpDelta: 0,
        ClearHealPercentDelta: 0,
        ShopPricePercent: 100,
        EliteWeightDelta: 0,
        PotionDropPercentDelta: 0,
        StartingCurseIds: System.Array.Empty<string>());

    // Asserted directly by AscensionSmokeTest against Effective(0). Written as a
    // property rather than leaning on the record's own == because the list makes
    // that reference equality, so two structurally identical instances would
    // compare unequal and the identity check would fail for a reason that has
    // nothing to do with the ladder.
    public bool IsIdentity =>
        EnemyHpPercent == 100 && EnemyDamagePercent == 100 && BossHpBonusPercent == 0
        && StartingMaxHpDelta == 0 && ClearHealPercentDelta == 0 && ShopPricePercent == 100
        && EliteWeightDelta == 0 && PotionDropPercentDelta == 0 && StartingCurseIds.Count == 0;

    // A boss takes EnemyHpPercent *and* BossHpBonusPercent, so a rung can lean
    // on bosses without touching the normal fights - whose mean is the
    // denominator every elite and boss ratio in BalanceReport is divided by.
    // Moving that denominator is the trap this project has now hit in four
    // forms; a boss-only knob is the one way to raise a boss without it.
    public int EnemyHp(int baseHp, bool isBoss) =>
        System.Math.Max(1, Scale(baseHp, EnemyHpPercent + (isBoss ? BossHpBonusPercent : 0)));

    // Guards zero rather than flooring it: a move authored at 0 damage is not an
    // attack, and rounding it up to 1 would give every enemy in the game a
    // scratch attack it does not have.
    public int EnemyDamage(int baseAmount) =>
        baseAmount <= 0 ? baseAmount : System.Math.Max(1, Scale(baseAmount, EnemyDamagePercent));

    public int ShopPrice(int basePrice) =>
        basePrice <= 0 ? basePrice : System.Math.Max(1, Scale(basePrice, ShopPricePercent));

    public int PotionPercent(int basePercent) =>
        System.Math.Clamp(basePercent - PotionDropPercentDelta, 0, 100);

    public int ClearHeal(int basePercent) =>
        System.Math.Max(0, basePercent - ClearHealPercentDelta);

    public int StartingMaxHp(int baseHp) =>
        System.Math.Max(1, baseHp + StartingMaxHpDelta);

    // The two halves of the elite-frequency knob, and they are two methods
    // because the weight is *moved* rather than added.
    //
    // MapGenerator's own comment records what an added weight costs: the `?`
    // node grew the node table 110 -> 119 without touching Elite's number, and
    // Elite silently lost 6.8% of its frequency because an unchanged weight is
    // not an unchanged share. Adding to Elite here would do the same thing to
    // every other type at once. Combat is the only other fight type, so moving
    // weight between the two holds both the table total and the
    // fights-vs-utility-rooms split constant, and changes only which kind of
    // fight a floor offers - which is the whole of what the rung is for.
    //
    // CombatWeight is applied by the caller *only* on floors where Elite is
    // actually in the table (floor >= 2). Subtracting on floor 0 or 1 would
    // shrink the table with nothing receiving the weight, which is the same
    // silent reshare running backwards.
    public int EliteWeight(int baseWeight) => System.Math.Max(0, baseWeight + EliteWeightDelta);

    public int CombatWeight(int baseWeight) => System.Math.Max(0, baseWeight - EliteWeightDelta);

    // Round half up. One rule, stated once, so the game and BalanceModel cannot
    // disagree about an enemy's HP by a point - which would put every encounter
    // cost in the report a hair off the fight the player actually gets.
    private static int Scale(int value, int percent) => (value * percent + 50) / 100;
}
