using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class PotionView : Button
{
    private PotionInstance _potion = null!;

    public void SetPotionInstance(PotionInstance potion)
    {
        _potion = potion;
        Text = potion.Definition.Name;
        TooltipText = potion.Definition.Description;
    }

    public override void _Ready()
    {
        Pressed += OnPressed;
    }

    private void OnPressed() => CombatManager.Instance.TryUsePotion(_potion);
}
