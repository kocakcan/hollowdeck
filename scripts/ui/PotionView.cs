using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class PotionView : Button
{
    // The belt draws RunState.MaxPotionSlots slots whether or not you hold
    // that many potions, so an empty slot has to match a filled one exactly -
    // shared from here rather than repeated as a literal in CombatScreen.
    public static readonly Vector2 SlotSize = new(48, 44);

    // Smaller than CardView's 28px keycap because the slot it sits in is 48px
    // wide, not 176 - but the same style, so the two read as one convention.
    private static readonly Vector2 HotkeyBadgeSize = new(18, 18);

    private PotionInstance _potion = null!;
    private PanelContainer? _hotkeyBadge;
    private Label? _hotkeyLabel;
    private string _hotkeyHint = "";

    public void SetPotionInstance(PotionInstance potion)
    {
        _potion = potion;
        // With an icon the belt shows icon-only buttons (name lives in the
        // tooltip); without one it falls back to the old text button.
        Icon = ArtAssets.PotionIcon(potion.Definition.Id);
        if (Icon is not null)
        {
            Text = "";
            ExpandIcon = true;
            IconAlignment = HorizontalAlignment.Center;
            CustomMinimumSize = SlotSize;
        }
        else
        {
            Text = potion.Definition.Name;
        }
        RefreshTooltip();
    }

    // Composed rather than assigned inline, because the hotkey and the potion
    // arrive in either order (CombatScreen.RefreshPotions sets the slot on a
    // reused view before it knows what's in it) and both belong in the
    // tooltip.
    private void RefreshTooltip()
    {
        if (_potion is null) return;

        // Target type included so an AllEnemies potion (Weak Potion) says so -
        // its EffectSpecs are indistinguishable from a single-target one's.
        var description = EffectDescriptionFormatter.Describe(_potion.Definition.Effects,
            new DescribeContext(CombatManager.Instance?.Player, _potion.Definition.Target));
        var key = _hotkeyHint.Length > 0 ? $" ({_hotkeyHint})" : "";
        // Without an icon the name is already the button's own Text, so the
        // tooltip carries only the key and the rules text.
        TooltipText = Icon is not null
            ? $"{_potion.Definition.Name}{key}\n{description}"
            : $"{description}{(key.Length > 0 ? $"\nHotkey:{key}" : "")}";
    }

    // The belt is keyboard-reachable through CombatScreen's hd_potion_* keys
    // rather than through focus, so the slot has to say which key it is -
    // there is nothing else on screen that could. Read out of the InputMap so
    // the badge can't drift from the binding that actually fires.
    public void SetHotkeySlot(int slotIndex)
    {
        _hotkeyHint = ScreenKeyboardNav.KeyHint($"hd_potion_{slotIndex + 1}");
        if (_hotkeyLabel is not null) _hotkeyLabel.Text = _hotkeyHint;
        if (_hotkeyBadge is not null) _hotkeyBadge.Visible = _hotkeyHint.Length > 0;
        RefreshTooltip();
    }

    public override void _Ready()
    {
        // Same reasoning as EnemyView/DeckViewButtons - don't let this
        // Button participate in Godot's automatic arrow-key focus
        // navigation, which would otherwise compete with CombatScreen's
        // arrow-key card/target cycling.
        FocusMode = FocusModeEnum.None;
        Pressed += OnPressed;
        BuildHotkeyBadge();
    }

    private void BuildHotkeyBadge()
    {
        _hotkeyBadge = new PanelContainer
        {
            // Ignore, not Stop: the badge covers the bottom-left corner of the
            // button's own rect, and a slot that stopped taking clicks there
            // would be a worse trade than the hint is worth.
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = _hotkeyHint.Length > 0,
        };
        _hotkeyBadge.AddThemeStyleboxOverride("panel", ChromeStyles.HotkeyBadgeStyle());
        AddChild(_hotkeyBadge);
        _hotkeyBadge.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _hotkeyBadge.OffsetTop = -HotkeyBadgeSize.Y;
        _hotkeyBadge.OffsetRight = HotkeyBadgeSize.X;
        _hotkeyBadge.OffsetBottom = 0;
        _hotkeyBadge.GrowVertical = GrowDirection.Begin;

        _hotkeyLabel = new Label
        {
            Text = _hotkeyHint,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hotkeyBadge.AddChild(_hotkeyLabel);
    }

    private void OnPressed()
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        CombatManager.Instance?.TryUsePotion(_potion);
    }
}
