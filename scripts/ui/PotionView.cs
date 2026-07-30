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

    private PotionInstance _potion = null!;

    public void SetPotionInstance(PotionInstance potion)
    {
        _potion = potion;
        // Target type included so an AllEnemies potion (Weak Potion) says so -
        // its EffectSpecs are indistinguishable from a single-target one's.
        var description = EffectDescriptionFormatter.Describe(potion.Definition.Effects,
            new DescribeContext(CombatManager.Instance?.Player, potion.Definition.Target));
        // With an icon the belt shows icon-only buttons (name lives in the
        // tooltip); without one it falls back to the old text button.
        Icon = ArtAssets.PotionIcon(potion.Definition.Id);
        if (Icon is not null)
        {
            Text = "";
            ExpandIcon = true;
            IconAlignment = HorizontalAlignment.Center;
            CustomMinimumSize = SlotSize;
            TooltipText = $"{potion.Definition.Name}\n{description}";
        }
        else
        {
            Text = potion.Definition.Name;
            TooltipText = description;
        }
    }

    public override void _Ready()
    {
        // Same reasoning as EnemyView/DeckViewButtons - don't let this
        // Button participate in Godot's automatic arrow-key focus
        // navigation, which would otherwise compete with CombatScreen's
        // arrow-key card/target cycling.
        FocusMode = FocusModeEnum.None;
        Pressed += OnPressed;
    }

    private void OnPressed()
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        CombatManager.Instance?.TryUsePotion(_potion);
    }
}
