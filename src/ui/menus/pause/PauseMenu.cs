using System;
using Godot;
using FPSGame.Network.Core;

namespace FPSGame.UI.Menus.Pause;

/// <summary>
/// Controls the in-game pause and match options overlay.
/// In multiplayer, this does not freeze the engine time, but releases mouse capture
/// and allows the player to resume or disconnect.
/// </summary>
public partial class PauseMenu : Control
{
    [Export] public string MainMenuScenePath { get; set; } = "res://src/ui/menus/lobby/ConnectionMenu.tscn";

    public event Action? Resumed;
    public event Action? LeftMatch;

    private Button _resumeButton = null!;
    private Button _closeXButton = null!;
    private Button _leaveButton = null!;
    private Label _matchInfoLabel = null!;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        _resumeButton = GetNode<Button>("%ResumeButton");
        _closeXButton = GetNode<Button>("%CloseXButton");
        _leaveButton = GetNode<Button>("%LeaveButton");
        _matchInfoLabel = GetNode<Label>("%MatchInfoLabel");

        _resumeButton.Pressed += Resume;
        _closeXButton.Pressed += Resume;
        _leaveButton.Pressed += OnLeavePressed;

        // Hidden by default
        Visible = false;
    }

    public override void _ExitTree()
    {
        _resumeButton.Pressed -= Resume;
        _closeXButton.Pressed -= Resume;
        _leaveButton.Pressed -= OnLeavePressed;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            Resume();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Open(int teamId, string playerName)
    {
        string teamName = teamId == 2 ? "Team Bravo (Red)" : "Team Alpha (Blue)";
        _matchInfoLabel.Text = $"Operator: {playerName} | Assigned: {teamName}";

        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Resume()
    {
        Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Resumed?.Invoke();
    }

    private void OnLeavePressed()
    {
        Visible = false;
        LeftMatch?.Invoke();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.Disconnect();
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }
}
