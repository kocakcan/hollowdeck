using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;

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

    // Fixed per-entry size (rather than letting GridContainer size each
    // column to its widest cell's natural content) - without this, a single
    // long description in one column stretches that whole column wider than
    // its neighbors, so the "cards" read as different sizes. Both the entry
    // box AND the wrapped Labels inside it need the same fixed width: a
    // WordSmart Label with no width of its own reports its full unwrapped
    // text as its minimum size, which would blow the column out regardless
    // of the box's CustomMinimumSize.
    private const float EntryWidth = 200f;
    private const float EntryHeight = 260f;
    private const float EntryContentWidth = EntryWidth - 16f;

    public static void Open(Node screenRoot, string title, IReadOnlyList<CardDefinition> cards, Combatant? liveContext = null)
    {
        foreach (var child in screenRoot.GetChildren())
        {
            if (child is PileViewPopup existing) existing.QueueFree();
        }

        var popup = new PileViewPopup();
        screenRoot.AddChild(popup);
        popup.Build(title, cards, liveContext);
    }

    private void Build(string title, IReadOnlyList<CardDefinition> cards, Combatant? liveContext)
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
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(grid);

        foreach (var card in cards.OrderBy(c => c.Cost).ThenBy(c => c.Name))
        {
            grid.AddChild(BuildCardEntry(card, liveContext));
        }
    }

    private static Control BuildCardEntry(CardDefinition card, Combatant? liveContext)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(EntryWidth, EntryHeight) };
        box.AddThemeStyleboxOverride("panel", EntryFrameStyle());

        var vbox = new VBoxContainer();
        box.AddChild(vbox);

        if (ArtAssets.CardIcon(card.Id) is { } icon)
        {
            vbox.AddChild(new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(0, 60),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
        }

        vbox.AddChild(new Label
        {
            Text = $"{card.Name} ({card.Cost})",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(EntryContentWidth, 0),
        });

        // Live context (Strength/Weak) so a pile viewed mid-combat shows the
        // same actually-would-land numbers as the hand does, not stale prose.
        var descriptionLabel = new Label
        {
            Text = EffectDescriptionFormatter.Describe(card.Effects, liveContext),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(EntryContentWidth, 0),
        };
        descriptionLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(descriptionLabel);

        return box;
    }

    private static StyleBoxFlat EntryFrameStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.16f, 0.9f),
            BorderColor = new Color(0.45f, 0.45f, 0.5f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(8);
        return style;
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
