using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;

namespace Hollowdeck.UI;

public partial class EnemyView : Button
{
    // Lets CardView hit-test "is the mouse over an enemy" for drag-to-target,
    // without CardView needing a reference to CombatScreen or the enemy row.
    public static readonly List<EnemyView> Instances = new();

    // Target-lock glow while a SingleEnemy card is being dragged over this
    // enemy. Overrides "normal" rather than "hover": the native Button hover
    // stylebox can never actually apply during a drag anyway (the dragged
    // CardView Panel sits on top and occludes this Button from Godot's mouse
    // picking), so there's no state to reconcile against.
    private static readonly StyleBoxFlat TargetLockStyle = BuildTargetLockStyle();

    private static StyleBoxFlat BuildTargetLockStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.216f, 0.243f, 0.325f, 1f),
            BorderColor = new Color(1f, 0.85f, 0.3f, 1f),
        };
        style.SetBorderWidthAll(4);
        style.SetCornerRadiusAll(6);
        style.ShadowColor = new Color(1f, 0.85f, 0.3f, 0.65f);
        style.ShadowSize = 10;
        return style;
    }

    public EnemyCombatant Combatant { get; set; } = null!;

    private TextureRect _sprite = null!;
    private Label _nameLabel = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpLabel = null!;
    private TextureRect _intentIcon = null!;
    private Label _intentLabel = null!;
    private TextureRect _debuffIcon = null!;
    private HBoxContainer _statusRow = null!;
    private Tween? _idleTween;

    public override void _Ready()
    {
        _sprite = GetNode<TextureRect>("VBox/Sprite");
        _nameLabel = GetNode<Label>("VBox/NameLabel");
        _hpBar = GetNode<ProgressBar>("VBox/HpFrame/HpBar");
        _hpLabel = GetNode<Label>("VBox/HpFrame/HpLabel");
        _intentIcon = GetNode<TextureRect>("VBox/IntentRow/IntentIcon");
        _intentLabel = GetNode<Label>("VBox/IntentRow/IntentLabel");
        _debuffIcon = GetNode<TextureRect>("VBox/IntentRow/DebuffIcon");
        _statusRow = GetNode<HBoxContainer>("VBox/StatusRow");
        _sprite.Texture = ArtAssets.EnemySprite(Combatant.Definition.Id);
        _sprite.PivotOffset = _sprite.Size / 2f;
        _nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _hpLabel.ThemeTypeVariation = "CombatDisplayLabel";
        ChromeStyles.ApplyHpBarStyle(_hpBar);
        Pressed += OnPressed;
        Instances.Add(this);
        Refresh();
        StartIdleBob();
    }

    // Subtle continuous "breathing" loop - scale/rotation only, since
    // _sprite sits inside a VBoxContainer which manages its position/size
    // (a Position tween here would just get fought and overridden every
    // layout pass). Phase-offset per instance via the initial random delay
    // so multiple enemies don't all bob in lockstep.
    private void StartIdleBob()
    {
        _idleTween?.Kill();
        _sprite.Scale = Vector2.One;
        var tween = _sprite.CreateTween();
        _idleTween = tween;
        tween.TweenInterval(GD.Randf() * 1.0);
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(_sprite, "scale", Vector2.One * 1.04f, 1.0);
        tween.TweenProperty(_sprite, "scale", Vector2.One, 1.0);
    }

    // Quick punch-and-settle on the sprite when this enemy takes damage,
    // layered alongside CombatScreen's existing modulate flash. Restarts the
    // idle bob afterward since both drive the same Scale property and would
    // otherwise fight each other.
    public void PlayHitRecoil()
    {
        _idleTween?.Kill();
        _sprite.Scale = Vector2.One;
        var tween = _sprite.CreateTween();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.SetParallel(true);
        tween.TweenProperty(_sprite, "scale", Vector2.One * 1.15f, 0.06);
        tween.TweenProperty(_sprite, "rotation_degrees", 6f, 0.06);
        tween.Chain();
        tween.SetParallel(true);
        tween.TweenProperty(_sprite, "scale", Vector2.One, 0.16).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(_sprite, "rotation_degrees", 0f, 0.16).SetTrans(Tween.TransitionType.Back);
        tween.Chain().TweenCallback(Callable.From(StartIdleBob));
    }

    // Brief telegraph lean while CombatManager's wind-up delay plays out,
    // so an attack reads as building up before it lands.
    public void PlayWindUp()
    {
        _idleTween?.Kill();
        _sprite.Scale = Vector2.One;
        var tween = _sprite.CreateTween();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(_sprite, "scale", Vector2.One * 1.08f, 0.12);
        tween.TweenProperty(_sprite, "scale", Vector2.One, 0.08);
        tween.TweenCallback(Callable.From(StartIdleBob));
    }

    // Whole-view fade/shrink/slump on death (not just the sprite) - unlike
    // the hit/idle animations, the enemy is leaving the fight entirely, so
    // animating the whole card (name/HP/status included) reads better than
    // just the portrait reacting. Safe to animate Scale/Rotation/Modulate on
    // this Button directly since only Position/Size are Container-managed.
    public void PlayDeathTween(System.Action onComplete)
    {
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.One * 0.7f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "rotation_degrees", 10f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "modulate:a", 0f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.Chain().TweenCallback(Callable.From(onComplete));
    }

    public override void _ExitTree()
    {
        Instances.Remove(this);
    }

    // Toggled continuously by CardView while dragging a SingleEnemy card.
    public void SetTargetLocked(bool locked)
    {
        if (locked) AddThemeStyleboxOverride("normal", TargetLockStyle);
        else RemoveThemeStyleboxOverride("normal");
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

        // Attack moves that also debuff the player (Acid Slime's corrode,
        // the boss's shadow_lash, etc.) otherwise looked identical to a
        // plain attack until it landed - a small badge of the actual status
        // icon telegraphs it up front, same as the main intent icon does
        // for damage/block/buff.
        var debuffStatus = IncomingDebuffStatus(Combatant.CurrentMove);
        _debuffIcon.Texture = debuffStatus is null ? null : ArtAssets.StatusIcon(debuffStatus.Value);
        _debuffIcon.Visible = _debuffIcon.Texture is not null;
        _debuffIcon.TooltipText = debuffStatus is null ? "" : $"Also inflicts {debuffStatus}";

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

    private static readonly HashSet<string> DebuffStatusNames = new() { "Weak", "Vulnerable", "Poison" };

    private static StatusType? IncomingDebuffStatus(EnemyMove? move)
    {
        var effect = move?.Effects.FirstOrDefault(e =>
            e.Action == "apply_status" && e.Scope == EffectScope.Target &&
            e.Status is not null && DebuffStatusNames.Contains(e.Status));
        if (effect?.Status is null) return null;
        return System.Enum.Parse<StatusType>(effect.Status);
    }

    private void OnPressed() => CombatManager.Instance.TryTargetEnemy(Combatant);
}
