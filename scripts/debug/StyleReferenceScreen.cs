using System.Collections.Generic;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Run;
using Hollowdeck.UI;

namespace Hollowdeck.Debug;

// Visual check for the Phase 1 design-token pass (UiTheme + ChromeStyles
// consolidation): renders every palette swatch, every CardType x Rarity
// frame combination, every status icon, and every button state on one
// screen, following ArtScreenshot's "boot it, screenshot it" pattern rather
// than pass/fail assertions - there's no automated way to assert a palette
// "looks right", a human has to look at it. Run windowed (not --headless):
// `godot --path . scenes/debug/StyleReferenceScreen.tscn`. Also opens fine
// directly in the editor for interactive inspection (hover the cards/buttons
// to see their hover states live).
public partial class StyleReferenceScreen : Control
{
    public override async void _Ready()
    {
        CardDatabase.LoadAll();

        var scroll = new ScrollContainer { AnchorRight = 1f, AnchorBottom = 1f };
        AddChild(scroll);
        var root = new VBoxContainer { CustomMinimumSize = new Vector2(1100, 0) };
        scroll.AddChild(root);
        root.AddThemeConstantOverride("separation", 24);

        root.AddChild(SectionLabel("Palette"));
        root.AddChild(BuildPaletteRow());

        root.AddChild(SectionLabel("Card Frames (CardType x Rarity)"));
        BuildCardFrameRow(root);

        root.AddChild(SectionLabel("Status Icons"));
        root.AddChild(BuildStatusIconRow());

        root.AddChild(SectionLabel("Button States"));
        root.AddChild(BuildButtonRow());

        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // The dummy renderer behind --headless returns a null backing
        // texture for GetViewport().GetTexture() (throws instead of
        // producing a blank image), so screenshotting only works windowed -
        // same constraint ArtScreenshot.cs documents. Headless runs still
        // build the whole layout above (the actual regression check), they
        // just skip the save and exit cleanly instead of hanging on an
        // unhandled exception with no Quit() ever called.
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("StyleReferenceScreen: layout built OK (skipping screenshot, running headless)");
        }
        else
        {
            GetViewport().GetTexture().GetImage().SavePng("user://style_reference.png");
            GD.Print("saved user://style_reference.png");
        }
        GetTree().Quit();
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        ThemeTypeVariation = "CombatDisplayLabel",
    };

    private static HFlowContainer BuildPaletteRow()
    {
        var row = new HFlowContainer();
        var swatches = new (string Name, Color Color)[]
        {
            ("BgDeep", UiTheme.Palette.BgDeep),
            ("BgPanel", UiTheme.Palette.BgPanel),
            ("AccentGold", UiTheme.Palette.AccentGold),
            ("AccentGoldBright", UiTheme.Palette.AccentGoldBright),
            ("AttackFill", UiTheme.Palette.AttackFill),
            ("AttackBorder", UiTheme.Palette.AttackBorder),
            ("AttackBorderHover", UiTheme.Palette.AttackBorderHover),
            ("SkillFill", UiTheme.Palette.SkillFill),
            ("SkillBorder", UiTheme.Palette.SkillBorder),
            ("SkillBorderHover", UiTheme.Palette.SkillBorderHover),
            ("Damage", UiTheme.Palette.Damage),
            ("Heal", UiTheme.Palette.Heal),
            ("Block", UiTheme.Palette.Block),
            ("RarityCommon", UiTheme.Palette.RarityCommon),
            ("RarityUncommon", UiTheme.Palette.RarityUncommon),
            ("RarityRare", UiTheme.Palette.RarityRare),
            ("StatusBuff", UiTheme.Palette.StatusBuff),
            ("StatusDebuff", UiTheme.Palette.StatusDebuff),
        };

        foreach (var (name, color) in swatches)
        {
            var cell = new VBoxContainer { CustomMinimumSize = new Vector2(120, 0) };
            cell.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(96, 48) });
            cell.AddChild(new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center });
            row.AddChild(cell);
        }
        return row;
    }

    // Real card data (strike=Attack, defend=Skill) cloned per rarity - the
    // cloning is local to this debug scene, it never touches CardDatabase,
    // so it can't leak a fake rarity into an actual run.
    //
    // Takes the already-in-tree parent and adds the row to it FIRST, before
    // populating - CardView.SetCardInstance no-ops until _Ready has resolved
    // its child node references, and _Ready only fires once a node's whole
    // ancestor chain is actually inside the live SceneTree, not merely once
    // it has a parent Node. CombatScreen.RefreshHand's AddChild-then-
    // SetCardInstance ordering (CombatScreen.cs:472-480) only works because
    // _handArea itself is already inside the tree; building a detached
    // HBoxContainer and adding cards to it before the container itself is
    // parented (the original bug here) leaves every CardView's IsInsideTree
    // false, so every card silently kept the .tscn's placeholder text.
    private static void BuildCardFrameRow(VBoxContainer root)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        root.AddChild(row);

        var baseAttack = CardDatabase.Get("strike");
        var baseSkill = CardDatabase.Get("defend");
        var cardScene = GD.Load<PackedScene>("res://scenes/CardView.tscn");

        foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare })
        {
            AddCard(row, cardScene, baseAttack, rarity);
            AddCard(row, cardScene, baseSkill, rarity);
        }
    }

    private static void AddCard(HBoxContainer row, PackedScene cardScene, CardDefinition template, Rarity rarity)
    {
        var clone = new CardDefinition
        {
            Id = template.Id,
            Name = $"{template.Name} ({rarity})",
            Cost = template.Cost,
            Type = template.Type,
            Target = template.Target,
            Exhaust = template.Exhaust,
            Rarity = rarity,
            Effects = template.Effects,
        };
        var view = cardScene.Instantiate<CardView>();
        view.CustomMinimumSize = new Vector2(180, 248);
        row.AddChild(view);
        view.SetCardInstance(new CardInstance(clone));
    }

    private static HBoxContainer BuildStatusIconRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        foreach (StatusType status in System.Enum.GetValues<StatusType>())
        {
            var cell = new VBoxContainer { CustomMinimumSize = new Vector2(80, 0) };
            if (ArtAssets.StatusIcon(status) is { } icon)
            {
                cell.AddChild(new TextureRect
                {
                    Texture = icon,
                    CustomMinimumSize = new Vector2(48, 48),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                });
            }
            cell.AddChild(new Label { Text = status.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
            row.AddChild(cell);
        }
        return row;
    }

    private static HBoxContainer BuildButtonRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        row.AddChild(new Button { Text = "Default" });
        row.AddChild(new Button { Text = "Disabled", Disabled = true });

        var chrome = new Button { Text = "End Turn (chrome)", CustomMinimumSize = new Vector2(160, 48) };
        chrome.AddThemeStyleboxOverride("normal", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_normal.png"));
        chrome.AddThemeStyleboxOverride("hover", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_hover.png"));
        chrome.AddThemeStyleboxOverride("pressed", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_pressed.png"));
        chrome.AddThemeStyleboxOverride("disabled", ChromeStyles.EndTurnButtonStyle("res://assets/ui/button_box_normal.png"));
        row.AddChild(chrome);

        row.AddChild(new Label { Text = "(hover/click live in the editor to preview hover/pressed states)" });
        return row;
    }
}
