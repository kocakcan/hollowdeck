using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Effects;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The post-fight reward, as a list the player claims from rather than a fan of
// cards that leaves the moment one is clicked.
//
// The old screen banked the gold and granted the relic before it ever loaded,
// showed both as labels, and exited on the first card click. That worked while
// a card was the only thing being offered and stopped working the moment a
// second claimable thing (the potion drop) landed on it: whichever the player
// touched first silently forfeited the other. Patching that - a guard on the
// card pick, a retitled button - treats the symptom; the screen was a fan
// pretending to be a reward list.
//
// So every reward is a row, claiming a row removes it, and one button leaves.
// Two things fall out for free: nothing can be forfeited by clicking in the
// wrong order, and the two items ROADMAP Phase 9 still has for this screen -
// the boss relic as a choice of three, and a skip streak on card rewards - are
// rows rather than another special case.
public partial class RewardScreen : Control
{
    private const float CardWidth = 224f;
    private const float MaxFanRotationDeg = 8f;
    private const float FanArcHeight = 20f;

    // A 32x32 icon at 2x plus padding, which is the floor for every row
    // regardless of how much text it carries.
    private const float RowHeight = 72f;

    // Kept rather than discarded, because claiming a row sets
    // FocusModeEnum.None on the button that was just pressed - which *releases*
    // the focus it holds (CLAUDE.md's measured note, pinned by
    // KeyboardSmokeTest) and leaves the ring nowhere. Regrab is what puts it
    // back on a legal control.
    private ScreenKeyboardNavListener? _keyboardNav;

    private VBoxContainer _rowList = null!;
    private Control _cardOverlay = null!;
    private Button _skipButton = null!;
    private Control _tipLine = null!;
    private readonly Dictionary<RewardKind, Button> _rowButtons = new();

    // The rules line under a row's name, alongside the text it says when the
    // row is claimable - so a row that has to explain a refusal can put the
    // reason where the rules text was and then put the rules text back.
    private readonly Dictionary<RewardKind, (Label Label, string Detail)> _rowDetails = new();

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "crypt", new Color(0.6f, 0.6f, 0.65f));
        DeckViewButtons.Attach(this);
        BuildActClearedBanner();

        _cardOverlay = GetNode<Control>("CardOverlay");
        _skipButton = GetNode<Button>("SkipButton");
        _skipButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _skipButton.Pressed += OnSkipPressed;

        var cancel = GetNode<Button>("CardOverlay/CardCancelButton");
        cancel.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        cancel.Pressed += CloseCardOverlay;

        BuildTipLine();
        BuildRewardList();

        // The first unclaimed row, not Skip: taking the rewards is the point of
        // the screen. Resolved through a lambda rather than a captured node
        // because Regrab re-runs it after a claim, by which time the row that
        // was first has been disabled and the answer has changed.
        _keyboardNav = ScreenKeyboardNav.Attach(this, FirstClaimableControl, OnCancelPressed);
    }

    // Focus goes into the overlay while it is open, and back to the list when
    // it is not. One function rather than re-Attaching, so there is a single
    // answer to "what does this screen focus" at any moment.
    private Control? FirstClaimableControl()
    {
        if (_cardOverlay.Visible)
        {
            return GetNode<Control>("CardOverlay/CardChoicesArea").GetChildren()
                .OfType<CardView>().FirstOrDefault();
        }

        return _rowList.GetChildren().OfType<Button>().FirstOrDefault(b => !b.Disabled)
            ?? (Control)_skipButton;
    }

    // Escape backs out of the card overlay rather than leaving the screen while
    // it is open - the overlay is a second view, and cancelling out of a view
    // that has its own Back button should do what that button does.
    private void OnCancelPressed()
    {
        if (_cardOverlay.Visible) CloseCardOverlay();
        else OnSkipPressed();
    }

    // Every fight's reward screen is titled "Victory Reward", which is fine
    // after a normal fight and actively misleading after a boss: a player who
    // has just killed one and is told "Victory" has every reason to think the
    // run is over, and the only thing that would have said otherwise - a new
    // map, one act further along - looks like the game restarting. So a boss
    // that ended an act retitles the screen with the act it ended, and names
    // both the act coming next and the max-HP bonus and heal that clearing one
    // silently granted (RunState.ActClear).
    private void BuildActClearedBanner()
    {
        var label = GetNode<Label>("TitleBlock/ActLabel");
        if (RewardContext.ActCleared is not { } cleared)
        {
            label.Visible = false;
            return;
        }

        GetNode<Label>("TitleBlock/TitleLabel").Text = $"Act {cleared.ClearedNumber} Cleared";

        // Act 3 grants neither bonus, and act 3's boss never reaches this
        // screen anyway - but the parts are dropped by value rather than by act
        // so a data change can't produce "+0 Max HP".
        var gains = new List<string>();
        if (cleared.MaxHpBonus > 0) gains.Add($"+{cleared.MaxHpBonus} Max HP");
        if (cleared.Healed > 0) gains.Add($"healed {cleared.Healed}");

        // ASCII only, like every other string in the game's UI: the pixel faces
        // in PixelSpec carry no punctuation past it (ART_SPEC section 5).
        string next = $"Next: Act {cleared.NextNumber} of {cleared.TotalActs} - {cleared.NextName}";
        label.Visible = true;
        label.Text = gains.Count > 0 ? $"{string.Join(", ", gains)}. {next}" : next;
        label.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
    }

    /// The node name of the tip's text label, for the same reason RowName
    /// exists: a test should not have to know how this screen lays itself out.
    public const string TipLabelName = "TipLabel";

    // One line of advice under the reward list, which is where the genre puts
    // it and for a reason worth stating: this is the only screen in a run where
    // the player is stationary, has already won, and is reading anyway. A tip
    // on the map competes with a route decision; a tip here competes with
    // nothing.
    //
    // It sits in the band between the framed list and the Skip button, which
    // was empty at every row count - the list is centred in its own area, so
    // even a four-row fight bottoms out about 100px above the button.
    private void BuildTipLine()
    {
        _tipLine = GetNode<Control>("TipLine");
        var tag = GetNode<Label>($"TipLine/TipTag");
        var label = GetNode<Label>($"TipLine/{TipLabelName}");

        tag.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        // N5, the same quiet the reward rows' own detail lines and the main
        // menu's version stamp use. A tip is not the subject of this screen and
        // must not out-read the rewards it sits under.
        label.AddThemeColorOverride("font_color", PixelSpec.Ramp.N5);

        // Advances with the run rather than with this screen's lifetime, so
        // re-entering a reward (or reloading a save) shows the same tip and the
        // *next* fight shows the next one. See TipDatabase.ForVisit for why
        // this is a rotation off the run seed rather than an RngStreams draw.
        var tip = TipDatabase.ForVisit(RunManager.Instance?.RunSeed ?? 0, RunState.VisitedNodeIds.Count);
        if (tip is null)
        {
            // No tips authored, or a suite that skipped TipDatabase.LoadAll.
            // Hide rather than throw: an absent tip is not worth a broken
            // screen, and the data audit in EffectSmokeTest is what fails if
            // the file is genuinely missing.
            _tipLine.Visible = false;
            return;
        }

        label.Text = ScreenKeyboardNav.ResolveKeyHints(tip.Text);
    }

    // One framed column of rows, in RewardKind's declaration order. A reward
    // that was not offered has no row at all; a reward already claimed keeps
    // its row, greyed, so the screen still says what the fight paid.
    private void BuildRewardList()
    {
        var area = GetNode<Control>("RewardListArea");
        foreach (var child in area.GetChildren()) child.QueueFree();
        _rowButtons.Clear();
        _rowDetails.Clear();

        _rowList = new VBoxContainer();
        _rowList.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Sm);

        if (RewardContext.GoldAwarded > 0)
        {
            // The treasure chest, which is already what "gold is in here" looks
            // like everywhere else in the game (the map's Treasure node).
            AddRow(RewardKind.Gold, ArtAssets.MapIcon(MapNodeType.Treasure),
                $"{RewardContext.GoldAwarded} Gold", "", UiTheme.Palette.AccentGoldBright);
        }

        if (RewardContext.GuaranteedRelic is { } relic)
        {
            // Same spelled-out treatment the potion row below documents, and
            // the tier matters more here than it does there: an elite and a
            // boss now pay from different pools, and "(Boss)" beside the name
            // is the only place the player is told which one this came out of.
            AddRow(RewardKind.Relic, ArtAssets.RelicIcon(relic.Id),
                $"{relic.Name} ({relic.Tier})", relic.Description,
                ChromeStyles.RelicTierColor(relic.Tier));
        }

        if (RewardContext.PotionDrop is { } potion)
        {
            // The tier is spelled out rather than left to the frame colour: a
            // border can only be read by a player who has already learned the
            // palette, and this is the first screen a potion rarity is shown on
            // at all.
            AddRow(RewardKind.Potion, ArtAssets.PotionIcon(potion.Id),
                $"{potion.Name} ({potion.Rarity})",
                EffectDescriptionFormatter.Describe(potion.Effects,
                    new DescribeContext(TargetType: potion.Target)),
                ChromeStyles.RarityColor(potion.Rarity));
        }

        if (RewardContext.CardChoices.Count > 0)
        {
            // Deliberately iconless: there is no card-back icon in the set, and
            // the alternatives all lie - a map node's sword says "fight" and
            // one of the offered cards' own art says "this card" when the row
            // is about choosing between three. The 72px slot is still laid out,
            // so the text column stays aligned with the rows above it.
            AddRow(RewardKind.Card, null, "Add a card to your deck", "");
        }

        // Centred in the band rather than pinned to its top, so a fight that
        // pays one row and a fight that pays four both sit in the same place
        // instead of the short list riding high with its own height of empty
        // space beneath it.
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        centre.AddChild(ScreenChrome.Frame(_rowList, UiTheme.Spacing.Md));
        area.AddChild(centre);
        RefreshRows();
    }

    /// The node name a reward row is given, so a test or a screenshot fixture
    /// can reach one without knowing how many rows this fight happened to
    /// offer. Find it with FindChild(RowName(kind), recursive: true,
    /// owned: false) - the rows are built in code, so they have no scene owner.
    public static string RowName(RewardKind kind) => $"{kind}Row";

    // Icon, name, and a muted second line where the reward has rules text -
    // the same shape ShopScreen's offer tiles use, turned on its side because a
    // list reads down and a shop reads across.
    private void AddRow(RewardKind kind, Texture2D? icon, string name, string detail, Color? nameColor = null)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(460, 0),
            // The row's own contents are drawn by the children below, so the
            // Button is the frame and the hit target rather than the label.
            Text = "",
            // Named so the smoke tests can address a row by the reward it is
            // for rather than by its index in a list whose length depends on
            // what the fight happened to drop. RowName is the one place that
            // convention is written down.
            Name = RowName(kind),
        };
        ChromeStyles.ApplyEmphasisButtonStyle(button);

        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Md);
        row.AddThemeConstantOverride("margin_left", (int)UiTheme.Spacing.Sm);

        var iconSlot = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        // 2x, matching the shop tile and the map's node icons.
        if (icon is not null) iconSlot.AddChild(ScreenChrome.PixelIcon(icon, 2));
        iconSlot.CustomMinimumSize = new Vector2(72, 0);
        row.AddChild(iconSlot);

        var text = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        text.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);

        var nameLabel = ScreenChrome.Heading(name, UiTheme.Fonts.Body);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        if (nameColor is { } tint) nameLabel.AddThemeColorOverride("font_color", tint);
        text.AddChild(nameLabel);

        if (detail.Length > 0)
        {
            var detailLabel = ScreenChrome.Body(detail);
            detailLabel.HorizontalAlignment = HorizontalAlignment.Left;
            detailLabel.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Small);
            detailLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N5);
            detailLabel.MouseFilter = MouseFilterEnum.Ignore;
            text.AddChild(detailLabel);
            _rowDetails[kind] = (detailLabel, detail);
        }

        row.AddChild(text);
        button.AddChild(row);

        // One height for every row, whether or not it carries a rules line. The
        // icon is 32x32 at 2x, so a row sized to its text alone is shorter than
        // its own icon - the gold row (no rules text) was 52px and the chest
        // spilled over the border at the top.
        button.CustomMinimumSize = new Vector2(460, RowHeight);

        button.Pressed += () => OnRowPressed(kind);
        _rowList.AddChild(button);
        _rowButtons[kind] = button;
    }

    // Disabled and FocusModeEnum.None move together on a claimed row. Disabled
    // alone excludes a control from neither Tab nor arrow navigation and does
    // not release focus it already holds - measured on 4.7.1, pinned by
    // KeyboardSmokeTest, and the reason the focus ring once parked on a map
    // node the player could not walk to.
    private void RefreshRows()
    {
        // While the card fan is open it is a modal: the dim swallows the mouse,
        // and the list has to leave the *focus* chain too or Tab walks straight
        // back out behind it onto rows the player cannot see. Not Disabled -
        // they are not illegal choices, just not this view's - so they keep
        // their colour under the dim rather than greying out for a reason that
        // will be gone in a moment.
        bool listIsBehindTheOverlay = _cardOverlay.Visible;

        // The scrim alone is not enough to push the list back. It is the same
        // 0.75 black PileViewPopup and LibraryInspectPopup use, and it works
        // for them because what shows through is body text on a dark screen;
        // here it is a column of gold headings sitting directly behind the fan,
        // which stayed perfectly legible and competed with the cards. Fading
        // the list as well is what turns it into context. Same failure the
        // event picker had (its event text read through the gaps in the grid),
        // and the same reason it is only visible in a screenshot.
        GetNode<Control>("RewardListArea").Modulate = listIsBehindTheOverlay
            ? new Color(1f, 1f, 1f, 0.3f)
            : Colors.White;

        // Hidden rather than faded with the list. The tip sits directly under
        // the fan's Back button, and a dimmed line of body text behind a modal
        // reads as something the player failed to dismiss. It has nothing to do
        // with choosing a card, so while that is the question it is not on
        // screen at all. Guarded on Text because a run with no tip authored
        // already hid it in BuildTipLine and must stay hidden.
        if (_tipLine is not null && GetNode<Label>($"TipLine/{TipLabelName}").Text.Length > 0)
        {
            _tipLine.Visible = !listIsBehindTheOverlay;
        }

        foreach (var (kind, button) in _rowButtons)
        {
            bool claimed = RewardContext.Claimed.Contains(kind);
            bool blocked = !claimed && kind == RewardKind.Potion && BeltIsFull;

            button.Disabled = claimed || blocked;
            button.FocusMode = button.Disabled || listIsBehindTheOverlay
                ? FocusModeEnum.None
                : FocusModeEnum.All;
            button.Modulate = claimed ? new Color(1f, 1f, 1f, 0.4f) : Colors.White;
            button.TooltipText = blocked ? BeltFullReason : "";

            // The reason goes on the row, not only in the tooltip. A blocked
            // row is FocusModeEnum.None, so a keyboard player cannot reach it
            // to raise a tooltip at all - and Godot's disabled styling on its
            // own reads as "slightly different", not as "you cannot take this".
            // This is the same mouse-only parity gap CLAUDE.md flags for
            // StatusRow, arriving somewhere it can be answered in one line.
            if (_rowDetails.TryGetValue(kind, out var detail))
            {
                detail.Label.Text = blocked ? BeltFullReason : detail.Detail;
                detail.Label.AddThemeColorOverride("font_color",
                    blocked ? UiTheme.Palette.AccentGoldBright : PixelSpec.Ramp.N5);
            }
        }

        _skipButton.FocusMode = listIsBehindTheOverlay ? FocusModeEnum.None : FocusModeEnum.All;
        RefreshExitButton();
    }

    private static bool BeltIsFull => RunState.Potions.Count >= RunState.MaxPotionSlots;

    private static string BeltFullReason =>
        $"Belt full ({RunState.MaxPotionSlots}) - drink one in a fight to make room.";

    // "Skip Rewards" only reads right while something is still on offer. Once
    // every row is claimed there is nothing left to skip, and calling it Skip
    // would suggest the player is about to give up what they just took.
    private void RefreshExitButton()
    {
        bool anythingLeft = _rowButtons.Any(pair => !RewardContext.Claimed.Contains(pair.Key));
        _skipButton.Text = anythingLeft ? "Skip Rewards" : "Continue";
    }

    private void OnRowPressed(RewardKind kind)
    {
        // Guarded on the run's own state rather than the Button's, so a row
        // cannot pay out twice even if the refresh below were ever skipped.
        if (RewardContext.Claimed.Contains(kind)) return;

        switch (kind)
        {
            case RewardKind.Gold:
                RunState.Gold += RewardContext.GoldAwarded;
                break;

            case RewardKind.Relic:
                if (RewardContext.GuaranteedRelic is not { } relic) return;
                RunState.Relics.Add(new RelicInstance(relic));
                break;

            case RewardKind.Potion:
                // Refused rather than overflowed: the combat HUD only ever
                // draws MaxPotionSlots slots, so a fourth potion is one the
                // player owns and can never reach.
                if (BeltIsFull || RewardContext.PotionDrop is not { } potion) return;
                RunState.Potions.Add(new PotionInstance(potion));
                break;

            case RewardKind.Card:
                // The only row that does not resolve on the spot - it opens the
                // fan, and the claim lands when a card is picked there.
                OpenCardOverlay();
                return;
        }

        AudioManager.Instance?.PlaySfx("reward_pickup");
        MarkClaimed(kind);
    }

    private void MarkClaimed(RewardKind kind)
    {
        RewardContext.Claimed.Add(kind);
        RefreshRows();

        // RunManager autosaves on *entering* a screen, so the save taken when
        // this screen opened has none of these claims in it. Without this, a
        // player who claimed their gold and then quit would come back to a run
        // that had never been paid - which is a regression the old screen could
        // not have, because it banked the gold before the screen existed.
        // RewardContext itself is not saved, so what is still unclaimed is
        // still forfeited by quitting; that is the same deal the card pick has
        // always offered.
        RunSaveManager.Save(RunManager.Instance.RunSeed);

        // Must follow RefreshRows, which has just set FocusModeEnum.None on the
        // row the player pressed and thereby released the focus ring.
        _keyboardNav?.Regrab();
    }

    private void OpenCardOverlay()
    {
        _cardOverlay.Visible = true;
        BuildCardChoices();
        RefreshRows();

        // Deliberately *not* Regrab: RegrabNow leaves an existing focus owner
        // alone by design (the player may have tabbed somewhere on purpose), so
        // it does nothing here - the row that was just pressed still holds
        // focus, and the ring would sit on the list behind the dim. Opening a
        // modal is the one case where focus has to be taken rather than
        // rescued. Deferred because the fan's Controls have not been through a
        // layout pass yet on the frame they are added.
        CallDeferred(MethodName.FocusFirstCard);
    }

    private void FocusFirstCard()
    {
        var first = GetNode<Control>("CardOverlay/CardChoicesArea").GetChildren()
            .OfType<CardView>().FirstOrDefault();
        if (first is not null) ScreenKeyboardNav.GrabFocusQuietly(first);
    }

    private void CloseCardOverlay()
    {
        _cardOverlay.Visible = false;
        ClearCardChoices();
        RefreshRows();

        // Regrab is right on the way *out*: the CardView that held focus has
        // just been freed, so there is no owner left for RegrabNow to defer to
        // and it falls through to FirstClaimableControl.
        _keyboardNav?.Regrab();
    }

    private void ClearCardChoices()
    {
        foreach (var child in GetNode<Control>("CardOverlay/CardChoicesArea").GetChildren()) child.QueueFree();
    }

    // Real CardView instances (the same frame combat hands/deck-view use)
    // laid out in a gentle fan with a per-card idle sway, replacing the old
    // plain Button+Label rows - "one card component everywhere" per the
    // visual overhaul brief, now that CardView's frame has been proven
    // through Phase 3's real-combat verification.
    private void BuildCardChoices()
    {
        var area = GetNode<Control>("CardOverlay/CardChoicesArea");
        foreach (var child in area.GetChildren()) child.QueueFree();

        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");
        var choices = RewardContext.CardChoices;
        int n = choices.Count;

        float spacing = n <= 1 ? 0f : Mathf.Min(CardWidth * 1.15f, (area.Size.X - CardWidth) / (n - 1));
        float totalWidth = CardWidth + (n - 1) * spacing;
        float startX = (area.Size.X - totalWidth) / 2f;

        for (int i = 0; i < n; i++)
        {
            var card = choices[i];
            var view = cardScene.Instantiate<CardView>();
            area.AddChild(view);
            view.Interactive = false;
            view.SetCardInstance(new CardInstance(card));
            view.Clicked += OnCardChosen;

            float t = n <= 1 ? 0.5f : (float)i / (n - 1);
            float centered = t - 0.5f;
            float rotationDeg = centered * 2f * MaxFanRotationDeg;
            float yOffset = FanArcHeight * (1f - Mathf.Cos(centered * Mathf.Pi));
            var pos = new Vector2(startX + i * spacing, area.Size.Y / 2f - 154f + yOffset);
            view.Position = pos;
            view.RotationDegrees = rotationDeg;
            view.PivotOffset = view.Size / 2f;

            PlayIdleSway(view, rotationDeg, i * 0.15f);
        }
    }

    // Slow, gentle rotation drift around the card's resting angle - phase-
    // staggered per card (same "spawn the loop with a random/staggered
    // start delay" idiom EnemyView's idle bob already uses) so a row of
    // reward cards doesn't sway in lockstep.
    private static void PlayIdleSway(CardView view, float restRotation, float phaseDelay)
    {
        var tween = view.CreateTween();
        tween.TweenInterval(phaseDelay);
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(view, "rotation_degrees", restRotation + 2.5f, 1.6);
        tween.TweenProperty(view, "rotation_degrees", restRotation - 2.5f, 1.6);
    }

    // Picking a card closes the overlay and returns to the list rather than
    // leaving the screen. The card row is one reward among several now, and a
    // pick that exited would forfeit every row the player had not got to yet -
    // which is the exact bug the list exists to make unrepresentable.
    private void OnCardChosen(CardInstance card)
    {
        if (RewardContext.Claimed.Contains(RewardKind.Card)) return;

        RunState.Deck.Add(card.Definition);
        _cardOverlay.Visible = false;
        ClearCardChoices();

        AudioManager.Instance?.PlaySfx("reward_pickup");
        MarkClaimed(RewardKind.Card);
    }

    private void OnSkipPressed() => Advance();

    private static void Advance() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
