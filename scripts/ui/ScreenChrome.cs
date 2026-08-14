using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The furniture every non-combat screen needs and each one used to improvise:
// a screen title, the run's HP/gold/relic readout, a framed panel to sit
// content in, and a pixel icon at an integer scale.
//
// Attached in code from _Ready, the same convention ScreenBackground and
// DeckViewButtons already use, and for the same reason - six screens sharing a
// header is one edit here rather than six .tscn files that will drift.
//
// The vocabulary is deliberately combat's, not a second one. CombatScreen's
// TopLeftColumn is a gold PanelStyle panel over a GridContainer of per-relic
// SlotStyle slots; AddRunStatus builds the same thing. Before this, Map/Rest/
// Shop each printed "HP: 21/50" as a bare Label at (24, 16) in the body face,
// which is how a player could walk from combat into a rest site and see the
// same number rendered two different ways (ROADMAP Phase 4).
public static class ScreenChrome
{
    // Top-left corner the status block starts at, matching CombatScreen's
    // TopLeftColumn offsets so the HUD does not shift when a screen changes.
    private const float MarginLeft = 20f;
    private const float MarginTop = 12f;

    // Title sits between the status block and the top-right pile counters.
    // Fixed width and centred on the 1152 design width rather than anchored,
    // so a long act name centres on the screen instead of on whatever space
    // is left over.
    private const float TitleWidth = 560f;
    private const float DesignWidth = 1152f;

    private const int HpBarWidth = 128;
    private const int HpBarHeight = 10;

    // Relics wrap after this many per row by default. Out of combat there is
    // more horizontal room than CombatScreen's 3-column bar has, but the block
    // still must not run under the centred title (which starts at x=296).
    //
    // A screen whose own content reaches further left than that overrides it -
    // see AddRunStatus's relicColumns. Six columns is 6*40 + 5*4 = 260px of
    // slots from x=20, so the block ends at x=280.
    public const int RelicColumns = 6;

    // Sizes a title may render at, largest first. Heading is what every screen
    // whose title is a fixed word ("Shop", "Rest Site") gets and the only size
    // this used to have; Body is the rung underneath, and there is nothing
    // between them because ART_SPEC section 6 puts every rendered size on the
    // 8px design em.
    private static readonly int[] TitleFontSizes = { UiTheme.Fonts.Heading, UiTheme.Fonts.Body };

    /// The screen's name, in the display face, centred at the top. Returns the
    /// label so a caller that animates it (or reads it back in a test) can.
    public static Label AddTitle(Control screen, string text, Color? color = null)
    {
        var label = new Label
        {
            Name = "ScreenTitle",
            Text = text,
            Position = new Vector2((DesignWidth - TitleWidth) / 2f, MarginTop),
            CustomMinimumSize = new Vector2(TitleWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ThemeTypeVariation = "CombatDisplayLabel",
        };
        label.AddThemeColorOverride("font_color", color ?? UiTheme.Palette.AccentGoldBright);
        screen.AddChild(label);

        // After AddChild, not before: TextFit measures through the label's own
        // theme, and the ThemeTypeVariation above only resolves to the display
        // face once the label is in the tree - measured against the default
        // theme, every title "fits" and this does nothing.
        //
        // The overflow direction is what makes this worth doing at all. The
        // label is centred, so a string wider than TitleWidth spills equally
        // from both ends, and the left end is where the run-status block is: a
        // playtest of act 3 put "ACT 3 OF 3 - THE HOLLOW THRONE" (the longest
        // title in the game, and the only one that overflows) into the gold
        // chip. Clipping instead would cut the act's name off both sides.
        TextFit.Apply(label, TitleWidth, TitleFontSizes);
        return label;
    }

    /// HP bar, gold, and the relics carried so far, framed the way combat
    /// frames them. Node names are stable (`RunStatusBar/HpLabel`,
    /// `RunStatusBar/GoldLabel`) because the smoke tests read them.
    ///
    /// `showRelics: false` is for screens where the relic row would duplicate
    /// something the screen itself is already about - TreasureScreen, whose
    /// entire content is the relic you just picked up.
    ///
    /// `relicColumns` narrows the grid for a screen whose content comes further
    /// left than the centred title does. The block trades width for height, so
    /// this is not free and the default stays 6: MapScreen pays for block
    /// height out of its node band (see MapScreen.BandTop), and act 3's ten
    /// floors have none to spare.
    public static Control AddRunStatus(Control screen, bool showRelics = true,
        int relicColumns = RelicColumns)
    {
        var column = new VBoxContainer
        {
            Name = "RunStatusBar",
            Position = new Vector2(MarginLeft, MarginTop),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);
        screen.AddChild(column);

        var topRow = new HBoxContainer { Name = "TopRow", MouseFilter = Control.MouseFilterEnum.Ignore };
        topRow.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Sm);
        column.AddChild(topRow);

        topRow.AddChild(BuildHpPanel());
        topRow.AddChild(BuildGoldPanel());

        if (showRelics) column.AddChild(BuildRelicRow(relicColumns));
        return column;
    }

    // HP as a number over a bar rather than the bare "HP: 34/50" label these
    // screens used to carry. The bar is what makes "34/50" land as a fraction
    // of a whole at a glance, which is the only reason to show a maximum.
    private static Control BuildHpPanel()
    {
        var panel = new PanelContainer
        {
            Name = "HpPanel",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        panel.AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());

        // Named rather than left to Godot's auto "VBoxContainer", because the
        // smoke tests address the label through it.
        var stack = new VBoxContainer { Name = "Readout" };
        stack.AddThemeConstantOverride("separation", 2);
        panel.AddChild(stack);

        var label = new Label
        {
            Name = "HpLabel",
            Text = $"HP {RunState.PlayerCurrentHp}/{RunState.PlayerMaxHp}",
            ThemeTypeVariation = "CombatDisplayLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Body);
        stack.AddChild(label);

        var bar = new ProgressBar
        {
            Name = "HpBar",
            MinValue = 0,
            MaxValue = RunState.PlayerMaxHp,
            Value = RunState.PlayerCurrentHp,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(HpBarWidth, HpBarHeight),
        };
        bar.AddThemeStyleboxOverride("background", ChromeStyles.HpBarBackground());
        bar.AddThemeStyleboxOverride("fill", ChromeStyles.HpBarFill());
        stack.AddChild(bar);
        return panel;
    }

    /// Node paths into the block `AddRunStatus` builds. Constants rather than
    /// literals scattered through six screens and three smoke tests, since the
    /// tree shape is this class's business and nobody else's.
    public const string HpLabelPath = "RunStatusBar/TopRow/HpPanel/Readout/HpLabel";
    public const string HpBarPath = "RunStatusBar/TopRow/HpPanel/Readout/HpBar";
    public const string GoldLabelPath = "RunStatusBar/TopRow/GoldPanel/GoldLabel";

    /// Re-reads RunState into an already-attached status block. Only the shop
    /// needs it (gold falls as you buy), but it lives here so a screen never
    /// reaches into the block's internals to patch one label.
    public static void RefreshRunStatus(Control screen)
    {
        if (screen.GetNodeOrNull<Label>(HpLabelPath) is { } hp)
        {
            hp.Text = $"HP {RunState.PlayerCurrentHp}/{RunState.PlayerMaxHp}";
        }
        if (screen.GetNodeOrNull<ProgressBar>(HpBarPath) is { } bar)
        {
            bar.MaxValue = RunState.PlayerMaxHp;
            bar.Value = RunState.PlayerCurrentHp;
        }
        if (screen.GetNodeOrNull<Label>(GoldLabelPath) is { } gold)
        {
            gold.Text = $"Gold: {RunState.Gold}";
        }
    }

    private static Control BuildGoldPanel()
    {
        var panel = new PanelContainer
        {
            Name = "GoldPanel",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        panel.AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());

        var label = new Label
        {
            Name = "GoldLabel",
            Text = $"Gold: {RunState.Gold}",
            ThemeTypeVariation = "CombatDisplayLabel",
        };
        label.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Body);
        label.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGold);
        panel.AddChild(label);
        return panel;
    }

    // One SlotStyle slot per relic, exactly as CombatScreen.RefreshRelics
    // builds them. MapScreen used to draw this as a bare 30x30 TextureRect row
    // pinned to y=570 - off the 32px grid, unframed, and stranded in the dead
    // space under the graph.
    private static Control BuildRelicRow(int columns)
    {
        var grid = new GridContainer
        {
            Name = "RelicRow",
            Columns = columns,
            // ShrinkBegin like the HP and gold panels beside it: a GridContainer
            // stretches to its parent VBox's width otherwise, which is set by
            // the wider HP/gold row above. Nothing renders in the difference -
            // the grid has no background - but the node's rect then claims
            // 280px of screen it does not occupy, and a layout test asking
            // "does anything sit under the relic grid" gets the wrong answer.
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        grid.AddThemeConstantOverride("h_separation", (int)UiTheme.Spacing.Xs);
        grid.AddThemeConstantOverride("v_separation", (int)UiTheme.Spacing.Xs);

        foreach (var relic in RunState.Relics)
        {
            var slot = new PanelContainer
            {
                TooltipText = $"{relic.Definition.Name}\n{relic.Definition.Description}",
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            slot.AddThemeStyleboxOverride("panel", ChromeStyles.SlotStyle(filled: true));
            grid.AddChild(slot);

            if (ArtAssets.RelicIcon(relic.Definition.Id) is { } icon)
            {
                var rect = new TextureRect { Texture = icon, MouseFilter = Control.MouseFilterEnum.Ignore };
                PixelSpec.ApplyPixelRect(rect, PixelSpec.IconGrid, PixelSpec.HudIconScale);
                slot.AddChild(rect);
            }
            else
            {
                slot.AddChild(new Label
                {
                    Text = relic.Definition.Name,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
        }
        return grid;
    }

    /// A 32x32 icon at an integer scale, filtered Nearest. The scale is a
    /// `PixelSpec` constant at every call site rather than a pixel size, so a
    /// layout can never introduce the fractional scale ART_SPEC section 2
    /// forbids.
    public static TextureRect PixelIcon(Texture2D texture, int scale)
    {
        var rect = new TextureRect { Texture = texture, MouseFilter = Control.MouseFilterEnum.Ignore };
        PixelSpec.ApplyPixelRect(rect, PixelSpec.IconGrid, scale);
        return rect;
    }

    /// Wraps content in the standard bronze-bordered panel with generous
    /// padding. What turns a column of centred labels floating on a tiled
    /// backdrop into something that reads as a piece of the interface.
    public static PanelContainer Frame(Control content, float padding = UiTheme.Spacing.Lg)
    {
        var style = ChromeStyles.PanelStyle();
        style.ContentMarginLeft = padding;
        style.ContentMarginRight = padding;
        style.ContentMarginTop = padding;
        style.ContentMarginBottom = padding;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(content);
        return panel;
    }

    /// Frame, but focusable and clickable - a tile the player picks rather than
    /// a panel they read. LibraryScreen's collection grids and the reward
    /// screen's boss relic picker are both grids of these.
    ///
    /// ActivatablePanel (a PanelContainer), not Button: a Button doesn't
    /// propagate a child's minimum size upward (it's not a Container), so
    /// arbitrary icon/heading/description content would get clipped to the
    /// button's own near-zero natural size. Focus/hover visuals are done by
    /// hand instead - the same idiom ChromeStyles.ApplyFocusableSliderStyle
    /// uses for HSlider, which has the identical problem (no built-in focus
    /// stylebox on a non-Button) - and click/ui_accept activation comes from
    /// ActivatablePanel for the same reason CardView hand-rolls its own.
    ///
    /// Lived in LibraryScreen until the reward screen needed the second copy.
    /// It is here rather than there because a focus ring drawn two ways is a
    /// seam the player sees and no suite can assert about.
    public static ActivatablePanel FocusableFrame(Control content, float padding = UiTheme.Spacing.Md)
    {
        // Focus is a second slice rather than a recolour of the first: chrome
        // is art now, and a StyleBoxTexture has no BorderColor to raise to
        // FocusRing. panel_focus carries the same shape at the top of the gold
        // ramp, which is the change the old two-line mutation was making.
        StyleBox Style(bool focused)
        {
            var style = ChromeStyles.PanelStyle(focused);
            style.ContentMarginLeft = padding;
            style.ContentMarginRight = padding;
            style.ContentMarginTop = padding;
            style.ContentMarginBottom = padding;
            return style;
        }

        var panel = new ActivatablePanel { FocusMode = Control.FocusModeEnum.All };
        panel.AddThemeStyleboxOverride("panel", Style(focused: false));
        panel.AddChild(content);

        panel.FocusEntered += () => panel.AddThemeStyleboxOverride("panel", Style(focused: true));
        panel.FocusExited += () => panel.AddThemeStyleboxOverride("panel", Style(focused: false));

        return panel;
    }

    /// A framed art plinth: one pixel icon at `SpriteScale`, centred, on the
    /// darker BgDeep face so the art separates from the panel around it. The
    /// scale is the same 5x an enemy sprite renders at, which is what makes a
    /// relic on the treasure screen feel like the same class of object as a
    /// thing you fought.
    public static PanelContainer ArtPlinth(Texture2D texture, int scale = PixelSpec.SpriteScale)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        panel.AddThemeStyleboxOverride("panel", ChromeStyles.PlinthStyle());
        panel.AddChild(PixelIcon(texture, scale));
        return panel;
    }

    /// A heading inside a panel - the display face, gold, one step down from
    /// the screen title.
    public static Label Heading(string text, int fontSize = UiTheme.Fonts.Heading)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = "CombatDisplayLabel",
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        return label;
    }

    /// Body copy inside a panel - the body face, wrapped, centred, and set in
    /// the warm off-white the ramp calls N7 rather than pure white.
    public static Label Body(string text, int width = 0)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Body);
        label.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);
        if (width > 0) label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }
}
