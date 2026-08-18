using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// End-of-run summary. Scores the run (RunScore, modelled on Slay the Spire's
// score screen), banks that score as meta progress, and announces anything
// the run just unlocked. Replaces the old flat "+10 Shards / +25 if you won"
// grant, which paid the same regardless of how the run actually went.
//
// Laid out as two columns - the outcome and what to do next on the left, the
// itemized score on the right - rather than one tall centred stack. Winning
// used to be announced by a single line of 16px body text reading "Victory!
// You cleared the run.", set in the same face and weight as "Defeated...",
// with nothing else on the screen distinguishing the two (ROADMAP Phase 4).
public partial class RunEndScreen : Control
{
    public override void _Ready()
    {
        ScreenBackground.AttachRoom(this, ScreenBackground.BackdropRoom.Doorway);
        bool won = RunEndContext.Outcome == RunEndOutcome.Win;

        var breakdown = RunScore.EvaluateCurrentRun();
        int score = RunScore.Total(breakdown);
        int progressBefore = MetaProgressionManager.Instance.TotalProgress;
        var newlyUnlocked = MetaProgressionManager.Instance.AddRunResult(
            score, RunManager.Instance.RunSeed, won ? "Win" : "Lose", RunState.AscensionLevel);
        RunSaveManager.Delete();

        ScreenChrome.AddTitle(this, won ? "Run Complete" : "Run Over",
            won ? UiTheme.Palette.AccentGoldBright : PixelSpec.Ramp.R4);

        BuildOutcome(won);

        // The rung sits with the seed rather than in the score column: both are
        // *what run this was*, and the score column is what it earned - where
        // the Ascension row RunScore appends already says the same number in the
        // currency the column is denominated in.
        GetNode<Label>("CenterContainer/Columns/VBoxContainer/SeedLabel").Text =
            $"Seed: {RunManager.Instance.RunSeed}"
            + (RunState.AscensionLevel > 0 ? $"    Ascension {RunState.AscensionLevel}" : "");

        GetNode<PanelContainer>("CenterContainer/Columns/ScorePanel")
            .AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        BuildScoreList(breakdown, score);

        GetNode<Label>("CenterContainer/Columns/VBoxContainer/ProgressLabel").Text =
            $"Progress: {progressBefore:N0} → {progressBefore + score:N0}";

        var unlockLabel = GetNode<Label>("CenterContainer/Columns/VBoxContainer/UnlockLabel");
        unlockLabel.Visible = newlyUnlocked.Count > 0;
        if (newlyUnlocked.Count > 0)
        {
            unlockLabel.Text = "Unlocked: " + string.Join(", ", newlyUnlocked.Select(UnlockName));
            unlockLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
            AudioManager.Instance?.PlaySfx("reward_pickup");
        }

        var restartButton = GetNode<Button>("CenterContainer/Columns/VBoxContainer/RestartButton");
        ChromeStyles.ApplyEmphasisButtonStyle(restartButton);
        restartButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        restartButton.Pressed += OnRestartPressed;

        var viewUnlocksButton = GetNode<Button>("CenterContainer/Columns/VBoxContainer/ViewUnlocksButton");
        viewUnlocksButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        viewUnlocksButton.Pressed += OnViewUnlocksPressed;

        // No cancel action: the run is over, and there is nothing to back out
        // to that isn't already one of these two buttons.
        ScreenKeyboardNav.Attach(this, () => restartButton);
    }

    // The two endings have to look different at a glance, and the only thing
    // available to make them so without commissioning art is the player
    // sprite plus the ramp: a win shows it lit and gold-titled, a loss shows
    // it drained toward the dark end and titled in the danger red. Same
    // silhouette, opposite treatment.
    private void BuildOutcome(bool won)
    {
        var outcome = GetNode<Label>("CenterContainer/Columns/VBoxContainer/OutcomeLabel");
        outcome.Text = won ? "VICTORY" : "DEFEATED";
        outcome.ThemeTypeVariation = "CombatDisplayLabel";
        outcome.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Title);
        outcome.AddThemeColorOverride("font_color",
            won ? UiTheme.Palette.AccentGoldBright : PixelSpec.Ramp.R4);

        if (ArtAssets.PlayerSprite() is not { } sprite) return;

        var plinth = ScreenChrome.ArtPlinth(sprite);
        if (!won) plinth.Modulate = new Color(0.45f, 0.38f, 0.4f);
        GetNode<CenterContainer>("CenterContainer/Columns/VBoxContainer/ArtSlot").AddChild(plinth);

        if (!won || SettingsManager.Instance.ReduceMotion) return;

        // A slow gold breath on the winning sprite. The one moment in the game
        // that is allowed to be self-congratulatory, and it costs one tween.
        var tween = plinth.CreateTween();
        // Drift's own period, unmodified - this is the breath it is named for.
        tween.SetLoops();
        tween.TweenPingPong(plinth, "modulate", new Color(1.25f, 1.15f, 0.9f), Colors.White, Motion.Drift);
    }

    // Category on the left, points on the right, then a Total row - the
    // itemized layout the genre's score screens use, so the player can see
    // which behaviours actually paid.
    private void BuildScoreList(List<RunScore.Entry> breakdown, int total)
    {
        var list = GetNode<VBoxContainer>("CenterContainer/Columns/ScorePanel/ScoreList");
        foreach (var entry in breakdown)
        {
            list.AddChild(BuildScoreRow(entry.Label, $"{entry.Points:N0}", bold: false));
        }
        if (breakdown.Count == 0)
        {
            list.AddChild(BuildScoreRow("No score earned", "0", bold: false));
        }
        list.AddChild(new HSeparator());
        list.AddChild(BuildScoreRow("Total Score", $"{total:N0}", bold: true));
    }

    private static Control BuildScoreRow(string label, string value, bool bold)
    {
        var row = new HBoxContainer();
        var name = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var points = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var child in new[] { name, points })
        {
            child.AddThemeFontSizeOverride("font_size", bold ? UiTheme.Fonts.Heading : UiTheme.Fonts.Body);
            child.AddThemeColorOverride("font_color",
                bold ? UiTheme.Palette.AccentGoldBright : PixelSpec.Ramp.N7);
            row.AddChild(child);
        }
        return row;
    }

    private static string UnlockName(UnlockEntry entry) => entry.Kind switch
    {
        UnlockKind.Card => Data.CardDatabase.All.FirstOrDefault(c => c.Id == entry.Id)?.Name ?? entry.Id,
        _ => Data.RelicDatabase.All.FirstOrDefault(r => r.Id == entry.Id)?.Name ?? entry.Id,
    };

    private void OnRestartPressed() => RunManager.Instance.StartNewRun();

    private void OnViewUnlocksPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MetaProgression);
}
