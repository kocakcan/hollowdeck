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
        Text = potion.Definition.Name;
        TooltipText = EffectDescriptionFormatter.Describe(potion.Definition.Effects, CombatManager.Instance?.Player);
    }

    public override void _Ready()
    {
        Pressed += OnPressed;
    }

    private void OnPressed() => CombatManager.Instance.TryUsePotion(_potion);
}
