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
        ActDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestSpacingNeverOverflowsForAnyHandSize();
        TestRegressionAtConfirmedOverflowCases();
        TestCombatScreenLayoutStaysInBoundsAtFifteenCards();
        TestEveryCardDescriptionFitsWithoutTruncation();
        TestHandNeverReachesTheEnemyRow();
        TestEveryCardNameFitsItsBanner();

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
        const float cardWidth = 176f;
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
        const float cardWidth = 176f;
        const float fanSafeWidth = 760f;

        foreach (int n in new[] { 11, 12 })
        {
            float spacing = HandFanLayout.ComputeSpacing(n, handAreaWidth, cardWidth, fanSafeWidth);
            float totalWidth = cardWidth + (n - 1) * spacing;
            Check($"no_overflow_regression_at_n_{n}", totalWidth <= handAreaWidth + 0.01f,
                $"totalWidth={totalWidth} handAreaWidth={handAreaWidth}");
        }
    }

    // Phase 2 regression guard. Cards used to be 224x308 resting at
    // FanBaseY=-140, putting a card's top edge at y=320 while EnemyRow ends
    // at y=330 - so a full hand physically covered the enemies, and on the
    // act-3 boss fight it occluded nearly all of The Hollow Throne. This
    // asserts the geometry that fixed it, so shrinking the card or raising
    // the fan can't silently undo it.
    private void TestHandNeverReachesTheEnemyRow()
    {
        float highestCardTop = CombatScreen.HighestCardTopY;
        Check("hand_clears_enemy_row",
            highestCardTop > CombatScreen.EnemyRowBottomY,
            $"highest card top is y={highestCardTop}, which is at or above EnemyRow's " +
            $"bottom y={CombatScreen.EnemyRowBottomY} - the hand would cover the enemies");
    }

    // The description had a fit check; the name never did, and it bit twice
    // during the pixel-art move - first when Silkscreen (much wider than the
    // serif face it replaced) overflowed the banner at 24px, then again when
    // the card narrowed to 176px. Longest name in the data is
    // "Reckless Charge+" at 16 characters.
    private void TestEveryCardNameFitsItsBanner()
    {
        var cardView = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(cardView);
        cardView.Interactive = false;

        var nameLabel = (Label)typeof(CardView)
            .GetField("_nameLabel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cardView)!;
        var nameBanner = (PanelContainer)typeof(CardView)
            .GetField("_nameBanner", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cardView)!;

        // Space actually visible: the banner minus the cost badge painted
        // over its left end.
        var bannerStyle = nameBanner.GetThemeStylebox("panel");
        float available = cardView.CustomMinimumSize.X - 16f - bannerStyle.ContentMarginLeft
                          - bannerStyle.ContentMarginRight;

        var overflowing = new List<string>();
        foreach (var card in CardDatabase.All)
        {
            foreach (var variant in new[] { card, CardUpgrade.Apply(card) })
            {
                cardView.SetCardInstance(new CardInstance(variant));
                var font = nameLabel.GetThemeFont("font");
                int size = nameLabel.GetThemeFontSize("font_size");
                float width = font.GetStringSize(nameLabel.Text, fontSize: size).X;
                if (width > available) overflowing.Add($"{variant.Id} ({width:F0}px > {available:F0}px)");
            }
        }

        Check("every_card_name_fits_its_banner", overflowing.Count == 0,
            $"overflowing: {string.Join(", ", overflowing)}");

        cardView.QueueFree();
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
        const float cardWidth = 176f;
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

    // Descriptions are generated, so their length isn't something an author
    // controls per card - and CardView's last resort when text won't fit its
    // 200x160 box even at the smallest font is to cut it and append an
    // ellipsis, which reads as a rendering glitch and hides mechanics from the
    // player. Adding the "to ALL enemies" suffix made the longest ones
    // materially longer (Thunderclap takes it twice), so every card - upgraded
    // too, since upgrades push amounts to two digits - is checked against the
    // real font and box, not eyeballed in a screenshot.
    private void TestEveryCardDescriptionFitsWithoutTruncation()
    {
        var cardView = GD.Load<PackedScene>("res://scenes/CardView.tscn").Instantiate<CardView>();
        AddChild(cardView);
        cardView.Interactive = false;

        var descriptionLabel = (RichTextLabel)typeof(CardView)
            .GetField("_descriptionLabel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cardView)!;

        var truncated = new List<string>();
        foreach (var card in CardDatabase.All)
        {
            foreach (var variant in new[] { card, CardUpgrade.Apply(card) })
            {
                cardView.SetCardInstance(new CardInstance(variant));
                if (descriptionLabel.Text.Contains('…')) truncated.Add(variant.Id);
            }
        }

        Check("every_card_description_fits_without_truncation", truncated.Count == 0,
            $"truncated: {string.Join(", ", truncated)}");

        cardView.QueueFree();
    }
}
