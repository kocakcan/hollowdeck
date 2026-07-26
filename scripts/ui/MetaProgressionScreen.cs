using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The unlock track, read-only: nothing on this screen is purchasable any
// more. Progress accumulates from run scores (RunScore) and content opens by
// itself as thresholds are crossed, so this screen's whole job is showing
// where the player is on that track and what's next.
//
// Every node comes from a container in MetaProgressionScreen.tscn - the
// previous version positioned each list at a fixed offset, which made the
// runtime-populated lists grow straight over the labels and Back button
// underneath them.
public partial class MetaProgressionScreen : Control
{
    private static readonly Color LockedTint = new(0.55f, 0.55f, 0.58f);

    private Label _progressLabel = null!;
    private Label _nextUnlockLabel = null!;
    private ProgressBar _nextUnlockBar = null!;
    private VBoxContainer _unlockTrackList = null!;
    private VBoxContainer _seedHistoryList = null!;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "etched", new Color(0.75f, 0.75f, 0.8f));
        _progressLabel = GetNode<Label>("Margin/Root/ProgressLabel");
        _nextUnlockLabel = GetNode<Label>("Margin/Root/NextUnlockLabel");
        _nextUnlockBar = GetNode<ProgressBar>("Margin/Root/NextUnlockBar");
        _unlockTrackList = GetNode<VBoxContainer>("Margin/Root/Columns/TrackColumn/TrackScroll/UnlockTrackList");
        _seedHistoryList = GetNode<VBoxContainer>("Margin/Root/Columns/HistoryColumn/HistoryScroll/SeedHistoryList");
        GetNode<Button>("Margin/Root/BackButton").Pressed += OnBackPressed;

        RefreshSummary();
        RefreshUnlockTrack();
        RefreshSeedHistory();
    }

    private void RefreshSummary()
    {
        var meta = MetaProgressionManager.Instance;
        _progressLabel.Text = $"Progress: {meta.TotalProgress:N0}    " +
                              $"Runs: {meta.RunsCompleted}    Best run: {meta.BestRunScore:N0}";

        if (meta.NextUnlock() is not { } next)
        {
            _nextUnlockLabel.Text = "Everything is unlocked.";
            _nextUnlockBar.Visible = false;
            return;
        }

        // The bar fills between the previous rung and the next one, not from
        // zero - otherwise every later unlock looks nearly complete just
        // because the running total is large.
        int previousThreshold = MetaProgressionManager.UnlockTrack
            .Where(e => e.Threshold <= meta.TotalProgress)
            .Select(e => e.Threshold)
            .DefaultIfEmpty(0)
            .Max();

        _nextUnlockLabel.Text = $"Next: {DisplayName(next)} at {next.Threshold:N0} " +
                                $"({next.Threshold - meta.TotalProgress:N0} to go)";
        _nextUnlockBar.Visible = true;
        _nextUnlockBar.MinValue = previousThreshold;
        _nextUnlockBar.MaxValue = next.Threshold;
        _nextUnlockBar.Value = meta.TotalProgress;
    }

    private void RefreshUnlockTrack()
    {
        foreach (var child in _unlockTrackList.GetChildren())
        {
            _unlockTrackList.RemoveChild(child);
            child.QueueFree();
        }

        var meta = MetaProgressionManager.Instance;
        var next = meta.NextUnlock();
        foreach (var entry in MetaProgressionManager.UnlockTrack)
        {
            bool unlocked = entry.Kind == UnlockKind.Card
                ? meta.IsCardUnlocked(entry.Id)
                : meta.IsRelicUnlocked(entry.Id);
            _unlockTrackList.AddChild(BuildTrackRow(entry, unlocked, isNext: entry == next));
        }
    }

    // One row per rung: icon, name + what it does, and its threshold (or
    // "Unlocked"). Locked rows are dimmed rather than hidden, so the track
    // reads as a visible path forward instead of an empty screen.
    private static Control BuildTrackRow(UnlockEntry entry, bool unlocked, bool isNext)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());
        if (!unlocked) panel.Modulate = LockedTint;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        var icon = new TextureRect
        {
            Texture = entry.Kind == UnlockKind.Card ? ArtAssets.CardIcon(entry.Id) : ArtAssets.RelicIcon(entry.Id),
            CustomMinimumSize = new Vector2(36, 36),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        row.AddChild(icon);

        var textColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        textColumn.AddThemeConstantOverride("separation", 2);
        row.AddChild(textColumn);

        var nameLabel = new Label { Text = $"{DisplayName(entry)}  ({entry.Kind})" };
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        if (isNext) nameLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        textColumn.AddChild(nameLabel);

        var descriptionLabel = new Label
        {
            Text = Description(entry),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        descriptionLabel.AddThemeFontSizeOverride("font_size", 13);
        textColumn.AddChild(descriptionLabel);

        var statusLabel = new Label
        {
            Text = unlocked ? "Unlocked" : $"{entry.Threshold:N0}",
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(90, 0),
        };
        statusLabel.AddThemeColorOverride("font_color",
            unlocked ? UiTheme.Palette.StatusBuff : UiTheme.Palette.AccentGold);
        row.AddChild(statusLabel);

        return panel;
    }

    // Names/descriptions are looked up defensively: a track entry naming
    // content that was later removed or renamed must render as an inert row,
    // not throw on a missing database key.
    private static string DisplayName(UnlockEntry entry) => entry.Kind switch
    {
        UnlockKind.Card => CardDatabase.All.FirstOrDefault(c => c.Id == entry.Id)?.Name ?? entry.Id,
        _ => RelicDatabase.All.FirstOrDefault(r => r.Id == entry.Id)?.Name ?? entry.Id,
    };

    private static string Description(UnlockEntry entry)
    {
        if (entry.Kind == UnlockKind.Relic)
        {
            return RelicDatabase.All.FirstOrDefault(r => r.Id == entry.Id)?.Description ?? "";
        }
        var card = CardDatabase.All.FirstOrDefault(c => c.Id == entry.Id);
        return card is null ? "" : EffectDescriptionFormatter.Describe(card.Effects);
    }

    private void RefreshSeedHistory()
    {
        foreach (var child in _seedHistoryList.GetChildren())
        {
            _seedHistoryList.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var entry in MetaProgressionManager.Instance.RecentSeeds)
        {
            // Runs logged before scoring existed have Score 0 - shown as "-"
            // rather than a zero they never actually earned.
            string score = entry.Score > 0 ? $"{entry.Score:N0} pts" : "-";
            var label = new Label { Text = $"{entry.Outcome,-5} {score,10}    seed {entry.Seed}    {ShortDate(entry.TimestampUtc)}" };
            label.AddThemeFontSizeOverride("font_size", 13);
            _seedHistoryList.AddChild(label);
        }
    }

    private static string ShortDate(string timestampUtc) =>
        System.DateTime.TryParse(timestampUtc, out var parsed) ? parsed.ToString("yyyy-MM-dd HH:mm") : timestampUtc;

    private void OnBackPressed()
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
    }
}
