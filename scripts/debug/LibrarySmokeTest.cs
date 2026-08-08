using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Headless check for LibraryScreen: every card/relic/potion renders, locked
// items dim per MetaProgressionManager (live, not cached at construction),
// potions never dim, and the three panes switch correctly. Always operates
// against a scratch save (ScratchPath), never the real one.
// Run via `godot --headless scenes/debug/LibrarySmokeTest.tscn`.
public partial class LibrarySmokeTest : Node
{
    private const string ScratchPath = "user://meta_progression_library_test.json";

    private static readonly UnlockEntry FirstCardEntry =
        MetaProgressionManager.UnlockTrack.First(e => e.Kind == UnlockKind.Card);
    private static readonly UnlockEntry FirstRelicEntry =
        MetaProgressionManager.UnlockTrack.First(e => e.Kind == UnlockKind.Relic);

    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        CardDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        TestScreenLoadsWithFullRoster();
        TestLockedCardIsDimmed();
        TestAlwaysAvailableCardIsNotDimmed();
        TestLockedRelicIsDimmed();
        TestUnlockAfterThresholdCrossedIsLive();
        TestPotionsAreNeverDimmed();
        TestDefaultPaneIsCards();
        TestCategoryButtonsSwitchPanes();

        GD.Print($"LibrarySmokeTest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private void ResetScratch()
    {
        if (FileAccess.FileExists(ScratchPath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ScratchPath));
        MetaProgressionManager.Instance.LoadFrom(ScratchPath);
    }

    private Node OpenScreen()
    {
        var packed = GD.Load<PackedScene>("res://scenes/LibraryScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);
        return instance;
    }

    private static CardView? FindCardView(GridContainer grid, string cardId) =>
        grid.GetChildren()
            .SelectMany(column => column.GetChildren())
            .OfType<CardView>()
            .FirstOrDefault(v => v.CardInstance!.Definition.Id == cardId);

    private static Control RelicTile(GridContainer grid, string relicId)
    {
        int index = RelicDatabase.All.Select(r => r.Id).ToList().IndexOf(relicId);
        return (Control)grid.GetChild(index);
    }

    private void TestScreenLoadsWithFullRoster()
    {
        ResetScratch();
        var screen = OpenScreen();

        var cardsGrid = screen.GetNode<GridContainer>("Content/CardsPane/Center/CardsGrid");
        var relicsGrid = screen.GetNode<GridContainer>("Content/RelicsPane/Center/RelicsGrid");
        var potionsGrid = screen.GetNode<GridContainer>("Content/PotionsPane/Center/PotionsGrid");

        Check("library_screen_loads", true, "");
        Check("library_card_count_matches_database",
            cardsGrid.GetChildCount() == CardDatabase.All.Count,
            $"grid={cardsGrid.GetChildCount()} db={CardDatabase.All.Count}");
        Check("library_relic_count_matches_database",
            relicsGrid.GetChildCount() == RelicDatabase.All.Count,
            $"grid={relicsGrid.GetChildCount()} db={RelicDatabase.All.Count}");
        Check("library_potion_count_matches_database",
            potionsGrid.GetChildCount() == PotionDatabase.All.Count,
            $"grid={potionsGrid.GetChildCount()} db={PotionDatabase.All.Count}");

        screen.QueueFree();
    }

    private void TestLockedCardIsDimmed()
    {
        ResetScratch(); // TotalProgress 0 - everything on UnlockTrack is locked
        var screen = OpenScreen();
        var cardsGrid = screen.GetNode<GridContainer>("Content/CardsPane/Center/CardsGrid");

        var view = FindCardView(cardsGrid, FirstCardEntry.Id);
        Check("library_locked_card_is_dimmed",
            view is not null && view.Modulate == ChromeStyles.LockedTint,
            $"card={FirstCardEntry.Id} modulate={view?.Modulate}");

        screen.QueueFree();
    }

    private void TestAlwaysAvailableCardIsNotDimmed()
    {
        ResetScratch();
        var screen = OpenScreen();
        var cardsGrid = screen.GetNode<GridContainer>("Content/CardsPane/Center/CardsGrid");

        var view = FindCardView(cardsGrid, "strike");
        Check("library_always_available_card_not_dimmed",
            view is not null && view.Modulate == Colors.White,
            $"modulate={view?.Modulate}");

        screen.QueueFree();
    }

    private void TestLockedRelicIsDimmed()
    {
        ResetScratch();
        var screen = OpenScreen();
        var relicsGrid = screen.GetNode<GridContainer>("Content/RelicsPane/Center/RelicsGrid");

        var tile = RelicTile(relicsGrid, FirstRelicEntry.Id);
        Check("library_locked_relic_is_dimmed", tile.Modulate == ChromeStyles.LockedTint,
            $"relic={FirstRelicEntry.Id} modulate={tile.Modulate}");

        screen.QueueFree();
    }

    private void TestUnlockAfterThresholdCrossedIsLive()
    {
        ResetScratch();
        MetaProgressionManager.Instance.AddRunResult(FirstCardEntry.Threshold, 1, "Win", ScratchPath);

        // A fresh instance, not a refresh of the old one - proves the screen
        // reads MetaProgressionManager at build time from current state
        // rather than baking in whatever was true when the scene first
        // loaded (the two would look identical on a screen that never
        // rebuilds, so this only means something because it's a new open).
        var screen = OpenScreen();
        var cardsGrid = screen.GetNode<GridContainer>("Content/CardsPane/Center/CardsGrid");

        var view = FindCardView(cardsGrid, FirstCardEntry.Id);
        Check("library_unlocked_after_threshold_crossed",
            view is not null && view.Modulate == Colors.White,
            $"card={FirstCardEntry.Id} modulate={view?.Modulate}");

        screen.QueueFree();
    }

    private void TestPotionsAreNeverDimmed()
    {
        ResetScratch(); // nothing unlocked - if potions read the gate at all, this would catch it
        var screen = OpenScreen();
        var potionsGrid = screen.GetNode<GridContainer>("Content/PotionsPane/Center/PotionsGrid");

        bool anyDimmed = potionsGrid.GetChildren()
            .OfType<Control>()
            .Any(tile => tile.Modulate == ChromeStyles.LockedTint);
        Check("library_potions_never_dimmed", !anyDimmed, "a potion tile carried LockedTint");

        screen.QueueFree();
    }

    private void TestDefaultPaneIsCards()
    {
        ResetScratch();
        var screen = OpenScreen();

        var cardsPane = screen.GetNode<Control>("Content/CardsPane");
        var relicsPane = screen.GetNode<Control>("Content/RelicsPane");
        var potionsPane = screen.GetNode<Control>("Content/PotionsPane");

        Check("library_default_pane_is_cards",
            cardsPane.Visible && !relicsPane.Visible && !potionsPane.Visible,
            $"cards={cardsPane.Visible} relics={relicsPane.Visible} potions={potionsPane.Visible}");

        screen.QueueFree();
    }

    private void TestCategoryButtonsSwitchPanes()
    {
        ResetScratch();
        var screen = OpenScreen();

        var cardsPane = screen.GetNode<Control>("Content/CardsPane");
        var relicsPane = screen.GetNode<Control>("Content/RelicsPane");
        var potionsPane = screen.GetNode<Control>("Content/PotionsPane");

        screen.GetNode<Button>("Content/CategoryRow/Relics").EmitSignal(Button.SignalName.Pressed);
        Check("library_relic_button_switches_pane",
            relicsPane.Visible && !cardsPane.Visible && !potionsPane.Visible,
            $"cards={cardsPane.Visible} relics={relicsPane.Visible} potions={potionsPane.Visible}");

        screen.GetNode<Button>("Content/CategoryRow/Potions").EmitSignal(Button.SignalName.Pressed);
        Check("library_potion_button_switches_pane",
            potionsPane.Visible && !cardsPane.Visible && !relicsPane.Visible,
            $"cards={cardsPane.Visible} relics={relicsPane.Visible} potions={potionsPane.Visible}");

        screen.QueueFree();
    }
}
