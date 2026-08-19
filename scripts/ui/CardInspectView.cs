using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Hold-to-inspect: the card the player is looking at, drawn at 2x over a scrim,
// for as long as the hold lasts.
//
// This is what replaced CardView's 1.15x hover bump. That bump was the last
// live instance in the hand of ART_SPEC section 2 - a fractional scale over the
// 32px icon at CardArtScale 3 and the 16px bitmap type - and it could not
// simply be deleted, because it was carrying a real affordance: it was the only
// way to read a card that the fan had half-covered. The lift and the halo that
// took its place say "this one, and it is clear of its neighbours"; this says
// what is actually printed on it.
//
// **Modelled on HoverTooltip, deliberately not on LibraryInspectPopup**, which
// is the closer-looking of the two. A peek must not steal focus, must not stop
// mouse input and must leave the player exactly where they were on release;
// that popup grabs focus, sets MouseFilter.Stop and saves/restores focus in
// _ExitTree, all correct for a modal and all wrong for a hold. The Ignore on
// every node in the peek - including the ones inside the loaded CardView.tscn,
// see IgnoreMouse - is load-bearing rather than tidy: anything here that
// swallowed mouse events would fire OnMouseExited on the hand card underneath
// and close the peek in the frame it opened.
//
// What it does share is CardView.AddScaledCard, which is where the spacer and the
// integer scale live.
public partial class CardInspectView : Control
{
    // Above the hand (100 hovered, 200 dragging) and above PileViewPopup's
    // 2000, below HoverTooltip's 2500 - which does not arise, because the
    // keyword panel is hidden while this is open (CardView.RefreshKeywordTooltip).
    private const int OverlayZIndex = 2200;

    public const int InspectScale = 2;

    // Lighter than the 0.75 the three modals use. Those are dismissed by an
    // explicit act and can afford to bury the screen; this one is released
    // rather than closed, and the fight underneath is the thing the player is
    // inspecting the card *about* - an enemy's telegraph has to stay readable
    // beside it.
    private const float ScrimAlpha = 0.55f;

    // The peek arrives from below by a whole number of source pixels at
    // CardArtScale, the same grid CardView.HoverLiftPx lands on. Section 9
    // permits a fast translation to interpolate between snapped endpoints, and
    // Pop's overshoot is the "small overshoot" its own entry in the vocabulary
    // describes - it is the one channel left on this screen that can carry one,
    // now that no pixel surface scales.
    private const int ArriveFromPx = 24;

    private static CardInspectView? _open;
    private CardView? _raiser;

    // One peek at a time, and the static is the whole guard. Two input paths
    // reach this - a mouse dwell and a held key - and they can be true of
    // different cards at once (rest the mouse anywhere while arrow-keying),
    // which is the same pair CardView.SetHighlighted's comment records.
    //
    // **Which is exactly why the raiser is tracked and told when it is
    // pre-empted.** CardView keeps an `_inspecting` flag meaning "a peek raised
    // by *this* card is open", and a Show that replaced one peek with another
    // silently made that false on the card it took the peek away from. The
    // consequence was not cosmetic: the old raiser still believed it owned a
    // peek, so its own mouse-exit called Dismiss and killed the *new* card's
    // peek while its key was still held - unrecoverable, since IsActionPressed
    // only fires on the press edge. It also left that card's keyword panel
    // suppressed with no peek to justify it.
    public static void Show(CardView raiser, CardInstance card)
    {
        var preempted = _open?._raiser;
        Dismiss();
        if (preempted is not null && GodotObject.IsInstanceValid(preempted) && preempted != raiser)
        {
            preempted.OnPeekPreempted();
        }

        var view = new CardInspectView { Name = "CardInspectView", _raiser = raiser };
        raiser.GetTree().CurrentScene.AddChild(view);
        view.Build(card);
        _open = view;
    }

    // Whether the open peek is the one this view raised. CardView asks before
    // dismissing, so a card that has already lost the peek cannot take away the
    // one that replaced it.
    public static bool RaisedBy(CardView view) =>
        _open is not null && GodotObject.IsInstanceValid(_open) && _open._raiser == view;

    public static void Dismiss()
    {
        if (_open is { } open)
        {
            if (GodotObject.IsInstanceValid(open)) open.QueueFree();
            _open = null;
        }
    }

    public static bool IsOpen => _open is not null && GodotObject.IsInstanceValid(_open);

    private void Build(CardInstance card)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = OverlayZIndex;
        MouseFilter = MouseFilterEnum.Ignore;

        var scrim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, ScrimAlpha),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(scrim);
        scrim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var spacer = CardView.AddScaledCard(this, card, InspectScale, showsLiveCombat: true, out _);
        spacer.MouseFilter = MouseFilterEnum.Ignore;

        // Centred by hand rather than by a CenterContainer, for the reason
        // AddScaledCard's own comment gives: a Container would re-sort and stomp
        // the card's Scale back to 1x. Rounded because the result is what a
        // bitmap face and a 6x icon are painted at.
        // GetViewportRect, not this node's own Size: the FullRect preset above
        // sets anchors and offsets, and Size does not catch up with them until
        // the layout pass after this frame - so centring against it here would
        // place the card against a zero-size box.
        var canvas = GetViewportRect().Size;
        var resting = ((canvas - spacer.CustomMinimumSize) / 2f).Round();
        spacer.Position = resting + new Vector2(0, ArriveFromPx);

        Modulate = new Color(1, 1, 1, 0f);
        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenTo(this, "modulate:a", 1f, Motion.Settle);
        tween.TweenTo(spacer, "position", resting, Motion.Pop);
    }

    // The peek dies with the screen it was parented to, and with the card view
    // that raised it - but the static has to be cleared either way or the next
    // Show would try to free a node that is already gone.
    public override void _ExitTree()
    {
        if (ReferenceEquals(_open, this)) _open = null;
    }
}
