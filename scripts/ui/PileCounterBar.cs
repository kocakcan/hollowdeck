using System;
using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The top-right pile HUD: one compact 40x44 cell per pile - a tinted
// card-stack glyph over a live count - packed into a contiguous strip.
//
// Replaces the vertical stack of full-width "Deck"/"Draw"/"Discard"/"Exhaust"
// text buttons this used to be. Those were legible, but four words cost the
// entire top-right corner (~176x150) to show four numbers that CombatScreen
// then printed a *second* time in a "Draw N . Discard N . Exhaust N" label -
// the same value-encoded-twice problem the energy pips had before they became
// one orb. The strip is 160x44 and carries the counts itself, so that label is
// gone (ROADMAP Phase 2).
//
// Cells are told apart by ramp family rather than by a word: Deck gold, Draw
// steel, Discard neutral, Exhaust ember - ember being what
// UiTheme.Palette.ExhaustAccent already means everywhere else in the game. The
// pile's name and its existing D/Q/W/E hotkey live in the cell's tooltip.
public partial class PileCounterBar : HBoxContainer
{
    // 4 cells x 40 = 160, which is what keeps the strip clear of EnemyRow's
    // offset_right=976 on the 1152-wide canvas once DeckViewButtons.Attach
    // insets it 12px from the right edge (left edge lands at 980). That
    // clearance is asserted by DeckViewSmokeTest - widening a cell without
    // re-checking it puts the strip back over the rightmost enemy's intent
    // icon, which is the bug the old vertical stack existed to avoid.
    public const int CellWidth = 40;
    public const int CellHeight = 44;

    private readonly List<PileCounterCell> _cells = new();
    private readonly bool _focusable;
    private readonly PileCounterCell _deck;
    private readonly PileCounterCell? _draw;
    private readonly PileCounterCell? _discard;
    private readonly PileCounterCell? _exhaust;

    public PileCounterBar(Control screen, bool includeCombatPiles)
    {
        // Zero separation: adjacent cells share an edge so the strip reads as
        // one HUD component rather than four floating buttons. Their 2px
        // bezels still divide them.
        AddThemeConstantOverride("separation", 0);

        // Combat drives its own keyboard input and puts every widget at
        // FocusModeEnum.None so arrow keys only ever cycle cards. Everywhere
        // else these cells are ordinary Buttons on a screen navigated by
        // focus, and excluding them there just made the deck unreachable by
        // Tab for no reason. The flag that already distinguishes the two
        // contexts decides it.
        _focusable = !includeCombatPiles;

        _deck = AddCell(PileTooltip("Deck", "hd_pile_deck"),
            PixelSpec.Ramp.G1, PixelSpec.Ramp.G2, PixelSpec.Ramp.G4,
            () => DeckViewButtons.OpenDeck(screen));

        // The ! on Instance here is safe for the same reason it is in
        // DeckViewButtons: includeCombatPiles is only ever true on
        // CombatScreen, whose own CombatManager child outlives these cells.
        if (includeCombatPiles)
        {
            _draw = AddCell(PileTooltip("Draw pile", "hd_pile_draw"),
                PixelSpec.Ramp.B1, PixelSpec.Ramp.B2, PixelSpec.Ramp.B4,
                () => DeckViewButtons.OpenPile(screen, "Draw Pile", CombatManager.Instance!.Player.Piles.DrawPile));
            _discard = AddCell(PileTooltip("Discard pile", "hd_pile_discard"),
                PixelSpec.Ramp.N3, PixelSpec.Ramp.N5, PixelSpec.Ramp.N7,
                () => DeckViewButtons.OpenPile(screen, "Discard Pile", CombatManager.Instance!.Player.Piles.Discard));
            // Burnt sienna (E2) rather than the brighter E3 the ember family
            // usually leads with: E3 beside the Deck cell's G4 gold is the one
            // pair on this strip that does not separate at 16px. Reading dim
            // and scorched is also just correct for a pile of burned cards.
            _exhaust = AddCell(PileTooltip("Exhaust pile", "hd_pile_exhaust"),
                PixelSpec.Ramp.E0, PixelSpec.Ramp.E1, PixelSpec.Ramp.E2,
                () => DeckViewButtons.OpenPile(screen, "Exhaust Pile", CombatManager.Instance!.Player.Piles.Exhaust));
        }
    }

    // The hotkey half comes out of the InputMap rather than being spelled into
    // the string, so rebinding hd_pile_* can't leave four cells advertising
    // keys that no longer do anything.
    private static string PileTooltip(string name, StringName action)
    {
        var key = ScreenKeyboardNav.KeyHint(action);
        return key.Length > 0 ? $"{name}  ({key})" : name;
    }

    // Exact, not GetCombinedMinimumSize(): every cell is a fixed pixel size by
    // design (the strip has a hard width budget, see CellWidth), so the caller
    // can position it before layout has run a frame.
    public Vector2 StripSize => new(CellWidth * _cells.Count, CellHeight);

    // Where a card flying to/from a pile should land. Read as global positions
    // after layout, which is always the case by the time cards are drawn or
    // discarded. These replace the three hardcoded *PileAnchor Control nodes
    // CombatScreen.tscn used to carry at the top *left* - the anchors have to
    // point at wherever the counters actually are, and hardcoding that twice
    // is how they drift apart.
    public Vector2 DrawCenter => (_draw ?? _deck).Center;
    public Vector2 DiscardCenter => (_discard ?? _deck).Center;
    public Vector2 ExhaustCenter => (_exhaust ?? _deck).Center;

    // Seeded here as well as polled below so the strip is correct on the frame
    // it is built rather than on the next one - otherwise every screen opens
    // with four zeroes for a frame, and a test that checks the counts without
    // first awaiting a ProcessFrame reads them as 0.
    public override void _Ready() => RefreshCounts();

    // Polled rather than pushed. The counts have several independent writers
    // (combat draw/discard/exhaust, plus RewardScreen and ShopScreen adding
    // cards to RunState.Deck outside combat), and a missed call site shows as
    // a silently stale number rather than a crash - the failure mode that made
    // the old duplicated label worth deleting in the first place. Each cell
    // compares against its last value before touching Label.Text, so a steady
    // state costs four List.Count reads and four int compares per frame.
    public override void _Process(double delta) => RefreshCounts();

    public void RefreshCounts()
    {
        // Outside combat there are no piles - the deck is just RunState.Deck.
        // CombatManager.Instance is set in its own _Ready but Player only in
        // StartCombat, and CombatScreen attaches this strip between the two,
        // so "a manager exists" is not the same as "there are piles to count".
        var combat = CombatManager.Instance;
        if (combat?.Player is null)
        {
            _deck.SetCount(RunState.Deck.Count);
            return;
        }

        var piles = combat.Player.Piles;
        // Mid-combat the "deck" is everything rotating through this fight, the
        // same definition DeckViewButtons.OpenDeck's popup uses - so the count
        // on the cell always matches the number in the popup's title.
        _deck.SetCount(piles.DrawPile.Count + piles.Hand.Count + piles.Discard.Count + piles.Exhaust.Count);
        _draw?.SetCount(piles.DrawPile.Count);
        _discard?.SetCount(piles.Discard.Count);
        _exhaust?.SetCount(piles.Exhaust.Count);
    }

    private PileCounterCell AddCell(string tooltip, Color dark, Color mid, Color light, Action onPressed)
    {
        var cell = new PileCounterCell(tooltip, dark, mid, light, _focusable);
        cell.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        cell.Pressed += onPressed;
        AddChild(cell);
        _cells.Add(cell);
        return cell;
    }
}

// One cell of the strip. A Button (so it keeps the theme's bezel and its
// hover/pressed states for free) with the glyph and count as children rather
// than as Button.Icon/Button.Text - Button stacks its own icon and text
// horizontally, which does not fit 40px.
public partial class PileCounterCell : Button
{
    private const int GlyphGrid = 16;

    private readonly Label _count;
    private int _lastValue = -1;

    public PileCounterCell(string tooltip, Color dark, Color mid, Color light, bool focusable)
    {
        CustomMinimumSize = new Vector2(PileCounterBar.CellWidth, PileCounterBar.CellHeight);
        TooltipText = tooltip;
        // In combat, same reasoning as EnemyView/PotionView: keep these out of
        // Godot's automatic arrow-key focus navigation, which would otherwise
        // compete with CombatScreen's own arrow-key card/target cycling. On
        // every other screen there is no such competition and the cells should
        // be reachable like any other button - see PileCounterBar's ctor.
        FocusMode = focusable ? FocusModeEnum.All : FocusModeEnum.None;

        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", 0);
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(column);

        var glyph = new TextureRect
        {
            Texture = BuildStackGlyph(dark, mid, light),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        PixelSpec.ApplyPixelRect(glyph, GlyphGrid, PixelSpec.HudIconScale);
        column.AddChild(glyph);

        _count = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // The display face, not the body face: these are pure numbers, and
        // Silkscreen's unambiguous digits are the entire reason it was chosen
        // over Pixelify Sans (docs/ART_SPEC.md section 7). Not the
        // CombatDisplayLabel variation, whose 24px + 4px outline does not fit
        // a 40px cell alongside the glyph.
        _count.AddThemeFontOverride("font", GD.Load<Font>(UiTheme.Fonts.DisplayPath));
        _count.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Body);
        _count.AddThemeColorOverride("font_color", PixelSpec.Ramp.N8);
        _count.AddThemeColorOverride("font_outline_color", PixelSpec.Ramp.N0);
        _count.AddThemeConstantOverride("outline_size", 3);
        column.AddChild(_count);
    }

    public Vector2 Center => GlobalPosition + Size / 2f;

    // What the cell actually renders, not the int behind it - DeckViewSmokeTest
    // asserts on this so a counter that stops being refreshed is caught.
    public string CountText => _count.Text;

    public void SetCount(int value)
    {
        if (value == _lastValue) return;
        _lastValue = value;
        _count.Text = value.ToString();
    }

    // Three overlapping cards read as a stack at a glance. Authored on a
    // 16x16 grid and drawn at 1x per PixelSpec, and built from three explicit
    // ramp entries rather than one colour darkened arithmetically - a
    // multiplied colour lands off-ramp, which is exactly what docs/ART_SPEC.md
    // section 5 exists to prevent.
    //
    // Each card is a 9x9 face inside an 11x11 N0 ring. Without the ring the
    // three faces butt straight up against each other and the glyph renders as
    // one solid 16px block of colour at this size - the outline is what makes
    // the stacking read, which is the same reason ROADMAP Phase 3 lists "batch
    // outline sprites so they separate from dark backdrops" as an artgen job.
    private static Texture2D BuildStackGlyph(Color dark, Color mid, Color light)
    {
        var image = Image.CreateEmpty(GlyphGrid, GlyphGrid, false, Image.Format.Rgba8);
        DrawCard(image, 5, 0, dark);  // back
        DrawCard(image, 2, 3, mid);
        DrawCard(image, 0, 5, light); // front, down-left of the ones behind it
        return ImageTexture.CreateFromImage(image);
    }

    private static void DrawCard(Image image, int x, int y, Color face)
    {
        image.FillRect(new Rect2I(x, y, 11, 11), PixelSpec.Ramp.N0);
        image.FillRect(new Rect2I(x + 1, y + 1, 9, 9), face);
    }
}
