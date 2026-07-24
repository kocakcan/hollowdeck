using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.UI;

public partial class EnemyView : Button
{
    // Lets CardView hit-test "is the mouse over an enemy" for drag-to-target,
    // without CardView needing a reference to CombatScreen or the enemy row.
    public static readonly List<EnemyView> Instances = new();

    public EnemyCombatant Combatant { get; set; } = null!;

    private TextureRect _sprite = null!;
    private Label _nameLabel = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpLabel = null!;
    private TextureRect _intentIcon = null!;
    private Label _intentLabel = null!;
    private HBoxContainer _statusRow = null!;

    public override void _Ready()
    {
        _sprite = GetNode<TextureRect>("VBox/Sprite");
        _nameLabel = GetNode<Label>("VBox/NameLabel");
        _hpBar = GetNode<ProgressBar>("VBox/HpFrame/HpBar");
        _hpLabel = GetNode<Label>("VBox/HpFrame/HpLabel");
        _intentIcon = GetNode<TextureRect>("VBox/IntentRow/IntentIcon");
        _intentLabel = GetNode<Label>("VBox/IntentRow/IntentLabel");
        _statusRow = GetNode<HBoxContainer>("VBox/StatusRow");
        _sprite.Texture = ArtAssets.EnemySprite(Combatant.Definition.Id);
        _nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _hpLabel.ThemeTypeVariation = "CombatDisplayLabel";
        // Placeholder tint until Phase 8 supplies a real ornate-frame/fill
        // texture - reads as a health bar (red fill) rather than the
        // default theme color in the meantime.
        _hpBar.Modulate = new Color(0.82f, 0.24f, 0.22f);
        Pressed += OnPressed;
        Instances.Add(this);
        Refresh();
    }

    public override void _ExitTree()
    {
        Instances.Remove(this);
    }

    public void Refresh()
    {
        _nameLabel.Text = Combatant.Name;
        _hpBar.MaxValue = Combatant.MaxHp;
        _hpBar.Value = Combatant.CurrentHp;
        _hpLabel.Text = $"{Combatant.CurrentHp}/{Combatant.MaxHp}" +
                         (Combatant.Block > 0 ? $"  🛡{Combatant.Block}" : "");
        var intent = Combatant.CurrentMove?.Intent;
        _intentIcon.Texture = intent is null ? null : ArtAssets.IntentIcon(intent.Type);
        _intentIcon.Visible = _intentIcon.Texture is not null;
        _intentLabel.Text = FormatIntent(intent);
        StatusRow.Populate(_statusRow, Combatant, 16);
    }

    private static string FormatIntent(EnemyIntent? intent)
    {
        if (intent is null) return "";
        return intent.Type switch
        {
            IntentType.Attack => $"{intent.DisplayAmount}",
            IntentType.Defend => "",
            IntentType.Buff => $"+{intent.DisplayAmount} Str",
            _ => "",
        };
    }

    private void OnPressed() => CombatManager.Instance.TryTargetEnemy(Combatant);
}
