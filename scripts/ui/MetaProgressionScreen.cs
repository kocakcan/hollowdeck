using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MetaProgressionScreen : Control
{
    private const int RelicUnlockCost = 60;

    private Label _shardsLabel = null!;
    private VBoxContainer _relicUnlocksList = null!;
    private VBoxContainer _seedHistoryList = null!;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "etched", new Color(0.75f, 0.75f, 0.8f));
        _shardsLabel = GetNode<Label>("ShardsLabel");
        _relicUnlocksList = GetNode<VBoxContainer>("RelicUnlocksList");
        _seedHistoryList = GetNode<VBoxContainer>("SeedHistoryList");
        GetNode<Button>("BackButton").Pressed += OnBackPressed;

        RefreshShardsLabel();
        RefreshRelicUnlocks();
        RefreshSeedHistory();
    }

    private void RefreshShardsLabel() => _shardsLabel.Text = $"Shards: {MetaProgressionManager.Instance.Shards}";

    private void RefreshRelicUnlocks()
    {
        foreach (var child in _relicUnlocksList.GetChildren())
        {
            _relicUnlocksList.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var relicId in MetaProgressionManager.LockedRelicIds)
        {
            if (MetaProgressionManager.Instance.IsRelicUnlocked(relicId)) continue;

            var relic = RelicDatabase.Get(relicId);
            var row = new VBoxContainer();
            var button = new Button
            {
                Text = $"Unlock {relic.Name} - {RelicUnlockCost} Shards",
                Disabled = MetaProgressionManager.Instance.Shards < RelicUnlockCost,
            };
            var description = new Label { Text = relic.Description };
            button.Pressed += () =>
            {
                if (!MetaProgressionManager.Instance.TryUnlockRelic(relicId, RelicUnlockCost)) return;
                RefreshShardsLabel();
                RefreshRelicUnlocks();
            };
            row.AddChild(button);
            row.AddChild(description);
            _relicUnlocksList.AddChild(row);
        }
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
            _seedHistoryList.AddChild(new Label { Text = $"Seed {entry.Seed} - {entry.Outcome} - {entry.TimestampUtc}" });
        }
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
}
