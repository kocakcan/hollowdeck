using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GetNode<Button>("CenterContainer/VBoxContainer/StartButton").Pressed += OnStartPressed;
    }

    private void OnStartPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.Map);
}
