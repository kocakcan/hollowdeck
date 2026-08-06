using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Run;

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
        // Was a rounded box with a 10px gold bloom, on a slate-blue face left
        // over from the deleted generic theme - the bloom and the radius are
        // both illegal in pixels (ART_SPEC section 6), and the slate blue was
        // off-ramp entirely. Now a hard heavy gold bezel on a warm face: the
        // lock still reads instantly because 4px of G5 against the ramp's
        // dark neutrals is the highest-contrast pair in the palette.
        var style = new StyleBoxFlat
        {
            BgColor = PixelSpec.Ramp.N3,
            BorderColor = PixelSpec.Ramp.G5,
        };
        style.SetBorderWidthAll(UiTheme.BorderWidth.Thick);
        return style;
    }

    public EnemyCombatant Combatant { get; set; } = null!;

    private TextureRect _sprite = null!;
    private TextureRect _shadow = null!;
    private static Texture2D? _shadowTexture;
    private Label _nameLabel = null!;
    private ProgressBar _hpBar = null!;
    private ProgressBar _ghostHpBar = null!;
    private Tween? _ghostHpTween;
    private Label _hpLabel = null!;
    private TextureRect _intentIcon = null!;
    private Label _intentLabel = null!;
    private TextureRect _debuffIcon = null!;
    private HBoxContainer _statusRow = null!;
    private Dictionary<StatusType, int>? _lastStatuses;
    private Tween? _idleTween;
    private Tween? _intentPulseTween;
    private string? _lastMoveId;
    private HoverTooltip? _intentTooltip;

    public override void _Ready()
    {
        // This whole card is a Button (for click-to-target), but Button's
        // default FocusModeEnum.All makes it a target of Godot's automatic
        // directional (arrow-key) focus navigation - which fights the
        // combat screen's own arrow-key card/target cycling, silently
        // shifting focus between enemies as a side effect of Left/Right.
        // This game has no keyboard-focus/Tab navigation design anywhere
        // else, so there's nothing lost by opting out of it here.
        FocusMode = FocusModeEnum.None;

        // Enemies stand free on the backdrop rather than inside a panel. This
        // node is a Button (for click-to-target), so it was picking up the
        // theme's Button stylebox and drawing a filled, bordered box around
        // every enemy - which is not how the genre composes a fight, and read
        // especially badly once the box was a slate-blue rectangle beside
        // bronze bezels. Only the target-lock state paints a background now;
        // at rest all four states are empty and the sprite sits on the
        // ground-plane gradient AttachCombat already draws.
        AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());

        _sprite = GetNode<TextureRect>("VBox/Sprite");
        _shadow = GetNode<TextureRect>("VBox/Sprite/Shadow");
        _shadow.Texture = _shadowTexture ??= BuildShadowTexture();
        _nameLabel = GetNode<Label>("VBox/NameLabel");
        // HpFrame is pinned to 160px wide and shrink-centered in the .tscn -
        // 160 being PixelSpec.SpriteScale * PixelSpec.CreatureGrid, i.e. the
        // rendered width of the sprite above it. It used to stretch the full
        // 220px of this view, which was invisible while every enemy sat inside
        // a panel of that width and became obvious once the panels came off:
        // the bar was wider than the creature it belonged to and read as
        // floor furniture rather than as part of the enemy.
        _hpBar = GetNode<ProgressBar>("VBox/HpFrame/HpBar");
        _ghostHpBar = GetNode<ProgressBar>("VBox/HpFrame/GhostHpBar");
        _hpLabel = GetNode<Label>("VBox/HpFrame/HpLabel");
        _intentIcon = GetNode<TextureRect>("VBox/IntentRow/IntentIcon");
        _intentLabel = GetNode<Label>("VBox/IntentRow/IntentLabel");
        _debuffIcon = GetNode<TextureRect>("VBox/IntentRow/DebuffIcon");
        _statusRow = GetNode<HBoxContainer>("VBox/StatusRow");
        _sprite.Texture = ArtAssets.EnemySprite(Combatant.Definition.Id);
        _sprite.PivotOffset = _sprite.Size / 2f;
        _nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _hpLabel.ThemeTypeVariation = "CombatDisplayLabel";
        ChromeStyles.ApplyHpBarStyle(_hpBar, _ghostHpBar);
        Pressed += OnPressed;
        MouseEntered += ShowIntentTooltip;
        MouseExited += HideIntentTooltip;
        Instances.Add(this);
        Refresh();
        StartIdleBob();
        StartIntentPulse();
    }

    // Soft elliptical contact shadow - a non-square radial gradient reads as
    // an ellipse rather than a circle. Purely a per-sprite decoration (not
    // tied to a shared floor line with the player's own sprite, which uses
    // a different positioning mechanism entirely - see Phase 4 plan notes).
    private static Texture2D BuildShadowTexture()
    {
        var gradient = new Gradient
        {
            Offsets = new float[] { 0f, 1f },
            Colors = new Color[] { new(0f, 0f, 0f, 0.55f), new(0f, 0f, 0f, 0f) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 20,
        };
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

    // Slow idle opacity breathing on the intent icon, same "still alive and
    // relevant" cue the sprite's idle bob gives the enemy itself.
    private void StartIntentPulse()
    {
        _intentPulseTween?.Kill();
        _intentIcon.Modulate = Colors.White;
        var tween = _intentIcon.CreateTween();
        _intentPulseTween = tween;
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(_intentIcon, "modulate:a", 0.6f, 0.9);
        tween.TweenProperty(_intentIcon, "modulate:a", 1f, 0.9);
    }

    // Pop when the telegraphed intent actually changes between refreshes
    // (e.g. after this enemy's turn resolves and it picks its next move),
    // instead of the icon/number silently swapping mid-idle-pulse.
    private void PlayIntentChangeFlash()
    {
        _intentPulseTween?.Kill();
        _intentIcon.Modulate = Colors.White;
        _intentIcon.PivotOffset = _intentIcon.Size / 2f;
        _intentIcon.Scale = Vector2.One * 1.5f;
        var tween = _intentIcon.CreateTween();
        tween.TweenProperty(_intentIcon, "scale", Vector2.One, 0.2).SetTrans(Tween.TransitionType.Back);
        tween.TweenCallback(Callable.From(StartIntentPulse));
    }

    // Whole-view fade/shrink/slump on death (not just the sprite) - unlike
    // the hit/idle animations, the enemy is leaving the fight entirely, so
    // animating the whole card (name/HP/status included) reads better than
    // just the portrait reacting. Safe to animate Scale/Rotation/Modulate on
    // this Button directly since only Position/Size are Container-managed.
    public void PlayDeathTween(System.Action onComplete)
    {
        AudioManager.Instance?.PlaySfx("enemy_death");
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "scale", Vector2.One * 0.7f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "rotation_degrees", 10f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "modulate:a", 0f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.Chain().TweenCallback(Callable.From(onComplete));
    }

    public override void _ExitTree()
    {
        HideIntentTooltip();
        Instances.Remove(this);
    }

    // What the enemy is about to do, in the same stacked panel a card explains
    // its keywords in - the standard the reference genre sets, and the reason
    // HoverTooltip was pulled out of CardView. Before this, the only thing
    // explaining an intent was a 20x20 debuff badge with a stock Godot tooltip
    // reading "Inflicts Weak"; the damage number and hit count on the row said
    // what would land but never what kind of move it was.
    //
    // The first box is the move: its intent type as the title, and its own
    // EffectSpecs rendered in the enemy's voice as the body. Every box after it
    // is a keyword that sentence mentions, identical to a card's. So an enemy
    // applying Frail and a card applying Frail explain it with the same words.
    private void ShowIntentTooltip()
    {
        if (_intentTooltip is not null) return;
        if (Combatant.CurrentMove is not { } move) return;

        var (title, color) = IntentLabel(move.Intent.Type);
        string body = DescribeMove(move, Combatant, CombatManager.Instance?.Player);

        var boxes = new List<Keywords.Entry> { new(title, color, body) };
        boxes.AddRange(Keywords.Find(body));
        // Beside, not above: this view is 220x300 with its intent row on the
        // top edge, so the card placement's "above, flipping below when it
        // doesn't fit" always flipped - and below a 300px-tall enemy is the
        // hand. See HoverTooltip.Placement.
        _intentTooltip = HoverTooltip.Show(this, boxes, HoverTooltip.Placement.BesideAnchor);
    }

    private void HideIntentTooltip()
    {
        if (_intentTooltip is null) return;
        if (IsInstanceValid(_intentTooltip)) _intentTooltip.Dismiss();
        _intentTooltip = null;
    }

    // Static, and taking source/target the way FormatIntent already does, so
    // Phase4ContentSmokeTest can sweep every move of every enemy without
    // standing up a scene - the same reason the telegraph label is static.
    public static string DescribeMove(EnemyMove move, Combatant source, Combatant? target) =>
        EffectDescriptionFormatter.Describe(move.Effects, new DescribeContext(
            Source: source,
            // The real player, not null: passing them resolves their live
            // Vulnerable into the printed number, rather than leaving the move
            // on the base numbers an un-aimed card shows - the same reason
            // LiveAttackAmount takes a target for the row label.
            Targets: target is null ? null : new[] { target },
            Voice: DescribeVoice.Enemy));

    // Debuff shares Attack's oxblood: both are the enemy doing something to
    // you, and the title only has to separate the four types from each other,
    // which the word already does.
    private static (string Title, Color Color) IntentLabel(IntentType type) => type switch
    {
        IntentType.Attack => ("Attack", UiTheme.Palette.Damage),
        IntentType.Defend => ("Defend", UiTheme.Palette.Block),
        IntentType.Buff => ("Buff", UiTheme.Palette.StatusBuff),
        IntentType.Debuff => ("Debuff", UiTheme.Palette.StatusDebuff),
        _ => ("Intent", UiTheme.Palette.AccentGold),
    };

    // Toggled continuously by CardView while dragging a SingleEnemy card.
    //
    // Unlocking restores the empty box rather than calling
    // RemoveThemeStyleboxOverride: _Ready installs a StyleBoxEmpty so enemies
    // stand free on the backdrop, and removing the override would fall back
    // to the theme's Button stylebox and paint the panel this deliberately
    // got rid of.
    public void SetTargetLocked(bool locked)
    {
        AddThemeStyleboxOverride("normal", locked ? TargetLockStyle : new StyleBoxEmpty());

        // Keyboard target-cycling gets the intent tooltip too. This view opts
        // out of Godot's focus navigation (see _Ready), so the lock *is* its
        // keyboard-selected state - the same reason CombatScreen drives the
        // hand's hover visual from SetHighlighted rather than from focus.
        if (locked) ShowIntentTooltip();
        else HideIntentTooltip();
    }

    // For CombatTargetingSmokeTest: the lock state is now a question of which
    // stylebox is installed, not whether one is, since there is always one.
    public bool IsTargetLocked => GetThemeStylebox("normal") is not StyleBoxEmpty;

    public void Refresh()
    {
        _nameLabel.Text = Combatant.Name;
        _hpBar.MaxValue = Combatant.MaxHp;
        _ghostHpBar.MaxValue = Combatant.MaxHp;
        ChromeStyles.TweenHpBar(_hpBar, _ghostHpBar, ref _ghostHpTween, Combatant.CurrentHp);
        _hpLabel.Text = $"{Combatant.CurrentHp}/{Combatant.MaxHp}" +
                         (Combatant.Block > 0 ? $"  🛡{Combatant.Block}" : "");
        var intent = Combatant.CurrentMove?.Intent;
        _intentIcon.Texture = intent is null ? null : ArtAssets.IntentIcon(intent.Type);
        _intentIcon.Visible = _intentIcon.Texture is not null;
        _intentLabel.Text = FormatIntent(Combatant.CurrentMove, Combatant, CombatManager.Instance?.Player);

        // Skip the flash on this view's very first Refresh() (called from
        // _Ready before StartIntentPulse has even run yet) - only an actual
        // change between refreshes should pop, not the enemy's initial reveal.
        string? currentMoveId = Combatant.CurrentMove?.MoveId;
        if (_lastMoveId is not null && currentMoveId != _lastMoveId && _intentIcon.Visible)
        {
            PlayIntentChangeFlash();
            // An open tooltip is describing the move that just changed. Rebuild
            // rather than leave it: an intent telegraph that has gone stale is
            // the canonical bad bug in this genre, and it is worse in prose
            // than on the row, because the prose is what the player believed.
            if (_intentTooltip is not null)
            {
                HideIntentTooltip();
                ShowIntentTooltip();
            }
        }
        _lastMoveId = currentMoveId;

        // Attack moves that also debuff the player (Acid Slime's corrode,
        // the boss's shadow_lash, etc.) otherwise looked identical to a
        // plain attack until it landed - a small badge of the actual status
        // icon telegraphs it up front, same as the main intent icon does
        // for damage/block/buff.
        // On a Debuff intent the status *is* the move, so "Also" would be a
        // lie in the other direction.
        //
        // It carries no tooltip of its own any more. It used to be the only
        // explanation an intent had ("Inflicts Weak"), and now the hover panel
        // says the whole move including that status and what it does. Leaving
        // it would fire a second, worse tooltip alongside the panel - this is
        // also the only child in the tree with a non-Ignore mouse filter, so
        // stopping it from taking hover is what keeps the panel the one answer.
        var debuffStatus = IncomingDebuffStatus(Combatant.CurrentMove);
        _debuffIcon.Texture = debuffStatus is null ? null : ArtAssets.StatusIcon(debuffStatus.Value);
        _debuffIcon.Visible = _debuffIcon.Texture is not null;
        _debuffIcon.MouseFilter = MouseFilterEnum.Ignore;

        StatusRow.Populate(_statusRow, Combatant, 16, _lastStatuses);
        _lastStatuses = new Dictionary<StatusType, int>(Combatant.Statuses);
    }

    // DisplayAmount is the move's hand-authored base amount (matches its
    // deal_damage effect's own base Amount, same "authored redundancy" the
    // card side avoids via EffectDescriptionFormatter) - recompute through
    // the same DamageMath the real resolution and card previews use so an
    // Attack intent always shows what would actually land right now (the
    // enemy's own Strength/Weak, and the player's current Vulnerable - a
    // real fact already in effect, since an intent always knows who it is
    // aimed at).
    // The label reads the whole move, not just the intent: how many hits it is
    // and which status it grants are facts about the effects, and deriving them
    // is what makes a telegraph structurally unable to lie. Only DisplayAmount
    // is authored, and only because it is the number a designer tunes.
    public static string FormatIntent(EnemyMove? move, Combatant source, Combatant? target)
    {
        var intent = move?.Intent;
        if (intent is null || move is null) return "";
        return intent.Type switch
        {
            IntentType.Attack => FormatAttack(intent, move, source, target),
            IntentType.Defend => "",
            IntentType.Buff => $"+{intent.DisplayAmount} {SelfGrantName(move)}",
            // The status icon badge beside this label names *which* debuff, so
            // repeating it here would only cost width the enemy row hasn't got.
            IntentType.Debuff => $"{intent.DisplayAmount}",
            _ => "",
        };
    }

    private static string FormatAttack(EnemyIntent intent, EnemyMove move, Combatant source, Combatant? target)
    {
        int amount = LiveAttackAmount(intent.DisplayAmount, source, target);
        int hits = HitCount(move);
        return hits > 1 ? $"{amount} x{hits}" : $"{amount}";
    }

    // A multi-hit is a run of identical deal_damage specs - the same shape
    // EffectDescriptionFormatter collapses into "twice"/"N times" for cards,
    // asked through its own SameEffect so the two can't drift apart.
    private static int HitCount(EnemyMove move)
    {
        var effects = move.Effects;
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i].Action != "deal_damage") continue;
            int hits = 1;
            while (i + hits < effects.Count && EffectDescriptionFormatter.SameEffect(effects[i], effects[i + hits])) hits++;
            return hits;
        }
        return 1;
    }

    // Short enough for the intent row, which shares an enemy's ~220px column
    // with the icon and the debuff badge. Falls back to the status's own name
    // so a tenth status telegraphs something true rather than "Str".
    private static string SelfGrantName(EnemyMove move)
    {
        var grant = move.Effects.FirstOrDefault(e =>
            e.Scope == EffectScope.Self && (e.Action == "apply_status" || e.Action == "heal"));
        if (grant is null) return "Str";
        if (grant.Action == "heal") return "HP";
        return grant.Status switch
        {
            "Strength" => "Str",
            "Dexterity" => "Dex",
            "Metallicize" => "Metal",
            null => "Str",
            var other => other,
        };
    }

    private static int LiveAttackAmount(int baseAmount, Combatant source, Combatant? target)
    {
        int amount = DamageMath.ComputeOutgoing(baseAmount, source);
        return target is null ? amount : DamageMath.ApplyVulnerable(amount, target);
    }

    private static StatusType? IncomingDebuffStatus(EnemyMove? move)
    {
        var effect = move?.Effects.FirstOrDefault(e =>
            e.Action == "apply_status" && e.Scope == EffectScope.Target &&
            e.Status is not null && System.Enum.TryParse<StatusType>(e.Status, out var parsed) &&
            StatusRow.IsDebuff(parsed));
        if (effect?.Status is null) return null;
        return System.Enum.Parse<StatusType>(effect.Status);
    }

    private void OnPressed() => CombatManager.Instance?.TryTargetEnemy(Combatant);
}
