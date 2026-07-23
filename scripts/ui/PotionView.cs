using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Effects;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class PotionView : Button
{
    private PotionInstance _potion = null!;

    public void SetPotionInstance(PotionInstance potion)
    {
        _potion = potion;
        var description = EffectDescriptionFormatter.Describe(potion.Definition.Effects, CombatManager.Instance?.Player);
        // With an icon the belt shows icon-only buttons (name lives in the
        // tooltip); without one it falls back to the old text button.
        Icon = ArtAssets.PotionIcon(potion.Definition.Id);
        if (Icon is not null)
        {
            Text = "";
            ExpandIcon = true;
            IconAlignment = HorizontalAlignment.Center;
            CustomMinimumSize = new Vector2(48, 44);
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
        Pressed += OnPressed;
    }

    private void OnPressed() => CombatManager.Instance.TryUsePotion(_potion);
}
