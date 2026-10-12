using Godot;
using FPSGame.Network.Core;

namespace FPSGame.UI.Menus.Lobby;

/// <summary>
/// Controls the initial connection menu for entering credentials,
/// hosting servers, and joining existing multiplayer sessions.
/// </summary>
public partial class ConnectionMenu : Control
{
    [Export] public string LobbyScenePath { get; set; } = "res://src/ui/menus/lobby/LobbyMenu.tscn";

    private LineEdit _playerNameInput = null!;
    private LineEdit _serverIpInput = null!;
    private LineEdit _serverPortInput = null!;
    private Button _hostButton = null!;
    private Button _joinButton = null!;
    private Button _soloGymButton = null!;
    private Button _dedicatedButton = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _playerNameInput = GetNode<LineEdit>("%PlayerNameInput");
        _serverIpInput = GetNode<LineEdit>("%ServerIpInput");
        _serverPortInput = GetNode<LineEdit>("%ServerPortInput");
        _hostButton = GetNode<Button>("%HostButton");
        _joinButton = GetNode<Button>("%JoinButton");
        _soloGymButton = GetNode<Button>("%SoloGymButton");
        _dedicatedButton = GetNode<Button>("%DedicatedButton");
        _statusLabel = GetNode<Label>("%StatusLabel");

        _hostButton.Pressed += OnHostButtonPressed;
        _joinButton.Pressed += OnJoinButtonPressed;
        _soloGymButton.Pressed += OnSoloGymButtonPressed;
        _dedicatedButton.Pressed += OnDedicatedButtonPressed;

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StatusChanged += UpdateStatus;
            NetworkManager.Instance.ConnectedToServer += OnConnectedSuccessfully;
            NetworkManager.Instance.ConnectionFailed += OnConnectionFailed;
        }

        UpdateStatus("Ready to connect");
    }

    public override void _ExitTree()
    {
        _hostButton.Pressed -= OnHostButtonPressed;
        _joinButton.Pressed -= OnJoinButtonPressed;
        _soloGymButton.Pressed -= OnSoloGymButtonPressed;
        _dedicatedButton.Pressed -= OnDedicatedButtonPressed;

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StatusChanged -= UpdateStatus;
            NetworkManager.Instance.ConnectedToServer -= OnConnectedSuccessfully;
            NetworkManager.Instance.ConnectionFailed -= OnConnectionFailed;
        }
    }

    private void OnSoloGymButtonPressed()
    {
        ApplyPlayerName();
        SetButtonsEnabled(false);
        UpdateStatus("Launching solo movement gym...");
        Error err = NetworkManager.Instance.HostGame(NetworkManager.DefaultPort);
        if (err == Error.Ok)
        {
            GetTree().ChangeSceneToFile("res://src/levels/world_gym/movement_gym.tscn");
        }
        else
        {
            SetButtonsEnabled(true);
        }
    }

    private void OnHostButtonPressed()
    {
        ApplyPlayerName();
        int port = ParsePort();

        SetButtonsEnabled(false);
        Error err = NetworkManager.Instance.HostGame(port);
        if (err == Error.Ok)
        {
            NavigateToLobby();
        }
        else
        {
            SetButtonsEnabled(true);
        }
    }

    private void OnJoinButtonPressed()
    {
        ApplyPlayerName();
        string ip = _serverIpInput.Text.Trim();
        int port = ParsePort();

        SetButtonsEnabled(false);
        UpdateStatus($"Connecting to {ip}:{port}...");

        Error err = NetworkManager.Instance.JoinGame(ip, port);
        if (err != Error.Ok)
        {
            SetButtonsEnabled(true);
        }
    }

    private void OnDedicatedButtonPressed()
    {
        int port = ParsePort();
        SetButtonsEnabled(false);
        NetworkManager.Instance.StartDedicatedServer(port);
    }

    private void OnConnectedSuccessfully()
    {
        NavigateToLobby();
    }

    private void OnConnectionFailed()
    {
        SetButtonsEnabled(true);
    }

    private void ApplyPlayerName()
    {
        string name = _playerNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Operator";
        }

        NetworkManager.Instance.SetLocalPlayerName(name);
    }

    private int ParsePort()
    {
        if (int.TryParse(_serverPortInput.Text.Trim(), out int port) && port > 1024 && port <= 65535)
        {
            return port;
        }

        return NetworkManager.DefaultPort;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _hostButton.Disabled = !enabled;
        _joinButton.Disabled = !enabled;
        _dedicatedButton.Disabled = !enabled;
    }

    private void UpdateStatus(string message)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = message;
        }
    }

    private void NavigateToLobby()
    {
        if (ResourceLoader.Exists(LobbyScenePath))
        {
            GetTree().ChangeSceneToFile(LobbyScenePath);
        }
        else
        {
            UpdateStatus("Connected! Lobby scene pending creation.");
        }
    }
}
