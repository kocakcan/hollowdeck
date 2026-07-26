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

file static class KeywordHighlights
{
    // Blurb + color per keyword that can appear in EffectDescriptionFormatter
    // output - kept CardView-local (not in EffectDescriptionFormatter itself)
    // since this is purely a presentation decoration; the formatter's plain-
    // text output stays the single source of truth other screens (Reward/
    // Shop/PileViewPopup) read unmodified.
    public static readonly (string Keyword, Color Color, string Blurb)[] All =
    {
        ("Vulnerable", UiTheme.Palette.StatusDebuff, "Takes more damage from attacks. Wears off by 1 each turn."),
        ("Weak", UiTheme.Palette.StatusDebuff, "Deals less damage with attacks. Wears off by 1 each turn."),
        ("Poison", UiTheme.Palette.StatusDebuff, "Loses HP each turn, ignoring Block, then drops by 1."),
        ("Strength", UiTheme.Palette.StatusBuff, "Attacks deal more damage."),
        ("Block", UiTheme.Palette.Block, "Reduces incoming damage this turn."),
    };
}

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
    // Body sizes the description may shrink to, largest first. Tightened from
    // the old {15,14,13,12,11} vector-face ladder: Jersey15 is a bitmap face
    // designed around 15px, so it stays clean near its design size but breaks
    // down into uneven stems well below it. Three steps in a narrow band
    // rather than five stepping down to 11 (see docs/ART_SPEC.md section 7).
    private static readonly int[] DescriptionFontSizes = { 16, 14, 12 };

    public CardInstance? CardInstance { get; private set; }

    // Combat hand cards drag-to-play (the default, and the only mode this
    // class supported before Phase 4). Reward/shop/deck-view screens set
    // this false to reuse the exact same frame/art/text rendering with a
    // plain click instead - no position tracking, no target highlighting,
    // no TryPlayCard. Interactive=true's _GuiInput branch is untouched from
    // before this flag existed, so combat behavior is byte-for-byte the same.
    public bool Interactive { get; set; } = true;
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
    private List<(string Keyword, string ColorHex, string Blurb)> _activeKeywords = new();
    private Control? _keywordTooltipPanel;

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
    }

    public void SetCardInstance(CardInstance card)
    {
        CardInstance = card;
        if (_nameLabel is null) return;

        var def = card.Definition;
        _nameLabel.Text = def.Name;
        _costLabel.Text = def.Cost.ToString();
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
        bool affordable = !Interactive
            || CombatManager.Instance?.Player is not { } player
            || player.CurrentEnergy >= def.Cost;
        Modulate = affordable ? Colors.White : UnaffordableTint;

        AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(def.Type, def.Rarity, hovered: false, CardUpgrade.IsUpgraded(def)));

        // A reused view (see CombatScreen's _cardViews dict) must not inherit
        // whatever enemy the card it *used* to show was being dragged onto.
        _liveTarget = null;
        var described = RefreshDescription();

        // Recomputed (not appended to) each call - a card view is reused in
        // place across refreshes, so a stale tooltip from whatever this view
        // used to show must go too.
        HideKeywordTooltip();
        _activeKeywords = KeywordPattern.Matches(described.Text)
            .Select(m => m.Value)
            .Distinct()
            .Select(k => KeywordHighlights.All.First(h => h.Keyword == k))
            .Select(h => (h.Keyword, h.Color.ToHtml(false), h.Blurb))
            .ToList();
    }

    // The enemy this card would currently hit, when that's knowable: the one
    // being dragged/arrow-key-aimed at for a SingleEnemy card. AllEnemies
    // cards don't need it - they hit everyone, so BuildDescribeContext reads
    // the live enemy list directly.
    private EnemyCombatant? _liveTarget;

    // Re-renders the description for whichever enemy is being targeted right
    // now, so dragging onto a Vulnerable enemy shows the damage that will
    // actually land instead of the generic "(~N vs Vulnerable)" hint. Only
    // the text is rebuilt, not the keyword tooltip panel: the panel can be
    // open mid-drag and rebuilding it every time the drag crosses an enemy
    // would flicker it. It may therefore still list Vulnerable for a beat
    // after the hint text it came from was replaced by a real number.
    public void RefreshDescriptionForTarget(EnemyCombatant? target)
    {
        if (CardInstance is null || _descriptionLabel is null) return;
        if (ReferenceEquals(_liveTarget, target)) return;
        _liveTarget = target;
        RefreshDescription();
    }

    private DescribedEffects RefreshDescription()
    {
        var described = EffectDescriptionFormatter.DescribeDetailed(
            CardInstance!.Definition.Effects, BuildDescribeContext());
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

    // Single regex pass over the original plain text, rather than a
    // sequential string.Replace-per-keyword loop - that would re-scan its
    // own already-substituted output on every pass, so a later keyword
    // (e.g. "Block") could match text sitting inside an earlier keyword's
    // just-inserted markup (Poison's blurb literally contains the word
    // "Block"). Regex.Replace only ever matches against the original
    // input, so replacement text is never re-scanned. Also reused by
    // SetCardInstance to find which keywords this card's text mentions, for
    // the hover/selection tooltip panel below.
    private static readonly Regex KeywordPattern = new(
        string.Join("|", KeywordHighlights.All.Select(k => Regex.Escape(k.Keyword))));

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
            ? KeywordPattern
            : new Regex($"{KeywordPattern}|{string.Join("|", numbers.Select(n => $@"\b{n}\b"))}");

        return pattern.Replace(plain, match =>
        {
            var keyword = KeywordHighlights.All.FirstOrDefault(k => k.Keyword == match.Value);
            if (keyword.Keyword is not null)
            {
                return $"[color=#{keyword.Color.ToHtml(false)}]{keyword.Keyword}[/color]";
            }

            int value = int.Parse(match.Value);
            var color = described.Buffed.Contains(value) ? UiTheme.Palette.StatusBuff : UiTheme.Palette.StatusDebuff;
            return $"[color=#{color.ToHtml(false)}]{match.Value}[/color]";
        });
    }

    // Shows every keyword this card's text mentions as a small stacked
    // panel above the card - triggered from OnMouseEntered/OnMouseExited
    // below, so it responds identically to real mouse hover and to
    // CombatScreen's keyboard SetHighlighted (arrow-key card selection).
    // Deliberately not Godot's built-in per-BBCode-span tooltip system
    // ([hint=...]/_make_custom_tooltip) - that only ever fires from actual
    // OS mouse motion, which arrow-key selection has none of.
    private void ShowKeywordTooltip()
    {
        if (_activeKeywords.Count == 0 || _keywordTooltipPanel is not null) return;

        var container = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 500 };
        container.AddThemeConstantOverride("separation", 4);
        foreach (var (keyword, colorHex, blurb) in _activeKeywords)
        {
            container.AddChild(BuildKeywordBox(keyword, colorHex, blurb));
        }

        GetTree().CurrentScene.AddChild(container);
        _keywordTooltipPanel = container;

        // Rough placement now (container's real size isn't known until its
        // own layout pass runs); RepositionKeywordTooltip corrects it one
        // idle frame later once Size reflects actual content.
        container.GlobalPosition = GlobalPosition + new Vector2(0, -40 * _activeKeywords.Count - 8);
        CallDeferred(nameof(RepositionKeywordTooltip));
    }

    private void RepositionKeywordTooltip()
    {
        if (_keywordTooltipPanel is null || !IsInstanceValid(_keywordTooltipPanel)) return;
        _keywordTooltipPanel.GlobalPosition = GlobalPosition + new Vector2(0, -_keywordTooltipPanel.Size.Y - 8);
    }

    private void HideKeywordTooltip()
    {
        if (_keywordTooltipPanel is null) return;
        if (IsInstanceValid(_keywordTooltipPanel)) _keywordTooltipPanel.QueueFree();
        _keywordTooltipPanel = null;
    }

    // One small dark bronze-bordered box per keyword: a colored title line
    // (matching that keyword's inline highlight color in the card text)
    // plus its description below - stacked by ShowKeywordTooltip when a
    // card mentions more than one keyword, same as the reference layout.
    private const float KeywordBoxWidth = 200f;

    private static Control BuildKeywordBox(string keyword, string colorHex, string blurb)
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
            Text = keyword,
            CustomMinimumSize = new Vector2(KeywordBoxWidth, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", Color.FromHtml(colorHex));
        title.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(title);

        var body = new Label
        {
            Text = blurb,
            CustomMinimumSize = new Vector2(KeywordBoxWidth, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        body.AddThemeFontSizeOverride("font_size", 13);
        body.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        vbox.AddChild(body);

        return panel;
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
    public void SetHighlighted(bool on)
    {
        if (on) OnMouseEntered();
        else OnMouseExited();
    }

    // Only combat hand cards get a hotkey badge (CombatScreen.RefreshHand
    // calls this per card); Shop/Reward/PileViewPopup cards never call it,
    // so HotkeyBadge stays hidden there.
    public void SetHotkeyNumber(int? number)
    {
        _hotkeyBadge.Visible = number is not null;
        if (number is { } n) _hotkeyLabel.Text = n.ToString();
    }

    private void OnMouseEntered()
    {
        if (_dragging || !Interactive) return;
        ZIndex = 100;
        var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine);
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", HoverScale, 0.12);
        tween.TweenProperty(this, "rotation_degrees", 0f, 0.12); // "stands up straight"
        if (CardInstance is not null) AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(CardInstance.Definition.Type, CardInstance.Definition.Rarity, hovered: true, CardUpgrade.IsUpgraded(CardInstance.Definition)));
        ShowKeywordTooltip();
    }

    private void OnMouseExited()
    {
        if (_dragging || !Interactive) return;
        ZIndex = _restZIndex;
        var tween = GetTree().CreateTween().SetTrans(Tween.TransitionType.Sine);
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", NormalScale, 0.12);
        tween.TweenProperty(this, "rotation_degrees", _homeRotation, 0.12);
        if (CardInstance is not null) AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(CardInstance.Definition.Type, CardInstance.Definition.Rarity, hovered: false, CardUpgrade.IsUpgraded(CardInstance.Definition)));
        HideKeywordTooltip();
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
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
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

        bool resolved = CombatManager.Instance!.TryPlayCard(CardInstance, target);
        if (resolved)
        {
            AudioManager.Instance?.PlaySfx("card_play");
            PlayResolveTween();
        }
        else
        {
            screenRoot.RemoveChild(this);
            handArea.AddChild(this);
            Position = localPosition;
            SnapHome();
        }
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
            if (enemyView.GetGlobalRect().HasPoint(mousePos)) return enemyView;
        }
        return null;
    }
}
