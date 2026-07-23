using Godot;
using Hollowdeck.Run;

namespace Hollowdeck.UI;

public partial class MapScreen : Control
{
    public override void _Ready()
    {
        GetNode<Button>("BackButton").Pressed += OnBackPressed;
    }

    private void OnBackPressed() => RunManager.Instance.ChangeScreen(RunManager.ScreenState.MainMenu);
}
