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
        // 1040 of the 1152-wide canvas, so the 5-column grid below clears the
        // scrollbar with room to spare and still leaves a 56px margin each
        // side. Height stays 620 - the canvas is only 648 tall.
        const float w = 1040f, h = 620f;
        panel.OffsetLeft = -w / 2f;
        panel.OffsetRight = w / 2f;
        panel.OffsetTop = -h / 2f;
        panel.OffsetBottom = h / 2f;

        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        var header = new HBoxContainer();
        vbox.AddChild(header);
        var titleLabel = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titleLabel.AddThemeFontSizeOverride("font_size", 24);
        header.AddChild(titleLabel);
        var sortButton = new Button { Text = "Sort: Cost" };
        sortButton.Pressed += () =>
        {
            AudioManager.Instance?.PlaySfx("ui_click");
            _sortByName = !_sortByName;
            sortButton.Text = _sortByName ? "Sort: Name" : "Sort: Cost";
            RepopulateGrid();
        };
        header.AddChild(sortButton);
        var closeButton = new Button { Text = "Close (Esc)" };
        closeButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        closeButton.Pressed += QueueFree;
        header.AddChild(closeButton);
        // The popup is drawn over the screen but isn't modal, so without
        // taking focus Tab would walk straight past it into the buttons
        // underneath - which are still live, and on the map screen would move
        // the player. Taking it here and handing it back in _ExitTree keeps
        // the popup a genuine dead end for the keyboard.
        _focusBeforeOpen = GetViewport().GuiGetFocusOwner();
        closeButton.CallDeferred(Control.MethodName.GrabFocus);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, h - 70f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        vbox.AddChild(scroll);
        // 5 columns of full-size CardView instances - the same renderer combat
        // hands use, at their natural 176x240. The old comment here claimed
        // cards were 224x308 and that a 4th column would overflow; both were
        // stale, and 3 columns left 384px of the panel empty. 5 columns is
        // 5*176 + 4*14 = 936 inside the panel's 1020px interior (1040 less the
        // theme's 10+10 content margins), which clears the vertical scrollbar.
        // Two full rows are visible at a time, so 10 cards per screenful.
        _grid = new GridContainer { Columns = 5 };
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
        // hd_cancel (Escape or right-click). This popup is a child of the
        // screen root, and Godot walks children before the parent's own
        // handler, so consuming the event here is also what stops
        // CombatScreen's own hd_cancel branch from cancelling targeting
        // behind an open popup.
        if (@event.IsActionPressed("hd_cancel"))
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }

    // Whatever had focus on the screen underneath, so dismissing the popup
    // puts the player back where they were rather than nowhere.
    private Control? _focusBeforeOpen;

    public override void _ExitTree()
    {
        if (_focusBeforeOpen is { } previous && IsInstanceValid(previous) && previous.IsInsideTree())
        {
            // Quietly, and therefore through a Callable rather than
            // CallDeferred(MethodName.GrabFocus): handing focus back where it
            // was is the popup tidying up after itself, not the player pointing
            // at whatever is underneath - which on a reward screen is a card
            // that would otherwise raise its keyword panel on the way out.
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(previous) && previous.IsInsideTree())
                {
                    ScreenKeyboardNav.GrabFocusQuietly(previous);
                }
            }).CallDeferred();
        }
    }
}
