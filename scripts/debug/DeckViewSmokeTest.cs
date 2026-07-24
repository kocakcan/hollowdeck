using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Headless check for two things fixed/added together: the victory-screen
// soft-lock (CombatEndPanel's ZIndex has to beat every hand CardView's, or
// the Continue button is unclickable-behind-cards) and the new deck/pile
// viewer popups (PileViewPopup/DeckViewButtons). Run via
// `godot --headless scenes/debug/DeckViewSmokeTest.tscn`.
public partial class DeckViewSmokeTest : Node
{
    private int _pass;
    private int _fail;

    public override async void _Ready()
    {
        var tree = GetTree();

        CardDatabase.LoadAll();
        EnemyDatabase.LoadAll();
        RelicDatabase.LoadAll();
        PotionDatabase.LoadAll();

        await TestDeckPopupOutsideCombat();
        await TestCombatEndPanelBeatsHandCardsAndPilePopupsWork();
        TestSlimePickerIsntStrictAlternation();

        GD.Print($"DeckViewSmokeTest: {_pass} passed, {_fail} failed");
        tree.Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string name, bool condition, string detail)
    {
        if (condition) { _pass++; GD.Print($"PASS {name}"); }
        else { _fail++; GD.Print($"FAIL {name}: {detail}"); }
    }

    private static GridContainer GetPopupGrid(PileViewPopup popup)
    {
        var panel = popup.GetChild<PanelContainer>(1);
        var vbox = panel.GetChild<VBoxContainer>(0);
        var scroll = vbox.GetChild<ScrollContainer>(1);
        return scroll.GetChild<GridContainer>(0);
    }

    private async Task TestDeckPopupOutsideCombat()
    {
        // Mixed short ("Strike") and long ("Bash", with its Vulnerable
        // clause) descriptions on purpose - a card entry sizing regression
        // (GridContainer column stretching to its widest cell's unwrapped
        // text) only shows up when descriptions differ in length.
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("strike"), CardDatabase.Get("defend"),
            CardDatabase.Get("bash"),
        };

        var screen = new Control { Size = new Vector2(1152, 648) };
        AddChild(screen);
        DeckViewButtons.Attach(screen);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var row = screen.GetChildren().OfType<VBoxContainer>().First();
        Check("deck_button_row_stays_within_screen",
            row.GetRect().Position.X + row.GetRect().Size.X <= screen.Size.X,
            $"row right edge={row.GetRect().Position.X + row.GetRect().Size.X}, screen width={screen.Size.X}");

        var deckButton = row.GetChildren().OfType<Button>().First(b => b.Text == "Deck");
        deckButton.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var popup = screen.GetChildren().OfType<PileViewPopup>().FirstOrDefault();
        Check("deck_popup_opens_from_button", popup is not null, "no PileViewPopup child found");
        if (popup is not null)
        {
            var grid = GetPopupGrid(popup);
            Check("deck_popup_lists_every_card_in_deck", grid.GetChildCount() == 4,
                $"entries={grid.GetChildCount()}");

            var widths = grid.GetChildren().OfType<Control>().Select(c => c.Size.X).Distinct().ToList();
            Check("deck_popup_entries_are_equal_width", widths.Count == 1,
                $"distinct widths found: {string.Join(", ", widths)}");

            popup.QueueFree();
        }

        screen.QueueFree();
    }

    private async Task TestCombatEndPanelBeatsHandCardsAndPilePopupsWork()
    {
        RunState.Gold = 0;
        RunState.PlayerMaxHp = 50;
        RunState.PlayerCurrentHp = 50;
        RunState.Deck = new List<CardDefinition>
        {
            CardDatabase.Get("strike"), CardDatabase.Get("strike"), CardDatabase.Get("strike"),
            CardDatabase.Get("defend"), CardDatabase.Get("defend"),
        };
        RunState.Relics = new List<RelicInstance>();
        RunState.Potions = new List<PotionInstance>();

        CombatContext.EnemyDefinitionIds = new List<string> { "slime" };
        CombatContext.IsElite = false;
        CombatContext.IsBoss = false;

        var packed = GD.Load<PackedScene>("res://scenes/CombatScreen.tscn");
        var instance = packed.Instantiate();
        AddChild(instance);

        var combat = instance.GetNode<CombatManager>("CombatManager");
        var handArea = instance.GetNode<Control>("HandArea");
        var hpFrame = instance.GetNode<Control>("PlayerHpFrame");
        var enemyRow = instance.GetNode<Control>("EnemyRow");
        var buttonStack = instance.GetChildren().OfType<VBoxContainer>().First();

        Check("deck_button_stack_does_not_overlap_enemy_row",
            !enemyRow.GetGlobalRect().Intersects(buttonStack.GetGlobalRect()),
            $"enemyRow={enemyRow.GetGlobalRect()}, buttonStack={buttonStack.GetGlobalRect()}");

        // Mid-fight (before CombatEnd), check the Draw/Discard/Exhaust
        // buttons/popups actually reflect live pile contents - this is the
        // main new feature, not just the bugfix.
        if (combat.State == CombatState.PlayerTurn && combat.Player.Piles.Hand.Count > 0)
        {
            DeckViewButtons.OpenPile(instance, "Draw Pile", combat.Player.Piles.DrawPile);
            var drawPopup = instance.GetChildren().OfType<PileViewPopup>().First();
            Check("draw_pile_popup_matches_pile_count",
                GetPopupGrid(drawPopup).GetChildCount() == combat.Player.Piles.DrawPile.Count,
                $"entries={GetPopupGrid(drawPopup).GetChildCount()}, pile={combat.Player.Piles.DrawPile.Count}");
            drawPopup.QueueFree();

            // The opening hand from this 5-card deck fills the fan out to
            // its widest (worst case for the FanSafeWidth overlap bug) -
            // the leftmost card's rotated bounding box shouldn't clip into
            // the HP/energy column now that FanSafeWidth is narrower.
            Check("opening_hand_is_full_five_cards", combat.Player.Piles.Hand.Count == 5,
                $"hand={combat.Player.Piles.Hand.Count}");
            var hpRect = hpFrame.GetGlobalRect();
            bool anyOverlap = false;
            foreach (var child in handArea.GetChildren())
            {
                if (child is not Control cardView) continue;
                var xform = cardView.GetGlobalTransform();
                var size = cardView.Size;
                var corners = new[]
                {
                    xform * Vector2.Zero, xform * new Vector2(size.X, 0),
                    xform * new Vector2(0, size.Y), xform * size,
                };
                foreach (var corner in corners)
                {
                    if (hpRect.HasPoint(corner)) anyOverlap = true;
                }
            }
            Check("full_hand_does_not_overlap_hp_frame", !anyOverlap, "a hand card's bounding corner falls inside PlayerHpFrame's rect");
        }

        var enemy = combat.Enemies[0];
        while (!enemy.IsDead && combat.State != CombatState.CombatEnd)
        {
            if (combat.State == CombatState.PlayerTurn)
            {
                // A 5-card deck draws a full hand every turn (see PileManager
                // .DrawHand(5) in CombatManager), which can leave unaffordable
                // cards sitting at index 0 once energy runs low - always
                // playing Hand[0] (as the smaller 2-card deck in
                // Phase4ContentSmokeTest gets away with) would spin forever
                // on a card that never becomes playable this turn.
                var playable = combat.Player.Piles.Hand.FirstOrDefault(c => c.Definition.Cost <= combat.Player.CurrentEnergy);
                if (playable is not null) combat.TryPlayCard(playable, enemy);
                else combat.TryEndTurn();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // RefreshStateUi() runs off the StateChanged signal fired inside the
        // transition above - give it one more frame to actually execute.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("fight_reaches_combat_end", combat.State == CombatState.CombatEnd, $"state={combat.State}");

        var combatEndPanel = instance.GetNode<Control>("CombatEndPanel");
        Check("combat_end_panel_is_visible", combatEndPanel.Visible, "panel not visible");

        int maxHandCardZIndex = handArea.GetChildren().OfType<CanvasItem>().Select(c => c.ZIndex)
            .DefaultIfEmpty(0).Max();
        Check("combat_end_panel_z_index_beats_every_hand_card",
            combatEndPanel.ZIndex > maxHandCardZIndex,
            $"panel z={combatEndPanel.ZIndex}, max hand card z={maxHandCardZIndex}");

        instance.QueueFree();
    }

    // Acid Slime (2 moves: 60% lick / 40% corrode-applies-Weak) via
    // WeightedRandomIntentPicker used to strictly alternate lick/corrode -
    // the "exclude the last move" anti-repeat logic, with only 2 moves
    // total, left exactly one candidate every time, forcing every-other-
    // turn Weak regardless of the 40% weight. 30 picks with true weighted
    // randomness has an astronomically small chance of landing zero
    // immediate repeats; strict alternation would guarantee zero.
    private void TestSlimePickerIsntStrictAlternation()
    {
        var picker = new WeightedRandomIntentPicker();
        var slime = new EnemyCombatant { Name = "Acid Slime", Definition = EnemyDatabase.Get("slime") };

        bool sawRepeat = false;
        for (int i = 0; i < 30; i++)
        {
            var move = picker.PickNext(slime);
            if (slime.LastMove is not null && move.MoveId == slime.LastMove.MoveId) sawRepeat = true;
            slime.LastMove = move;
        }

        Check("slime_picker_can_repeat_a_move_instead_of_forced_alternation", sawRepeat,
            "no repeat in 30 picks - still behaving like strict alternation");
    }
}
