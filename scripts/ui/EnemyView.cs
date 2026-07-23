using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.UI;

public partial class EnemyView : Button
{
    public EnemyCombatant Combatant { get; set; } = null!;

    private Label _nameLabel = null!;
    private Label _hpLabel = null!;
    private Label _intentLabel = null!;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("VBox/NameLabel");
        _hpLabel = GetNode<Label>("VBox/HpLabel");
        _intentLabel = GetNode<Label>("VBox/IntentLabel");
        Pressed += OnPressed;
        Refresh();
    }

    public void Refresh()
    {
        _nameLabel.Text = Combatant.Name;
        _hpLabel.Text = $"HP {Combatant.CurrentHp}/{Combatant.MaxHp}" +
                         (Combatant.Block > 0 ? $"  🛡{Combatant.Block}" : "");
        _intentLabel.Text = FormatIntent(Combatant.CurrentMove?.Intent);
    }

    private static string FormatIntent(EnemyIntent? intent)
    {
        if (intent is null) return "";
        return intent.Type switch
        {
            IntentType.Attack => $"⚔ {intent.DisplayAmount}",
            IntentType.Defend => "🛡",
            IntentType.Buff => $"+{intent.DisplayAmount} Str",
            _ => "",
        };
    }

    private void OnPressed() => CombatManager.Instance.TryTargetEnemy(Combatant);
}
