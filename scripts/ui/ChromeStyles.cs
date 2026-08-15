using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.UI;

// Procedural chrome: HP-bar bezels, card frames, badges and the emphasis
// button treatment. Owns all StyleBox construction so it reads from one set
// of UiTheme tokens rather than duplicated color literals.
//
// Rebuilt for pixel art (docs/ART_SPEC.md section 6). Two things are now
// illegal here and were removed throughout:
//   - corner radii. StyleBoxFlat rounds corners by anti-aliasing them, which
//     cannot exist in pixels. Every box is hard-edged; UiTheme.Radius is gone.
//   - ShadowSize glows. Same problem - a blur has no pixel representation.
//     Emphasis is carried by border weight and ramp brightness instead.
//
// Half this file is StyleBoxTexture over generated 9-slice art and half is
// still StyleBoxFlat, and the split is a rule rather than an unfinished
// migration:
//
//   A box gets a 9-slice iff its colours are fixed at author time.
//
// A StyleBoxTexture has no BorderColor. Its only runtime colour knob is
// ModulateColor, which *multiplies* - so tinting a bronze frame to say
// "Uncommon" lands on a colour outside the section 5 ramp, which is the one
// thing this whole medium commitment exists to prevent. So PanelStyle,
// SlotStyle, PlinthStyle and the emphasis button are art (their colours are
// decided in tools/artgen/src/icons/chrome.rs), while CardFrameStyle - whose
// border is a rarity lerped with upgrade and again with hover - stays flat,
// along with the badges, the HP bars, the slider and the map-node boxes. A
// card frame wanting art would need one texture per CardType x Rarity x hover
// x upgraded, which is the per-card-class explosion risk 1 is about.
//
// The three "glow" surfaces section 6 names - a Rare CardFrameStyle,
// BossNodeGlowStyle and TargetLockStyle - are flat for a second reason on top
// of that one, and it is the reason they could never have been art. Each is now
// an animated ring (GlowRing, docs/PIXEL_ART_ROADMAP.md section 3), so its
// border colour is decided per *frame* rather than at author time, which is the
// exact condition the rule above disqualifies. All three therefore take that
// colour as a parameter and default it to the still they replace, which is what
// keeps them pure functions and every existing call site unchanged.
//
// TargetLockStyle lives here rather than in EnemyView, where it was a static
// readonly box shared by every view: a driver that mutated it would have driven
// all of them, and this file's own header says chrome has one home.
public static class ChromeStyles
{
    // The generated 9-slices, by filename under assets/theme/. This list is the
    // registry PixelSpecSmokeTest drives in *both* directions - every name here
    // must resolve to a texture, and every PNG in that directory must appear
    // here - so a slice cannot be generated and left unused, and a name cannot
    // be typo'd into an invisible panel.
    //
    // Consts rather than a hand-list at each call site for the reason
    // TestNoTweenTransformsAPixelSprite learned the hard way: a check that
    // knows only the names someone already thought of is green over the ones
    // they didn't.
    public static class Slices
    {
        public const string Panel = "panel";
        public const string PanelFocus = "panel_focus";
        public const string SlotFilled = "slot_filled";
        public const string SlotEmpty = "slot_empty";
        public const string Plinth = "plinth";
        public const string ButtonNormal = "button_normal";
        public const string ButtonHover = "button_hover";
        public const string ButtonPressed = "button_pressed";
        public const string ButtonDisabled = "button_disabled";
        public const string ButtonFocus = "button_focus";
        public const string EmphasisNormal = "emphasis_normal";
        public const string EmphasisHover = "emphasis_hover";
        public const string EmphasisPressed = "emphasis_pressed";
        public const string EmphasisDisabled = "emphasis_disabled";

        // Ordered as authored, and read by both the smoke test and anything
        // that wants to sweep the set. The theme resource names five of these
        // itself (the button states + panel) rather than going through this
        // class, which is why the smoke test also walks the .tres.
        public static readonly string[] All =
        {
            Panel, PanelFocus, SlotFilled, SlotEmpty, Plinth,
            ButtonNormal, ButtonHover, ButtonPressed, ButtonDisabled, ButtonFocus,
            EmphasisNormal, EmphasisHover, EmphasisPressed, EmphasisDisabled,
        };
    }

    // The corner budget, which is also the texture margin: ART_SPEC section 1
    // says a 9-slice's corners must be at most a third of the slice. 5 of 16
    // and 8 of 24. These are what SliceStyle sets and what
    // TestChromeCornersFitTheSlice checks against the texture's real size, so
    // an art change that grows the corner fails rather than clipping.
    public const int SmallCorner = 5;
    public const int LargeCorner = 8;

    // The one place a chrome PNG becomes a StyleBox.
    //
    // Two things here are correctness rather than taste:
    //
    //   - Tile, not Stretch and not TileFit. Stretch is Godot's default, and it
    //     resamples the edge strip to whatever fractional width the box happens
    //     to be - a non-integer scale, which section 2 calls a bug rather than a
    //     judgement call. TileFit rescales tiles to fit a whole number of them,
    //     which is the same violation reached from the other side. Tile repeats
    //     the strip at 1:1 and clips the last one, and the art is drawn so that
    //     every edge pixel is a function of one axis, so the seam is invisible.
    //
    //   - A missing texture falls back to `flat` rather than shipping an empty
    //     StyleBoxTexture, which draws nothing at all. That turns a mis-packed
    //     build from an interface with invisible panels into the interface that
    //     shipped before the art existed, plus a named error - the argument
    //     DataFile.cs makes for content JSON. The fallback is unreachable in a
    //     correct build, and TestEveryChromeSliceIsDrawnBySomething is what
    //     keeps it that way.
    private static StyleBox SliceStyle(string name, int corner, StyleBoxFlat flat)
    {
        var texture = ArtAssets.ChromeSlice(name);
        if (texture is null)
        {
            GD.PushError($"ChromeStyles: chrome slice '{name}' is missing from assets/theme/ - "
                + "falling back to a flat box. Run `artgen generate chrome` and re-import.");
            return flat;
        }

        var style = new StyleBoxTexture
        {
            Texture = texture,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
        };
        style.SetTextureMarginAll(corner);
        return style;
    }

    // Shared dim tint for a locked-but-visible item (a card/relic still
    // shown so the collection reads as a whole roster, not just what's
    // unlocked). One definition, so MetaProgressionScreen and LibraryScreen
    // can't drift into two slightly different greys for the same meaning.
    public static readonly Color LockedTint = new(0.55f, 0.55f, 0.58f);

    public static StyleBoxFlat HpBarBackground()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgPanel,
            BorderColor = PixelSpec.Ramp.G1,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }

    public static StyleBoxFlat HpBarFill()
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.R3,
            BorderColor = PixelSpec.Ramp.R4,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thin);
        return style;
    }

    // Amber, distinct from the main bar's red, so the lagging "damage you
    // just took" zone reads as a separate layer rather than a paler version
    // of the same bar. Opaque now rather than alpha 0.85: a blended fill
    // lands on off-ramp colors, and the ghost sits over the bezel background
    // anyway so it never needed the transparency.
    public static StyleBoxFlat HpGhostBarFill() => new() { BgColor = PixelSpec.Ramp.E2 };

    // GhostHpBar (behind) draws the bezel background + a lagging fill; the
    // real HpBar (in front) draws its normal fill but with an EMPTY
    // background, so the ghost's background/fill actually shows through in
    // the gap between the two values instead of being fully occluded by an
    // opaque background HpBar would otherwise paint every frame regardless
    // of its own fill percentage.
    public static void ApplyHpBarStyle(ProgressBar bar, ProgressBar ghostBar)
    {
        bar.Modulate = Colors.White; // clear the old placeholder tint hack
        ghostBar.Modulate = Colors.White;

        ghostBar.AddThemeStyleboxOverride("background", HpBarBackground());
        ghostBar.AddThemeStyleboxOverride("fill", HpGhostBarFill());

        bar.AddThemeStyleboxOverride("background", new StyleBoxEmpty());
        bar.AddThemeStyleboxOverride("fill", HpBarFill());
    }

    // Main bar always tweens to the new value quickly; the ghost bar only
    // lags behind on a HP loss (a heal or no-op change just snaps the ghost
    // along so it never looks like an inverted "future healing" preview).
    // ghostTween is a ref to the caller's stored Tween field so a rapid
    // second hit can kill the still-draining ghost tween instead of two
    // tweens fighting over the same Value property.
    public static void TweenHpBar(ProgressBar bar, ProgressBar ghostBar, ref Tween? ghostTween, double newValue)
    {
        double oldValue = bar.Value;
        bar.CreateTween().TweenProperty(bar, "value", newValue, 0.25).SetTrans(Tween.TransitionType.Sine);

        ghostTween?.Kill();
        if (newValue >= oldValue)
        {
            ghostBar.Value = newValue;
            return;
        }

        ghostBar.Value = oldValue;
        var tween = ghostBar.CreateTween();
        ghostTween = tween;
        tween.TweenInterval(0.15);
        tween.TweenProperty(ghostBar, "value", newValue, 0.4).SetTrans(Tween.TransitionType.Sine);
    }

    // The "this is the important button" treatment - End Turn, and the main
    // menu's entries. Replaced the sourced CC0 ornate wooden frame this used
    // to wrap (StumpyStrust's Fantasy UI Box): that texture is smooth,
    // anti-aliased fantasy art, and sitting it beside Silkscreen glyphs and
    // 32x32 sprites was exactly the mixed-media seam the pixel-art commitment
    // exists to close.
    //
    // It carried that loss as "weight, not ornament" - a double-thickness gold
    // bezel and nothing else - for as long as there was no chrome art. There is
    // now, so the ornament is back in the medium it should have been in: a
    // generated double bezel with a stepped corner bracket, the same shape at
    // every state with the ramp doing the state work. ART_SPEC section 7's
    // "Known cost, accepted" named palette and ornament as how the
    // illuminated-manuscript character comes back after Cinzel and IM Fell
    // English; this is the ornament half.
    //
    // Applies all four states in one call because the four were always set
    // together at every call site.
    public static void ApplyEmphasisButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", EmphasisState(Slices.EmphasisNormal, PixelSpec.Ramp.N2, PixelSpec.Ramp.G2));
        button.AddThemeStyleboxOverride("hover", EmphasisState(Slices.EmphasisHover, PixelSpec.Ramp.N3, PixelSpec.Ramp.G4));
        button.AddThemeStyleboxOverride("pressed", EmphasisState(Slices.EmphasisPressed, PixelSpec.Ramp.N1, PixelSpec.Ramp.G3));
        button.AddThemeStyleboxOverride("disabled", EmphasisState(Slices.EmphasisDisabled, PixelSpec.Ramp.N1, PixelSpec.Ramp.N3));
    }

    // Godot's Slider defines no focus stylebox at all - unlike Button and
    // CheckButton, which get one from hollowdeck_theme.tres - so a focused
    // volume slider looked exactly like an unfocused one. On a screen that is
    // almost entirely sliders, that meant keyboard navigation had no visible
    // position. Recolouring the track is the closest equivalent the control
    // offers, and it makes the same two changes sb_btn_focus does: FocusRing,
    // and double thickness.
    public static void ApplyFocusableSliderStyle(HSlider slider)
    {
        SetSliderFocus(slider, focused: false);
        slider.FocusEntered += () => SetSliderFocus(slider, focused: true);
        slider.FocusExited += () => SetSliderFocus(slider, focused: false);
    }

    // Both halves, because either one alone has a value at which it is
    // invisible: the filled portion covers the whole track at max volume
    // (which is the default, so the first screenshot of this showed no focus
    // at all), and the fill is nothing to look at at zero.
    private static void SetSliderFocus(HSlider slider, bool focused)
    {
        slider.AddThemeStyleboxOverride("slider", SliderTrackStyle(focused));
        slider.AddThemeStyleboxOverride("grabber_area", SliderFillStyle(focused));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", SliderFillStyle(focused));
    }

    // Mirrors sb_slider in hollowdeck_theme.tres - the unfocused branch must
    // stay identical to it, or a slider would visibly change on first paint.
    private static StyleBoxFlat SliderTrackStyle(bool focused)
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.N1,
            BorderColor = focused ? UiTheme.Palette.FocusRing : PixelSpec.Ramp.G1,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        style.SetBorderWidthAll(focused ? UiTheme.BorderWidth.Thick : UiTheme.BorderWidth.Normal);
        return style;
    }

    // Mirrors sb_slider_grabber, same rule as above.
    private static StyleBoxFlat SliderFillStyle(bool focused) =>
        new() { BgColor = focused ? UiTheme.Palette.FocusRing : PixelSpec.Ramp.G3 };

    // The seed field on RunSetupScreen is the first and only LineEdit in the
    // project, and hollowdeck_theme.tres has no LineEdit entry at all - so an
    // unstyled one inherits the default font and *nothing else*, rendering with
    // Godot's stock blue-grey box, blue caret and blue selection. That is the
    // mixed-media seam the pixel-art commitment exists to close, arriving
    // through the one control type nobody had used yet.
    //
    // Written here rather than in the theme resource for the same reason
    // ApplyFocusableSliderStyle is: it is one control on one screen, and the
    // ramp colours are already tokens here. If a second LineEdit ever lands,
    // move it into the .tres - a control styled two ways is the seam this file
    // header is about.
    //
    // No font size is set: the theme default (Tiny5-Regular at 16) is already
    // on the 8px design em, and an override here would be a literal
    // PixelSpecSmokeTest has to keep agreeing with.
    public static void ApplyLineEditStyle(LineEdit field)
    {
        field.AddThemeStyleboxOverride("normal", LineEditStyle(PixelSpec.Ramp.G1, UiTheme.BorderWidth.Normal));
        field.AddThemeStyleboxOverride("focus", LineEditStyle(UiTheme.Palette.FocusRing, UiTheme.BorderWidth.Thick));
        // G0 over the deep face, the same "this slot is not in play" treatment
        // SlotStyle uses for an empty potion slot - the field goes read-only
        // once a blessing is claimed and re-seeding would erase it.
        field.AddThemeStyleboxOverride("read_only", LineEditStyle(PixelSpec.Ramp.G0, UiTheme.BorderWidth.Normal));

        field.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);
        field.AddThemeColorOverride("font_uneditable_color", LockedTint);
        field.AddThemeColorOverride("font_selected_color", UiTheme.Palette.BgDeep);
        field.AddThemeColorOverride("font_placeholder_color", PixelSpec.Ramp.N4);
        field.AddThemeColorOverride("caret_color", UiTheme.Palette.AccentGoldBright);
        field.AddThemeColorOverride("selection_color", UiTheme.Palette.AccentGold);
    }

    private static StyleBoxFlat LineEditStyle(Color border, int borderWidth)
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgDeep,
            BorderColor = border,
        };
        style.SetBorderWidthAll(borderWidth);
        style.ContentMarginLeft = UiTheme.Spacing.Sm;
        style.ContentMarginRight = UiTheme.Spacing.Sm;
        style.ContentMarginTop = UiTheme.Spacing.Xs;
        style.ContentMarginBottom = UiTheme.Spacing.Xs;
        return style;
    }

    // The 16/10 content margins are load-bearing across two files:
    // hollowdeck_theme.tres's sb_btn_focus deliberately matches them so that
    // gaining keyboard focus never changes a button's size. Move one and the
    // other has to move with it.
    private static StyleBox EmphasisState(string slice, Color fill, Color border)
    {
        var flat = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        flat.SetBorderWidthAll(UiTheme.BorderWidth.Thick);

        var style = SliceStyle(slice, LargeCorner, flat);
        style.ContentMarginLeft = 16;
        style.ContentMarginTop = 10;
        style.ContentMarginRight = 16;
        style.ContentMarginBottom = 10;
        return style;
    }

    // The one place a Rarity becomes a colour. Pulled out of CardFrameStyle
    // when potions gained a tier and the reward screen's drop tile needed the
    // same three colours: a second switch would be a second place for the
    // ramp to drift, and rarity's whole job in this codebase is that a border
    // colour means the same thing wherever it appears.
    public static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => UiTheme.Palette.RarityUncommon,
        Rarity.Rare => UiTheme.Palette.RarityRare,
        _ => UiTheme.Palette.RarityCommon,
    };

    // The same job for RelicTier, which cannot reuse the switch above: it has
    // six members, and three of them name a *source* rather than a power
    // level. The first three deliberately land on the identical three ramp
    // entries a card rarity does - a tier colour has to mean one thing across
    // the whole game - and the other three take a ramp of their own each, so
    // "where did this come from" is a different question from "how strong is
    // it" and looks like one.
    public static Color RelicTierColor(RelicTier tier) => tier switch
    {
        RelicTier.Uncommon => UiTheme.Palette.RarityUncommon,
        RelicTier.Rare => UiTheme.Palette.RarityRare,
        RelicTier.Boss => UiTheme.Palette.TierBoss,
        RelicTier.Shop => UiTheme.Palette.TierShop,
        RelicTier.Event => UiTheme.Palette.TierEvent,
        _ => UiTheme.Palette.RarityCommon,
    };

    // Two independent color channels rather than one fighting a second
    // painted on top of it: fill reads CardType (Attack/Skill), border reads
    // Rarity (Common/Uncommon/Rare). Hover brightens whichever border color
    // is already showing (a pure brightness signal, not a hue swap), Rare
    // keeps its gold glow at rest, and an upgraded card blends its border
    // toward green independent of rarity - an upgraded Rare card reads as
    // gold-with-a-green-cast rather than one signal replacing the other.
    public static StyleBoxFlat CardFrameStyle(CardType type, Rarity rarity, bool hovered, bool isUpgraded = false,
        Color? rareGlow = null)
    {
        var borderColor = RarityColor(rarity);
        if (isUpgraded) borderColor = borderColor.Lerp(UiTheme.Palette.UpgradeAccent, 0.35f);
        if (hovered) borderColor = borderColor.Lerp(Colors.White, 0.4f);

        // Rare's rest-state emphasis used to be a 6px blur glow. With blurs
        // gone it reads through the ramp instead - the border steps up the gold
        // ramp - plus the extra border weight below.
        //
        // rareGlow is the current frame of CardView's GlowRing, and it defaults
        // to the top of that ramp, which is where the static version sat. So an
        // un-driven call (every grid, popup and picker that renders a card
        // without a ring) paints exactly what it painted before this parameter
        // existed.
        if (!hovered && rarity == Rarity.Rare) borderColor = rareGlow ?? UiTheme.Palette.RarityRareGlow;

        var style = new StyleBoxFlat
        {
            // PowerFill was claimed in UiTheme when the type/rarity channel
            // split landed, precisely so the third fill would not have to be
            // picked under deadline when Power arrived. This is that.
            BgColor = type switch
            {
                CardType.Attack => UiTheme.Palette.AttackFill,
                CardType.Power => UiTheme.Palette.PowerFill,
                CardType.Status => UiTheme.Palette.StatusFill,
                CardType.Curse => UiTheme.Palette.CurseFill,
                _ => UiTheme.Palette.SkillFill,
            },
            BorderColor = borderColor,
        };

        // Border weight is now the only emphasis channel, so it carries both
        // signals: hover is thickest, an un-hovered Rare still reads heavier
        // than a Common.
        style.SetBorderWidthAll(hovered || rarity == Rarity.Rare
            ? UiTheme.BorderWidth.Thick
            : UiTheme.BorderWidth.Normal);

        return style;
    }

    // Badge (cost pip, Exhaust pip). Square, not circular: the old version
    // used a 999 corner radius to force a circle at any size, and a
    // radius-rounded box is an anti-aliased curve. A hard 2px-ringed square
    // reads the same at a glance and is native to the medium.
    public static StyleBoxFlat BadgeStyle(Color fill, Color ring)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = ring };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }

    // Keyboard-hotkey badge - deliberately a muted slate rounded-square
    // "keycap" rather than another BadgeStyle circle, so it can never be
    // mistaken for the gold cost badge or bronze exhaust badge sharing the
    // same card frame (both of those are circular and gold/bronze).
    public static StyleBoxFlat HotkeyBadgeStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.18f, 0.22f),
            BorderColor = new Color(0.55f, 0.6f, 0.68f),
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }

    // The lit keycap: which card the *keyboard* is currently on. Deliberately
    // not a border-colour change on the card frame itself - that channel is
    // already spoken for by rarity, and the brightened-border hover state sits
    // right next to it, so a third meaning there would be unreadable. Lighting
    // the badge instead puts the signal on the very thing that says which key
    // to press, and can't collide with any rarity.
    public static StyleBoxFlat HotkeyBadgeSelectedStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.AccentGoldBright,
            BorderColor = UiTheme.Palette.FocusRing,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }

    // Subtle darker overlay shared by the card's name banner, art window,
    // and description box - semi-transparent black alpha-blends over
    // whatever's already painted beneath it (the card's type-colored fill),
    // so all three zones read as distinct insets without introducing a
    // third hue that would compete with type (fill) and rarity (border).
    public static StyleBoxFlat InsetPanelStyle()
    {
        var style = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.28f) };
        style.ContentMarginLeft = 4;
        style.ContentMarginRight = 4;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        return style;
    }

    // Generic bronze-bordered HUD row panel - same visual language as the
    // HP bar bezel (HpBarBackground), reused for any row that currently
    // paints straight onto the fog/vignette backdrop with no framing at all
    // (relic bar, potion belt, gold display).
    //
    // Returns StyleBox, not StyleBoxTexture: three callers set their own
    // content margins on the way out (ScreenChrome.Frame and FocusableFrame
    // widen the padding, and this is the base for both), and ContentMargin* is
    // on the StyleBox base class, so widening the return type is all that was
    // needed to keep them working. Anything reaching for BorderColor here is
    // asking for a colour the art decides - see the file header.
    //
    // The border was an off-ramp Color(0.5, 0.4, 0.22) literal for as long as
    // this was flat, sitting between G2 and G3 and matching neither. Moving it
    // into the art retired it: chrome.rs draws G1 over G2, which is the pair
    // HpBarBackground already uses.
    //
    // panel is a 16px slice rather than 24 because the shortest boxes in the
    // game are this one - ScreenChrome's HP and gold panels sit at
    // ContentMarginTop = 4 - and a StyleBoxTexture whose box is under twice its
    // texture margin folds its own corners together.
    public static StyleBox PanelStyle() => PanelStyle(focused: false);

    public static StyleBox PanelStyle(bool focused)
    {
        var flat = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgPanel,
            BorderColor = focused ? UiTheme.Palette.FocusRing : PixelSpec.Ramp.G2,
        };
        flat.SetBorderWidthAll(focused ? UiTheme.BorderWidth.Thick : UiTheme.BorderWidth.Normal);

        var style = SliceStyle(focused ? Slices.PanelFocus : Slices.Panel, SmallCorner, flat);
        style.ContentMarginLeft = UiTheme.Spacing.Sm;
        style.ContentMarginRight = UiTheme.Spacing.Sm;
        style.ContentMarginTop = UiTheme.Spacing.Xs;
        style.ContentMarginBottom = UiTheme.Spacing.Xs;
        return style;
    }

    // The art plinth's face: one pixel icon at 5x on the deeper BgDeep ground,
    // so the art separates from the panel around it. Lived inline in
    // ScreenChrome.ArtPlinth while it was a flat box; it is here now because
    // chrome art has one home, which is what this file's header is about.
    //
    // 24px, unlike the panel: a plinth is never smaller than the 160px sprite
    // it frames, so it has the room to carry the heavier frame.
    public static StyleBox PlinthStyle()
    {
        var flat = new StyleBoxFlat
        {
            BgColor = UiTheme.Palette.BgDeep,
            BorderColor = PixelSpec.Ramp.G1,
        };
        flat.SetBorderWidthAll(UiTheme.BorderWidth.Normal);

        var style = SliceStyle(Slices.Plinth, LargeCorner, flat);
        style.ContentMarginLeft = UiTheme.Spacing.Md;
        style.ContentMarginRight = UiTheme.Spacing.Md;
        style.ContentMarginTop = UiTheme.Spacing.Md;
        style.ContentMarginBottom = UiTheme.Spacing.Md;
        return style;
    }

    // A single HUD slot - one relic, one potion - as opposed to PanelStyle's
    // frame around a whole row. The relic bar and potion belt used to be one
    // wide PanelStyle box each holding a couple of small items, which read as
    // mostly empty; framing per item instead makes the row exactly as wide as
    // its contents (ROADMAP Phase 2).
    //
    // An empty slot is drawn, not omitted, so the potion belt shows capacity
    // rather than shrinking - it recedes via a dimmer bezel (G0 over the deep
    // background) instead of disappearing, which is the difference between
    // "you have room for two more" and "there is nothing here".
    public static StyleBox SlotStyle(bool filled)
    {
        var flat = new StyleBoxFlat
        {
            BgColor = filled ? UiTheme.Palette.BgPanel : UiTheme.Palette.BgDeep,
            BorderColor = filled ? PixelSpec.Ramp.G1 : PixelSpec.Ramp.G0,
        };
        flat.SetBorderWidthAll(UiTheme.BorderWidth.Normal);

        var style = SliceStyle(filled ? Slices.SlotFilled : Slices.SlotEmpty, SmallCorner, flat);
        style.ContentMarginLeft = UiTheme.Spacing.Xs;
        style.ContentMarginRight = UiTheme.Spacing.Xs;
        style.ContentMarginTop = UiTheme.Spacing.Xs;
        style.ContentMarginBottom = UiTheme.Spacing.Xs;
        return style;
    }

    // Always-on emphasis for the boss map node, keyed to the "Damage"
    // semantic red (danger/ominous) rather than Rarity's gold, since this is
    // not a rarity signal.
    //
    // Was a 999-radius circle with a 12px red bloom; both are anti-aliased
    // effects. Now a hard box with the heaviest border weight in the system
    // over a deep oxblood face - it still reads as the one node on the map
    // you are not supposed to walk into casually, and the skull icon it
    // frames was always doing most of that work anyway.
    //
    // border is the current frame of MapScreen's GlowRing, cycling R3->R4->R5
    // rather than section 6's literal gold: the semantic above is the whole
    // point of this style, and a gold ring would say "rare" on the one node
    // whose meaning is danger. Defaults to Damage (= R5), the top of that ramp
    // and the colour this painted before the ring existed.
    public static StyleBoxFlat BossNodeGlowStyle(Color? border = null)
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.R0,
            BorderColor = border ?? UiTheme.Palette.Damage,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thick);
        return style;
    }

    // Target-lock glow while a SingleEnemy card is being dragged over an enemy,
    // or while the keyboard is cycled onto it. Moved here from a static readonly
    // field on EnemyView when the ring landed - one box shared by every view is
    // fine for a still and wrong for something a driver repaints.
    //
    // Was a rounded box with a 10px gold bloom, on a slate-blue face left over
    // from the deleted generic theme - the bloom and the radius are both illegal
    // in pixels (section 6), and the slate blue was off-ramp entirely. Now a
    // hard heavy gold bezel on a warm face: the lock reads instantly because the
    // top of the gold ramp against the dark neutrals is the highest-contrast
    // pair in the palette. Defaults to G5, which is where the still sat.
    public static StyleBoxFlat TargetLockStyle(Color? border = null)
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.N3,
            BorderColor = border ?? PixelSpec.Ramp.G5,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thick);
        return style;
    }

    // The map node the player has already walked through. Disabled like every
    // other illegal move, but it is history rather than a wall, so it keeps a
    // warm border instead of falling through to the theme's neutral
    // sb_btn_disabled (#2e2724).
    //
    // G0 and not G1/G2 is the load-bearing part: G1 is what the theme paints on
    // a *reachable* node, so anything at or above it would make the path you
    // already took compete with the move you can still make. One step below
    // says "trail" without saying "click me". Warm-vs-neutral is then what
    // separates visited from untraversed independently of MapScreen's
    // brightness axis, so the two signals reinforce rather than duplicate.
    public static StyleBoxFlat MapNodeTrailStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.N1,
            BorderColor = PixelSpec.Ramp.G0,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Normal);
        return style;
    }
}
