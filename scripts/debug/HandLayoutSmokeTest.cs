using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Regression coverage for the RefreshHand spacing-clamp bug: a hand of 11+
// cards used to run off both edges of HandArea because Mathf.Clamp ignores
// its max bound once max < min. Run via
// `godot --headless scenes/debug/HandLayoutSmokeTest.tscn`.
public partial class HandLayoutSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestSpacingNeverOverflowsForAnyHandSize();
        TestRegressionAtConfirmedOverflowCases();
        TestCombatScreenLayoutStaysInBoundsAtFifteenCards();

        GD.Print($"HandLayoutSmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _pass++;
            GD.Print($"PASS {name}");
        }
        else
        {
            _fail++;
            GD.Print($"FAIL {name}: {detail}");
        }
    }

    private void TestSpacingNeverOverflowsForAnyHandSize()
    {
        const float handAreaWidth = 1152f;
        const float cardWidth = 224f;
        const float fanSafeWidth = 760f;

        bool allFit = true;
        string worst = "";
        for (int n = 1; n <= 20; n++)
        {
            float spacing = HandFanLayout.ComputeSpacing(n, handAreaWidth, cardWidth, fanSafeWidth);
            float totalWidth = cardWidth + (n - 1) * spacing;
            if (totalWidth > handAreaWidth + 0.01f)
            {
                allFit = false;
                worst = $"n={n} totalWidth={totalWidth} > handAreaWidth={handAreaWidth}";
            }
        }
        Check("fan_never_overflows_hand_area_for_n_1_to_20", allFit, worst);
    }

    private void TestRegressionAtConfirmedOverflowCases()
    {
        const float handAreaWidth = 1152f;
        const float cardWidth = 224f;
        const float fanSafeWidth = 760f;

        foreach (int n in new[] { 11, 12 })
        {
            float spacing = HandFanLayout.ComputeSpacing(n, handAreaWidth, cardWidth, fanSafeWidth);
            float totalWidth = cardWidth + (n - 1) * spacing;
            Check($"no_overflow_regression_at_n_{n}", totalWidth <= handAreaWidth + 0.01f,
                $"totalWidth={totalWidth} handAreaWidth={handAreaWidth}");
        }
    }

    private void TestCombatScreenLayoutStaysInBoundsAtFifteenCards()
    {
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Deck = new List<CardDefinition>(CardDatabase.All);
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        Hollowdeck.Combat.CombatContext.EnemyDefinitionIds = new List<string> { "cultist" };
        Hollowdeck.Combat.CombatContext.IsBoss = false;

        var packed = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var combat = instance.GetNode<Hollowdeck.Combat.CombatManager>("CombatManager");
        // Grow the hand well past the n=11 overflow threshold, then force
        // the same private layout pass a real draw event would trigger -
        // this is a debug-only test, reflection into a private method is
        // acceptable here to exercise the real RefreshHand code path.
        combat.Player.Piles.DrawHand(10);
        var refreshHand = instance.GetType().GetMethod("RefreshHand", BindingFlags.NonPublic | BindingFlags.Instance);
        refreshHand!.Invoke(instance, null);

        var handArea = instance.GetNode<Control>("HandArea");
        float handAreaWidth = handArea.Size.X;
        const float cardWidth = 224f;
        // Cards tween toward their slot over ~0.2s rather than snapping
        // there instantly (SnapHome), so check the layout target
        // (_homePosition, what RefreshHand actually computed) rather than
        // the live, still-animating Position.
        var homePositionField = typeof(CardView).GetField("_homePosition", BindingFlags.NonPublic | BindingFlags.Instance);

        bool allInBounds = true;
        string worst = "";
        foreach (var child in handArea.GetChildren())
        {
            if (child is not CardView cardView) continue;
            var home = (Vector2)homePositionField!.GetValue(cardView)!;
            float left = home.X;
            float right = left + cardWidth;
            if (left < -0.01f || right > handAreaWidth + 0.01f)
            {
                allInBounds = false;
                worst = $"{cardView.Name}: left={left} right={right} handAreaWidth={handAreaWidth}";
            }
        }
        Check("fifteen_card_hand_stays_within_hand_area_in_real_scene", allInBounds, worst);

        instance.QueueFree();
    }
}
