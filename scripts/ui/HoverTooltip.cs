using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hollowdeck.UI;

// The stacked explanation panel that appears beside whatever the player is
// pointing at - a card's keywords, an enemy's telegraphed move. One widget so
// there is one answer to "what does a tooltip look like in this game", which
// there was not: this started as private methods inside CardView, so only the
// combat hand had it, and the enemy intent had to make do with a stock Godot
// TooltipText reading "Inflicts Weak".
//
// Deliberately not Godot's built-in tooltip system (TooltipText,
// _make_custom_tooltip, [hint=] spans). That only ever fires from real OS
// mouse motion, and both surfaces this serves are fully keyboard-driven -
// arrow-key card selection and arrow-key enemy target-locking have no mouse
// motion to fire it. Callers drive Show/Hide from whichever input path
// happened, and the two behave identically because they run the same code.
public partial class HoverTooltip : VBoxContainer
{
    // Above PileViewPopup's own ZIndex of 2000. The popup is the deepest
    // overlay in the game and its cards are focusable, so a tooltip that
    // merely beat the screen (the old value was 500) rendered *behind* the
    // very popup that spawned it.
    private const int OverlayZIndex = 2500;

    private const float BoxWidth = 200f;
    private const float AnchorGap = 8f;
    private const float ViewportMargin = 8f;

    // Where the panel sits relative to what it explains. Two hosts, two
    // answers: a card is a small box in a row of them with clear air above it,
    // an enemy is a 220x300 view whose top edge is already near the top of the
    // screen.
    public enum Placement
    {
        // Above the anchor, flipping below when there is no room. Cards.
        AboveAnchor,

        // Beside the anchor, on whichever side faces *away* from the middle of
        // the screen, top-aligned with it. Enemies: above is off-screen for
        // them, and the flip-below that produced put the panel squarely over
        // the fanned hand - the one region of a combat screen a panel must
        // never cover, since it is what the player is reading it in order to
        // decide about. Facing outward is what keeps a left-hand enemy's panel
        // off the enemy standing next to it.
        BesideAnchor,
    }

    private Control? _anchor;
    private Placement _placement;

    // Parented to the current scene rather than to the anchor: the anchor is
    // often inside a ScrollContainer (the card picker, the deck popup) or a
    // clipping panel, and a child would be clipped by it. No screen in the
    // game uses a CanvasLayer, so the scene root is the topmost Control
    // everywhere and one parenting rule covers every caller.
    public static HoverTooltip? Show(
        Control anchor, IEnumerable<Keywords.Entry> boxes, Placement placement = Placement.AboveAnchor)
    {
        var entries = boxes.ToList();
        if (entries.Count == 0) return null;

        var tooltip = new HoverTooltip
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = OverlayZIndex,
            _anchor = anchor,
            _placement = placement,
        };
        tooltip.AddThemeConstantOverride("separation", 4);
        foreach (var entry in entries) tooltip.AddChild(BuildBox(entry));

        anchor.GetTree().CurrentScene.AddChild(tooltip);

        // Rough placement now, because the container's real Size isn't known
        // until its own layout pass runs; _Process corrects it from the next
        // frame on, once Size reflects the actual content. Rough in the same
        // *direction* the corrected position will be, so the first frame isn't
        // a visible jump across the screen.
        tooltip.GlobalPosition = placement == Placement.BesideAnchor
            ? anchor.GlobalPosition + new Vector2(anchor.Size.X + AnchorGap, 0)
            : anchor.GlobalPosition + new Vector2(0, -40 * entries.Count - AnchorGap);
        return tooltip;
    }

    // Not named Hide: Control already has one, and it means "set Visible =
    // false". A tooltip is never hidden-but-alive - it is built on show and
    // freed on dismiss, so the caller's null field is the only "no tooltip"
    // state there is.
    public void Dismiss()
    {
        _anchor = null;
        QueueFree();
    }

    // Tracked every frame rather than positioned once. Three of the four hosts
    // move after the tooltip is up: RewardScreen tweens an endless idle sway on
    // its cards, the card picker scrolls under keyboard focus, and the combat
    // hand re-fans as cards are played. A one-shot CallDeferred placement (what
    // this used to be) left the panel behind in all three.
    public override void _Process(double delta)
    {
        if (_anchor is null || !IsInstanceValid(_anchor) || !_anchor.IsInsideTree())
        {
            Dismiss();
            return;
        }

        GlobalPosition = Clamp(_anchor.GetGlobalRect());
    }

    // Above the anchor by default - that is where the reference layout puts it
    // and it keeps the panel clear of the hand. A card in the top row of a
    // picker grid has no room up there, so flip below rather than letting the
    // panel run off the top of the screen; horizontally, slide back inside.
    private Vector2 Clamp(Rect2 anchorRect)
    {
        var screen = GetViewportRect().Size;
        return _placement == Placement.BesideAnchor
            ? new Vector2(ClampX(BesideX(anchorRect, screen), screen), ClampY(BesideY(anchorRect), screen))
            : new Vector2(ClampX(anchorRect.Position.X, screen), ClampY(AboveOrBelowY(anchorRect), screen));
    }

    private float AboveOrBelowY(Rect2 anchorRect)
    {
        float above = anchorRect.Position.Y - Size.Y - AnchorGap;
        return above >= ViewportMargin ? above : anchorRect.End.Y + AnchorGap;
    }

    // Outward from the middle of the screen, so the panel lands in the margin
    // rather than on the enemy next door. The clamp below can still pull it
    // back over the anchor in the worst case (four enemies, so the outermost is
    // already close to the edge) - overlapping the creature the panel is
    // *about* is the acceptable failure; overlapping the hand is not.
    // Bottom-aligned to the anchor rather than top-aligned. Both keep the panel
    // level with the enemy and off the hand; this one also keeps it out of the
    // top corners, where combat's own chrome lives - the pile counter bar on
    // the right, the gold/relic column on the left. An enemy view is 300px tall
    // against a ~135px panel, so the difference is the whole HUD band.
    private float BesideY(Rect2 anchorRect) => anchorRect.End.Y - Size.Y;

    private float BesideX(Rect2 anchorRect, Vector2 screen) =>
        anchorRect.GetCenter().X < screen.X / 2f
            ? anchorRect.Position.X - Size.X - AnchorGap
            : anchorRect.End.X + AnchorGap;

    private float ClampX(float x, Vector2 screen) =>
        Mathf.Clamp(x, ViewportMargin, Mathf.Max(ViewportMargin, screen.X - Size.X - ViewportMargin));

    private float ClampY(float y, Vector2 screen) =>
        Mathf.Clamp(y, ViewportMargin, Mathf.Max(ViewportMargin, screen.Y - Size.Y - ViewportMargin));

    // One small dark bronze-bordered box per keyword: a coloured title line
    // matching that keyword's inline highlight colour in the card text, plus
    // its explanation below. Stacked by Show when there is more than one.
    private static Control BuildBox(Keywords.Entry entry)
    {
        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        panel.AddThemeStyleboxOverride("panel", ChromeStyles.PanelStyle());

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 2);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text = entry.Keyword,
            CustomMinimumSize = new Vector2(BoxWidth, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", entry.Color);
        title.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(title);

        var body = new Label
        {
            Text = entry.Blurb,
            CustomMinimumSize = new Vector2(BoxWidth, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        body.AddThemeFontSizeOverride("font_size", 16);
        body.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        vbox.AddChild(body);

        return panel;
    }
}
