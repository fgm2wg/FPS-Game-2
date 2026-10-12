using System;

namespace FPSGame.Network.Data;

/// <summary>
/// Data payload representing a connected player across the network.
/// </summary>
public struct NetworkPlayerInfo : IEquatable<NetworkPlayerInfo>
{
    public long PeerId { get; set; }
    public string Name { get; set; }
    public int TeamId { get; set; } // 0 = Unassigned/Lobby, 1 = Team Alpha, 2 = Team Bravo
    public int Ping { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Score { get; set; }
    public bool IsReady { get; set; }

    public NetworkPlayerInfo(long peerId, string name, int teamId = 0)
    {
        PeerId = peerId;
        Name = string.IsNullOrWhiteSpace(name) ? $"Player_{peerId}" : name;
        TeamId = teamId;
        Ping = 0;
        Kills = 0;
        Deaths = 0;
        Assists = 0;
        Score = 0;
        IsReady = false;
    }

    public readonly bool Equals(NetworkPlayerInfo other) => PeerId == other.PeerId;
    public override readonly bool Equals(object? obj) => obj is NetworkPlayerInfo other && Equals(other);
    public override readonly int GetHashCode() => PeerId.GetHashCode();

    public static bool operator ==(NetworkPlayerInfo left, NetworkPlayerInfo right) => left.Equals(right);
    public static bool operator !=(NetworkPlayerInfo left, NetworkPlayerInfo right) => !left.Equals(right);
}
