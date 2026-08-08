using Godot;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// "Click/press a tile to see it bigger" overlay for LibraryScreen, one level
// up from PileViewPopup's shell (dim backdrop, hd_cancel/click-outside/
// Close to dismiss, focus save-and-restore) but sized to a single item
// rather than a scrollable grid. Cards get a Show Upgrade toggle backed by
// CardUpgrade.Apply, since it's the only one of the three that ever has a
// "+" version; relics and potions never upgrade, so OpenItem never offers
// one.
public partial class LibraryInspectPopup : Control
{
    private const int ZIndexAboveCombatEnd = 2000;
    private static readonly Vector2 CardPopupSize = new(352, 480);

    private CardDefinition? _baseCard;
    private CardView? _cardView;
    private Button? _upgradeButton;
    private bool _showingUpgrade;
    private Control? _focusBeforeOpen;

    public static void OpenCard(Node screenRoot, CardDefinition card)
    {
        var popup = Open(screenRoot);
        popup.BuildCard(card);
    }

    public static void OpenItem(Node screenRoot, string name, Texture2D? icon, string description)
    {
        var popup = Open(screenRoot);
        popup.BuildItem(name, icon, description);
    }

    private static LibraryInspectPopup Open(Node screenRoot)
    {
        foreach (var child in screenRoot.GetChildren())
        {
            if (child is LibraryInspectPopup existing) existing.QueueFree();
        }

        var popup = new LibraryInspectPopup { Name = "LibraryInspectPopup" };
        screenRoot.AddChild(popup);
        popup.BuildShell();
        return popup;
    }

    // Backdrop and dismissal are shared by both entry points; the content
    // panel each one fills in is the only thing that differs.
    private VBoxContainer _content = null!;

    private void BuildShell()
    {
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

        // Ignore, not Stop: a single-item popup has no interior scroll
        // surface of its own, so nothing here needs to intercept a click -
        // it should fall through to this Control's own _GuiInput below,
        // which is what makes clicking anywhere outside the framed panel
        // dismiss it.
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Md);

        var frame = ScreenChrome.Frame(_content);
        frame.MouseFilter = MouseFilterEnum.Stop;
        center.AddChild(frame);
    }

    private void BuildCard(CardDefinition card)
    {
        _baseCard = card;

        var header = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        header.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Sm);
        _content.AddChild(header);

        if (card.IsPlayable)
        {
            _upgradeButton = new Button { Name = "UpgradeButton", Text = "Show Upgrade" };
            _upgradeButton.Pressed += ToggleUpgrade;
            header.AddChild(_upgradeButton);
        }

        var closeButton = new Button { Name = "CloseButton", Text = "Close (Esc)" };
        closeButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        closeButton.Pressed += QueueFree;
        header.AddChild(closeButton);

        // Control.Scale is a render transform only - it does not grow the
        // node's own layout Size - so the scaled CardView is wrapped in a
        // spacer sized to its scaled footprint, or it would overlap the
        // header row above it instead of pushing it up. The spacer is a
        // plain Control, not a Container: a Container re-fits its children
        // on every sort pass (added, theme change, ...) via
        // fit_child_in_rect, which stomps a child's Scale back to identity -
        // measured, not assumed, after CenterContainer silently reset this
        // exact card back to 1x one frame after it was set to 2x. A plain
        // Control never re-sorts, so the manual centring below survives.
        var spacer = new Control { CustomMinimumSize = CardPopupSize };
        _content.AddChild(spacer);

        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        _cardView = cardScene.Instantiate<CardView>();
        spacer.AddChild(_cardView);
        _cardView.Interactive = false;
        _cardView.Position = (CardPopupSize - _cardView.CustomMinimumSize) / 2f;
        _cardView.PivotOffset = _cardView.CustomMinimumSize / 2f;
        _cardView.Scale = Vector2.One * 2f;
        _cardView.SetCardInstance(new CardInstance(card));

        _focusBeforeOpen = GetViewport().GuiGetFocusOwner();
        closeButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ToggleUpgrade()
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        _showingUpgrade = !_showingUpgrade;
        var shown = _showingUpgrade ? CardUpgrade.Apply(_baseCard!) : _baseCard!;
        _cardView!.SetCardInstance(new CardInstance(shown));
        _upgradeButton!.Text = _showingUpgrade ? "Show Original" : "Show Upgrade";
    }

    private void BuildItem(string name, Texture2D? icon, string description)
    {
        if (icon is not null)
        {
            var plinth = ScreenChrome.ArtPlinth(icon);
            plinth.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            _content.AddChild(plinth);
        }

        _content.AddChild(ScreenChrome.Heading(name));
        _content.AddChild(ScreenChrome.Body(description, 440));

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _content.AddChild(footer);
        var closeButton = new Button { Name = "CloseButton", Text = "Close (Esc)" };
        closeButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        closeButton.Pressed += QueueFree;
        footer.AddChild(closeButton);

        _focusBeforeOpen = GetViewport().GuiGetFocusOwner();
        closeButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            QueueFree();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("hd_cancel"))
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        if (_focusBeforeOpen is { } previous && IsInstanceValid(previous) && previous.IsInsideTree())
        {
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
