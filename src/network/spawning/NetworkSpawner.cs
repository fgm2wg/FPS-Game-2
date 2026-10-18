using System.Collections.Generic;
using Godot;
using FPSGame.Network.Core;

namespace FPSGame.Network.Spawning;

/// <summary>
/// Authoritative network entity spawner.
/// Utilizes MultiplayerSpawner to instantiate and synchronize player instances across all connected peers.
/// </summary>
public partial class NetworkSpawner : Node
{
    [Export] public PackedScene? PlayerScene { get; set; }
    [Export] public NodePath PlayersContainerPath { get; set; } = new("Players");
    [Export] public NodePath SpawnPointsContainerPath { get; set; } = new("SpawnPoints");

    private Node _playersContainer = null!;
    private Node? _spawnPointsContainer;
    private readonly List<Marker3D> _alphaSpawns = new();
    private readonly List<Marker3D> _bravoSpawns = new();

    public override void _Ready()
    {
        _playersContainer = GetNode<Node>(PlayersContainerPath);

        if (HasNode(SpawnPointsContainerPath))
        {
            _spawnPointsContainer = GetNode<Node>(SpawnPointsContainerPath);
            CollectSpawnPoints();
        }

        if (Multiplayer.MultiplayerPeer == null)
        {
            // Standalone editor testing (e.g. pressing F6 directly on map scene)
            GD.Print("[NetworkSpawner] Running in standalone offline mode. Spawning local player.");
            SpawnPlayerEntity(1, 1);
        }
        else if (NetworkManager.Instance != null && NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.PlayerConnected += HandlePlayerConnected;
            NetworkManager.Instance.PlayerDisconnected += HandlePlayerDisconnected;
            SpawnAllActivePlayers();
        }
        else if (NetworkManager.Instance != null && !NetworkManager.Instance.IsServer)
        {
            // Notify server that this client has fully loaded into the map
            RpcId(1, nameof(ClientLoadedMapRpc));
        }
    }

    public override void _ExitTree()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.PlayerConnected -= HandlePlayerConnected;
            NetworkManager.Instance.PlayerDisconnected -= HandlePlayerDisconnected;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientLoadedMapRpc()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;

        long senderId = Multiplayer.GetRemoteSenderId();
        int teamId = 1;
        if (NetworkManager.Instance.Players.TryGetValue(senderId, out var info))
        {
            teamId = info.TeamId;
        }

        SpawnPlayerEntity(senderId, teamId);

        string nodeName = senderId.ToString();
        if (_playersContainer.GetNodeOrNull<Node3D>(nodeName) is { } player)
        {
            RpcId(senderId, nameof(SyncClientSpawnRpc), player.GlobalPosition);
        }
    }

    private void HandlePlayerConnected(FPSGame.Network.Data.NetworkPlayerInfo info)
    {
        SpawnPlayerEntity(info.PeerId, info.TeamId);
    }

    private void CollectSpawnPoints()
    {
        if (_spawnPointsContainer == null) return;

        foreach (Node child in _spawnPointsContainer.GetChildren())
        {
            if (child is Marker3D marker)
            {
                if (marker.Name.ToString().Contains("Bravo", System.StringComparison.OrdinalIgnoreCase))
                {
                    _bravoSpawns.Add(marker);
                }
                else
                {
                    _alphaSpawns.Add(marker);
                }
            }
        }
    }

    private void SpawnAllActivePlayers()
    {
        if (PlayerScene == null || NetworkManager.Instance == null) return;

        foreach (var player in NetworkManager.Instance.Players.Values)
        {
            SpawnPlayerEntity(player.PeerId, player.TeamId);
        }
    }

    public void SpawnPlayerEntity(long peerId, int teamId)
    {
        if (PlayerScene == null)
        {
            GD.PushWarning("[NetworkSpawner] PlayerScene is not assigned!");
            return;
        }

        // Avoid duplicate spawns
        string nodeName = peerId.ToString();
        if (_playersContainer.HasNode(nodeName))
        {
            if (peerId != 1 && _playersContainer.GetNodeOrNull<Node3D>(nodeName) is { } existingPlayer)
            {
                RpcId(peerId, nameof(SyncClientSpawnRpc), existingPlayer.GlobalPosition);
            }
            return;
        }

        Node3D playerInstance = PlayerScene.Instantiate<Node3D>();
        playerInstance.Name = nodeName;

        if (playerInstance is FPSGame.Entities.Player.Controllers.PlayerController playerCtrl)
        {
            playerCtrl.TeamId = teamId;
        }

        // Position at appropriate team spawn point before AddChild so initial network replication has valid transform
        Vector3 spawnPos = GetSpawnPosition(teamId, (int)peerId);
        playerInstance.Position = spawnPos;

        // Add to scene tree before setting GlobalPosition so global transform calculations are valid
        _playersContainer.AddChild(playerInstance);
        playerInstance.GlobalPosition = spawnPos;

        // If this is a remote client, authoritatively tell that client its spawn position
        if (peerId != 1)
        {
            RpcId(peerId, nameof(SyncClientSpawnRpc), spawnPos);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncClientSpawnRpc(Vector3 spawnPos)
    {
        string myName = Multiplayer.GetUniqueId().ToString();
        if (_playersContainer != null && _playersContainer.GetNodeOrNull<Node3D>(myName) is { } myPlayer)
        {
            if (myPlayer is FPSGame.Entities.Player.Controllers.PlayerController ctrl)
            {
                ctrl.TeleportToSpawn(spawnPos);
            }
            else
            {
                myPlayer.GlobalPosition = spawnPos;
            }
        }
    }

    private Vector3 GetSpawnPosition(int teamId, int seedOffset)
    {
        var spawnList = teamId == 2 ? _bravoSpawns : _alphaSpawns;
        if (spawnList.Count > 0)
        {
            int index = System.Math.Abs(seedOffset) % spawnList.Count;
            return spawnList[index].GlobalPosition;
        }

        // Fallback default spawn offset matching the 300m engagement distance
        return teamId == 2 ? new Vector3(515.0f, 66.0f, -1822.0f) : new Vector3(215.0f, 106.0f, -1822.0f);
    }

    private void HandlePlayerDisconnected(long peerId)
    {
        string nodeName = peerId.ToString();
        if (_playersContainer.HasNode(nodeName))
        {
            _playersContainer.GetNode(nodeName).QueueFree();
        }
    }
}
