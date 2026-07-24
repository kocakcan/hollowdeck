using Godot;

namespace Hollowdeck.UI;

// Pure spacing math for CombatScreen.RefreshHand's card fan, pulled out
// (same reasoning as Effects/DamageMath.cs) so the "never overflow
// handArea" invariant is unit-testable without spinning up a full
// CombatScreen scene.
public static class HandFanLayout
{
    public static float ComputeSpacing(int cardCount, float handAreaWidth, float cardWidth, float fanSafeWidth)
    {
        if (cardCount <= 1) return 0f;

        float availableWidth = handAreaWidth - cardWidth;
        float preferredSpacing = (fanSafeWidth - cardWidth) / (cardCount - 1);
        float maxSpacing = Mathf.Min(cardWidth * 0.85f, availableWidth / (cardCount - 1));
        float minSpacing = cardWidth * 0.45f;

        // Max-then-min instead of a single Mathf.Clamp call: Clamp checks
        // value < min first and returns min unconditionally in that branch,
        // silently ignoring max once max < min (happens once hands get
        // large enough that maxSpacing drops below the floor) - that let
        // cards run off both edges of the hand area. This ordering
        // guarantees the result never exceeds maxSpacing, so totalWidth can
        // never exceed handAreaWidth, for any cardCount.
        return Mathf.Min(Mathf.Max(preferredSpacing, minSpacing), maxSpacing);
    }
}
