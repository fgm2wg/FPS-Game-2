using System;
using System.Collections.Generic;
using Godot;
using FPSGame.Network.Data;

namespace FPSGame.Network.Core;

/// <summary>
/// Authoritative network lifecycle singleton.
/// Manages ENetMultiplayerPeer, peer registration, client-server handshake, and player roster.
/// Configured as an Autoload singleton in project.godot.
/// </summary>
public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; } = null!;

    public const int DefaultPort = 7777;
    public const int DefaultMaxPlayers = 12; // 6v6
    public const string DefaultAddress = "127.0.0.1";

    // Player state dictionary (PeerId -> NetworkPlayerInfo)
    private readonly Dictionary<long, NetworkPlayerInfo> _players = new();
    public IReadOnlyDictionary<long, NetworkPlayerInfo> Players => _players;

    public string LocalPlayerName { get; private set; } = "Player";
    public bool IsServer => Multiplayer.IsServer();
    public bool IsActive => Multiplayer.HasMultiplayerPeer() && 
                            Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected;

    // Strongly-typed events
    public event Action<NetworkPlayerInfo>? PlayerConnected;
    public event Action<long>? PlayerDisconnected;
    public event Action? ConnectedToServer;
    public event Action? ConnectionFailed;
    public event Action? ServerDisconnected;
    public event Action<string>? StatusChanged;
    public event Action<long, int>? PlayerTeamChanged;
    public event Action<string>? MatchStarted;

    private ENetMultiplayerPeer? _peer;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _Ready()
    {
        Multiplayer.PeerConnected += HandlePeerConnected;
        Multiplayer.PeerDisconnected += HandlePeerDisconnected;
        Multiplayer.ConnectedToServer += HandleConnectedToServer;
        Multiplayer.ConnectionFailed += HandleConnectionFailed;
        Multiplayer.ServerDisconnected += HandleServerDisconnected;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= HandlePeerConnected;
        Multiplayer.PeerDisconnected -= HandlePeerDisconnected;
        Multiplayer.ConnectedToServer -= HandleConnectedToServer;
        Multiplayer.ConnectionFailed -= HandleConnectionFailed;
        Multiplayer.ServerDisconnected -= HandleServerDisconnected;

        Disconnect();

        if (Instance == this)
        {
            Instance = null!;
        }
    }

    public void SetLocalPlayerName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            LocalPlayerName = name.Trim();
        }
    }

    /// <summary>
    /// Hosts a listen server on the specified port.
    /// </summary>
    public Error HostGame(int port = DefaultPort, int maxPlayers = DefaultMaxPlayers)
    {
        Disconnect();

        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateServer(port, maxPlayers);
        if (err != Error.Ok)
        {
            StatusChanged?.Invoke($"Failed to host server on port {port}: {err}");
            return err;
        }

        Multiplayer.MultiplayerPeer = _peer;

        // Register host player (Peer ID 1)
        var hostInfo = new NetworkPlayerInfo(1, LocalPlayerName, teamId: 1);
        _players[1] = hostInfo;

        StatusChanged?.Invoke($"Hosting listen server on port {port} (Max players: {maxPlayers})");
        PlayerConnected?.Invoke(hostInfo);
        return Error.Ok;
    }

    /// <summary>
    /// Hosts a headless dedicated server without a local player controller.
    /// </summary>
    public Error StartDedicatedServer(int port = DefaultPort, int maxPlayers = DefaultMaxPlayers)
    {
        Disconnect();

        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateServer(port, maxPlayers);
        if (err != Error.Ok)
        {
            StatusChanged?.Invoke($"Failed to start dedicated server on port {port}: {err}");
            return err;
        }

        Multiplayer.MultiplayerPeer = _peer;
        StatusChanged?.Invoke($"Dedicated server listening on port {port} (Max players: {maxPlayers})");
        return Error.Ok;
    }

    /// <summary>
    /// Connects to a remote server at the specified address and port.
    /// </summary>
    public Error JoinGame(string address = DefaultAddress, int port = DefaultPort)
    {
        Disconnect();

        if (string.IsNullOrWhiteSpace(address))
        {
            address = DefaultAddress;
        }

        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateClient(address, port);
        if (err != Error.Ok)
        {
            StatusChanged?.Invoke($"Failed to connect to {address}:{port}: {err}");
            return err;
        }

        Multiplayer.MultiplayerPeer = _peer;
        StatusChanged?.Invoke($"Connecting to {address}:{port}...");
        return Error.Ok;
    }

    /// <summary>
    /// Closes the current multiplayer connection and resets state.
    /// </summary>
    public void Disconnect()
    {
        if (_peer != null)
        {
            _peer.Close();
            _peer.Dispose();
            _peer = null;
        }

        Multiplayer.MultiplayerPeer = null;
        _players.Clear();
        StatusChanged?.Invoke("Disconnected");
    }

    public void RequestTeamChange(int newTeamId)
    {
        if (!IsActive) return;

        long myPeerId = Multiplayer.GetUniqueId();
        if (IsServer)
        {
            SetPlayerTeamInternal(myPeerId, newTeamId);
            PlayerTeamChanged?.Invoke(myPeerId, newTeamId);
            Rpc(nameof(SyncTeamChangeRpc), myPeerId, newTeamId);
        }
        else
        {
            RpcId(1, nameof(RequestTeamChangeRpc), newTeamId);
        }
    }

    public void RequestStartMatch(string mapScenePath)
    {
        if (!IsServer)
        {
            GD.PushWarning("Only the host/server can start the match!");
            return;
        }

        Rpc(nameof(StartMatchRpc), mapScenePath);
    }

    #region Connection Signal Callbacks

    private void HandlePeerConnected(long peerId)
    {
        StatusChanged?.Invoke($"Peer {peerId} connected");

        if (IsServer)
        {
            // Sync all already-registered players to the newly connected peer
            foreach (var kvp in _players)
            {
                RpcId(peerId, nameof(SyncPlayerRpc), kvp.Key, kvp.Value.Name, kvp.Value.TeamId);
            }
        }
    }

    private void HandlePeerDisconnected(long peerId)
    {
        if (_players.Remove(peerId))
        {
            StatusChanged?.Invoke($"Peer {peerId} disconnected");
            PlayerDisconnected?.Invoke(peerId);

            if (IsServer)
            {
                Rpc(nameof(SyncUnregisterPlayerRpc), peerId);
            }
        }
    }

    private void HandleConnectedToServer()
    {
        long myPeerId = Multiplayer.GetUniqueId();
        StatusChanged?.Invoke($"Connected to server as Peer {myPeerId}");
        ConnectedToServer?.Invoke();

        // Send registration request to server
        RpcId(1, nameof(RequestRegisterPlayerRpc), LocalPlayerName);
    }

    private void HandleConnectionFailed()
    {
        StatusChanged?.Invoke("Connection failed: Server unavailable or timed out");
        ConnectionFailed?.Invoke();
        Disconnect();
    }

    private void HandleServerDisconnected()
    {
        StatusChanged?.Invoke("Server closed or disconnected");
        ServerDisconnected?.Invoke();
        Disconnect();
    }

    #endregion

    #region Remote Procedure Calls (RPCs)

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestRegisterPlayerRpc(string playerName)
    {
        if (!IsServer) return;

        long senderId = Multiplayer.GetRemoteSenderId();
        int teamId = BalanceTeams();

        var playerInfo = new NetworkPlayerInfo(senderId, playerName, teamId);
        _players[senderId] = playerInfo;
        PlayerConnected?.Invoke(playerInfo);

        // Broadcast new player registration to all connected clients
        Rpc(nameof(SyncPlayerRpc), senderId, playerName, teamId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncPlayerRpc(long peerId, string playerName, int teamId)
    {
        var playerInfo = new NetworkPlayerInfo(peerId, playerName, teamId);
        _players[peerId] = playerInfo;
        PlayerConnected?.Invoke(playerInfo);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncUnregisterPlayerRpc(long peerId)
    {
        if (_players.Remove(peerId))
        {
            PlayerDisconnected?.Invoke(peerId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestTeamChangeRpc(int newTeamId)
    {
        if (!IsServer) return;

        long senderId = Multiplayer.GetRemoteSenderId();
        SetPlayerTeamInternal(senderId, newTeamId);
        PlayerTeamChanged?.Invoke(senderId, newTeamId);

        Rpc(nameof(SyncTeamChangeRpc), senderId, newTeamId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncTeamChangeRpc(long peerId, int newTeamId)
    {
        SetPlayerTeamInternal(peerId, newTeamId);
        PlayerTeamChanged?.Invoke(peerId, newTeamId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartMatchRpc(string mapScenePath)
    {
        StatusChanged?.Invoke($"Loading match: {mapScenePath}");
        MatchStarted?.Invoke(mapScenePath);
    }

    #endregion

    public void UpdatePlayerStats(long peerId, int score, int kills, int deaths, int assists)
    {
        if (_players.TryGetValue(peerId, out var info))
        {
            info.Score = score;
            info.Kills = kills;
            info.Deaths = deaths;
            info.Assists = assists;
            _players[peerId] = info;
        }
    }

    private void SetPlayerTeamInternal(long peerId, int teamId)
    {
        if (_players.TryGetValue(peerId, out var info))
        {
            info.TeamId = teamId;
            _players[peerId] = info;
        }
    }

    private int BalanceTeams()
    {
        int team1Count = 0;
        int team2Count = 0;

        foreach (var p in _players.Values)
        {
            if (p.TeamId == 1) team1Count++;
            else if (p.TeamId == 2) team2Count++;
        }

        return team1Count <= team2Count ? 1 : 2;
    }
}
