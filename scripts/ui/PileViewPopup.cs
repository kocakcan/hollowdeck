using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Read-only "what's in this pile" overlay - Slay-the-Spire-style click-to-
// inspect for the master deck and, mid-combat, the draw/discard/exhaust
// piles. Built entirely in code (no .tscn) and spawned on demand via Open()
// rather than a RunManager scene state, since it's a transient look-and-
// dismiss popup, not real navigation - see DeckViewButtons for the buttons/
// keybinds that call it.
public partial class PileViewPopup : Control
{
    // CombatEndPanel (CombatScreen) uses 1000 so a popup opened right after a
    // win/loss (before the player clicks Continue) still renders on top.
    private const int ZIndexAboveCombatEnd = 2000;

    private GridContainer _grid = null!;
    private PackedScene _cardScene = null!;
    private IReadOnlyList<CardDefinition> _cards = null!;
    private bool _sortByName;

    // CardView.SetCardInstance already reads CombatManager.Instance.Player
    // for live Strength/Weak context itself, so unlike the old bespoke
    // entry rendering here, this popup no longer needs a caller-supplied
    // liveContext to pass through - it was only ever used to hand that same
    // value to EffectDescriptionFormatter.Describe manually.
    public static void Open(Node screenRoot, string title, IReadOnlyList<CardDefinition> cards)
    {
        foreach (var child in screenRoot.GetChildren())
        {
            if (child is PileViewPopup existing) existing.QueueFree();
        }

        var popup = new PileViewPopup();
        screenRoot.AddChild(popup);
        popup.Build(title, cards);
    }

    private void Build(string title, IReadOnlyList<CardDefinition> cards)
    {
        _cards = cards;
        _cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = ZIndexAboveCombatEnd;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        AddChild(panel);
        panel.SetAnchorsPreset(LayoutPreset.Center);
        const float w = 960f, h = 620f;
        panel.OffsetLeft = -w / 2f;
        panel.OffsetRight = w / 2f;
        panel.OffsetTop = -h / 2f;
        panel.OffsetBottom = h / 2f;

        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        var header = new HBoxContainer();
        vbox.AddChild(header);
        var titleLabel = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titleLabel.AddThemeFontSizeOverride("font_size", 22);
        header.AddChild(titleLabel);
        var sortButton = new Button { Text = "Sort: Cost" };
        sortButton.Pressed += () =>
        {
            _sortByName = !_sortByName;
            sortButton.Text = _sortByName ? "Sort: Name" : "Sort: Cost";
            RepopulateGrid();
        };
        header.AddChild(sortButton);
        var closeButton = new Button { Text = "Close (Esc)" };
        closeButton.Pressed += QueueFree;
        header.AddChild(closeButton);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, h - 70f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        vbox.AddChild(scroll);
        // 3 columns of full-size (224x308) CardView instances - the same
        // renderer combat hands use - fits the 960-wide panel with room to
        // spare; 4 columns of full-size cards would overflow it.
        _grid = new GridContainer { Columns = 3 };
        _grid.AddThemeConstantOverride("h_separation", 14);
        _grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(_grid);

        RepopulateGrid();
    }

    private void RepopulateGrid()
    {
        foreach (var child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }

        var ordered = _sortByName
            ? _cards.OrderBy(c => c.Name)
            : _cards.OrderBy(c => c.Cost).ThenBy(c => c.Name);
        foreach (var card in ordered)
        {
            var view = _cardScene.Instantiate<CardView>();
            _grid.AddChild(view);
            view.Interactive = false;
            view.SetCardInstance(new CardInstance(card));
        }
    }

    // Click anywhere outside the panel (the panel itself has its own Stop
    // filter and absorbs its own clicks) to dismiss, matching typical popup
    // conventions elsewhere.
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            QueueFree();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }
}
