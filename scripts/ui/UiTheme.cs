using Godot;

namespace Hollowdeck.UI;

// Single source of truth for the palette/spacing/motion tokens referenced by
// procedurally-built StyleBoxFlat/StyleBoxTexture code across ChromeStyles,
// CardView, MapScreen, etc. Named UiTheme (not Theme) to avoid colliding with
// Godot's own Theme resource type used for hollowdeck_theme.tres/
// combat_theme.tres - those .tres files remain the mechanism for
// font/label theme-type-variations; this class only holds values that vary
// by rarity/type/state at runtime and can't be expressed in a static .tres.
public static class UiTheme
{
    public static class Palette
    {
        // Backgrounds/panels
        public static readonly Color BgDeep = new(0.05f, 0.04f, 0.04f);
        public static readonly Color BgPanel = new(0.08f, 0.06f, 0.05f);

        // Accent (bronze/gold chrome, matches ChromeStyles' existing bezel)
        public static readonly Color AccentGold = new(0.78f, 0.62f, 0.28f);
        public static readonly Color AccentGoldBright = new(0.92f, 0.78f, 0.4f);

        // Card type fill only - border now comes from Rarity (see below), not
        // CardType, so fill and border read as two independent channels
        // instead of one dimension fighting a second painted on top of it.
        public static readonly Color AttackFill = new(0.32f, 0.13f, 0.13f);
        public static readonly Color SkillFill = new(0.12f, 0.26f, 0.22f);

        // Damage/heal/block semantics (FloatingText, HP bar fill)
        public static readonly Color Damage = new(1f, 0.35f, 0.35f);
        public static readonly Color Heal = new(0.4f, 1f, 0.4f);
        public static readonly Color Block = new(0.55f, 0.75f, 1f);

        // Rarity drives card border color (Common/Uncommon/Rare); hover
        // brightens whichever rarity color is already showing rather than
        // swapping to a type hue, so hover stays a pure brightness signal.
        public static readonly Color RarityCommon = new(0.55f, 0.53f, 0.5f);
        public static readonly Color RarityUncommon = new(0.35f, 0.55f, 0.85f);
        public static readonly Color RarityRare = new(0.85f, 0.68f, 0.25f);
        public static readonly Color RarityRareGlow = new(0.95f, 0.8f, 0.3f, 0.65f);

        // Upgraded cards blend their (rarity) border toward this green,
        // independent of rarity - both signals read at once. Exhaust cards
        // get a small corner badge in this color (matches the existing
        // "hotter" tint CardView's exit tween already uses for exhaust).
        public static readonly Color UpgradeAccent = new(0.45f, 0.85f, 0.45f);
        public static readonly Color ExhaustAccent = new(0.95f, 0.65f, 0.3f);

        // Status semantics (buff vs debuff framing)
        public static readonly Color StatusBuff = new(0.55f, 0.85f, 0.55f);
        public static readonly Color StatusDebuff = new(0.85f, 0.4f, 0.4f);
    }

    public static class Spacing
    {
        public const float Xs = 4f;
        public const float Sm = 8f;
        public const float Md = 12f;
        public const float Lg = 20f;
        public const float Xl = 32f;
    }

    public static class Radius
    {
        public const float Card = 10f;
        public const float Panel = 6f;
        public const float Pip = 4f;
    }

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

    public static class Fonts
    {
        public const string DisplayPath = "res://assets/fonts/Cinzel-Regular.ttf";
        public const string BodyPath = "res://assets/fonts/IMFellEnglish-Regular.ttf";
    }
}
