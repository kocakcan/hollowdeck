using Godot;

namespace Hollowdeck.UI;

// Semantic layer over PixelSpec.Ramp: what each color is *for*, referenced by
// procedurally-built StyleBox code across ChromeStyles, CardView, MapScreen.
// Named UiTheme (not Theme) to avoid colliding with Godot's own Theme
// resource type used for hollowdeck_theme.tres - that file remains the
// mechanism for font/label theme-type-variations, while this class holds the
// values that vary by rarity/type/state at runtime and can't be expressed in
// a static .tres.
public static class UiTheme
{
    // Every color here is an alias onto PixelSpec.Ramp rather than its own
    // literal. The names stay because they carry the *meaning* (what a color
    // is for) while the ramp carries the *value* (what it is) - but there is
    // now exactly one place a color is defined, which is the whole point of
    // docs/ART_SPEC.md. A semantic that needs a color not on the ramp is a
    // spec change, not a new literal.
    public static class Palette
    {
        // Backgrounds/panels
        public static readonly Color BgDeep = PixelSpec.Ramp.N0;
        public static readonly Color BgPanel = PixelSpec.Ramp.N1;

        // Accent (bronze/gold chrome, matches ChromeStyles' existing bezel)
        public static readonly Color AccentGold = PixelSpec.Ramp.G3;
        public static readonly Color AccentGoldBright = PixelSpec.Ramp.G4;

        // "The keyboard is here" - the focus ring on every focusable Control,
        // and the lit hotkey badge on a keyboard-selected card. One step above
        // AccentGoldBright on purpose: the theme's focus box and the hover
        // border ChromeStyles.EmphasisState paints were both G4, so a focused
        // button was indistinguishable from a hovered one and keyboard
        // navigation had no visible position at all. Whatever this is, it must
        // not equal AccentGoldBright.
        public static readonly Color FocusRing = PixelSpec.Ramp.G5;

        // Card type fill only - border now comes from Rarity (see below), not
        // CardType, so fill and border read as two independent channels
        // instead of one dimension fighting a second painted on top of it.
        public static readonly Color AttackFill = PixelSpec.Ramp.R2;
        public static readonly Color SkillFill = PixelSpec.Ramp.V1;
        // Reserved for the Power card type (ROADMAP Phase 6) - the third fill
        // is claimed here so the type/rarity channel split stays coherent
        // when it lands, rather than picking a hue under deadline.
        public static readonly Color PowerFill = PixelSpec.Ramp.P1;

        // The two unplayable types. Both are deliberately *darker* than the
        // three playable fills rather than a new hue: the signal a player needs
        // at a glance in a fanned hand is "this one is dead", and the ramp's
        // low end says that without competing with the rarity border. Status is
        // the neutral end (inert clutter); Curse is the bruised near-black at
        // the bottom of the purple ramp, one step below PowerFill so the two
        // can never be confused for each other.
        public static readonly Color StatusFill = PixelSpec.Ramp.N3;
        public static readonly Color CurseFill = PixelSpec.Ramp.P0;

        // Damage/heal/block semantics (FloatingText, HP bar fill)
        public static readonly Color Damage = PixelSpec.Ramp.R5;
        public static readonly Color Heal = PixelSpec.Ramp.V4;
        public static readonly Color Block = PixelSpec.Ramp.B4;

        // Rarity drives card border color (Common/Uncommon/Rare); hover
        // brightens whichever rarity color is already showing rather than
        // swapping to a type hue, so hover stays a pure brightness signal.
        public static readonly Color RarityCommon = PixelSpec.Ramp.N6;
        public static readonly Color RarityUncommon = PixelSpec.Ramp.B3;
        public static readonly Color RarityRare = PixelSpec.Ramp.G3;
        // Rare's "glow" is an animated pixel border cycling G3->G4->G5 rather
        // than a blur (ART_SPEC section 6), so this is the ramp top rather
        // than the semi-transparent bloom color it used to be.
        public static readonly Color RarityRareGlow = PixelSpec.Ramp.G5;

        // RelicTier's three source tiers, which sit alongside the three above
        // rather than extending them - a Boss relic is not "rarer than Rare",
        // it is from somewhere else. One ramp each so no two source tiers can
        // be confused with each other or with a rarity: oxblood is danger and
        // is where a boss lives everywhere else in the game, ember is act II's
        // tint and the warmest thing that is not gold (which Rare has taken),
        // and violet is the arcane ramp events already draw on.
        public static readonly Color TierBoss = PixelSpec.Ramp.R4;
        public static readonly Color TierShop = PixelSpec.Ramp.E3;
        public static readonly Color TierEvent = PixelSpec.Ramp.P3;

        // Upgraded cards blend their (rarity) border toward this green,
        // independent of rarity - both signals read at once. Exhaust cards
        // get a small corner badge in this color (matches the existing
        // "hotter" tint CardView's exit tween already uses for exhaust).
        public static readonly Color UpgradeAccent = PixelSpec.Ramp.V4;
        public static readonly Color ExhaustAccent = PixelSpec.Ramp.E3;

        // Status semantics (buff vs debuff framing)
        public static readonly Color StatusBuff = PixelSpec.Ramp.V3;
        public static readonly Color StatusDebuff = PixelSpec.Ramp.R4;
    }

    public static class Spacing
    {
        public const float Xs = 4f;
        public const float Sm = 8f;
        public const float Md = 12f;
        public const float Lg = 20f;
        public const float Xl = 32f;
    }

    // Radius intentionally absent: StyleBoxFlat rounds corners by
    // anti-aliasing them, which has no pixel representation. Every box in the
    // game is hard-edged (docs/ART_SPEC.md section 6). If you find yourself
    // wanting a radius, you want a 9-slice with pre-drawn pixel corners.

    public static class BorderWidth
    {
        public const int Thin = 1;
        public const int Normal = 2;
        public const int Thick = 4;
    }

    public static class Motion
    {
        public const float Fast = 0.12f;
        public const float Normal = 0.2f;
        public const float Slow = 0.35f;

        public const Tween.TransitionType EaseStandard = Tween.TransitionType.Sine;
        public const Tween.TransitionType EaseOvershoot = Tween.TransitionType.Back;
    }

    // Bitmap faces, replacing Cinzel/IM Fell English. Those were high-res
    // serif - anti-aliased sub-pixel curves - which is the exact opposite of
    // the medium, so keeping them would have relocated the art/text seam
    // rather than closing it (docs/ART_SPEC.md section 7).
    //
    // Two faces, split by job: Silkscreen has no lowercase and is very wide,
    // which makes it wrong for sentences and right for short display strings
    // (titles, buttons, card/enemy names, HP and damage numbers); Tiny5 has
    // proper lowercase, unambiguous digits, and fits card rules text.
    //
    // Pixelify Sans was tried first and rejected on evidence: at 16px its 2,
    // 3, 5 and 8 are mutually ambiguous - "HP: 21/50" read as "81/50" and
    // "Deal 12 damage" as "Deal 13". That is disqualifying for a game whose
    // entire text content is numbers.
    //
    // Jersey 15 was the body face after that, and had the same disease for a
    // subtler reason: its design em is 27px (the "15" is its *cap height*),
    // and it was being rendered at 16, i.e. 0.59 device pixels per design
    // pixel. The rasterizer dropped ~40% of every stem, so "Deal 6 damage"
    // rendered as "Deal 8 damage" - the exact failure Pixelify Sans was
    // rejected for, arrived at from the other direction. Tiny5 replaced it:
    // an 8px design em, so 16 is an exact 2x, with the same 10px cap height
    // Jersey15@16 had. It is ~16% wider, which was the original reason it
    // lost to Jersey 15 - a tradeoff only worth making when the narrower face
    // is actually legible, and it wasn't. Every card still fits at 16.
    //
    // Both are imported with antialiasing, hinting and subpixel positioning
    // disabled (see the .import files) - any of those resamples the glyph off
    // the pixel grid and undoes the point of a bitmap face. Run
    // tools/font-grid.py against any candidate face before adopting it.
    public static class Fonts
    {
        public const string DisplayPath = "res://assets/fonts/Silkscreen-Bold.ttf";
        public const string BodyPath = "res://assets/fonts/Tiny5-Regular.ttf";

        // Exact multiples of the 8px design em both faces share, so glyphs
        // land on the pixel grid with no resampling.
        public const int Small = 8;
        public const int Body = 16;
        public const int Heading = 24;
        public const int Title = 32;
    }
}
