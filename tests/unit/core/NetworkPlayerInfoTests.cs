using System;
using FPSGame.Network.Data;

namespace FPSGame.Tests.Unit.Core;

/// <summary>
/// Unit tests for NetworkPlayerInfo data structure and equality semantics.
/// </summary>
public static class NetworkPlayerInfoTests
{
    public static bool RunAllTests()
    {
        bool passed = true;

        passed &= TestDefaultInitialization();
        passed &= TestEqualitySemantics();
        passed &= TestCustomCallSign();

        return passed;
    }

    private static bool TestDefaultInitialization()
    {
        var info = new NetworkPlayerInfo(1, "");
        if (info.PeerId != 1) return false;
        if (info.Name != "Player_1") return false;
        if (info.TeamId != 0) return false;
        if (info.Kills != 0 || info.Deaths != 0 || info.Score != 0) return false;
        return true;
    }

    private static bool TestEqualitySemantics()
    {
        var p1 = new NetworkPlayerInfo(42, "Ghost", teamId: 1);
        var p2 = new NetworkPlayerInfo(42, "Spectre", teamId: 2);
        var p3 = new NetworkPlayerInfo(99, "Ghost", teamId: 1);

        // Equality must be strictly bound to PeerId
        if (p1 != p2) return false;
        if (p1 == p3) return false;
        if (!p1.Equals(p2)) return false;
        if (p1.GetHashCode() != p2.GetHashCode()) return false;
        return true;
    }

    private static bool TestCustomCallSign()
    {
        var info = new NetworkPlayerInfo(2, "Viper");
        return info.Name == "Viper";
    }
}
