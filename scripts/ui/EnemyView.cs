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
    //
    // The box itself is ChromeStyles.TargetLockStyle now, and it is repainted
    // every frame by this ring rather than installed once - ART_SPEC section 6's
    // animated pixel border. Null whenever the view is unlocked, which is the
    // one piece of state SetTargetLocked has to keep straight.
    private GlowRing? _targetLockRing;

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
    private SpriteAnimator? _animator;
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
        _animator = SpriteAnimator.Attach(_sprite, Combatant.Definition.Id, SpriteAnimator.CreatureClips);
        _nameLabel.ThemeTypeVariation = "CombatDisplayLabel";
        _hpLabel.ThemeTypeVariation = "CombatDisplayLabel";
        ChromeStyles.ApplyHpBarStyle(_hpBar, _ghostHpBar);
        Pressed += OnPressed;
        MouseEntered += ShowIntentTooltip;
        MouseExited += HideIntentTooltip;
        Instances.Add(this);
        Refresh();
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

    // The breathing loop, the hit recoil and the wind-up lean are all frame
    // clips now (tools/artgen/src/anim.rs, played by SpriteAnimator).
    //
    // They were scale/rotation tweens - 1.04, 1.15 plus 6 degrees, 1.08 - and
    // the comment that used to sit here explained the choice: _sprite is inside
    // a VBoxContainer, which owns position and size, so Scale was "the one
    // transform a container does not touch". The reasoning was sound and the
    // result violated ART_SPEC section 2 on every frame it played, because a
    // 32px source at scale 1.04 renders 5.2 device pixels per source pixel.
    //
    // A frame swap is not a transform, so the container objection never arises
    // and the sprite is at an integer scale by construction. Nothing here
    // writes _sprite.Scale or _sprite.RotationDegrees any more, and
    // PixelSpecSmokeTest.TestNoTweenTransformsAPixelSprite fails the build if
    // anything starts again.
    public void PlayHitRecoil() => _animator?.Play("hit");

    // Brief telegraph lean while CombatManager's wind-up delay plays out,
    // so an attack reads as building up before it lands.
    public void PlayWindUp() => _animator?.Play("windup");

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
    //
    // The pop was a 1.5x -> 1.0 Scale tween, and the intent icon is a 32x32
    // pixel asset drawn at HudIconScale, so it was the same ART_SPEC section 2
    // violation the sprite clips exist to end - on a smaller node and therefore
    // easier to miss. Brightness carries the same "this just changed" beat
    // without a geometric transform, and it is the emphasis channel ART_SPEC
    // section 6 already reaches for when a blur is unavailable.
    private void PlayIntentChangeFlash()
    {
        _intentPulseTween?.Kill();
        _intentIcon.Modulate = Colors.White;
        var tween = _intentIcon.CreateTween();
        tween.TweenProperty(_intentIcon, "modulate", new Color(2f, 2f, 2f), 0.06);
        tween.TweenProperty(_intentIcon, "modulate", Colors.White, 0.18);
        tween.TweenCallback(Callable.From(StartIntentPulse));
    }

    // Death is two layers, and the split is the point. The *sprite* comes
    // apart on its own frame clip; the rest of the card - name, HP bar, status
    // row - fades, because the enemy is leaving the fight entirely and
    // animating only the portrait would leave its furniture standing.
    //
    // The fade is all that is left of the old tween. It used to also shrink to
    // 0.7 and rotate 10 degrees, which resampled the pixel sprite riding inside
    // it; alpha is not a geometric transform, so it resamples nothing and is
    // the one property a pixel asset may still be tweened on (ART_SPEC 9).
    public void PlayDeathTween(System.Action onComplete)
    {
        AudioManager.Instance?.PlaySfx("enemy_death");
        _animator?.PlayTerminal("death");
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(onComplete));
    }

    // The other way out of a fight, and it has to look like a different event
    // than dying: an enemy that fled is one the player failed to kill, and the
    // death tween's collapse-and-fade would read as a kill they did get. So it
    // *leaves* - sideways, at speed, no death SFX, no slump.
    //
    // Same completion-callback shape as PlayDeathTween because CombatScreen
    // drives both from the same diff of Enemies against _enemyViews.
    //
    // The old version squeezed the whole view to (0.15, 0.85) from a right-edge
    // pivot, and its comment explained why it could not simply move the thing:
    // this node is a child of EnemyRow, an HBoxContainer owns its children's
    // position, and a position tween would be overwritten on the next sort.
    //
    // The frame clip walks the creature out of its own 32x32 cell instead, so
    // an escape finally *travels* rather than being crushed sideways - and it
    // needs no transform at all, which is what makes it expressible inside a
    // container in the first place.
    public void PlayEscapeTween(System.Action onComplete)
    {
        _animator?.PlayTerminal("escape");
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.35).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(onComplete));
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
        // The two that change who is in the fight. Summon borrows the buff
        // colour because that is what it is from the enemy side - the room
        // getting better for them - and Escape takes the gold that every other
        // "this costs you something you own" surface in the game uses.
        IntentType.Summon => ("Summon", UiTheme.Palette.StatusBuff),
        IntentType.Escape => ("Escape", UiTheme.Palette.AccentGold),
        // The one intent that is not aimed at the player, and the only one on a
        // cold neutral rather than a signal colour: nothing is coming yet, and
        // the row must not read as a threat the way the buff gold would.
        IntentType.Dormant => ("Dormant", PixelSpec.Ramp.N6),
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
        // Stop the old ring before either branch touches the stylebox. A ring
        // left running for even one more frame repaints "normal" *after* the
        // unlock below put the empty box back, and IsTargetLocked is defined as
        // "not StyleBoxEmpty" - so the stale tick is an enemy that reports
        // itself locked with nothing aimed at it. GlowRing.Stop is
        // non-deferred for exactly this.
        //
        // It runs on the lock branch too, so a second SetTargetLocked(true)
        // cannot stack two drivers on one view. Neither caller can reach that
        // today - UpdateTargetHighlight early-returns on an unchanged target and
        // SetKeyboardTarget always unlocks the previous view first - which makes
        // this a guard against the third caller rather than against the two.
        _targetLockRing?.Stop();
        _targetLockRing = null;

        if (locked)
        {
            _targetLockRing = GlowRing.Attach(
                this, new[] { "normal" }, c => ChromeStyles.TargetLockStyle(c), GlowRing.Gold);
        }
        else
        {
            AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        }

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

    // Sizes the name may render at, largest first. Two rungs and no third:
    // ART_SPEC section 6 puts every rendered size on the 8px design em, so the
    // only step between Heading and Body is 8, which is unreadable at arm's
    // length. TrimChar underneath both is the last resort, and it is still
    // reachable - a 20-character name in a four-enemy group has 194px.
    private static readonly int[] NameFontSizes = { UiTheme.Fonts.Heading, UiTheme.Fonts.Body };

    // The width CombatScreen.FitEnemiesToTheRow last handed this view, which is
    // what the name has to fit inside. Tracked rather than read off Size,
    // because Refresh() runs before the container has sorted (from _Ready, and
    // from RefreshEnemies ahead of the fit pass) and would measure a zero or a
    // stale box. Seeded with EnemyView.tscn's own authored minimum so the very
    // first Refresh has a real number.
    private float _cellWidth = 220f;

    /// Called by CombatScreen.FitEnemiesToTheRow with the width the row can
    /// afford this enemy. Refits the name, because "does the name fit" is a
    /// question about exactly this number.
    public void SetCellWidth(float width)
    {
        _cellWidth = width;
        CustomMinimumSize = new Vector2(width, CustomMinimumSize.Y);
        FitNameToCell();
    }

    private void FitNameToCell() => TextFit.Apply(_nameLabel, _cellWidth, NameFontSizes);

    public void Refresh()
    {
        _nameLabel.Text = Combatant.Name;
        FitNameToCell();
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
            // What arrives is a fact about the effects, exactly like a Buff's
            // status name: the authored number is only how many.
            IntentType.Summon => SummonName(move, intent.DisplayAmount),
            // The gold is the only thing an escape costs the player, so it is
            // the whole label. A move that steals nothing shows nothing, and
            // the "Escape" title carries it.
            IntentType.Escape => StolenGold(move) is int gold and > 0 ? $"-{gold}g" : "",
            // A sleeper's row says what it gains while it is left alone, which
            // is a Buff's label under a different icon - deliberately, because
            // the number is the price of not waking it and the player has to be
            // able to read that price. The icon carries the rest.
            IntentType.Dormant => $"+{intent.DisplayAmount} {SelfGrantName(move)}",
            _ => "",
        };
    }

    // The summoned enemy's own Name, so a mis-authored id telegraphs nothing
    // rather than something false - the same Find-not-Get tolerance
    // EffectDescriptionFormatter's add_card arm takes, and for the same reason:
    // this runs on a live combat screen.
    private static string SummonName(EnemyMove move, int count)
    {
        var spec = move.Effects.FirstOrDefault(e => e.Action == "summon_enemy");
        var summoned = spec?.EnemyId is { Length: > 0 } id ? EnemyDatabase.Find(id) : null;
        if (summoned is null) return "";
        return count > 1 ? $"{count}x {summoned.Name}" : summoned.Name;
    }

    // Positive: what the player loses, not what the enemy gains. The spec
    // itself is authored negative (gain_gold is one action, and a second
    // "steal_gold" differing only by a sign would be two places to change),
    // and this is the one place that sign is turned back around for display.
    private static int StolenGold(EnemyMove move)
    {
        var spec = move.Effects.FirstOrDefault(e => e.Action == "gain_gold" && e.Amount < 0);
        return spec is null ? 0 : -spec.Amount;
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
        return target is null ? amount : DamageMath.ApplyIncoming(amount, target);
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
