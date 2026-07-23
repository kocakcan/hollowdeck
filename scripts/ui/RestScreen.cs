using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

// Rest map nodes only offer a heal this pass - card upgrades are explicitly
// deferred (see the Phase 4 plan; no CardInstance-level upgrade mechanic
// exists yet).
public partial class RestScreen : Control
{
    private const float HealFraction = 0.3f;

    public override void _Ready()
    {
        ScreenBackground.Attach(this, "dirt", new Color(0.5f, 0.42f, 0.35f));
        int healAmount = Mathf.RoundToInt(RunState.PlayerMaxHp * HealFraction);
        var healButton = GetNode<Button>("CenterContainer/VBoxContainer/HealButton");
        healButton.Text = $"Rest - Heal {healAmount} HP";
        healButton.Pressed += () => OnHealPressed(healAmount);

        GetNode<Button>("CenterContainer/VBoxContainer/LeaveButton").Pressed += OnLeavePressed;
    }

    private void OnHealPressed(int amount)
    {
        RunState.PlayerCurrentHp = Mathf.Min(RunState.PlayerMaxHp, RunState.PlayerCurrentHp + amount);
        OnLeavePressed();
    }

    private void OnLeavePressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
