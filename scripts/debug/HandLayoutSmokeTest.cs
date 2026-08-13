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
        AscensionDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestSpacingNeverOverflowsForAnyHandSize();
        TestRegressionAtConfirmedOverflowCases();
        TestCombatScreenLayoutStaysInBoundsAtFifteenCards();
        TestEveryCardDescriptionFitsWithoutTruncation();
        TestHandNeverReachesTheEnemyRow();
        TestNoCardHangsOffTheBottomOfTheCanvas();
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
    // "Reckless Blow+" at 14 characters, and it is 14 because the symmetric
    // badge reservation left 116px rather than the 126 an asymmetric one did.
    //
    // The budget is per card, not per screen - see inside the loop.
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

        var overflowing = new List<string>();
        foreach (var card in CardDatabase.All)
        {
            foreach (var variant in new[] { card, CardUpgrade.Apply(card) })
            {
                cardView.SetCardInstance(new CardInstance(variant));

                // Read back *inside* the loop: the banner's padding is a
                // function of whether this card shows a badge at all (CardView's
                // ApplyNameBannerPadding), so an unplayable card - which has no
                // cost badge - is measured against 36px more than the rest.
                // Hoisted out of the loop this budgeted every card the widest
                // case, and never saw a name running under a badge.
                var bannerStyle = nameBanner.GetThemeStylebox("panel");
                float available = cardView.CustomMinimumSize.X - 16f - bannerStyle.ContentMarginLeft
                                  - bannerStyle.ContentMarginRight;

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

    // The other end of the fan from TestHandNeverReachesTheEnemyRow, and the
    // one nothing was watching. A card is 240 tall and rotates about its own
    // center, so an outer card's *corners* reach further down than its rect
    // does: at 12 degrees the bottom-left one sits 136px below the center,
    // 16px more than half the card. With FanBaseY=-72 and a 36px arc that
    // corner landed at y=680 on a 648px canvas - the leftmost card was cut off
    // by the bottom of the screen and the hotkey badge that lives in exactly
    // that corner never rendered, so the first card in hand had no visible
    // number while every other card did.
    //
    // Measured off the real screen rather than recomputed from the constants,
    // so it fails if the fan formula changes shape and not just if a number
    // moves. All four corners, because which one is lowest depends on the sign
    // of the rotation.
    private void TestNoCardHangsOffTheBottomOfTheCanvas()
    {
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Deck = new List<CardDefinition>(CardDatabase.All);
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        Hollowdeck.Combat.CombatContext.EnemyDefinitionIds = new List<string> { "cultist" };
        Hollowdeck.Combat.CombatContext.IsBoss = false;

        var instance = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn").Instantiate();
        AddChild(instance);

        var combat = instance.GetNode<Hollowdeck.Combat.CombatManager>("CombatManager");
        var refreshHand = instance.GetType().GetMethod("RefreshHand", BindingFlags.NonPublic | BindingFlags.Instance);
        var handArea = instance.GetNode<Control>("HandArea");
        // The layout target, not the live Position: cards are still tweening
        // toward their slot when this runs (same reason as the fifteen-card
        // test above).
        var homePositionField = typeof(CardView).GetField("_homePosition", BindingFlags.NonPublic | BindingFlags.Instance);
        var homeRotationField = typeof(CardView).GetField("_homeRotation", BindingFlags.NonPublic | BindingFlags.Instance);

        // Two sizes rather than one: a lone card sits at the fan's center with
        // no rotation, and everything past two cards puts one at each extreme,
        // so these bracket every hand the game can deal.
        foreach (int target in new[] { 2, 10 })
        {
            int missing = target - combat.Player.Piles.Hand.Count;
            if (missing > 0) combat.Player.Piles.DrawHand(missing);
            refreshHand!.Invoke(instance, null);

            float lowest = 0f;
            string worst = "";
            foreach (var child in handArea.GetChildren())
            {
                if (child is not CardView cardView) continue;
                var home = (Vector2)homePositionField!.GetValue(cardView)!;
                float rotation = Mathf.DegToRad((float)homeRotationField!.GetValue(cardView)!);
                var size = cardView.CustomMinimumSize;
                var center = handArea.Position + home + size / 2f;

                foreach (var corner in new[]
                         {
                             new Vector2(-size.X, -size.Y) / 2f, new Vector2(size.X, -size.Y) / 2f,
                             new Vector2(-size.X, size.Y) / 2f, new Vector2(size.X, size.Y) / 2f,
                         })
                {
                    float y = (center + corner.Rotated(rotation)).Y;
                    if (y <= lowest) continue;
                    lowest = y;
                    worst = $"{cardView.CardInstance?.Definition.Id} corner at y={y:F0}";
                }
            }

            Check($"no_card_corner_below_the_canvas_at_{target}_cards",
                lowest <= CombatScreen.CanvasBottomY + 0.01f,
                $"{worst}, past the {CombatScreen.CanvasBottomY} canvas floor - the hotkey badge " +
                "sits in that corner and would be off screen");
        }

        instance.QueueFree();
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
