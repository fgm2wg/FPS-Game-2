using System.Collections.Generic;
using Godot;
using FPSGame.Gamemodes.TDM;
using FPSGame.Network.Core;

namespace FPSGame.UI.HUD.Killfeed;

/// <summary>
/// Tactical in-game kill feed widget displayed under the minimap.
/// Shows up to 5 concurrent entries, fading out after 5 seconds.
/// Local player names are highlighted in yellow.
/// </summary>
public partial class KillFeedWidget : VBoxContainer
{
    private class KillFeedEntry
    {
        public HBoxContainer Container = null!;
        public float RemainingTime = 5.0f;
    }

    private readonly List<KillFeedEntry> _entries = new();
    private const int MaxEntries = 5;
    private const float EntryLifetime = 5.0f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.KillFeedEvent += OnKillFeedEvent;
        }
    }

    public override void _ExitTree()
    {
        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.KillFeedEvent -= OnKillFeedEvent;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            entry.RemainingTime -= dt;

            // Fade out during the last second
            if (entry.RemainingTime <= 1.0f)
            {
                float alpha = Mathf.Max(0.0f, entry.RemainingTime);
                entry.Container.Modulate = new Color(1, 1, 1, alpha);
            }

            if (entry.RemainingTime <= 0.0f)
            {
                entry.Container.QueueFree();
                _entries.RemoveAt(i);
            }
        }
    }

    public void OnKillFeedEvent(long killerId, long victimId, string weaponName)
    {
        long myPeerId = Multiplayer.GetUniqueId();

        string killerName = "Player";
        int killerTeam = 1;
        if (NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(killerId, out var killerInfo))
        {
            killerName = killerInfo.Name;
            killerTeam = killerInfo.TeamId;
        }

        string victimName = "Player";
        int victimTeam = 2;
        if (NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(victimId, out var victimInfo))
        {
            victimName = victimInfo.Name;
            victimTeam = victimInfo.TeamId;
        }

        // Color resolution
        Color yellowHighlight = new Color(1.0f, 0.88f, 0.15f);
        Color alphaBlue = new Color(0.35f, 0.7f, 1.0f);
        Color bravoRed = new Color(1.0f, 0.38f, 0.38f);

        Color killerColor = (killerId == myPeerId) ? yellowHighlight : (killerTeam == 2 ? bravoRed : alphaBlue);
        Color victimColor = (victimId == myPeerId) ? yellowHighlight : (victimTeam == 2 ? bravoRed : alphaBlue);

        // Build UI entry row
        var row = new HBoxContainer();
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.AddThemeConstantOverride("separation", 6);

        var killerLabel = new Label();
        killerLabel.Text = killerName;
        killerLabel.AddThemeColorOverride("font_color", killerColor);
        killerLabel.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(killerLabel);

        var weaponLabel = new Label();
        weaponLabel.Text = $"[{weaponName}]";
        weaponLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.82f));
        weaponLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(weaponLabel);

        var victimLabel = new Label();
        victimLabel.Text = victimName;
        victimLabel.AddThemeColorOverride("font_color", victimColor);
        victimLabel.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(victimLabel);

        // Cap to 5 entries
        if (_entries.Count >= MaxEntries)
        {
            var oldest = _entries[0];
            oldest.Container.QueueFree();
            _entries.RemoveAt(0);
        }

        AddChild(row);
        _entries.Add(new KillFeedEntry
        {
            Container = row,
            RemainingTime = EntryLifetime
        });
    }
}
