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

    // Derived from the fixed 224x308 card layout (VBox 8px inset, 6px
    // separation x2 gaps, NameBanner 32px, ArtWindow 84px, DescriptionPanel's
    // own 4px/2px content margins) rather than read live from
    // DescriptionPanel.Size - SetCardInstance runs synchronously right after
    // AddChild, before the VBoxContainer's deferred sort pass has actually
    // sized DescriptionPanel, so a live read would measure a stale/zero box
    // on a card's first render. Same fixed-constant approach CombatScreen's
    // FanSafeWidth and PileViewPopup's EntryContentWidth already use to
    // avoid this class of Container-timing bug.
    private static readonly Vector2 DescriptionBoxSize = new(200, 160);
    private static readonly int[] DescriptionFontSizes = { 15, 14, 13, 12, 11 };

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
        _nameLabel.ThemeTypeVariation = "CombatDisplayLabel";

        var inset = ChromeStyles.InsetPanelStyle();
        _nameBanner.AddThemeStyleboxOverride("panel", inset);
        _artWindow.AddThemeStyleboxOverride("panel", inset);
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

        bool affordable = CombatManager.Instance?.Player is not { } player || player.CurrentEnergy >= def.Cost;
        Modulate = affordable ? Colors.White : UnaffordableTint;

        AddThemeStyleboxOverride("panel", ChromeStyles.CardFrameStyle(def.Type, def.Rarity, hovered: false, CardUpgrade.IsUpgraded(def)));

        // Live player context (Strength/Weak) so the shown damage number is
        // always what would actually land, not stale hand-authored prose.
        string plain = EffectDescriptionFormatter.Describe(def.Effects, CombatManager.Instance?.Player);
        SetDescriptionText(plain);

        // Recomputed (not appended to) each call - a card view is reused in
        // place across refreshes (see CombatScreen's _cardViews dict), so a
        // stale tooltip from whatever this view used to show must go too.
        HideKeywordTooltip();
        _activeKeywords = KeywordPattern.Matches(plain)
            .Select(m => m.Value)
            .Distinct()
            .Select(k => KeywordHighlights.All.First(h => h.Keyword == k))
            .Select(h => (h.Keyword, h.Color.ToHtml(false), h.Blurb))
            .ToList();
    }

    private void SetDescriptionText(string plain)
    {
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
        if (!fits) fitted = TruncateToFit(plain, font, fontSize);

        _descriptionLabel.Text = $"[center][font_size={fontSize}]{HighlightKeywords(fitted)}[/font_size][/center]";
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

    private static string HighlightKeywords(string plain)
    {
        return KeywordPattern.Replace(plain, match =>
        {
            var (keyword, color, _) = KeywordHighlights.All.First(k => k.Keyword == match.Value);
            return $"[color=#{color.ToHtml(false)}]{keyword}[/color]";
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
    }

    private void ClearTargetHighlight()
    {
        if (GodotObject.IsInstanceValid(_targetLockedView)) _targetLockedView!.SetTargetLocked(false);
        _targetLockedView = null;
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

        bool resolved = CombatManager.Instance.TryPlayCard(CardInstance, target);
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
