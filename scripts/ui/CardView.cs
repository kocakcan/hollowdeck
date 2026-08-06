using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Hover (scale tween) + manual drag on a Control-based card. Home position is
// tracked and laid out manually by whoever spawns this (CombatScreen), rather
// than via a real Container - that sidesteps the Container-vs-manual-Position
// conflict a real HBoxContainer would cause during drag (the seam Phase 0's
// prototype flagged), at the cost of the spawner doing its own row layout.
//
// On release: for a SingleEnemy card, hit-test EnemyView.Instances against
// the drop position and target whichever enemy (if any) the card was
// dropped on - same drag-to-target feel as the genre reference. Dropped
// with no enemy under it, or rejected by CombatManager (wrong state, not
// enough energy), it snaps back to home. If it resolved, it plays a quick
// resolve tween instead of just vanishing - see OnReleased for why it has
// to reparent itself out of the hand area first.
public partial class CardView : Panel
{
    private static readonly Vector2 HoverScale = new(1.15f, 1.15f);
    private static readonly Vector2 NormalScale = Vector2.One;
    private static readonly Color UnaffordableTint = new(0.55f, 0.55f, 0.55f);

    // Derived from the fixed 176x240 card layout (VBox 8px inset, 6px
    // separation x2 gaps, NameBanner 24px, ArtWindow 96px, DescriptionPanel's
    // own 4px/2px content margins) rather than read live from
    // DescriptionPanel.Size - SetCardInstance runs synchronously right after
    // AddChild, before the VBoxContainer's deferred sort pass has actually
    // sized DescriptionPanel, so a live read would measure a stale/zero box
    // on a card's first render. Same fixed-constant approach CombatScreen's
    // FanSafeWidth and PileViewPopup's EntryContentWidth already use to
    // avoid this class of Container-timing bug.
    //
    // Width:  176 - 8*2 inset - 4*2 panel margin = 152
    // Height: 240 - 8*2 inset - 24 banner - 96 art - 6*2 separation - 2*2 = 88
    private static readonly Vector2 DescriptionBoxSize = new(152, 88);

    // Matches the CostBadge rect in CardView.tscn. Used to pad the name
    // banner clear of the badge that overlaps it.
    private const int CostBadgeWidth = 30;
    // Body sizes the description may shrink to, largest first - now a single
    // entry, because on Tiny5 (8px design em) the only sizes below 16 that
    // don't resample are 8, which is unreadable.
    //
    // The ladder existed to rescue long cards, and it no longer has to: every
    // card in the data, base and upgraded, fits this box at 16. The worst is
    // Thunderclap+ at 4 lines x 18px = 72px against 88px of room, measured by
    // HandLayoutSmokeTest's TestEveryCardDescriptionFitsWithoutTruncation -
    // so if new content ever does overflow, that test fails rather than the
    // card silently rendering at an off-grid size. Fix it by shortening the
    // text or enlarging the box, not by adding a step back here.
    private static readonly int[] DescriptionFontSizes = { 16 };

    public CardInstance? CardInstance { get; private set; }

    // Combat hand cards drag-to-play (the default, and the only mode this
    // class supported before Phase 4). Reward/shop/deck-view screens set
    // this false to reuse the exact same frame/art/text rendering with a
    // plain click instead - no position tracking, no target highlighting,
    // no TryPlayCard. Interactive=true's _GuiInput branch is untouched from
    // before this flag existed, so combat behavior is byte-for-byte the same.
    //
    // It also decides whether the card joins Godot's focus navigation. A
    // non-interactive card is a click-to-choose control on a screen that is
    // navigated by focus (Reward, Shop, the pile popup), so it has to be
    // focusable or the reward pick is mouse-only. A combat hand card must not
    // be: CombatScreen drives the hand with its own arrow keys, and focus
    // navigation would fight it - the same reason EnemyView and PotionView
    // sit at FocusModeEnum.None. The backing field's initializer deliberately
    // does not run this setter, so an untouched CardView keeps Panel's
    // FocusModeEnum.None default.
    private bool _interactive = true;
    public bool Interactive
    {
        get => _interactive;
        set
        {
            _interactive = value;
            FocusMode = value ? FocusModeEnum.None : FocusModeEnum.All;
        }
    }

    public event Action<CardInstance>? Clicked;

    private Label _nameLabel = null!;
    private TextureRect _artIcon = null!;
    private TextureRect _artIconShadow = null!;
    private RichTextLabel _descriptionLabel = null!;
    private PanelContainer _nameBanner = null!;
    private Panel _artWindow = null!;
    private PanelContainer _descriptionPanel = null!;
    private PanelContainer _costBadge = null!;
    private Label _costLabel = null!;
    private PanelContainer _exhaustBadge = null!;
    private PanelContainer _hotkeyBadge = null!;
    private Label _hotkeyLabel = null!;
    private bool _dragging;
    // Set once the card has resolved and is playing its exit flourish - see
    // TryPlayFromHand.
    private bool _leavingHand;
    private Vector2 _homePosition;
    private float _homeRotation;
    private int _restZIndex;
    private EnemyView? _targetLockedView;

    // Keywords (Block/Vulnerable/...) this card's own description text
    // mentions, resolved once in SetCardInstance rather than re-scanned on
    // every hover - shown/hidden together as one stacked panel, driven by
    // whichever of OnMouseEntered/OnMouseExited last fired, whether that
    // came from real mouse hover or CombatScreen's SetHighlighted (arrow-key
    // selection) - one code path covers both input methods identically.
    private List<Keywords.Entry> _activeKeywords = new();
    private HoverTooltip? _keywordTooltip;

    // The two independent reasons the panel is up, tracked apart because
    // either can end while the other holds. A single "is it showing" flag
    // could not, and the workaround for that - hiding on mouse-exit only when
    // the card did not also have focus - stranded the panel permanently on the
    // one card of every choice screen that ScreenKeyboardNav focuses on load:
    // the mouse could raise it, and then nothing was left that would take it
    // down. _focusShowing is focus *minus* that quiet grab, which is why the
    // screen still opens silent.
    private bool _hovered;
    private bool _focusShowing;

    public override void _Ready()
    {
        PivotOffset = Size / 2f;
        _homePosition = Position;
        _nameBanner = GetNode<PanelContainer>("VBox/NameBanner");
        _nameLabel = GetNode<Label>("VBox/NameBanner/NameLabel");
        _artWindow = GetNode<Panel>("VBox/ArtWindow");
        _artIconShadow = GetNode<TextureRect>("VBox/ArtWindow/ArtIconShadow");
        _artIcon = GetNode<TextureRect>("VBox/ArtWindow/ArtIcon");
        _descriptionPanel = GetNode<PanelContainer>("VBox/DescriptionPanel");
        _descriptionLabel = GetNode<RichTextLabel>("VBox/DescriptionPanel/DescriptionLabel");
        _costBadge = GetNode<PanelContainer>("CostBadge");
        _costLabel = GetNode<Label>("CostBadge/CostLabel");
        _exhaustBadge = GetNode<PanelContainer>("ExhaustBadge");
        _hotkeyBadge = GetNode<PanelContainer>("HotkeyBadge");
        _hotkeyLabel = GetNode<Label>("HotkeyBadge/HotkeyLabel");
        // CardTitleLabel, not CombatDisplayLabel: same Silkscreen face but
        // 16px. The display variation's 24px was sized for a serif face and
        // clips card names now that Silkscreen is much wider ("TWIN STRIKE"
        // rendered as "WIN STRIKE").
        _nameLabel.ThemeTypeVariation = "CardTitleLabel";

        var inset = ChromeStyles.InsetPanelStyle();
        _artWindow.AddThemeStyleboxOverride("panel", inset);

        // The name banner gets its own inset with extra left padding. The cost
        // badge is anchored to the card's top-left corner and paints over the
        // banner, so a centred name had its first character hidden underneath
        // it ("TWIN STRIKE" read as "WIN STRIKE"). Padding the banner's
        // content past the badge re-centres the name in the space actually
        // visible, rather than in the space nominally available.
        var nameInset = ChromeStyles.InsetPanelStyle();
        nameInset.ContentMarginLeft = CostBadgeWidth;
        _nameBanner.AddThemeStyleboxOverride("panel", nameInset);
        _descriptionPanel.AddThemeStyleboxOverride("panel", ChromeStyles.InsetPanelStyle());
        _costBadge.AddThemeStyleboxOverride("panel", ChromeStyles.BadgeStyle(UiTheme.Palette.AccentGoldBright, UiTheme.Palette.AccentGold));
        _costLabel.AddThemeColorOverride("font_color", UiTheme.Palette.BgPanel);
        _exhaustBadge.AddThemeStyleboxOverride("panel", ChromeStyles.BadgeStyle(UiTheme.Palette.BgPanel, UiTheme.Palette.ExhaustAccent));
        _hotkeyBadge.AddThemeStyleboxOverride("panel", ChromeStyles.HotkeyBadgeStyle());

        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
        FocusEntered += OnFocusEntered;
        FocusExited += OnFocusExited;
    }

    // How far the focus glow bleeds past the card's own edge. Border colour
    // alone is not enough here: a Rare card's *resting* border is already
    // RarityRareGlow, which is the same top-of-ramp gold FocusRing is, so on a
    // reward screen offering a Rare the focused card and the Rare card looked
    // identical. A halo is the channel nothing else on these cards uses.
    //
    // This was a 1.08x scale tween, which is the wrong channel for three
    // reasons that all showed at once on the rest site's upgrade grid. It is a
    // fractional scale over pixel art, so the 32px icon drawn at CardArtScale 3
    // became 103.68px and the 16px bitmap text became 17.28 - both resampled,
    // which is precisely what PixelSpec exists to forbid. It grew the card
    // outside its 176x240 grid cell, over its own Upgrade button (12px of
    // separation against 19.2px of growth) and into its neighbours. And the
    // ScrollContainer that grid lives in clipped whatever grew past the
    // viewport edge. A StyleBoxFlat shadow draws outside the box for free, so
    // the card itself stays at exactly 1.0 and every pixel stays on the grid.
    private const int FocusHaloSize = 8;

    // Non-interactive cards get no hover visual at all (OnMouseEntered bails
    // on !Interactive), so keyboard focus needs its own or a focused reward
    // card is indistinguishable from the two beside it.
    private void OnFocusEntered()
    {
        ApplyFocusFrame(true);
        // The halo always moves with focus - it is how the player finds the
        // keyboard on a screen full of cards. The keyword panel does not,
        // because focus arriving is not the same event as the player looking:
        // every choice screen grabs focus for its first card on load
        // (ScreenKeyboardNav), and that used to open a panel over the layout
        // before the player had touched anything.
        _focusShowing = !ScreenKeyboardNav.GrantingFocus;
        RefreshKeywordTooltip();
    }

    private void OnFocusExited()
    {
        ApplyFocusFrame(false);
        _focusShowing = false;
        RefreshKeywordTooltip();
    }

    private void ApplyFocusFrame(bool focused)
    {
        if (Interactive || CardInstance is null) return;

        var def = CardInstance.Definition;
        var style = ChromeStyles.CardFrameStyle(def.Type, def.Rarity, focused, CardUpgrade.IsUpgraded(def));
        if (focused)
        {
            style.BorderColor = UiTheme.Palette.FocusRing;
            style.ShadowColor = new Color(UiTheme.Palette.FocusRing, 0.55f);
            style.ShadowSize = FocusHaloSize;
            style.ShadowOffset = Vector2.Zero; // a halo, not a drop shadow
        }
        AddThemeStyleboxOverride("panel", style);

        // Paint over the neighbours the halo now overlaps. No tween and no
        // ReduceMotion gate needed: this is a static repaint, not motion -
        // which also retires the bug where arrow-keying faster than
        // UiTheme.Motion.Fast left two live tweens fighting over "scale".
        ZIndex = focused ? 10 : 0;
    }

    public void SetCardInstance(CardInstance card)
    {
        CardInstance = card;
        if (_nameLabel is null) return;

        var def = card.Definition;
        _nameLabel.Text = def.Name;
        // An X card's badge shows the letter, not the -1 sentinel. An
        // unplayable card has no badge at all: a Curse is authored cost 0, and
        // a 0 in the energy badge reads as "free to play", which is the one
        // thing it is not.
        _costBadge.Visible = def.IsPlayable;
        _costLabel.Text = def.IsXCost ? "X" : def.Cost.ToString();
        _exhaustBadge.Visible = def.Exhaust;

        var icon = ArtAssets.CardIcon(def.Id);
        _artIcon.Texture = icon;
        _artIconShadow.Texture = icon;

        // Only a real hand card can be "too expensive to play right now" -
        // a card sitting in a shop, a reward pick or a pile-view popup has
        // no energy cost context at all, so it must never dim. Interactive
        // is already the flag separating those two worlds, and gating on it
        // keeps the tint correct even if the CombatManager.Instance lifetime
        // regresses again.
        // An unplayable card is dimmed at every energy level, which is the only
        // signal the frame has that TryPlayCard will refuse it. An X card is
        // affordable whenever there is any energy at all - it spends whatever
        // is left, and TryPlayCard refuses it at zero.
        bool affordable = !Interactive
            || (def.IsPlayable
                && (CombatManager.Instance?.Player is not { } player
                    || (def.IsXCost ? player.CurrentEnergy >= 1 : player.CurrentEnergy >= def.Cost)));
        Modulate = affordable ? Colors.White : UnaffordableTint;

        AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(def.Type, def.Rarity, hovered: false, CardUpgrade.IsUpgraded(def)));
        // Re-painting the frame above would otherwise drop the focus ring off
        // a card that is currently focused (PileViewPopup re-sorts in place).
        if (HasFocus()) ApplyFocusFrame(true);

        // A reused view (see CombatScreen's _cardViews dict) must not inherit
        // whatever enemy the card it *used* to show was being dragged onto.
        _liveTarget = null;
        var described = RefreshDescription();

        // Recomputed (not appended to) each call - a card view is reused in
        // place across refreshes, so a stale tooltip from whatever this view
        // used to show must go too.
        HideKeywordTooltip();
        _activeKeywords = Keywords.Find(described.Text);
    }

    // The enemy this card would currently hit, when that's knowable: the one
    // being dragged/arrow-key-aimed at for a SingleEnemy card. AllEnemies
    // cards don't need it - they hit everyone, so BuildDescribeContext reads
    // the live enemy list directly.
    private EnemyCombatant? _liveTarget;

    // Re-renders the description for whichever enemy is being targeted right
    // now, so dragging onto a Vulnerable enemy shows the damage that will
    // actually land rather than the base number an un-aimed card carries. Only
    // the text is rebuilt, not the keyword tooltip panel: the panel can be
    // open mid-drag and rebuilding it every time the drag crosses an enemy
    // would flicker it.
    public void RefreshDescriptionForTarget(EnemyCombatant? target)
    {
        if (CardInstance is null || _descriptionLabel is null) return;
        if (ReferenceEquals(_liveTarget, target)) return;
        _liveTarget = target;
        RefreshDescription();
    }

    private DescribedEffects RefreshDescription()
    {
        // DescribeCard, not DescribeDetailed: the keyword sentence (Retain,
        // Innate, Ethereal, Unplayable) is a fact about the card rather than
        // about its effects, and this is the one renderer that has the whole
        // CardDefinition. It is also what feeds Keywords.Find below, so the
        // three new keywords get their hover blurbs from the same text scan
        // that already explains Block and Exhaust.
        var described = EffectDescriptionFormatter.DescribeCard(
            CardInstance!.Definition, BuildDescribeContext());
        SetDescriptionText(described);
        return described;
    }

    // Live player context (Strength/Weak) so the shown damage number is
    // always what would actually land, plus the card's own target type (so
    // an AllEnemies card says so) and who it would hit (so Vulnerable is
    // resolved for real). Only an Interactive card is in a combat where
    // "who would this hit" means anything - a shop/reward/deck-view card
    // shows base numbers, same reasoning as the affordability tint above.
    private DescribeContext BuildDescribeContext()
    {
        var def = CardInstance!.Definition;
        var combat = CombatManager.Instance;
        if (!Interactive || combat is null) return new DescribeContext(TargetType: def.Target);

        IReadOnlyList<Combatant>? targets = def.Target switch
        {
            CardTargetType.AllEnemies => combat.Enemies.Cast<Combatant>().ToList(),
            CardTargetType.SingleEnemy when _liveTarget is not null => new List<Combatant> { _liveTarget },
            _ => null,
        };
        return new DescribeContext(combat.Player, def.Target, targets);
    }

    private void SetDescriptionText(DescribedEffects described)
    {
        string plain = described.Text;
        var font = _descriptionLabel.GetThemeFont("normal_font") ?? ThemeDB.FallbackFont;
        int fontSize = DescriptionFontSizes[^1];
        string fitted = plain;
        bool fits = false;
        foreach (int size in DescriptionFontSizes)
        {
            if (font.GetMultilineStringSize(plain, HorizontalAlignment.Center, DescriptionBoxSize.X, size).Y > DescriptionBoxSize.Y) continue;
            fontSize = size;
            fits = true;
            break;
        }
        // A truncated string has had characters cut out of it, so the number
        // positions the modifier highlight would key off may no longer be
        // there - fall back to keyword-only highlighting rather than risk
        // tinting the wrong digits.
        if (!fits)
        {
            fitted = TruncateToFit(plain, font, fontSize);
            described = new DescribedEffects(fitted, new List<int>(), new List<int>());
        }

        _descriptionLabel.Text = $"[center][font_size={fontSize}]{HighlightKeywords(fitted, described)}[/font_size][/center]";
    }

    private static string TruncateToFit(string text, Font font, int fontSize)
    {
        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = text[..len].TrimEnd() + "…";
            if (font.GetMultilineStringSize(candidate, HorizontalAlignment.Center, DescriptionBoxSize.X, fontSize).Y <= DescriptionBoxSize.Y)
            {
                return candidate;
            }
        }
        return "…";
    }

    // Modified damage numbers (Strength/Weak on the player, Vulnerable on the
    // enemy being targeted) are tinted so it's visible *that* the number
    // moved, not just what it moved to - the number alone gives the player no
    // way to tell 8 from a buffed 6. Folded into the same single Regex pass
    // as the keywords above rather than a second string.Replace over its
    // output, for the same no-re-scanning reason.
    //
    // The numbers come from EffectDescriptionFormatter (which did the math);
    // this only matches them as whole words. Known cosmetic limit: if a card
    // prints the same digits in an unmodified position too ("Deal 8 damage.
    // Gain 8 Block." with 6 damage buffed to 8), both get tinted.
    private static string HighlightKeywords(string plain, DescribedEffects described)
    {
        var numbers = described.Buffed.Concat(described.Weakened).Distinct().ToList();
        var pattern = numbers.Count == 0
            ? Keywords.Pattern
            : new Regex($"{Keywords.Pattern}|{string.Join("|", numbers.Select(n => $@"\b{n}\b"))}");

        return pattern.Replace(plain, match =>
        {
            var keyword = Keywords.All.FirstOrDefault(k => k.Keyword == match.Value);
            if (keyword.Keyword is not null)
            {
                return $"[color=#{keyword.Color.ToHtml(false)}]{keyword.Keyword}[/color]";
            }

            int value = int.Parse(match.Value);
            var color = described.Buffed.Contains(value) ? UiTheme.Palette.StatusBuff : UiTheme.Palette.StatusDebuff;
            return $"[color=#{color.ToHtml(false)}]{match.Value}[/color]";
        });
    }

    // The one place the panel's visibility is decided, so hover and focus can
    // arrive and leave in any order without either overruling the other. Every
    // caller sets one of the two flags and comes here; nothing calls Show or
    // Hide directly, which is what stops a second contradictory rule appearing
    // at one of the call sites the way the last one did.
    private void RefreshKeywordTooltip()
    {
        if (_hovered || _focusShowing) ShowKeywordTooltip();
        else HideKeywordTooltip();
    }

    // Shows every keyword this card's text mentions as a small stacked panel
    // beside the card. Driven from four places that all mean "the player is
    // looking at this card": real mouse hover (OnMouseEntered/Exited below),
    // keyboard focus on the choice screens (OnFocusEntered/Exited), and
    // CombatScreen's SetHighlighted for arrow-key selection in the hand, which
    // routes through the hover pair.
    //
    // The panel itself is HoverTooltip, shared with the enemy intent telegraph
    // so a card and an enemy explain Weak in identical furniture.
    private void ShowKeywordTooltip()
    {
        if (_activeKeywords.Count == 0 || _keywordTooltip is not null) return;
        _keywordTooltip = HoverTooltip.Show(this, _activeKeywords);
    }

    private void HideKeywordTooltip()
    {
        if (_keywordTooltip is null) return;
        if (IsInstanceValid(_keywordTooltip)) _keywordTooltip.Dismiss();
        _keywordTooltip = null;
    }

    // pos/rotationDeg are this card's resting slot in the fan (CombatScreen
    // computes the fan formula); zIndex is its paint order at rest so the
    // fan's overlap direction is consistent - hover/drag temporarily bump
    // above this, then restore it on release/exit.
    public void SetHomeTransform(Vector2 pos, float rotationDeg, int zIndex)
    {
        _homePosition = pos;
        _homeRotation = rotationDeg;
        _restZIndex = zIndex;
        if (_dragging) return;
        Position = pos;
        RotationDegrees = rotationDeg;
        ZIndex = zIndex;
    }

    // Drives the same hover visual (scale/rotation/border) from keyboard
    // card selection (CombatScreen's arrow-key nav), so a keyboard-selected
    // card reads identically to a mouse-hovered one with no separate visual
    // language to maintain.
    //
    // Plus one thing hover does not do: light the hotkey badge. This method is
    // only ever called by CombatScreen's keyboard selection - a real mouse
    // hover arrives on the mouse_entered signal instead - so the lit keycap is
    // what tells the two apart. They can be true of *different* cards at the
    // same time (rest the mouse anywhere while arrow-keying), and before this
    // both rendered identically, so the selection appeared to jump to whatever
    // the cursor happened to be over.
    public void SetHighlighted(bool on)
    {
        if (on) OnMouseEntered();
        else OnMouseExited();

        _hotkeyBadge.AddThemeStyleboxOverride("panel",
            on ? ChromeStyles.HotkeyBadgeSelectedStyle() : ChromeStyles.HotkeyBadgeStyle());
        if (on) _hotkeyLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N0);
        else _hotkeyLabel.RemoveThemeColorOverride("font_color");
    }

    // Only combat hand cards get a hotkey badge (CombatScreen.RefreshHand
    // calls this per card); Shop/Reward/PileViewPopup cards never call it,
    // so HotkeyBadge stays hidden there.
    public void SetHotkeyNumber(int? number)
    {
        _hotkeyBadge.Visible = number is not null;
        if (number is { } n) _hotkeyLabel.Text = n.ToString();
    }

    // The stand-up-and-grow visual is combat-hand-only (it is the "this is the
    // card you would play" language, and it fights the choice screens' own
    // layouts). The tooltip is not: the !Interactive guard used to sit above
    // ShowKeywordTooltip, and since Reward/Shop/CardPicker/PileViewPopup all
    // set Interactive = false, every one of them silently dropped the hover
    // explanation on the floor. Splitting the guard is the whole fix - the
    // mouse_entered signal was always firing, it was just discarded.
    private void OnMouseEntered()
    {
        if (_dragging || _leavingHand) return;
        _hovered = true;
        if (Interactive)
        {
            ZIndex = 100;
            var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine);
            tween.SetParallel(true);
            tween.TweenProperty(this, "scale", HoverScale, 0.12);
            tween.TweenProperty(this, "rotation_degrees", 0f, 0.12); // "stands up straight"
            if (CardInstance is not null) AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(CardInstance.Definition.Type, CardInstance.Definition.Rarity, hovered: true, CardUpgrade.IsUpgraded(CardInstance.Definition)));
        }
        RefreshKeywordTooltip();
    }

    private void OnMouseExited()
    {
        if (_dragging || _leavingHand) return;
        _hovered = false;
        if (Interactive)
        {
            ZIndex = _restZIndex;
            var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine);
            tween.SetParallel(true);
            tween.TweenProperty(this, "scale", NormalScale, 0.12);
            tween.TweenProperty(this, "rotation_degrees", _homeRotation, 0.12);
            if (CardInstance is not null) AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(CardInstance.Definition.Type, CardInstance.Definition.Rarity, hovered: false, CardUpgrade.IsUpgraded(CardInstance.Definition)));
        }
        // Not necessarily a hide: a card the player focused themselves keeps
        // its panel while the mouse wanders off it, which is what _focusShowing
        // says and RefreshKeywordTooltip honours.
        RefreshKeywordTooltip();
    }

    // Cards animate in from the draw pile when newly added to hand -
    // staggerDelaySec cascades a multi-card draw instead of popping all at
    // once. Target position/rotation are whatever SetHomeTransform already
    // set immediately before this is called.
    public void PlayDrawTween(Vector2 fromGlobalPos, float staggerDelaySec)
    {
        var toPos = _homePosition;
        var toRotation = _homeRotation;
        Scale = Vector2.One * 0.6f;
        Modulate = new Color(1, 1, 1, 0f);
        GlobalPosition = fromGlobalPos;
        RotationDegrees = toRotation + (GD.Randf() * 20f - 10f);

        var tween = GetTree().CreateTween();
        tween.TweenInterval(staggerDelaySec);
        tween.SetParallel(true);
        tween.SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "position", toPos, 0.28);
        tween.TweenProperty(this, "rotation_degrees", toRotation, 0.28);
        tween.TweenProperty(this, "scale", Vector2.One, 0.28);
        tween.TweenProperty(this, "modulate:a", 1f, 0.18).SetTrans(Tween.TransitionType.Sine);
    }

    // Discard/exhaust without being played (end-of-turn hand clear, or a
    // future exhaust-from-hand effect) - flies to the given pile anchor and
    // fades out. Exhaust gets a faster, brighter/hotter flourish so it reads
    // as distinct from an ordinary discard.
    public void PlayExitTween(Vector2 toGlobalPos, bool isExhaust)
    {
        var screenRoot = GetTree().CurrentScene;
        var globalPos = GlobalPosition;
        GetParent().RemoveChild(this);
        screenRoot.AddChild(this);
        GlobalPosition = globalPos;
        ZIndex = 50;

        float duration = isExhaust ? 0.18f : 0.26f;
        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "global_position", toGlobalPos, duration).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "scale", Vector2.One * (isExhaust ? 0.1f : 0.3f), duration).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", isExhaust ? new Color(1.6f, 1.3f, 0.7f, 0f) : new Color(1f, 1f, 1f, 0f), duration);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    private void SnapHome()
    {
        ZIndex = _restZIndex;
        var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Back);
        tween.SetParallel(true);
        tween.TweenProperty(this, "position", _homePosition, 0.2);
        tween.TweenProperty(this, "rotation_degrees", _homeRotation, 0.2);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Interactive)
        {
            // ui_accept rather than hd_confirm: this is a stock focused
            // control on a screen navigated by focus, so it should answer the
            // same key as every Button beside it. Godot routes key events to
            // the focused Control's _GuiInput, which is what makes this the
            // right place for it rather than an _UnhandledInput on the screen.
            bool released = @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
            if (released || @event.IsActionPressed("ui_accept"))
            {
                GetViewport().SetInputAsHandled();
                if (CardInstance is not null)
                {
                    AudioManager.Instance?.PlaySfx("ui_click");
                    Clicked?.Invoke(CardInstance);
                }
            }
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                ZIndex = mb.Pressed ? 200 : _restZIndex;
                if (mb.Pressed)
                {
                    RotationDegrees = 0f;
                }
                if (!mb.Pressed)
                {
                    OnReleased();
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _dragging:
                Position += motion.Relative;
                UpdateTargetHighlight();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // Continuously highlights whichever enemy is under the cursor while
    // dragging a SingleEnemy card - the drag-and-drop equivalent of the
    // hover glow potions already get via native Button hover (which can't
    // fire here since this card's own Panel occludes the enemy underneath).
    private void UpdateTargetHighlight()
    {
        EnemyView? underMouse = CardInstance?.Definition.Target == CardTargetType.SingleEnemy
            ? FindEnemyViewUnderMouse()
            : null;
        if (underMouse == _targetLockedView) return;
        if (GodotObject.IsInstanceValid(_targetLockedView)) _targetLockedView!.SetTargetLocked(false);
        underMouse?.SetTargetLocked(true);
        _targetLockedView = underMouse;
        // The glow says *which* enemy is locked; this says what the card will
        // actually do to it (their Vulnerable is now a known fact, not a
        // hypothetical). Same information EnemyView's intent number has always
        // shown from the enemy's side.
        RefreshDescriptionForTarget(underMouse?.Combatant);
    }

    private void ClearTargetHighlight()
    {
        if (GodotObject.IsInstanceValid(_targetLockedView)) _targetLockedView!.SetTargetLocked(false);
        _targetLockedView = null;
        RefreshDescriptionForTarget(null);
    }

    public override void _ExitTree()
    {
        ClearTargetHighlight();
        HideKeywordTooltip();
    }

    private void OnReleased()
    {
        ClearTargetHighlight();

        if (CardInstance is null || CombatManager.Instance is null)
        {
            SnapHome();
            return;
        }

        EnemyCombatant? target = null;
        if (CardInstance.Definition.Target == CardTargetType.SingleEnemy)
        {
            target = FindEnemyViewUnderMouse()?.Combatant;
        }

        TryPlayFromHand(target);
    }

    // The play half of OnReleased, shared with CombatScreen's keyboard path so
    // Space and a mouse drop are the same operation. They were not: the
    // keyboard called CombatManager.TryPlayCard directly, which left this node
    // parented under the hand area, so RefreshHand tore it down and flew it to
    // the discard counter with PlayExitTween - the *discarded* animation - and
    // a card played with the keyboard never got the resolve flourish a
    // dragged one did.
    //
    // Returns whether the card actually resolved.
    public bool TryPlayFromHand(EnemyCombatant? target)
    {
        if (CardInstance is null || CombatManager.Instance is null) return false;

        // Reparent out of the hand area BEFORE calling TryPlayCard: if it
        // resolves, TryPlayCard fires HandChanged synchronously, and
        // CombatScreen's rebuild only tears down whatever is still parented
        // under _handArea - moving out first is what lets the resolve tween
        // below actually get to play instead of the node being destroyed
        // out from under it in the same call.
        var handArea = GetParent();
        var screenRoot = GetTree().CurrentScene;
        var localPosition = Position;
        var globalPosition = GlobalPosition;
        handArea.RemoveChild(this);
        screenRoot.AddChild(this);
        GlobalPosition = globalPosition;

        if (CombatManager.Instance.TryPlayCard(CardInstance, target))
        {
            // Freezes the hover/selection visuals for the rest of this node's
            // life. The card is mid-flight now, and both a real mouse-exit
            // (it just teleported out from under the cursor) and
            // CombatScreen's SelectCard(null) would otherwise start a
            // competing scale tween against PlayResolveTween's.
            _leavingHand = true;
            AudioManager.Instance?.PlaySfx("card_play");
            PlayResolveTween();
            return true;
        }

        screenRoot.RemoveChild(this);
        handArea.AddChild(this);
        Position = localPosition;
        SnapHome();
        return false;
    }

    private void PlayResolveTween()
    {
        bool isExhaust = CardInstance?.Definition.Exhaust ?? false;
        var tween = GetTree().CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.One * (isExhaust ? 0.15f : 0.4f), 0.18).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", isExhaust ? new Color(1.6f, 1.3f, 0.7f, 0f) : new Color(1, 1, 1, 0f), 0.18);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    private EnemyView? FindEnemyViewUnderMouse()
    {
        var mousePos = GetGlobalMousePosition();
        foreach (var enemyView in EnemyView.Instances)
        {
            // A killed enemy keeps its slot in the row, and its rect, for the
            // 0.35s of PlayDeathTween. Without this check the corpse still
            // wins the hit test - it is first in Instances - so a drag over
            // it locked a target that was fading to invisible and appeared to
            // show no glow at all.
            if (enemyView.Combatant.IsDead) continue;
            if (enemyView.GetGlobalRect().HasPoint(mousePos)) return enemyView;
        }
        return null;
    }
}
