using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Browse-only compendium of every card, relic and potion in the game -
// reachable from the Main Menu, with no active RunState behind it (no
// HP/gold/relics to show, so this skips ScreenChrome.AddRunStatus entirely).
//
// Everything renders, always: only 9 cards and 5 relics are ever gated
// (MetaProgressionManager.UnlockTrack) and no potion is gated at all, so
// hiding locked content would make the roster look thinner than it is.
// Locked items dim instead, the same ChromeStyles.LockedTint
// MetaProgressionScreen's own unlock-track rows already use.
public partial class LibraryScreen : Control
{
    private const int CardColumns = 5;
    private const int TileColumns = 4;
    private const int TileWidth = 220;

    private const float ContentLeft = 76f;
    private const float ContentTop = 44f;
    private const float ContentWidth = 1000f;
    // Two full 240px card rows plus one v_separation (14) is 494 - the
    // binding case, since relic/potion tiles are shorter. Tight against the
    // 648-tall canvas once title/category-row/back-button chrome is
    // accounted for; a shorter pane clips the second row mid-description
    // rather than at a row boundary, which reads as broken rather than "more
    // below, scroll for it".
    private const float PaneHeight = 500f;

    private Button _cardsButton = null!;
    private Button _relicsButton = null!;
    private Button _potionsButton = null!;
    private Button _backButton = null!;

    private Control _cardsPane = null!;
    private Control _relicsPane = null!;
    private Control _potionsPane = null!;
    private GridContainer _cardsGrid = null!;
    private GridContainer _relicsGrid = null!;
    private GridContainer _potionsGrid = null!;

    private readonly List<Control> _cardTiles = new();
    private readonly List<Control> _relicTiles = new();
    private readonly List<Control> _potionTiles = new();

    private ScreenKeyboardNavListener? _keyboardNav;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "etched", new Color(0.85f, 0.85f, 0.9f));
        ScreenChrome.AddTitle(this, "Library");

        var content = new VBoxContainer
        {
            Name = "Content",
            Position = new Vector2(ContentLeft, ContentTop),
            CustomMinimumSize = new Vector2(ContentWidth, 0),
        };
        content.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Sm);
        AddChild(content);

        var categoryRow = new HBoxContainer { Name = "CategoryRow", Alignment = BoxContainer.AlignmentMode.Center };
        categoryRow.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Sm);
        content.AddChild(categoryRow);

        _cardsButton = BuildCategoryButton("Cards", ShowCards);
        _relicsButton = BuildCategoryButton("Relics", ShowRelics);
        _potionsButton = BuildCategoryButton("Potions", ShowPotions);
        categoryRow.AddChild(_cardsButton);
        categoryRow.AddChild(_relicsButton);
        categoryRow.AddChild(_potionsButton);
        _cardsButton.FocusNeighborRight = _cardsButton.GetPathTo(_relicsButton);
        _relicsButton.FocusNeighborLeft = _relicsButton.GetPathTo(_cardsButton);
        _relicsButton.FocusNeighborRight = _relicsButton.GetPathTo(_potionsButton);
        _potionsButton.FocusNeighborLeft = _potionsButton.GetPathTo(_relicsButton);

        (_cardsPane, _cardsGrid) = BuildPane("Cards", CardColumns);
        (_relicsPane, _relicsGrid) = BuildPane("Relics", TileColumns);
        (_potionsPane, _potionsGrid) = BuildPane("Potions", TileColumns);
        content.AddChild(_cardsPane);
        content.AddChild(_relicsPane);
        content.AddChild(_potionsPane);

        // Plain, not ApplyEmphasisButtonStyle - matches MetaProgressionScreen's
        // own Back button, and its shorter theme padding is what leaves the
        // pane above enough of the 648-tall canvas for two full card rows.
        _backButton = new Button { Name = "BackButton", Text = "Back", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        _backButton.Pressed += OnBackPressed;
        content.AddChild(_backButton);

        PopulateCards();
        PopulateRelics();
        PopulatePotions();
        ShowCards();

        _keyboardNav = ScreenKeyboardNav.Attach(this, PreferredFocus, OnBackPressed);
    }

    // Name is the button's own text ("Cards"/"Relics"/"Potions") - unique
    // among the three, and readable in a smoke test's GetNode path without a
    // separate constant to keep in sync.
    private static Button BuildCategoryButton(string text, Action onPressed)
    {
        var button = new Button { Name = text, Text = text };
        ChromeStyles.ApplyEmphasisButtonStyle(button);
        button.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        button.Pressed += onPressed.Invoke;
        return button;
    }

    // Horizontal scroll stays off and the CenterContainer takes the full
    // width the (disabled) axis hands it, which is what centres a grid
    // that's narrower than the pane - same shape PileViewPopup uses, minus
    // the centring, which that popup didn't need at 936 of a 1020px panel.
    private static (Control pane, GridContainer grid) BuildPane(string name, int columns)
    {
        var scroll = new ScrollContainer
        {
            Name = $"{name}Pane",
            CustomMinimumSize = new Vector2(ContentWidth, PaneHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        var center = new CenterContainer { Name = "Center", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(center);

        var grid = new GridContainer { Name = $"{name}Grid", Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        center.AddChild(grid);

        return (scroll, grid);
    }

    private void PopulateCards()
    {
        ClearGrid(_cardsGrid);
        _cardTiles.Clear();

        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        var meta = MetaProgressionManager.Instance;

        foreach (var def in CardDatabase.All)
        {
            var column = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(CardPicker.CardColumnWidth, 0),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            };
            column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);
            _cardsGrid.AddChild(column);

            var view = cardScene.Instantiate<CardView>();
            column.AddChild(view);
            view.Interactive = false;
            view.SetCardInstance(new CardInstance(def));
            view.Clicked += instance => LibraryInspectPopup.OpenCard(this, instance.Definition);

            // SetCardInstance sets its own Modulate (affordability tint,
            // always White here since Interactive is false) - the lock tint
            // has to be applied after, or this overwrites it right back.
            if (!meta.IsCardUnlocked(def.Id))
            {
                view.Modulate = ChromeStyles.LockedTint;
                var entry = MetaProgressionManager.UnlockTrack
                    .First(e => e.Kind == UnlockKind.Card && e.Id == def.Id);
                column.AddChild(LockCaption(entry.Threshold, CardPicker.CardColumnWidth));
            }

            _cardTiles.Add(view);
        }

        CardPicker.WireGridNavigation(_cardsGrid, _cardTiles, null);
        LinkCategoryButton(_cardsButton, _cardTiles);
    }

    private void PopulateRelics()
    {
        ClearGrid(_relicsGrid);
        _relicTiles.Clear();
        var meta = MetaProgressionManager.Instance;

        foreach (var def in RelicDatabase.All)
        {
            bool unlocked = meta.IsRelicUnlocked(def.Id);
            int? threshold = unlocked
                ? null
                : MetaProgressionManager.UnlockTrack
                    .First(e => e.Kind == UnlockKind.Relic && e.Id == def.Id).Threshold;

            // The tier is named for the same reason the potion tile below
            // names its rarity, plus one this screen is the only answer to: a
            // relic's tier also says *where it comes from*, and Boss, Shop and
            // Event are pools the player would otherwise have to infer from
            // where relics happened to turn up.
            string description = $"{def.Tier}. " + def.Description;
            var tile = BuildInfoTile(def.Name, ArtAssets.RelicIcon(def.Id), description, unlocked, threshold,
                () => LibraryInspectPopup.OpenItem(this, def.Name, ArtAssets.RelicIcon(def.Id), description));
            _relicsGrid.AddChild(tile);
            _relicTiles.Add(tile);
        }

        CardPicker.WireGridNavigation(_relicsGrid, _relicTiles, null);
        LinkCategoryButton(_relicsButton, _relicTiles);
    }

    private void PopulatePotions()
    {
        ClearGrid(_potionsGrid);
        _potionTiles.Clear();

        // Potions are never gated - UnlockKind has no Potion case - so every
        // tile here renders unlocked, unconditionally.
        foreach (var def in PotionDatabase.All)
        {
            // The tier is named on the tile because this screen's whole job is
            // explaining the content. A potion rarity the player can only ever
            // meet as a frequency is a number that exists for PotionPool and
            // for nobody else - cards get theirs through CardView's border, and
            // a potion has no CardView.
            string description = $"{def.Rarity}. " + EffectDescriptionFormatter.Describe(
                def.Effects, new DescribeContext(TargetType: def.Target));
            var tile = BuildInfoTile(def.Name, ArtAssets.PotionIcon(def.Id), description, unlocked: true, threshold: null,
                () => LibraryInspectPopup.OpenItem(this, def.Name, ArtAssets.PotionIcon(def.Id), description));
            _potionsGrid.AddChild(tile);
            _potionTiles.Add(tile);
        }

        CardPicker.WireGridNavigation(_potionsGrid, _potionTiles, null);
        LinkCategoryButton(_potionsButton, _potionTiles);
    }

    private static Control BuildInfoTile(string name, Texture2D? icon, string description, bool unlocked, int? threshold,
        Action onActivated)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(TileWidth, 0) };
        column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);

        if (icon is not null)
        {
            column.AddChild(ScreenChrome.ArtPlinth(icon, PixelSpec.CardArtScale));
        }

        column.AddChild(ScreenChrome.Heading(name, UiTheme.Fonts.Body));
        column.AddChild(ScreenChrome.Body(description, TileWidth));
        if (threshold is { } t) column.AddChild(LockCaption(t, TileWidth));

        var tile = ScreenChrome.FocusableFrame(column);
        tile.Activated += onActivated;
        if (!unlocked) tile.Modulate = ChromeStyles.LockedTint;
        return tile;
    }

    private static Label LockCaption(int threshold, int width)
    {
        var label = ScreenChrome.Body($"Locked - {threshold:N0} progress", width);
        label.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGold);
        return label;
    }

    private static void ClearGrid(GridContainer grid)
    {
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void LinkCategoryButton(Button button, IReadOnlyList<Control> tiles)
    {
        if (tiles.Count == 0) return;
        button.FocusNeighborBottom = button.GetPathTo(tiles[0]);
        // WireGridNavigation never touches a first-row tile's Top neighbour
        // (only rows past the first get one), so this survives every later
        // re-wire the pane switch below triggers.
        tiles[0].FocusNeighborTop = tiles[0].GetPathTo(button);
    }

    private void ShowCards()
    {
        _cardsPane.Visible = true;
        _relicsPane.Visible = false;
        _potionsPane.Visible = false;
        // Re-run so the Back button's up-neighbour points at *this* pane's
        // last row - it was last set by whichever pane populated most
        // recently, which is stale the moment the player switches tabs.
        CardPicker.WireGridNavigation(_cardsGrid, _cardTiles, _backButton);
        _keyboardNav?.Regrab();
    }

    private void ShowRelics()
    {
        _cardsPane.Visible = false;
        _relicsPane.Visible = true;
        _potionsPane.Visible = false;
        CardPicker.WireGridNavigation(_relicsGrid, _relicTiles, _backButton);
        _keyboardNav?.Regrab();
    }

    private void ShowPotions()
    {
        _cardsPane.Visible = false;
        _relicsPane.Visible = false;
        _potionsPane.Visible = true;
        CardPicker.WireGridNavigation(_potionsGrid, _potionTiles, _backButton);
        _keyboardNav?.Regrab();
    }

    private Control? PreferredFocus()
    {
        if (_cardsPane.Visible) return _cardTiles.Count > 0 ? _cardTiles[0] : _cardsButton;
        if (_relicsPane.Visible) return _relicTiles.Count > 0 ? _relicTiles[0] : _relicsButton;
        return _potionTiles.Count > 0 ? _potionTiles[0] : _potionsButton;
    }

    private void OnBackPressed()
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
    }
}
