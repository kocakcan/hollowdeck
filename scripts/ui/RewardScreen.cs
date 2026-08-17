using System;
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

    // A relic tile's *content* width, not the tile's. ScreenChrome.FocusableFrame
    // adds Md of margin on each side, so a tile measures 224 + 2x12 = 248 and
    // three of them plus two Md gaps come to 768 of the overlay's 800px band.
    // The real headroom is 32px: a fourth tile does not fit, and neither does
    // raising FocusableFrame's padding by more than 5.
    private const float RelicTileWidth = 224f;

    /// The overlay and the two views inside it, named here for the same reason
    /// TipLabelName and RowName are: a suite or a screenshot fixture should
    /// address this screen by what a thing *is* rather than by where it
    /// currently sits in the tree.
    public const string OverlayName = "ChoiceOverlay";
    public const string CardChoicesPath = "ChoiceOverlay/CardChoicesArea";
    public const string RelicChoicesPath = "ChoiceOverlay/RelicChoicesArea";
    public const string OverlayCancelPath = "ChoiceOverlay/ChoiceCancelButton";
    public const string OverlayTitlePath = "ChoiceOverlay/ChoiceTitle";

    // Kept rather than discarded, because claiming a row sets
    // FocusModeEnum.None on the button that was just pressed - which *releases*
    // the focus it holds (CLAUDE.md's measured note, pinned by
    // KeyboardSmokeTest) and leaves the ring nowhere. Regrab is what puts it
    // back on a legal control.
    private ScreenKeyboardNavListener? _keyboardNav;

    // Set the first time this screen is left. See OnSkipPressed for why one
    // press has to be one skip.
    private bool _skipResolved;

    private VBoxContainer _rowList = null!;
    private Control _overlay = null!;
    private Button _skipButton = null!;
    private Control _tipLine = null!;
    private readonly Dictionary<RewardKind, Button> _rowButtons = new();

    // The relic picker's tiles, in the order they are offered. Held rather than
    // re-found because ActivatablePanel is a script class and FindChildren's
    // type filter is not a reliable way to ask for one - and FirstClaimableControl
    // needs the answer on every Regrab, not only when the picker is built.
    private readonly List<Control> _relicTiles = new();

    // The rules line under a row's name, alongside the text it says when the
    // row is claimable - so a row that has to explain a refusal can put the
    // reason where the rules text was and then put the rules text back.
    private readonly Dictionary<RewardKind, (Label Label, string Detail)> _rowDetails = new();

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "crypt", new Color(0.6f, 0.6f, 0.65f));
        DeckViewButtons.Attach(this);
        BuildActClearedBanner();

        _overlay = GetNode<Control>(OverlayName);
        _skipButton = GetNode<Button>("SkipButton");
        _skipButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _skipButton.Pressed += OnSkipPressed;

        var cancel = GetNode<Button>(OverlayCancelPath);
        cancel.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        cancel.Pressed += CloseOverlay;

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
        if (_overlay.Visible) return FirstOverlayChoice();

        return _rowList.GetChildren().OfType<Button>().FirstOrDefault(b => !b.Disabled)
            ?? (Control)_skipButton;
    }

    // The overlay hosts two views - the card fan and the boss relic picker - and
    // exactly one is visible at a time. Asking the *area* rather than tracking
    // which view was opened is what keeps that a fact about the tree rather than
    // a flag that can disagree with it.
    private Control? FirstOverlayChoice()
    {
        if (GetNode<Control>(CardChoicesPath).Visible)
        {
            return GetNode<Control>(CardChoicesPath).GetChildren()
                .OfType<CardView>().FirstOrDefault();
        }

        return _relicTiles.Count > 0 ? _relicTiles[0] : null;
    }

    // Escape backs out of the overlay rather than leaving the screen while one
    // is open - the overlay is a second view, and cancelling out of a view that
    // has its own Back button should do what that button does.
    private void OnCancelPressed()
    {
        if (_overlay.Visible) CloseOverlay();
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

        // One relic is an elite's, and the row *is* that relic - it names it and
        // hands it over on press. Three is a boss's, and no single relic can be
        // the row, so it asks the question and opens a picker instead. The count
        // is the only thing that decides which, so there is no state here that
        // can disagree with what CombatScreen offered.
        if (RewardContext.RelicChoices.Count == 1)
        {
            // Same spelled-out treatment the potion row below documents, and
            // the tier matters more here than it does there: an elite and a
            // boss now pay from different pools, and "(Boss)" beside the name
            // is the only place the player is told which one this came out of.
            var relic = RewardContext.RelicChoices[0];
            AddRow(RewardKind.Relic, ArtAssets.RelicIcon(relic.Id),
                $"{relic.Name} ({relic.Tier})", relic.Description,
                ChromeStyles.RelicTierColor(relic.Tier));
        }
        else if (RewardContext.RelicChoices.Count > 1)
        {
            // Iconless for the same reason the card row below is: any one of the
            // three relics' own icons would say "this relic" on a row that is
            // about choosing between them. Tinted with the tier they are all
            // drawn from, which is the one thing true of the whole offer.
            AddRow(RewardKind.Relic, null, "Choose a boss relic",
                $"{RewardContext.RelicChoices.Count} offered - take one",
                ChromeStyles.RelicTierColor(RelicTier.Boss));
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
            AddRow(RewardKind.Card, null, "Add a card to your deck", CardRowDetail());
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

    // The line under the Card row's name: the rule when there is no streak, and
    // the streak's own odds when there is one.
    //
    // Present at streak 0 rather than appearing on the first skip, which was the
    // cheaper option and the wrong one. A player who never happens to skip would
    // never be told the mechanic exists, and "skipping is a strategy" is not a
    // strategy anyone can adopt from a rule they cannot see. The row already
    // reserves the second line's height for every other row on the screen, so it
    // costs no layout.
    //
    // Describes *this* offer, which is why it is captured when the row is built
    // and not recomputed on refresh: the three cards behind this row were drawn
    // at the rung named here, and claiming one does not retroactively change the
    // odds they came out of.
    private static string CardRowDetail()
    {
        int streak = RunState.CardSkipStreak;
        if (streak <= 0) return "Skip to sharpen the next offer";

        string capped = streak >= CardPool.MaxSkipStreak ? " (max)" : "";
        return $"{streak} skipped - Rare odds {RarePercent(streak)}%{capped}";
    }

    // Computed from the weight table rather than written down beside it. A
    // literal here would be a second copy of the ladder, and the one that shows
    // the player a number - which is the drifted-telegraph shape this project
    // refuses everywhere else. Normalised over the whole enum rather than
    // divided by 100, so it survives the weights being retuned to any total.
    private static int RarePercent(int streak)
    {
        int total = 0;
        foreach (var rarity in Enum.GetValues<Rarity>()) total += CardPool.WeightOf(rarity, streak);
        return total <= 0 ? 0 : Mathf.RoundToInt(100f * CardPool.WeightOf(Rarity.Rare, streak) / total);
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
        // While the overlay is open it is a modal: the dim swallows the mouse,
        // and the list has to leave the *focus* chain too or Tab walks straight
        // back out behind it onto rows the player cannot see. Not Disabled -
        // they are not illegal choices, just not this view's - so they keep
        // their colour under the dim rather than greying out for a reason that
        // will be gone in a moment.
        //
        // One overlay node hosting both views rather than two sibling overlays,
        // which is why this is one question. The card fan and the boss relic
        // picker have identical answers to every line below it, and a second
        // Control would have made each of them "or the other one too" - five
        // places to widen and five places to forget, which is exactly the shape
        // CombatManager.DecayAtTurnEnd exists to refuse.
        bool listIsBehindTheOverlay = _overlay.Visible;

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
                if (RewardContext.RelicChoices.Count == 0) return;

                // A boss offered three, so this row asks rather than pays - the
                // claim lands in OnRelicChosen, the way the card row's lands in
                // OnCardChosen. An elite offered one and there is nothing to
                // ask, so it still resolves on the spot.
                if (RewardContext.RelicChoices.Count > 1)
                {
                    OpenRelicOverlay();
                    return;
                }

                RunState.Relics.Add(new RelicInstance(RewardContext.RelicChoices[0]));
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

    private void OpenCardOverlay() => ShowOverlay(cards: true, title: "", BuildCardChoices);

    // The card fan needs no caption - three cards over a dim can only be one
    // question. Three relic tiles are not so obvious, and the row that did ask
    // ("Choose a boss relic") is sitting behind the scrim at 0.3 by the time
    // they appear, which is legible as context and not as a prompt.
    private void OpenRelicOverlay() =>
        ShowOverlay(cards: false, title: "Choose a Boss Relic", BuildRelicChoices);

    // Both views raise the same overlay, so the dim, the modal focus rules and
    // the list fade are written once. The only per-view state is which area is
    // visible, and that is set here rather than tracked in a field.
    //
    // `build` runs *after* the overlay is raised, not before, because
    // BuildCardChoices lays the fan out against area.Size.X - it is a hand
    // positioned Control rather than a container, so it reads a size instead of
    // being given one. Building first happens to work today, since the area's
    // anchors resolve whether or not its parent is drawn; it is ordered this way
    // so that stops being something to know.
    private void ShowOverlay(bool cards, string title, Action build)
    {
        GetNode<Control>(CardChoicesPath).Visible = cards;
        GetNode<Control>(RelicChoicesPath).Visible = !cards;

        var titleLabel = GetNode<Label>(OverlayTitlePath);
        titleLabel.Text = title;
        titleLabel.Visible = title.Length > 0;
        titleLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);

        _overlay.Visible = true;
        build();
        RefreshRows();

        // Deliberately *not* Regrab: RegrabNow leaves an existing focus owner
        // alone by design (the player may have tabbed somewhere on purpose), so
        // it does nothing here - the row that was just pressed still holds
        // focus, and the ring would sit on the list behind the dim. Opening a
        // modal is the one case where focus has to be taken rather than
        // rescued. Deferred because the view's Controls have not been through a
        // layout pass yet on the frame they are added.
        CallDeferred(MethodName.FocusFirstChoice);
    }

    private void FocusFirstChoice()
    {
        if (FirstOverlayChoice() is { } first) ScreenKeyboardNav.GrabFocusQuietly(first);
    }

    private void CloseOverlay()
    {
        _overlay.Visible = false;
        ClearChoices();
        RefreshRows();

        // Regrab is right on the way *out*: the control that held focus has just
        // been freed, so there is no owner left for RegrabNow to defer to and it
        // falls through to FirstClaimableControl.
        _keyboardNav?.Regrab();
    }

    // Both areas, unconditionally. Clearing only the view that happened to be
    // open would leave the other one's Controls alive and focusable under a
    // hidden parent, which is the kind of thing that works until the day both
    // views get opened in one visit to this screen - which a boss reward, with
    // a relic choice *and* a card choice, is.
    private void ClearChoices()
    {
        foreach (var child in GetNode<Control>(CardChoicesPath).GetChildren()) child.QueueFree();
        foreach (var child in GetNode<Control>(RelicChoicesPath).GetChildren()) child.QueueFree();
        _relicTiles.Clear();
    }

    // Real CardView instances (the same frame combat hands/deck-view use)
    // laid out in a gentle fan with a per-card idle sway, replacing the old
    // plain Button+Label rows - "one card component everywhere" per the
    // visual overhaul brief, now that CardView's frame has been proven
    // through Phase 3's real-combat verification.
    private void BuildCardChoices()
    {
        var area = GetNode<Control>(CardChoicesPath);
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
    private const float SwayDegrees = 2.5f;
    private const float SwaySeconds = 1.6f;

    private static void PlayIdleSway(CardView view, float restRotation, float phaseDelay)
    {
        var tween = view.CreateTween();
        tween.TweenInterval(phaseDelay);
        tween.SetLoops();
        tween.TweenPingPong(
            view, "rotation_degrees", restRotation + SwayDegrees, restRotation - SwayDegrees,
            Motion.Drift.Over(SwaySeconds));
    }

    // Picking a card closes the overlay and returns to the list rather than
    // leaving the screen. The card row is one reward among several now, and a
    // pick that exited would forfeit every row the player had not got to yet -
    // which is the exact bug the list exists to make unrepresentable.
    private void OnCardChosen(CardInstance card)
    {
        if (RewardContext.Claimed.Contains(RewardKind.Card)) return;

        RunState.Deck.Add(card.Definition);

        // Taking a card spends the streak, on the same line the deck gains it
        // rather than inside MarkClaimed - the reset and the increment in
        // OnSkipPressed are one rule, and a reader should find both halves of
        // it where the two outcomes actually happen. MarkClaimed's re-save is
        // what persists this.
        RunState.CardSkipStreak = 0;

        _overlay.Visible = false;
        ClearChoices();

        AudioManager.Instance?.PlaySfx("reward_pickup");
        MarkClaimed(RewardKind.Card);
    }

    // Three relics side by side, each in the same focusable frame LibraryScreen
    // renders its collection in. Tiles rather than a second list of rows: the
    // question here is "which of these", and comparing three things is what a
    // row of tiles is for - a list of rows inside a modal that sits over a list
    // of rows reads as the screen having lost its place.
    //
    // No idle sway. The card fan's exists because a fan of cards is the screen's
    // one flourish; a relic tile is a specification, and a drifting one is
    // harder to read against the two beside it.
    private void BuildRelicChoices()
    {
        var area = GetNode<Control>(RelicChoicesPath);
        foreach (var child in area.GetChildren()) child.QueueFree();
        _relicTiles.Clear();

        var grid = new GridContainer { Columns = RewardContext.RelicChoices.Count };
        grid.AddThemeConstantOverride("h_separation", (int)UiTheme.Spacing.Md);
        grid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Centred in the band the way the reward list is centred in its own, so
        // two offers and three offers sit in the same place.
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        centre.AddChild(grid);
        area.AddChild(centre);

        foreach (var relic in RewardContext.RelicChoices)
        {
            var chosen = relic; // Captured per iteration, not per loop.

            var column = new VBoxContainer { CustomMinimumSize = new Vector2(RelicTileWidth, 0) };
            column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);

            if (ArtAssets.RelicIcon(relic.Id) is { } icon)
            {
                column.AddChild(ScreenChrome.ArtPlinth(icon, PixelSpec.CardArtScale));
            }

            // Autowrap is the load-bearing line here, and it is load-bearing for
            // a reason that is not about wrapping. ScreenChrome.Heading returns
            // an unwrapped Label, whose minimum width is its whole string - and
            // a child wider than its VBoxContainer widens the container. So a
            // long relic name does not overflow its tile, it *widens* that tile,
            // and three tiles sharing an 800px band then hang off both ends for
            // the CenterContainer to clip. A wrapping Label has a small minimum
            // width instead, so the column keeps the 224 it was given.
            //
            // The longest Boss name today is 14 characters against about 17 that
            // fit on one line, which is exactly the "constant that fits the best
            // case" shape this project has shipped three times (ROADMAP Phase
            // 11). Measured, with this line gone: a 38-character name lands a
            // 269px tile beside two 248s.
            var name = ScreenChrome.Heading(relic.Name, UiTheme.Fonts.Body);
            name.AddThemeColorOverride("font_color", ChromeStyles.RelicTierColor(relic.Tier));
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(name);
            column.AddChild(ScreenChrome.Body(relic.Description, (int)RelicTileWidth));

            var tile = ScreenChrome.FocusableFrame(column);
            tile.Activated += () => OnRelicChosen(chosen);
            grid.AddChild(tile);
            _relicTiles.Add(tile);

            // A rung down before wrapping, the EnemyView ladder one screen over.
            // This buys legibility, not layout - the autowrap above already
            // holds the band, and deleting this line breaks no assertion about
            // width. What it changes is how many lines a long name costs: at
            // Body the 38-character case wraps to three, and GridContainer
            // levels every tile to the tallest, so one long name makes all three
            // taller. Asserted through the applied size in ScreenSmokeTest,
            // because that is the only thing about it anything can observe.
            //
            // Applied after the tile is in the tree, since TextFit measures
            // through the label's own theme and a detached Label has none. Wrap
            // is the floor rather than TrimChar: a tile has vertical room where
            // an enemy caption in a fixed row does not.
            TextFit.Apply(name, RelicTileWidth, UiTheme.Fonts.Body, UiTheme.Fonts.Small);
        }

        // The same index-based wiring LibraryScreen's relic grid uses, and for
        // the same reason: Godot's directional focus search is a geometry test
        // over everything focusable on screen, which behind a modal includes the
        // rows underneath it.
        CardPicker.WireGridNavigation(grid, _relicTiles, GetNode<Button>(OverlayCancelPath));
    }

    // The mirror of OnCardChosen, guarded the same way and for the same reason:
    // the claim is a fact about the run, so a second activation - a stray Enter
    // on a tile that has not been freed yet - cannot pay a second relic.
    private void OnRelicChosen(RelicDefinition relic)
    {
        if (RewardContext.Claimed.Contains(RewardKind.Relic)) return;

        RunState.Relics.Add(new RelicInstance(relic));
        _overlay.Visible = false;
        ClearChoices();

        AudioManager.Instance?.PlaySfx("reward_pickup");
        MarkClaimed(RewardKind.Relic);
    }

    // Leaving the screen, and the one place a skip streak is built.
    //
    // The guard is CombatScreen._continueResolved's, one screen over and for
    // the same reason: there are two independent ways in - the button (_Ready)
    // and hd_cancel through OnCancelPressed - and Advance is not instant
    // because ScreenFade holds the scene up. That window used to be harmless
    // here, since a second ChangeScreen to the same state does nothing visible.
    // It stops being harmless the moment this handler moves a counter: a click
    // plus an Escape inside the fade would build two rungs off one skip, which
    // is exactly how the double relic grant happened.
    private void OnSkipPressed()
    {
        if (_skipResolved) return;
        _skipResolved = true;

        if (LeavingDeclinesACard())
        {
            // Capped as it is built rather than only where it is spent, so the
            // counter and the ladder rung are one number. Letting it run to 7
            // while CardPool clamped the draw at 3 would put "7 skipped" on the
            // row beside rung 3's odds.
            RunState.CardSkipStreak = Math.Min(RunState.CardSkipStreak + 1, CardPool.MaxSkipStreak);
        }

        // No explicit save: ChangeScreen below autosaves on *entering* Map
        // (RunManager.AutoSaveScreens), and it does so before the scene swap,
        // so the line above is already in the file by the time the map loads.
        Advance();
    }

    /// Whether leaving the screen right now declines a card reward - the fact a
    /// skip streak is built from.
    ///
    /// The offer and the claim set, never the button's label. RefreshExitButton
    /// already retitles Skip as "Continue" once every row is taken, so a player
    /// who took the card and pressed it is leaving through the same handler
    /// while having skipped nothing.
    ///
    /// Public and static because it is the whole condition, and driving it
    /// through the screen costs a scene change: Skip ends in ChangeScreen, which
    /// on a hard cut swaps the tree's current scene out from under the suite
    /// that pressed it. So a test asks this directly and presses the real button
    /// only for the one thing a predicate cannot show - that two presses build
    /// one rung.
    public static bool LeavingDeclinesACard() =>
        RewardContext.CardChoices.Count > 0 && !RewardContext.Claimed.Contains(RewardKind.Card);

    private static void Advance() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
