using System;
using Godot;
using FPSGame.Network.Core;
using FPSGame.Network.Data;

namespace FPSGame.Gamemodes.TDM;

public enum MatchState
{
    Warmup,
    InProgress,
    Ended
}

/// <summary>
/// Authoritative match manager for 6v6 Team Deathmatch.
/// Governs match countdown, 20-minute match clock, 100-kill win condition, and score tracking.
/// </summary>
public partial class MatchManager : Node
{
    public static MatchManager Instance { get; private set; } = null!;

    public const int TargetKillsToWin = 100;
    public const float DefaultWarmupSeconds = 5.0f;
    public const float DefaultMatchSeconds = 1200.0f; // 20 minutes

    public MatchState CurrentState { get; private set; } = MatchState.Warmup;
    public float WarmupTimeRemaining { get; private set; } = DefaultWarmupSeconds;
    public float MatchTimeRemaining { get; private set; } = DefaultMatchSeconds;
    public int TeamAlphaKills { get; private set; }
    public int TeamBravoKills { get; private set; }
    public int WinningTeam { get; private set; } // 0 = None/Draw, 1 = Alpha Blue, 2 = Bravo Red

    public bool IsMovementLocked => CurrentState == MatchState.Warmup || CurrentState == MatchState.Ended;

    public event Action<MatchState>? StateChanged;
    public event Action<float>? WarmupTick;
    public event Action<float>? MatchTimerTick;
    public event Action<int, int>? ScoresUpdated;
    public event Action<int>? MatchFinished;
    public event Action<long, long, string>? KillFeedEvent;
    public event Action<long>? PlayerStatsUpdated;

    private float _networkSyncTimer;
    private const float NetworkSyncInterval = 0.5f;

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
        CurrentState = MatchState.Warmup;
        WarmupTimeRemaining = DefaultWarmupSeconds;
        MatchTimeRemaining = DefaultMatchSeconds;
        TeamAlphaKills = 0;
        TeamBravoKills = 0;
        WinningTeam = 0;

        StateChanged?.Invoke(CurrentState);
        ScoresUpdated?.Invoke(TeamAlphaKills, TeamBravoKills);

        if (NetworkManager.Instance != null && NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.PlayerConnected += HandlePlayerConnectedServer;
        }
    }

    public override void _ExitTree()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.PlayerConnected -= HandlePlayerConnectedServer;
        }

        if (Instance == this)
        {
            Instance = null!;
        }
    }

    private void HandlePlayerConnectedServer(NetworkPlayerInfo info)
    {
        RpcId(info.PeerId, nameof(SyncMatchStateRpc),
            (int)CurrentState,
            WarmupTimeRemaining,
            MatchTimeRemaining,
            TeamAlphaKills,
            TeamBravoKills,
            WinningTeam);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool isServer = NetworkManager.Instance != null && NetworkManager.Instance.IsServer;

        if (CurrentState == MatchState.Warmup)
        {
            WarmupTimeRemaining = Mathf.Max(0.0f, WarmupTimeRemaining - dt);
            WarmupTick?.Invoke(WarmupTimeRemaining);

            if (isServer)
            {
                _networkSyncTimer += dt;
                if (_networkSyncTimer >= NetworkSyncInterval)
                {
                    _networkSyncTimer = 0.0f;
                    BroadcastMatchState();
                }

                if (WarmupTimeRemaining <= 0.0f)
                {
                    CurrentState = MatchState.InProgress;
                    StateChanged?.Invoke(CurrentState);
                    BroadcastMatchState();
                }
            }
        }
        else if (CurrentState == MatchState.InProgress)
        {
            MatchTimeRemaining = Mathf.Max(0.0f, MatchTimeRemaining - dt);
            MatchTimerTick?.Invoke(MatchTimeRemaining);

            if (isServer)
            {
                _networkSyncTimer += dt;
                if (_networkSyncTimer >= NetworkSyncInterval)
                {
                    _networkSyncTimer = 0.0f;
                    BroadcastMatchState();
                }

                // Check win conditions
                if (TeamAlphaKills >= TargetKillsToWin)
                {
                    EndMatch(1);
                }
                else if (TeamBravoKills >= TargetKillsToWin)
                {
                    EndMatch(2);
                }
                else if (MatchTimeRemaining <= 0.0f)
                {
                    int winner = TeamAlphaKills > TeamBravoKills ? 1 : (TeamBravoKills > TeamAlphaKills ? 2 : 0);
                    EndMatch(winner);
                }
            }
        }
    }

    public void RecordKill(long killerId, long victimId, string weaponName = "M4A1", long? assistId = null)
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
        if (CurrentState != MatchState.InProgress) return;

        // Update player stats
        if (NetworkManager.Instance.Players.TryGetValue(killerId, out var killer))
        {
            killer.Kills++;
            killer.Score += 100;
            NetworkManager.Instance.UpdatePlayerStats(killerId, killer.Score, killer.Kills, killer.Deaths, killer.Assists);

            if (killer.TeamId == 1) TeamAlphaKills++;
            else if (killer.TeamId == 2) TeamBravoKills++;

            Rpc(nameof(SyncPlayerStatsRpc), killerId, killer.Score, killer.Kills, killer.Deaths, killer.Assists);
        }

        if (NetworkManager.Instance.Players.TryGetValue(victimId, out var victim))
        {
            victim.Deaths++;
            NetworkManager.Instance.UpdatePlayerStats(victimId, victim.Score, victim.Kills, victim.Deaths, victim.Assists);

            Rpc(nameof(SyncPlayerStatsRpc), victimId, victim.Score, victim.Kills, victim.Deaths, victim.Assists);
        }

        if (assistId.HasValue && NetworkManager.Instance.Players.TryGetValue(assistId.Value, out var assistant))
        {
            assistant.Assists++;
            assistant.Score += 50;
            NetworkManager.Instance.UpdatePlayerStats(assistId.Value, assistant.Score, assistant.Kills, assistant.Deaths, assistant.Assists);

            Rpc(nameof(SyncPlayerStatsRpc), assistId.Value, assistant.Score, assistant.Kills, assistant.Deaths, assistant.Assists);
        }

        ScoresUpdated?.Invoke(TeamAlphaKills, TeamBravoKills);
        BroadcastMatchState();

        // Broadcast kill feed event locally on server and to all clients
        KillFeedEvent?.Invoke(killerId, victimId, weaponName);
        Rpc(nameof(BroadcastKillFeedRpc), killerId, victimId, weaponName);

        // Check if kill hit target
        if (TeamAlphaKills >= TargetKillsToWin) EndMatch(1);
        else if (TeamBravoKills >= TargetKillsToWin) EndMatch(2);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncPlayerStatsRpc(long peerId, int score, int kills, int deaths, int assists)
    {
        NetworkManager.Instance?.UpdatePlayerStats(peerId, score, kills, deaths, assists);
        PlayerStatsUpdated?.Invoke(peerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BroadcastKillFeedRpc(long killerId, long victimId, string weaponName)
    {
        KillFeedEvent?.Invoke(killerId, victimId, weaponName);
    }

    private void EndMatch(int winningTeam)
    {
        CurrentState = MatchState.Ended;
        WinningTeam = winningTeam;
        StateChanged?.Invoke(CurrentState);
        MatchFinished?.Invoke(WinningTeam);
        BroadcastMatchState();
    }

    private void BroadcastMatchState()
    {
        Rpc(nameof(SyncMatchStateRpc),
            (int)CurrentState,
            WarmupTimeRemaining,
            MatchTimeRemaining,
            TeamAlphaKills,
            TeamBravoKills,
            WinningTeam);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void SyncMatchStateRpc(int stateInt, float warmupTime, float matchTime, int alphaKills, int bravoKills, int winningTeam)
    {
        var newState = (MatchState)stateInt;
        bool stateChanged = CurrentState != newState;

        CurrentState = newState;
        WarmupTimeRemaining = warmupTime;
        MatchTimeRemaining = matchTime;
        TeamAlphaKills = alphaKills;
        TeamBravoKills = bravoKills;
        WinningTeam = winningTeam;

        if (stateChanged)
        {
            StateChanged?.Invoke(CurrentState);
            if (CurrentState == MatchState.Ended)
            {
                MatchFinished?.Invoke(WinningTeam);
            }
        }

        WarmupTick?.Invoke(Mathf.Max(0.0f, WarmupTimeRemaining));
        MatchTimerTick?.Invoke(Mathf.Max(0.0f, MatchTimeRemaining));
        ScoresUpdated?.Invoke(TeamAlphaKills, TeamBravoKills);
    }
}
