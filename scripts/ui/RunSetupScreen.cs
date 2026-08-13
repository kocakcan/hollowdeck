using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Hollowdeck.Data;
using Hollowdeck.Events;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// The screen between MainMenu and the first map. Two things live here, and
// they are on one screen because they are the same fact seen twice: a run is a
// pure function of its seed, and the first decision in it is the blessing.
//
// The blessing offers are BlessingDefinitions resolved through
// EventOutcomeRegistry - the *non-combat* registry, which exists precisely
// because EffectContext requires a live CombatManager/Combatant that no menu
// screen has. So a blessing needs no new effect vocabulary at all: it is an
// event choice offered before the map.
//
// Three things about the shape are expensive to rediscover:
//
//  - **Re-seeding rebuilds the whole run, and that is the feature.** Typing a
//    seed or pressing Reroll calls RunManager.BeginRun, which re-Inits every
//    RNG stream and re-runs RunState.InitNewRun - so the map, the enemies and
//    the three offers below all change together. Anything less would make a
//    "reproduced" seed not reproduce.
//  - **Which means re-seeding after a blessing resolves would erase it**, since
//    InitNewRun resets Deck/Relics/HP. The seed field and Reroll therefore leave
//    play the moment a blessing is claimed - Editable/Disabled *and*
//    FocusModeEnum.None together, because Disabled alone excludes a control
//    from neither Tab nor arrow navigation and does not release focus it
//    already holds (see ScreenKeyboardNav).
//  - **The screen must handle a pending card picker.** Begin returns one for
//    remove_chosen_card/upgrade_chosen_card, and a screen that dropped a
//    non-null Pending would resolve the blessing to nothing with no error -
//    exactly the silent data/code-seam no-op this codebase produces. The
//    picker half is EventScreen's, down to the Regrab on both swaps.
//
// No art plinth, unlike the other five in-run screens. Those needed one
// because they were otherwise a wall of centred text; three framed tiles and a
// seed row are already structure, and the nearest available icon would be a
// map node's, which would say something untrue about what this screen is.
public partial class RunSetupScreen : Control
{
    private const int OfferCount = 3;

    // A tile's *content* width, not the tile's - ScreenChrome.FocusableFrame
    // adds Md a side, so three tiles plus two Md gaps come to 768. Deliberately
    // the same 224 the boss-relic picker uses: two three-tile rows in one game
    // that are almost the same width would read as a mistake.
    private const float TileWidth = 224f;

    // "-2147483648" is the longest int, and the field is digits-plus-a-leading-
    // minus, so nothing longer can be legal.
    private const int SeedMaxLength = 11;

    private LineEdit _seedField = null!;
    private Button _rerollButton = null!;
    private Control _ascensionBox = null!;
    private CheckButton _ascensionToggle = null!;
    private Label _ascensionLabel = null!;
    private GridContainer _offerGrid = null!;
    private Label _promptLabel = null!;
    private Label _resultLabel = null!;
    private Button _beginButton = null!;
    private ScreenKeyboardNavListener? _keyboardNav;

    private Control _mainView = null!;
    private Control _pickerView = null!;
    private Label _pickerTitle = null!;
    private GridContainer _pickerList = null!;

    private readonly List<Control> _offerTiles = new();

    // Guards the claim rather than trusting the tiles to have been freed - the
    // RewardScreen rule, and for the same reason: a stray Enter on a tile that
    // is still in the tree must not resolve a second blessing onto a run that
    // has already had one.
    private bool _claimed;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "etched", new Color(0.8f, 0.78f, 0.85f));
        // The deck is inspectable here on purpose: "remove a card" and "upgrade
        // a card" are both authored blessings, and neither is a decision if the
        // player cannot look at what they are choosing from.
        DeckViewButtons.Attach(this);
        ScreenChrome.AddTitle(this, "Before the Descent");
        // Live state, not decoration - a blessing moves HP, gold or both, and
        // ShowResult refreshes this block afterwards for that reason.
        ScreenChrome.AddRunStatus(this);

        _mainView = GetNode<Control>("CenterContainer");
        _pickerView = GetNode<Control>("PickerCenterContainer");
        _pickerTitle = GetNode<Label>("PickerCenterContainer/PickerVBox/PickerTitleLabel");
        _pickerTitle.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
        _pickerList = GetNode<GridContainer>("PickerCenterContainer/PickerVBox/ScrollContainer/PickerList");

        _promptLabel = GetNode<Label>("CenterContainer/VBoxContainer/PromptLabel");
        _promptLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N7);
        _resultLabel = GetNode<Label>("CenterContainer/VBoxContainer/ResultLabel");
        _resultLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGold);
        _offerGrid = GetNode<GridContainer>("CenterContainer/VBoxContainer/OfferGrid");

        _beginButton = GetNode<Button>("CenterContainer/VBoxContainer/BeginButton");
        ChromeStyles.ApplyEmphasisButtonStyle(_beginButton);
        _beginButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _beginButton.Pressed += OnBeginPressed;

        var seedLabel = GetNode<Label>("CenterContainer/VBoxContainer/SeedRow/SeedLabel");
        seedLabel.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGold);

        // The field commits on Enter and discards on the way out, which is not
        // the blur-to-commit a player would assume - so it is stated on screen
        // rather than left to be discovered by a seed that silently did nothing.
        var seedHint = GetNode<Label>("CenterContainer/VBoxContainer/SeedRow/SeedHintLabel");
        seedHint.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Small);
        seedHint.AddThemeColorOverride("font_color", PixelSpec.Ramp.N5);

        _seedField = GetNode<LineEdit>("CenterContainer/VBoxContainer/SeedRow/SeedField");
        ChromeStyles.ApplyLineEditStyle(_seedField);
        _seedField.MaxLength = SeedMaxLength;
        _seedField.Text = RunManager.Instance.RunSeed.ToString();
        _seedField.TextChanged += OnSeedTextChanged;
        // Enter commits; leaving the field any other way *discards* the edit
        // and snaps the text back to the live seed.
        //
        // Committing on FocusExited is the obvious alternative and it is a real
        // bug, because Godot moves focus on mouse-*down*: typing a seed and
        // then clicking a blessing fires the commit between the press and the
        // release, and committing rebuilds the offers - so the tile being
        // clicked is freed underneath the cursor, that click resolves nothing,
        // and a different blessing has slid into its place for the next one.
        // Measured with a synthetic press/release pair; there is no ordering of
        // "rebuild the run" and "claim from the run" that survives being
        // interleaved with one click.
        _seedField.TextSubmitted += _ => CommitSeed();
        _seedField.FocusExited += RevertSeedField;

        _rerollButton = GetNode<Button>("CenterContainer/VBoxContainer/SeedRow/RerollButton");
        _rerollButton.Pressed += () => AudioManager.Instance?.PlaySfx("ui_click");
        _rerollButton.Pressed += () => ApplySeed(new Random().Next());

        SetUpAscensionToggle();
        RefreshOffers();

        // No onCancel. Like an event, this is a decision with no way back: the
        // run has already been minted by the time the screen opens, and there
        // is nothing behind it to return to that would not leave that run
        // orphaned.
        _keyboardNav = ScreenKeyboardNav.Attach(this, PreferredFocus);
    }

    // Three states in the order they can appear: the picker while it is open,
    // then the offers while none is taken, then Begin.
    private Control? PreferredFocus()
    {
        if (_pickerView.Visible)
        {
            // The card, not the button under it - CardPicker makes the card the
            // grid's only focus stop so arrow keys walk it one press per card.
            return _pickerList.GetChildren().SelectMany(c => c.GetChildren())
                .OfType<CardView>().FirstOrDefault();
        }
        if (!_claimed) return _offerTiles.FirstOrDefault() ?? (Control?)_beginButton;
        return _beginButton;
    }

    // -- the seed half ----------------------------------------------------

    // Strips anything that cannot be part of an int as it is typed, rather
    // than letting the field accept prose and rejecting it on commit. A
    // leading minus is legal: seeds are ints and RunManager mints them from
    // Random.Next, but a save or a bug report can carry a negative one and
    // refusing to type it back in would be the exact hole this screen closes.
    private void OnSeedTextChanged(string text)
    {
        string filtered = FilterSeed(text);
        if (filtered == text) return;

        int caret = _seedField.CaretColumn - (text.Length - filtered.Length);
        _seedField.Text = filtered;
        _seedField.CaretColumn = Mathf.Clamp(caret, 0, filtered.Length);
    }

    private static string FilterSeed(string text)
    {
        var kept = new StringBuilder();
        foreach (char c in text)
        {
            if (c >= '0' && c <= '9') kept.Append(c);
            else if (c == '-' && kept.Length == 0) kept.Append(c);
        }
        return kept.ToString();
    }

    // Reverts rather than throwing on anything unparseable - including the
    // empty field and an int overflow, which MaxLength cannot rule out
    // ("99999999999" is 11 characters). The unchanged-seed arm falls through to
    // the same revert, so pressing Enter on the seed already in play does not
    // regenerate the map for nothing.
    private void CommitSeed()
    {
        if (_claimed) return;

        if (!int.TryParse(_seedField.Text.Trim(), out int seed) || seed == RunManager.Instance.RunSeed)
        {
            RevertSeedField();
            return;
        }

        ApplySeed(seed);
    }

    // An uncommitted edit is discarded on the way out, so the field is always
    // either being typed into or showing the seed the run is actually on. See
    // the FocusExited comment in _Ready for why this is not a commit.
    private void RevertSeedField() => _seedField.Text = RunManager.Instance.RunSeed.ToString();

    // The whole run, rebuilt. BeginRun re-Inits every stream and re-runs
    // InitNewRun, so the map behind this screen changes with the offers on it.
    //
    // The rung is passed on every call rather than defaulted from RunState,
    // which would read the field BeginRun is about to overwrite. Callers that
    // are only changing the seed carry the current rung forward explicitly.
    private void ApplySeed(int seed) => ApplySeed(seed, RunState.AscensionLevel);

    private void ApplySeed(int seed, int ascension)
    {
        if (_claimed) return;

        RunManager.Instance.BeginRun(seed, ascension);
        _seedField.Text = RunManager.Instance.RunSeed.ToString();
        RefreshAscensionLabel();
        RefreshOffers();
        ScreenChrome.RefreshRunStatus(this);
        _keyboardNav?.Regrab();
    }

    // Renamed from SetSeedControlsInPlay when the ascension toggle joined it.
    // The toggle belongs here rather than beside it for exactly the reason the
    // seed field does: flipping it calls BeginRun, BeginRun calls InitNewRun,
    // and InitNewRun resets Deck/Relics/HP - so a rung changed after a blessing
    // has resolved erases the blessing silently.
    private void SetRunControlsInPlay(bool inPlay)
    {
        // Both halves, together. FocusModeEnum.None is the only one of the two
        // that excludes a control from Tab and arrow navigation *and* releases
        // focus it is already holding; Disabled/read-only does neither, which
        // is how the focus ring once parked on an unwalkable map node.
        _seedField.Editable = inPlay;
        _seedField.FocusMode = inPlay ? FocusModeEnum.All : FocusModeEnum.None;
        _rerollButton.Disabled = !inPlay;
        _rerollButton.FocusMode = inPlay ? FocusModeEnum.All : FocusModeEnum.None;
        _ascensionToggle.Disabled = !inPlay;
        _ascensionToggle.FocusMode = inPlay ? FocusModeEnum.All : FocusModeEnum.None;
    }

    // -- the ascension half -----------------------------------------------

    // Hidden outright below limit 1, rather than shown disabled. A player who
    // has not finished a run has no ladder to see, and a greyed control that
    // has never once been usable is noise rather than a goal - the unlock
    // screen is where a locked thing belongs. It also keeps this screen's
    // layout, and the four runsetup screenshot fixtures, exactly what they were
    // for a new save.
    private void SetUpAscensionToggle()
    {
        _ascensionBox = GetNode<Control>("CenterContainer/VBoxContainer/AscensionBox");
        _ascensionToggle = GetNode<CheckButton>(
            "CenterContainer/VBoxContainer/AscensionBox/AscensionToggle");
        _ascensionLabel = GetNode<Label>(
            "CenterContainer/VBoxContainer/AscensionBox/AscensionLabel");

        // Body, not Small. Small is 8px - the bottom of ART_SPEC's ladder, and
        // what the ENTER APPLIES chip beside the seed field uses. It is a size
        // for a two-word hint, and at a shot it turned this line into an
        // unreadable grey rule 200 characters long. This is the only place the
        // player is told what the rung actually does, so it is body text and it
        // wraps.
        _ascensionLabel.AddThemeFontSizeOverride("font_size", UiTheme.Fonts.Body);
        _ascensionLabel.AddThemeColorOverride("font_color", PixelSpec.Ramp.N6);

        int limit = MetaProgressionManager.Instance?.AscensionLimit ?? 0;
        _ascensionBox.Visible = limit > 0;
        if (limit <= 0) return;

        _ascensionToggle.Text = $"ASCENSION {limit}";
        // CheckButton rather than CheckBox: the theme styles the first and has no
        // entry at all for the second, and an unstyled control inherits Godot's
        // stock look - the mixed-media seam the pixel-art commitment exists to
        // close, which is exactly how the seed field's LineEdit arrived one phase
        // ago. The two Settings toggles are the same control.
        _ascensionToggle.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGold);
        _ascensionToggle.ButtonPressed = RunState.AscensionLevel > 0;
        _ascensionToggle.Toggled += OnAscensionToggled;

        RefreshAscensionLabel();
    }

    private void OnAscensionToggled(bool on)
    {
        AudioManager.Instance?.PlaySfx("ui_click");

        // The same rebuild Reroll does, on the same seed. It has to be a
        // rebuild rather than a field assignment: the rung decides the starting
        // max HP, the cards imposed on the opening deck and how often the map
        // rolls an Elite, and two of those three are already built by the time
        // this screen is on screen.
        ApplySeed(RunManager.Instance.RunSeed,
            on ? MetaProgressionManager.Instance?.AscensionLimit ?? 0 : 0);
    }

    private void RefreshAscensionLabel() =>
        _ascensionLabel.Text = RunState.AscensionLevel > 0
            ? Summarise(RunState.Ascension)
            : "Off. Win a run to raise the ladder; switch it on to climb it.";

    // Derived from the resolved modifiers rather than assembled out of the
    // rungs' authored labels, and the difference is the one this project keeps
    // writing down: prose that restates a number drifts from it. Every figure
    // here is read from the same AscensionModifiers the fight, the shop and the
    // map generator read, so a retuned ladder cannot leave this line describing
    // the old one.
    //
    // The rungs' own labels are still what BalanceReport prints and what the
    // toggle names on the way up - they say which knob a rung turned, which is
    // the half a cumulative summary cannot show.
    private static string Summarise(AscensionModifiers asc)
    {
        var parts = new List<string>();

        if (asc.EnemyHpPercent != 100 || asc.EnemyDamagePercent != 100)
        {
            var enemy = new List<string>();
            if (asc.EnemyHpPercent != 100) enemy.Add($"+{asc.EnemyHpPercent - 100}% health");
            if (asc.EnemyDamagePercent != 100) enemy.Add($"+{asc.EnemyDamagePercent - 100}% damage");
            parts.Add("Enemies " + string.Join(", ", enemy));
        }

        if (asc.BossHpBonusPercent != 0) parts.Add($"Bosses +{asc.BossHpBonusPercent}% more health");
        if (asc.EliteWeightDelta != 0) parts.Add("Elites are more common");
        if (asc.StartingMaxHpDelta != 0)
        {
            parts.Add($"You begin at {asc.StartingMaxHp(RunState.StartingMaxHp)} HP");
        }

        if (asc.StartingCurseIds.Count > 0)
        {
            var names = asc.StartingCurseIds
                .Select(id => CardDatabase.All.FirstOrDefault(c => c.Id == id)?.Name)
                .Where(n => n is not null);
            parts.Add("Your deck carries " + string.Join(" and ", names));
        }

        if (asc.ClearHealPercentDelta != 0) parts.Add($"Act clears heal {asc.ClearHeal(100)}%");
        if (asc.ShopPricePercent != 100) parts.Add($"Shops charge +{asc.ShopPricePercent - 100}%");
        if (asc.PotionDropPercentDelta != 0) parts.Add("Potions drop less often");

        return parts.Count == 0 ? "No modifiers." : string.Join("  -  ", parts);
    }

    // -- the blessing half ------------------------------------------------

    private void RefreshOffers()
    {
        foreach (var child in _offerGrid.GetChildren())
        {
            _offerGrid.RemoveChild(child);
            child.QueueFree();
        }
        _offerTiles.Clear();

        // RngStreams.Shop, the stream every non-combat grant already draws
        // from (the shop's stock, the event roll, gain_potion). This is the
        // first draw off it in a run, so the offers are a function of the seed
        // alone - which is only true because ApplySeed re-Inits the stream
        // rather than drawing again from wherever it had got to.
        foreach (var blessing in BlessingDatabase.Offer(OfferCount, RngStreams.Shop))
        {
            var chosen = blessing; // Captured per iteration, not per loop.

            var column = new VBoxContainer { CustomMinimumSize = new Vector2(TileWidth, 0) };
            column.AddThemeConstantOverride("separation", (int)UiTheme.Spacing.Xs);

            // Autowrap is load-bearing for a reason that is not about wrapping,
            // and it is the same one the boss-relic tiles carry:
            // ScreenChrome.Heading returns an *unwrapped* Label whose minimum
            // width is its whole string, and a child wider than its
            // VBoxContainer widens the container. So a long blessing name does
            // not overflow its tile, it widens that tile and the row hangs off
            // both ends of the band for the CenterContainer to clip.
            var name = ScreenChrome.Heading(blessing.Label, UiTheme.Fonts.Body);
            name.AddThemeColorOverride("font_color", UiTheme.Palette.AccentGoldBright);
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(name);
            column.AddChild(ScreenChrome.Body(blessing.Description, (int)TileWidth));

            var tile = ScreenChrome.FocusableFrame(column);
            tile.Activated += () => OnBlessingChosen(chosen);
            _offerGrid.AddChild(tile);
            _offerTiles.Add(tile);

            // A rung down before wrapping, the EnemyView ladder. Buys
            // legibility rather than layout - the autowrap above already holds
            // the band - and has to run after the tile is in the tree, since
            // TextFit measures through the label's own theme and a detached
            // Label has none.
            TextFit.Apply(name, TileWidth, UiTheme.Fonts.Body, UiTheme.Fonts.Small);
        }

        // Index-based rather than Godot's geometric focus search, which is a
        // test over everything focusable on screen - here that includes the
        // seed field and Reroll sitting directly above the row.
        CardPicker.WireGridNavigation(_offerGrid, _offerTiles, _beginButton);

        // WireGridNavigation gives a Top neighbour only to rows past the first,
        // so without this the ascension toggle is reachable by Tab and by
        // nothing else - the offers are where focus lands, and Up off them goes
        // nowhere. LibraryScreen.LinkCategoryButton is the same fix for the same
        // gap, and this survives the re-wire above for the same reason it does.
        if (_ascensionBox.Visible && _offerTiles.Count > 0)
        {
            _offerTiles[0].FocusNeighborTop = _offerTiles[0].GetPathTo(_ascensionToggle);
            _ascensionToggle.FocusNeighborBottom = _ascensionToggle.GetPathTo(_offerTiles[0]);
        }
    }

    private void OnBlessingChosen(BlessingDefinition blessing)
    {
        if (_claimed) return;
        _claimed = true;

        AudioManager.Instance?.PlaySfx("reward_pickup");

        // Before anything resolves: from here on, re-seeding or changing the
        // rung would call InitNewRun and throw away whatever this blessing is
        // about to grant.
        SetRunControlsInPlay(false);

        foreach (var child in _offerGrid.GetChildren())
        {
            _offerGrid.RemoveChild(child);
            child.QueueFree();
        }
        _offerTiles.Clear();
        _promptLabel.Visible = false;

        var resolution = EventOutcomeRegistry.Begin(blessing.Outcomes, blessing.ResultText);
        if (resolution.Pending is { } picker)
        {
            ShowPicker(picker, resolution.Text);
            return;
        }

        ShowResult(resolution.Text);
    }

    // Deliberately no Cancel, the EventScreen rule rather than the rest site's:
    // the blessing has already been claimed and its other outcomes have already
    // resolved, so the only way out is to pick.
    private void ShowPicker(ICardPickerOutcome picker, string textSoFar)
    {
        _pickerTitle.Text = picker.Prompt;
        CardPicker.PopulateFor(_pickerList, picker, message =>
        {
            _pickerView.Visible = false;
            _mainView.Visible = true;
            ShowResult(Combine(textSoFar, message));
        });

        _mainView.Visible = false;
        _pickerView.Visible = true;
        // Hiding a view removes its controls from focus navigation, and the
        // tile that was just activated has already been freed - so focus has to
        // move with the swap, both into the picker and back out.
        _keyboardNav?.Regrab();
    }

    private void ShowResult(string text)
    {
        _resultLabel.Text = text;
        _resultLabel.Visible = true;
        _beginButton.Visible = true;
        // The blessing can have moved HP, max HP or gold, and the status block
        // is a snapshot taken in _Ready.
        ScreenChrome.RefreshRunStatus(this);
        _keyboardNav?.Regrab();
    }

    // A blessing whose earlier outcomes had nothing to say leaves the picker's
    // own message standing alone rather than appended to authored prose that
    // was written for the whole row.
    private static string Combine(string textSoFar, string message) =>
        textSoFar.Length == 0 ? message : $"{textSoFar} {message}";

    private static void OnBeginPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
