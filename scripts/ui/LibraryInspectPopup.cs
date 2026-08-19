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
    private const int InspectScale = CardInspectView.InspectScale;

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

        // The spacer, the plain-Control-not-Container rule and the integer
        // scale all live in CardView.AddScaledCard now, shared with the combat
        // peek. A library tile is not a picture of anything in a live combat,
        // so it prints authored numbers - which is what it always did.
        CardView.AddScaledCard(
            _content, new CardInstance(card), InspectScale, showsLiveCombat: false, out _cardView);

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
