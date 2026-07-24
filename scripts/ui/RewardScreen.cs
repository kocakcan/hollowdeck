using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class RewardScreen : Control
{
    private const float CardWidth = 224f;
    private const float MaxFanRotationDeg = 8f;
    private const float FanArcHeight = 20f;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "crypt", new Color(0.6f, 0.6f, 0.65f));
        DeckViewButtons.Attach(this);
        GetNode<Label>("TitleBlock/GoldLabel").Text = $"You found {RewardContext.GoldAwarded} gold.";

        var relicLabel = GetNode<Label>("TitleBlock/RelicLabel");
        if (RewardContext.GuaranteedRelic is { } relic)
        {
            relicLabel.Visible = true;
            relicLabel.Text = $"Relic: {relic.Name} - {relic.Description}";
        }
        else
        {
            relicLabel.Visible = false;
        }

        BuildCardChoices();
        GetNode<Button>("SkipButton").Pressed += OnSkipPressed;
    }

    // Real CardView instances (the same frame combat hands/deck-view use)
    // laid out in a gentle fan with a per-card idle sway, replacing the old
    // plain Button+Label rows - "one card component everywhere" per the
    // visual overhaul brief, now that CardView's frame has been proven
    // through Phase 3's real-combat verification.
    private void BuildCardChoices()
    {
        var area = GetNode<Control>("CardChoicesArea");
        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        var choices = RewardContext.CardChoices;
        int n = choices.Count;

        float spacing = n <= 1 ? 0f : Mathf.Min(CardWidth * 1.15f, (area.Size.X - CardWidth) / (n - 1));
        float totalWidth = CardWidth + (n - 1) * spacing;
        float startX = (area.Size.X - totalWidth) / 2f;

        for (int i = 0; i < n; i++)
        {
            var card = choices[i];
            var view = cardScene.Instantiate<CardView>();
            area.AddChild(view);
            view.Interactive = false;
            view.SetCardInstance(new CardInstance(card));
            view.Clicked += OnCardChosen;

            float t = n <= 1 ? 0.5f : (float)i / (n - 1);
            float centered = t - 0.5f;
            float rotationDeg = centered * 2f * MaxFanRotationDeg;
            float yOffset = FanArcHeight * (1f - Mathf.Cos(centered * Mathf.Pi));
            var pos = new Vector2(startX + i * spacing, area.Size.Y / 2f - 154f + yOffset);
            view.Position = pos;
            view.RotationDegrees = rotationDeg;
            view.PivotOffset = view.Size / 2f;

            PlayIdleSway(view, rotationDeg, i * 0.15f);
        }
    }

    // Slow, gentle rotation drift around the card's resting angle - phase-
    // staggered per card (same "spawn the loop with a random/staggered
    // start delay" idiom EnemyView's idle bob already uses) so a row of
    // reward cards doesn't sway in lockstep.
    private static void PlayIdleSway(CardView view, float restRotation, float phaseDelay)
    {
        var tween = view.CreateTween();
        tween.TweenInterval(phaseDelay);
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(view, "rotation_degrees", restRotation + 2.5f, 1.6);
        tween.TweenProperty(view, "rotation_degrees", restRotation - 2.5f, 1.6);
    }

    private void OnCardChosen(CardInstance card)
    {
        RunState.Deck.Add(card.Definition);
        Advance();
    }

    private void OnSkipPressed() => Advance();

    private static void Advance() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
