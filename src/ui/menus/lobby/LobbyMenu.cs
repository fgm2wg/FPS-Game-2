using System.Linq;
using Godot;
using FPSGame.Network.Core;
using FPSGame.Network.Data;

namespace FPSGame.UI.Menus.Lobby;

/// <summary>
/// Controls the pre-match lobby interface where connected players choose teams,
/// inspect the roster, and the host initiates the match.
/// </summary>
public partial class LobbyMenu : Control
{
    [Export] public string ConnectionMenuScenePath { get; set; } = "res://src/ui/menus/lobby/ConnectionMenu.tscn";
    [Export] public string DefaultMatchMapPath { get; set; } = "res://src/levels/maps/starter_terrain.tscn";

    private ItemList _alphaRosterList = null!;
    private ItemList _bravoRosterList = null!;
    private Button _joinAlphaButton = null!;
    private Button _joinBravoButton = null!;
    private Button _startMatchButton = null!;
    private Button _leaveLobbyButton = null!;
    private OptionButton? _mapSelectButton;
    private Label _roomStatusLabel = null!;

    public override void _Ready()
    {
        _alphaRosterList = GetNode<ItemList>("%AlphaRosterList");
        _bravoRosterList = GetNode<ItemList>("%BravoRosterList");
        _joinAlphaButton = GetNode<Button>("%JoinAlphaButton");
        _joinBravoButton = GetNode<Button>("%JoinBravoButton");
        _startMatchButton = GetNode<Button>("%StartMatchButton");
        _leaveLobbyButton = GetNode<Button>("%LeaveLobbyButton");
        _roomStatusLabel = GetNode<Label>("%RoomStatusLabel");

        if (HasNode("%MapSelectOptionButton"))
        {
            _mapSelectButton = GetNode<OptionButton>("%MapSelectOptionButton");
            _mapSelectButton.Clear();
            _mapSelectButton.AddItem("Map: Starter Terrain (Mountains & Valleys)", 0);
            _mapSelectButton.AddItem("Map: Movement Gym (Flat Training Arena)", 1);
            _mapSelectButton.Selected = 0;
            _mapSelectButton.ItemSelected += OnMapSelected;
        }

        _joinAlphaButton.Pressed += () => OnTeamSelectPressed(1);
        _joinBravoButton.Pressed += () => OnTeamSelectPressed(2);
        _startMatchButton.Pressed += OnStartMatchPressed;
        _leaveLobbyButton.Pressed += OnLeaveLobbyPressed;

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.PlayerConnected += OnRosterChanged;
            NetworkManager.Instance.PlayerDisconnected += OnPlayerDisconnected;
            NetworkManager.Instance.PlayerTeamChanged += OnPlayerTeamChanged;
            NetworkManager.Instance.ServerDisconnected += OnServerDisconnected;
            NetworkManager.Instance.MatchStarted += OnMatchStarted;

            // Only the server/host can trigger match start
            _startMatchButton.Visible = NetworkManager.Instance.IsServer;
        }

        RefreshRosterUI();
    }

    public override void _ExitTree()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.PlayerConnected -= OnRosterChanged;
            NetworkManager.Instance.PlayerDisconnected -= OnPlayerDisconnected;
            NetworkManager.Instance.PlayerTeamChanged -= OnPlayerTeamChanged;
            NetworkManager.Instance.ServerDisconnected -= OnServerDisconnected;
            NetworkManager.Instance.MatchStarted -= OnMatchStarted;
        }

        if (_mapSelectButton != null)
        {
            _mapSelectButton.ItemSelected -= OnMapSelected;
        }
    }

    private void OnMapSelected(long index)
    {
        DefaultMatchMapPath = index switch
        {
            1 => "res://src/levels/world_gym/movement_gym.tscn",
            _ => "res://src/levels/maps/starter_terrain.tscn"
        };
    }

    private void OnTeamSelectPressed(int teamId)
    {
        NetworkManager.Instance?.RequestTeamChange(teamId);
    }

    private void OnStartMatchPressed()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;

        NetworkManager.Instance.RequestStartMatch(DefaultMatchMapPath);
    }

    private void OnLeaveLobbyPressed()
    {
        NetworkManager.Instance?.Disconnect();
        GetTree().ChangeSceneToFile(ConnectionMenuScenePath);
    }

    private void OnRosterChanged(NetworkPlayerInfo _) => RefreshRosterUI();
    private void OnPlayerDisconnected(long _) => RefreshRosterUI();
    private void OnPlayerTeamChanged(long _, int __) => RefreshRosterUI();

    private void OnServerDisconnected()
    {
        GetTree().ChangeSceneToFile(ConnectionMenuScenePath);
    }

    private void OnMatchStarted(string mapScenePath)
    {
        if (ResourceLoader.Exists(mapScenePath))
        {
            GetTree().ChangeSceneToFile(mapScenePath);
        }
        else
        {
            _roomStatusLabel.Text = $"Starting map: {mapScenePath} (Scene pending creation)";
        }
    }

    private void RefreshRosterUI()
    {
        if (NetworkManager.Instance == null) return;

        _alphaRosterList.Clear();
        _bravoRosterList.Clear();

        long myPeerId = Multiplayer.GetUniqueId();
        bool isHost = NetworkManager.Instance.IsServer;

        // Configure Start Match button visibility & text depending on whether this instance is Host or Client
        if (isHost)
        {
            _startMatchButton.Text = "START MATCH (HOST)";
            _startMatchButton.Disabled = false;
            _startMatchButton.Visible = true;
        }
        else
        {
            _startMatchButton.Text = "WAITING FOR HOST TO START...";
            _startMatchButton.Disabled = true;
            _startMatchButton.Visible = true;
        }

        if (_mapSelectButton != null)
        {
            _mapSelectButton.Disabled = !isHost;
        }

        var players = NetworkManager.Instance.Players.Values.ToList();
        int alphaCount = 0;
        int bravoCount = 0;

        Color yellowHighlight = new Color(1.0f, 0.85f, 0.15f, 1.0f);
        Color defaultColor = Colors.White;

        foreach (var player in players)
        {
            bool isMe = player.PeerId == myPeerId;
            string hostTag = player.PeerId == 1 ? " [HOST]" : "";
            string youTag = isMe ? " [YOU]" : "";
            string entry = $"{player.Name}{hostTag}{youTag} (ID: {player.PeerId})";

            int itemIdx;
            if (player.TeamId == 1)
            {
                itemIdx = _alphaRosterList.AddItem(entry);
                _alphaRosterList.SetItemCustomFgColor(itemIdx, isMe ? yellowHighlight : defaultColor);
                alphaCount++;
            }
            else if (player.TeamId == 2)
            {
                itemIdx = _bravoRosterList.AddItem(entry);
                _bravoRosterList.SetItemCustomFgColor(itemIdx, isMe ? yellowHighlight : defaultColor);
                bravoCount++;
            }
            else
            {
                // Unassigned defaults into alpha list visually
                itemIdx = _alphaRosterList.AddItem($"{entry} [UNASSIGNED]");
                _alphaRosterList.SetItemCustomFgColor(itemIdx, isMe ? yellowHighlight : defaultColor);
                alphaCount++;
            }
        }

        string roleText = isHost ? "HOST (You control match start)" : "CLIENT (Waiting for host)";
        _roomStatusLabel.Text = $"[{roleText}] Total Players: {players.Count}/12 | Team Alpha: {alphaCount} | Team Bravo: {bravoCount}";
    }
}
