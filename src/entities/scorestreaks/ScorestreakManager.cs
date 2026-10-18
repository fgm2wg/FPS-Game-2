using System;
using System.Collections.Generic;
using Godot;
using FPSGame.Entities.Player.Controllers;
using FPSGame.UI.HUD;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// Authoritative network coordinator for tactical scorestreaks (UAV, Care Package, Airstrike).
/// Replicates aircraft flight, audio, supply drops, and aerial bombardments across all peers.
/// </summary>
public partial class ScorestreakManager : Node
{
    public static ScorestreakManager? Instance { get; private set; }

    private static long _nextCrateId = 1000;
    private readonly HashSet<long> _claimedCrates = new();

    public static PackedScene? A10Scene { get; private set; }
    public static PackedScene? C17Scene { get; private set; }
    public static PackedScene? UavScene { get; private set; }
    public static PackedScene? CrateScene { get; private set; }
    public static PackedScene? ChuteScene { get; private set; }
    public static PackedScene? B52Scene { get; private set; }
    public static PackedScene? Mk84Scene { get; private set; }

    public static AudioStream? A10Audio { get; private set; }
    public static AudioStream? C17Audio { get; private set; }
    public static AudioStream? RadioAudio { get; private set; }

    public static void PreloadAssets()
    {
        A10Scene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/a10.glb");
        C17Scene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/c17.glb");
        UavScene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/mq1_uav.glb");
        CrateScene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/supply_crate.glb");
        ChuteScene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/parachute.glb");
        B52Scene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/b52.glb");
        Mk84Scene ??= ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/mk84.glb");

        A10Audio ??= ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/a10_strafe.mp3");
        C17Audio ??= ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/c17_flyby.wav")
                  ?? ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/c17_flyby.mp3");
        RadioAudio ??= ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/radio_confirm.mp3");
    }

    private static float _cachedPeakHeight = float.MinValue;

    /// <summary>
    /// Computes the highest terrain/mountain elevation in the map so aircraft can safely cruise 50-100m above it.
    /// </summary>
    public static float GetMapPeakHeight(SceneTree? tree)
    {
        if (_cachedPeakHeight > -9000f) return _cachedPeakHeight;
        if (tree?.Root == null) return 110.0f;

        float peak = -9999f;

        // 1. Query Terrain3D if available in the scene tree
        var terrainNode = tree.Root.FindChild("Terrain3D", true, false);
        if (terrainNode != null)
        {
            var data = terrainNode.Get("data").AsGodotObject();
            if (data != null)
            {
                for (float x = 20; x <= 1000; x += 80)
                {
                    for (float z = -2020; z <= -1050; z += 80)
                    {
                        float h = data.Call("get_height", new Vector3(x, 0, z)).AsSingle();
                        if (h > peak) peak = h;
                    }
                }
            }
        }

        // 2. Fallback to SpawnPoints and markers
        var spawnContainer = tree.Root.FindChild("SpawnPoints", true, false);
        if (spawnContainer != null)
        {
            foreach (Node child in spawnContainer.GetChildren())
            {
                if (child is Node3D n3d && n3d.GlobalPosition.Y > peak)
                {
                    peak = n3d.GlobalPosition.Y;
                }
            }
        }

        if (peak < 110.0f) peak = 110.0f;

        _cachedPeakHeight = peak;
        return _cachedPeakHeight;
    }

    public static ScorestreakManager EnsureExists(SceneTree? tree)
    {
        PreloadAssets();
        if (Instance != null && Instance.IsInsideTree()) return Instance;

        if (tree?.Root != null)
        {
            var existing = tree.Root.GetNodeOrNull<ScorestreakManager>("ScorestreakManager")
                ?? tree.Root.FindChild("ScorestreakManager", true, false) as ScorestreakManager;
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var mgr = new ScorestreakManager { Name = "ScorestreakManager" };
            tree.Root.AddChild(mgr);
            Instance = mgr;
            return mgr;
        }

        return Instance!;
    }

    public override void _EnterTree()
    {
        Instance = this;
        PreloadAssets();
    }

    public override void _Ready()
    {
        WarmupAssets();
    }

    private void WarmupAssets()
    {
        PreloadAssets();

        // Pre-instantiate models into a hidden off-screen warmup container.
        // This forces Godot's Forward+ Direct3D 12 renderer to parse GLTF scenes, upload vertex/index buffers,
        // and precompile pipeline state objects (PSOs) at map load instead of stalling the main thread on first streak call!
        var warmup = new Node3D
        {
            Name = "ScorestreakWarmup",
            Position = new Vector3(0.0f, -9999.0f, 0.0f),
            Visible = false
        };
        AddChild(warmup);

        try
        {
            if (A10Scene != null)
            {
                var a10 = A10Scene.Instantiate<Node3D>();
                warmup.AddChild(a10);
            }
            if (C17Scene != null)
            {
                var c17 = C17Scene.Instantiate<Node3D>();
                warmup.AddChild(c17);
            }
            if (UavScene != null)
            {
                var uav = UavScene.Instantiate<Node3D>();
                warmup.AddChild(uav);
            }
            if (CrateScene != null)
            {
                var crate = CrateScene.Instantiate<Node3D>();
                warmup.AddChild(crate);
            }
            if (ChuteScene != null)
            {
                var chute = ChuteScene.Instantiate<Node3D>();
                warmup.AddChild(chute);
            }
            if (B52Scene != null)
            {
                var b52 = B52Scene.Instantiate<Node3D>();
                warmup.AddChild(b52);
            }
            if (Mk84Scene != null)
            {
                var mk84 = Mk84Scene.Instantiate<Node3D>();
                warmup.AddChild(mk84);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ScorestreakManager] Warmup warning: {ex.Message}");
        }
    }

    public void CallUav(long callerPeerId, int teamId, Vector3 origin)
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                Rpc(nameof(ReplicateUavRpc), callerPeerId, teamId, origin);
            }
            else
            {
                RpcId(1, nameof(RequestUavRpc), callerPeerId, teamId, origin);
            }
        }
        else
        {
            SpawnLocalUav(callerPeerId, teamId, origin);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestUavRpc(long callerPeerId, int teamId, Vector3 origin)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(ReplicateUavRpc), callerPeerId, teamId, origin);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateUavRpc(long callerPeerId, int teamId, Vector3 origin)
    {
        SpawnLocalUav(callerPeerId, teamId, origin);
    }

    private void SpawnLocalUav(long callerPeerId, int teamId, Vector3 origin)
    {
        var drone = new UavDrone();
        drone.Initialize(origin, callerPeerId, teamId);
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        tree?.Root?.CallDeferred(Node.MethodName.AddChild, drone);
    }

    public void CallCarePackage(long callerPeerId, int teamId, Vector3 dropTarget, Vector3 flightDir, StrikeTargetMarker? marker)
    {
        long crateId = (callerPeerId * 1000000L) + (_nextCrateId++);
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                Rpc(nameof(ReplicateCarePackageRpc), crateId, callerPeerId, teamId, dropTarget, flightDir);
            }
            else
            {
                RpcId(1, nameof(RequestCarePackageRpc), crateId, callerPeerId, teamId, dropTarget, flightDir);
            }
        }
        else
        {
            SpawnLocalCarePackage(crateId, callerPeerId, teamId, dropTarget, flightDir, marker);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestCarePackageRpc(long crateId, long callerPeerId, int teamId, Vector3 dropTarget, Vector3 flightDir)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(ReplicateCarePackageRpc), crateId, callerPeerId, teamId, dropTarget, flightDir);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateCarePackageRpc(long crateId, long callerPeerId, int teamId, Vector3 dropTarget, Vector3 flightDir)
    {
        // On remote peers, marker can be null or spawned locally
        SpawnLocalCarePackage(crateId, callerPeerId, teamId, dropTarget, flightDir, null);
    }

    private void SpawnLocalCarePackage(long crateId, long callerPeerId, int teamId, Vector3 dropTarget, Vector3 flightDir, StrikeTargetMarker? marker)
    {
        var drop = new CarePackageDrop();
        drop.Initialize(crateId, dropTarget, flightDir, callerPeerId, teamId, marker);
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        tree?.Root?.CallDeferred(Node.MethodName.AddChild, drop);
    }

    public void CallAirstrike(long callerPeerId, int teamId, Vector3 strikeTarget, Vector3 flightDir, StrikeTargetMarker? marker)
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                Rpc(nameof(ReplicateAirstrikeRpc), callerPeerId, teamId, strikeTarget, flightDir);
            }
            else
            {
                RpcId(1, nameof(RequestAirstrikeRpc), callerPeerId, teamId, strikeTarget, flightDir);
            }
        }
        else
        {
            SpawnLocalAirstrike(callerPeerId, teamId, strikeTarget, flightDir, marker);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestAirstrikeRpc(long callerPeerId, int teamId, Vector3 strikeTarget, Vector3 flightDir)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(ReplicateAirstrikeRpc), callerPeerId, teamId, strikeTarget, flightDir);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateAirstrikeRpc(long callerPeerId, int teamId, Vector3 strikeTarget, Vector3 flightDir)
    {
        SpawnLocalAirstrike(callerPeerId, teamId, strikeTarget, flightDir, null);
    }

    private void SpawnLocalAirstrike(long callerPeerId, int teamId, Vector3 strikeTarget, Vector3 flightDir, StrikeTargetMarker? marker)
    {
        var pass = new AirstrikePass();
        pass.Initialize(strikeTarget, flightDir, callerPeerId, marker);
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        tree?.Root?.CallDeferred(Node.MethodName.AddChild, pass);
    }

    public void CallBombingRun(long callerPeerId, int teamId, Vector3 strikeCenter, Vector3 flightDir, BombingRunMarker? marker)
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                Rpc(nameof(ReplicateBombingRunRpc), callerPeerId, teamId, strikeCenter, flightDir);
            }
            else
            {
                RpcId(1, nameof(RequestBombingRunRpc), callerPeerId, teamId, strikeCenter, flightDir);
            }
        }
        else
        {
            SpawnLocalBombingRun(callerPeerId, teamId, strikeCenter, flightDir, marker);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestBombingRunRpc(long callerPeerId, int teamId, Vector3 strikeCenter, Vector3 flightDir)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(ReplicateBombingRunRpc), callerPeerId, teamId, strikeCenter, flightDir);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateBombingRunRpc(long callerPeerId, int teamId, Vector3 strikeCenter, Vector3 flightDir)
    {
        SpawnLocalBombingRun(callerPeerId, teamId, strikeCenter, flightDir, null);
    }

    private void SpawnLocalBombingRun(long callerPeerId, int teamId, Vector3 strikeCenter, Vector3 flightDir, BombingRunMarker? marker)
    {
        var pass = new BombingRunPass();
        pass.Initialize(strikeCenter, flightDir, callerPeerId, marker);
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        tree?.Root?.CallDeferred(Node.MethodName.AddChild, pass);
    }

    private static string RollCrateReward()
    {
        float roll = GD.Randf();
        if (roll < 0.40f) return "UAV";
        if (roll < 0.75f) return "AIRSTRIKE";
        return "BOMBING_RUN";
    }

    public void ClaimCrate(long crateId, long claimerPeerId)
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                if (_claimedCrates.Contains(crateId)) return;
                _claimedCrates.Add(crateId);
                string reward = RollCrateReward();
                Rpc(nameof(ReplicateCrateClaimedRpc), crateId, claimerPeerId, reward);
            }
            else
            {
                RpcId(1, nameof(RequestClaimCrateRpc), crateId, claimerPeerId);
            }
        }
        else
        {
            string reward = RollCrateReward();
            ExecuteCrateClaimed(crateId, claimerPeerId, reward);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestClaimCrateRpc(long crateId, long claimerPeerId)
    {
        if (!Multiplayer.IsServer()) return;
        if (_claimedCrates.Contains(crateId)) return;
        _claimedCrates.Add(crateId);

        string reward = RollCrateReward();
        Rpc(nameof(ReplicateCrateClaimedRpc), crateId, claimerPeerId, reward);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateCrateClaimedRpc(long crateId, long claimerPeerId, string reward)
    {
        ExecuteCrateClaimed(crateId, claimerPeerId, reward);
    }

    private void ExecuteCrateClaimed(long crateId, long claimerPeerId, string reward)
    {
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;

        // 1. Despawn the crate across all peers
        var crates = tree.GetNodesInGroup("CarePackageCrates");
        foreach (Node node in crates)
        {
            if (node is CarePackageCrate crate && (crate.CrateNetId == crateId || crate.Name == $"CarePackageCrate_{crateId}"))
            {
                crate.Despawn();
                break;
            }
        }

        // 2. Award scorestreak to the capturing player
        long myPeerId = (Multiplayer != null && Multiplayer.HasMultiplayerPeer()) ? Multiplayer.GetUniqueId() : 1;
        if (claimerPeerId == myPeerId)
        {
            var players = tree.GetNodesInGroup("Players");
            foreach (Node p in players)
            {
                if (p is PlayerController pc && pc.IsLocalPlayer)
                {
                    pc.AwardScorestreak(reward);
                    break;
                }
            }
        }

        // 3. Announcement feedback across all peers
        MatchHUD.Instance?.ShowAnnouncement($"CARE PACKAGE CLAIMED (+1 {reward})", 2.2f);
    }
}
