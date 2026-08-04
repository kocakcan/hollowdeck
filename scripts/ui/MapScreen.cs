using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
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
    private const float MaxFloorSpacing = 130f;
    private const float MaxColumnSpacing = 150f;

    // 2x the 32px icon grid. Was 56, which is 1.75x - a fractional scale, and
    // the button's ExpandIcon was resampling every node icon to reach it. See
    // BuildButtons: the icon is a child TextureRect at an exact integer scale
    // now rather than the Button's own Icon property.
    private const float NodeSize = PixelSpec.IconGrid * 2;

    // Right-hand margin the rightmost (boss) node must stay inside. Floor
    // spacing is derived from it rather than fixed at MaxFloorSpacing: act 3's
    // 10 floors at 130px each would be 1300px wide against a 1152px design
    // width, so the boss node would sit off-screen. Shorter acts still get the
    // roomier 130.
    private const float RightMargin = 90f;

    // Vertical band the graph is allowed to occupy: below the run-status
    // block, above the footer row. Column spacing is derived from what is left
    // exactly as floor spacing is derived from the width, which is what
    // reclaimed the bottom 45% of this screen - the old layout stacked every
    // floor downward from a fixed y=60 at a fixed 80px pitch, so a four-deep
    // act ended at y=328 and the rest of the canvas was empty (ROADMAP
    // Phase 4).
    private const float TopMargin = 116f;
    private const float BottomMargin = 88f;
    private const float DesignHeight = 648f;

    private Control _nodeButtons = null!;
    private readonly Dictionary<string, Vector2> _nodeCenters = new();

    // Where the keyboard starts. Godot's focus navigation skips Disabled
    // controls, and BuildButtons disables every unreachable node, so from here
    // Tab and the arrow keys only ever visit legal moves - the map needed no
    // navigation code of its own, only somewhere to begin.
    private Button? _firstReachableButton;

    public override void _Ready()
    {
        var act = RunState.CurrentAct;
        ScreenBackground.Attach(this, act.MapBackground, Color.FromString(act.MapTint, Colors.White));
        DeckViewButtons.Attach(this);
        _nodeButtons = GetNode<Control>("NodeButtons");
        GetNode<Button>("BackButton").Pressed += OnBackPressed;

        // Gold, HP and the relic row all used to be hand-placed Labels on this
        // scene - the relics as a bare 30x30 TextureRect strip pinned to
        // y=570, which is why a lone relic rendered as an unframed icon
        // floating in the map's dead space.
        // "of N", not just the act number: a run's length was nowhere on screen,
        // so a player who had just cleared act 2's boss had no way to know a
        // third chapter was what the fresh map in front of them meant.
        ScreenChrome.AddTitle(this, $"Act {RunState.ActIndex + 1} of {ActDatabase.Count} — {act.Name}");
        ScreenChrome.AddRunStatus(this);

        BuildLayout();
        BuildButtons();
        QueueRedraw();

        // Falls back to Back if the act somehow offers no move, so the screen
        // is never a keyboard dead end.
        ScreenKeyboardNav.Attach(this,
            () => (Control?)_firstReachableButton ?? GetNode<Button>("BackButton"),
            OnBackPressed);
    }

    // Draws from _nodeCenters, which BuildLayout filled from the graph that
    // was live when this screen was built - deliberately not re-read from
    // RunState here. A freed-but-not-yet-collected MapScreen still gets one
    // more _Draw at the end of the frame, and by then RunState.MapNodes can
    // already be the *next* act's graph (AdvanceAct regenerates it, and the
    // smoke tests do it twice in a row), whose ids this instance has never
    // seen. That threw a KeyNotFoundException out of _Draw every suite run.
    public override void _Draw()
    {
        foreach (var node in RunState.MapNodes)
        {
            if (!_nodeCenters.TryGetValue(node.Id, out var from)) continue;
            bool isChoosableNow = node.Id == RunState.CurrentNodeId;
            foreach (var nextId in node.NextNodeIds)
            {
                if (_nodeCenters.TryGetValue(nextId, out var to))
                {
                    DrawCurvedPath(from, to, isChoosableNow);
                }
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
        // Derived from the longest act that has to fit, not a constant - see
        // RightMargin. GroupBy's key is the floor index, so Max() + 1 is the
        // floor count of whatever act is being rendered.
        int floorCount = RunState.MapNodes.Count == 0 ? 1 : RunState.MapNodes.Max(n => n.Floor) + 1;
        float availableWidth = 1152f - OriginX - NodeSize - RightMargin;
        float floorSpacing = floorCount <= 1
            ? MaxFloorSpacing
            : Mathf.Min(MaxFloorSpacing, availableWidth / (floorCount - 1));

        // Same derivation, vertically: the widest floor decides the pitch, so
        // a 4-deep act spreads across the whole band and a 2-deep one does not
        // stretch into a ladder.
        var floors = RunState.MapNodes.GroupBy(n => n.Floor).ToList();
        int widestFloor = floors.Count == 0 ? 1 : floors.Max(f => f.Count());
        float availableHeight = DesignHeight - TopMargin - BottomMargin - NodeSize;
        float columnSpacing = widestFloor <= 1
            ? MaxColumnSpacing
            : Mathf.Min(MaxColumnSpacing, availableHeight / (widestFloor - 1));
        float bandCenterY = TopMargin + (availableHeight + NodeSize) / 2f;

        foreach (var floor in floors)
        {
            var nodes = floor.OrderBy(n => n.Column).ToList();
            // Each floor is centred on the band rather than stacked downward
            // from the top: a floor with two nodes used to sit in the top two
            // slots with a gap under it, which read as a graph that had run
            // out of room rather than one that had branched.
            float startY = bandCenterY - (nodes.Count - 1) * columnSpacing / 2f;
            for (int i = 0; i < nodes.Count; i++)
            {
                var center = new Vector2(
                    OriginX + floor.Key * floorSpacing + NodeSize / 2f,
                    startY + i * columnSpacing);
                _nodeCenters[nodes[i].Id] = center;
            }
        }
    }

    // Boss reads as visually dominant/ominous - bigger footprint (grown
    // around the same center so the layout/path math above is untouched)
    // plus an always-on glow, instead of relying solely on its icon/"BOSS"
    // text to carry that weight like every other node type does.
    private const float BossNodeSize = PixelSpec.IconGrid * 3;

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
            //
            // As a centred child TextureRect at an exact 2x (3x for the boss),
            // not the Button's own Icon with ExpandIcon: ExpandIcon stretches
            // the texture to whatever the content rect happens to be, which is
            // a fractional scale of the 32px grid and blurs the icon under
            // Nearest into uneven pixel widths (ART_SPEC section 2).
            var icon = ArtAssets.MapIcon(node.Type);
            if (icon is not null)
            {
                // Left on the default top-left anchors and positioned by hand.
                // LayoutPreset.Center re-bases Position on the parent's centre
                // rather than its corner, so centring by offset on top of it
                // moves the icon a further half-node down and right - which is
                // exactly how it rendered.
                var rect = ScreenChrome.PixelIcon(icon, isBoss ? 3 : 2);
                rect.Size = rect.CustomMinimumSize;
                rect.Position = (new Vector2(size, size) - rect.CustomMinimumSize) / 2f;
                button.AddChild(rect);
            }
            else
            {
                button.Text = NodeLabel(node.Type);
            }
            if (isReachable)
            {
                button.Pressed += () => OnNodeChosen(node);
                _firstReachableButton ??= button;
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
        // Square, not a 999-radius circle: a radius-rounded box is an
        // anti-aliased curve (ART_SPEC section 6). The ring still reads as
        // "you are here" because it is the only pulsing element on the map.
        var style = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = UiTheme.Palette.AccentGoldBright };
        style.SetBorderWidthAll(3);
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

    private void OnNodeChosen(MapNode node)
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        RunState.CurrentNodeId = node.Id;
        bool firstVisit = RunState.VisitedNodeIds.Add(node.Id);

        // Scoring tallies (RunScore's Floors Climbed / Mystery Machine).
        // Floor is 0-based in MapGenerator, so climbing to floor 3 means 4
        // rooms entered. Counted on entering the node rather than on
        // finishing it, matching StS - dying on a floor still counts it.
        // Floors accumulate across acts (each act renumbers from 0) - see
        // RunStats.FloorsInPreviousActs.
        RunState.Stats.MaxFloorReached = Mathf.Max(
            RunState.Stats.MaxFloorReached, RunState.Stats.FloorsInPreviousActs + node.Floor + 1);
        if (firstVisit && node.Type == MapNodeType.Event) RunState.Stats.EventRoomsVisited++;

        // Rewards come from the act's own data, so later acts pay for their
        // pricier shops without a magic number per node type here.
        var act = RunState.CurrentAct;

        switch (node.Type)
        {
            case MapNodeType.Combat:
                CombatContext.EnemyDefinitionIds = node.EnemyIds;
                CombatContext.IsElite = false;
                CombatContext.IsBoss = false;
                CombatContext.GoldReward = act.NormalGoldBase + node.EnemyIds.Count * act.GoldPerEnemy;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
                break;
            case MapNodeType.Elite:
                CombatContext.EnemyDefinitionIds = node.EnemyIds;
                CombatContext.IsElite = true;
                CombatContext.IsBoss = false;
                CombatContext.GoldReward = act.EliteGold;
                RunManager.Instance.ChangeScreen(RunManager.ScreenState.Combat);
                break;
            case MapNodeType.Boss:
                CombatContext.EnemyDefinitionIds = node.EnemyIds;
                CombatContext.IsElite = false;
                CombatContext.IsBoss = true;
                CombatContext.GoldReward = act.BossGold;
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

    private void OnBackPressed()
    {
        AudioManager.Instance?.PlaySfx("ui_click");
        RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
    }
}
