using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Map;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Renders RunState.MapNodes (built once per run by MapGenerator) as a
// clickable branching graph: buttons positioned by Floor/Column, connecting
// lines hand-drawn in _Draw(). Only nodes reachable from RunState
// .CurrentNodeId are enabled - see ReachableIds().
public partial class MapScreen : Control
{
    private const float OriginX = 60f;
    private const float OriginY = 60f;
    private const float FloorSpacing = 130f;
    private const float ColumnSpacing = 80f;
    private const float NodeSize = 56f;

    private Control _nodeButtons = null!;
    private Label _goldLabel = null!;
    private Label _hpLabel = null!;
    private HBoxContainer _relicsRow = null!;
    private readonly Dictionary<string, Vector2> _nodeCenters = new();

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "black_cobalt", new Color(0.85f, 0.85f, 0.9f));
        DeckViewButtons.Attach(this);
        _nodeButtons = GetNode<Control>("NodeButtons");
        _goldLabel = GetNode<Label>("GoldLabel");
        _hpLabel = GetNode<Label>("HpLabel");
        _relicsRow = GetNode<HBoxContainer>("RelicsRow");
        GetNode<Button>("BackButton").Pressed += OnBackPressed;

        BuildLayout();
        BuildButtons();
        RefreshInfo();
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var node in RunState.MapNodes)
        {
            var from = _nodeCenters[node.Id];
            bool isChoosableNow = node.Id == RunState.CurrentNodeId;
            foreach (var nextId in node.NextNodeIds)
            {
                DrawCurvedPath(from, _nodeCenters[nextId], isChoosableNow);
            }
        }
    }

    // Gentle bow through an offset midpoint instead of a straight line, for
    // a hand-drawn feel - a real Bezier curve (via Curve2D) rather than a
    // sharp two-segment kink. The offset is derived from the endpoints
    // themselves (not RNG) so it's stable across the repeated QueueRedraw()
    // calls a Control naturally gets, instead of jittering every redraw.
    // Paths leading out of the player's current node get a brighter/thicker
    // stroke; everything else dims, matching "available vs. unreachable."
    private void DrawCurvedPath(Vector2 from, Vector2 to, bool highlighted)
    {
        var direction = (to - from).Normalized();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        float wobbleSeed = Mathf.Sin(from.X * 0.07f + from.Y * 0.05f + to.X * 0.03f);
        var control = (from + to) / 2f + perpendicular * (wobbleSeed * 12f);

        var curve = new Curve2D();
        curve.AddPoint(from, @out: (control - from) * 0.5f);
        curve.AddPoint(to, @in: (control - to) * 0.5f);
        var points = curve.Tessellate();

        var color = highlighted ? new Color(0.85f, 0.7f, 0.35f, 0.9f) : new Color(0.45f, 0.45f, 0.47f, 0.45f);
        DrawPolyline(points, color, highlighted ? 3f : 1.5f, antialiased: true);
    }

    private void BuildLayout()
    {
        foreach (var floor in RunState.MapNodes.GroupBy(n => n.Floor))
        {
            var nodes = floor.OrderBy(n => n.Column).ToList();
            for (int i = 0; i < nodes.Count; i++)
            {
                var center = new Vector2(
                    OriginX + floor.Key * FloorSpacing + NodeSize / 2f,
                    OriginY + i * ColumnSpacing + NodeSize / 2f);
                _nodeCenters[nodes[i].Id] = center;
            }
        }
    }

    // Boss reads as visually dominant/ominous - bigger footprint (grown
    // around the same center so the layout/path math above is untouched)
    // plus an always-on glow, instead of relying solely on its icon/"BOSS"
    // text to carry that weight like every other node type does.
    private const float BossNodeSize = NodeSize + 18f;

    private void BuildButtons()
    {
        var reachable = ReachableIds();
        foreach (var node in RunState.MapNodes)
        {
            var center = _nodeCenters[node.Id];
            bool isReachable = reachable.Contains(node.Id);
            bool isBoss = node.Type == MapNodeType.Boss;
            float size = isBoss ? BossNodeSize : NodeSize;
            var button = new Button
            {
                Position = center - new Vector2(size / 2f, size / 2f),
                Size = new Vector2(size, size),
                Disabled = !isReachable,
                Modulate = RunState.VisitedNodeIds.Contains(node.Id) ? new Color(0.6f, 0.6f, 0.6f) : Colors.White,
                TooltipText = NodeLabel(node.Type),
            };
            if (isBoss)
            {
                button.AddThemeStyleboxOverride("normal", ChromeStyles.BossNodeGlowStyle());
                button.AddThemeStyleboxOverride("hover", ChromeStyles.BossNodeGlowStyle());
                button.AddThemeStyleboxOverride("disabled", ChromeStyles.BossNodeGlowStyle());
            }
            // Icon-only node buttons when art exists; text label fallback.
            var icon = ArtAssets.MapIcon(node.Type);
            if (icon is not null)
            {
                button.Icon = icon;
                button.ExpandIcon = true;
                button.IconAlignment = HorizontalAlignment.Center;
            }
            else
            {
                button.Text = NodeLabel(node.Type);
            }
            if (isReachable)
            {
                button.Pressed += () => OnNodeChosen(node);
            }
            _nodeButtons.AddChild(button);
        }

        BuildCurrentNodeRing();
    }

    // A pulsing gold ring at the player's current position - previously
    // there was no rendering tied to RunState.CurrentNodeId at all, only
    // enabled/disabled buttons implied where the player was by elimination.
    private void BuildCurrentNodeRing()
    {
        if (string.IsNullOrEmpty(RunState.CurrentNodeId)) return;
        if (!_nodeCenters.TryGetValue(RunState.CurrentNodeId, out var center)) return;

        const float ringSize = NodeSize + 20f;
        var ring = new Panel
        {
            Position = center - new Vector2(ringSize / 2f, ringSize / 2f),
            Size = new Vector2(ringSize, ringSize),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = UiTheme.Palette.AccentGoldBright };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(999);
        ring.AddThemeStyleboxOverride("panel", style);
        _nodeButtons.AddChild(ring);
        _nodeButtons.MoveChild(ring, 0);

        var tween = ring.CreateTween();
        tween.SetLoops();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(ring, "modulate:a", 0.35f, 0.7);
        tween.TweenProperty(ring, "modulate:a", 1f, 0.7);
    }

    private HashSet<string> ReachableIds()
    {
        if (string.IsNullOrEmpty(RunState.CurrentNodeId))
        {
            return RunState.MapNodes.Where(n => n.Floor == 0).Select(n => n.Id).ToHashSet();
        }
        return RunState.GetMapNode(RunState.CurrentNodeId).NextNodeIds.ToHashSet();
    }

    private static string NodeLabel(MapNodeType type) => type switch
    {
        MapNodeType.Combat => "Fight",
        MapNodeType.Elite => "Elite!",
        MapNodeType.Rest => "Rest",
        MapNodeType.Shop => "Shop",
        MapNodeType.Treasure => "Chest",
        MapNodeType.Boss => "BOSS",
        MapNodeType.Event => "Event",
        _ => "?",
    };

    // Relics render as an icon row (same treatment as CombatScreen's relic
    // bar) rather than a comma-joined name list, which wrapped into the map
    // area once the list grew.
    private void RefreshInfo()
    {
        _goldLabel.Text = $"Gold: {RunState.Gold}";
        _hpLabel.Text = $"HP: {RunState.PlayerCurrentHp}/{RunState.PlayerMaxHp}";

        if (RunState.Relics.Count == 0)
        {
            _relicsRow.AddChild(new Label
            {
                Text = "Relics: none yet",
                Modulate = new Color(1f, 1f, 1f, 0.6f),
            });
            return;
        }

        foreach (var relic in RunState.Relics)
        {
            var tooltip = $"{relic.Definition.Name}\n{relic.Definition.Description}";
            if (ArtAssets.RelicIcon(relic.Definition.Id) is { } icon)
            {
                _relicsRow.AddChild(new TextureRect
                {
                    Texture = icon,
                    CustomMinimumSize = new Vector2(30, 30),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TooltipText = tooltip,
                    MouseFilter = MouseFilterEnum.Stop,
                });
            }
            else
            {
                _relicsRow.AddChild(new Label { Text = relic.Definition.Name, TooltipText = tooltip });
            }
        }
    }

    private void OnNodeChosen(MapNode node)
    {
        RunState.CurrentNodeId = node.Id;
        RunState.VisitedNodeIds.Add(node.Id);

        switch (node.Type)
        {
            case MapNodeType.Combat:
                CombatContext.EnemyDefinitionIds = node.EnemyIds;
                CombatContext.IsElite = false;
                CombatContext.IsBoss = false;
                CombatContext.GoldReward = 20 + node.EnemyIds.Count * 5;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
                break;
            case MapNodeType.Elite:
                CombatContext.EnemyDefinitionIds = node.EnemyIds;
                CombatContext.IsElite = true;
                CombatContext.IsBoss = false;
                CombatContext.GoldReward = 45;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
                break;
            case MapNodeType.Boss:
                CombatContext.EnemyDefinitionIds = node.EnemyIds;
                CombatContext.IsElite = false;
                CombatContext.IsBoss = true;
                CombatContext.GoldReward = 0;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
                break;
            case MapNodeType.Rest:
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Rest);
                break;
            case MapNodeType.Shop:
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Shop);
                break;
            case MapNodeType.Treasure:
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Treasure);
                break;
            case MapNodeType.Event:
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Event);
                break;
        }
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
}
