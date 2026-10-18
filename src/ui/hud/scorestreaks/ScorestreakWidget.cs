using Godot;

namespace FPSGame.UI.HUD.Scorestreaks;

/// <summary>
/// Tactical segmented scorestreak progression ladder.
/// Features 8 discrete kill segments split across:
/// - UAV Recon [4] (3 kills)
/// - Care Package [5] (4 kills)
/// - Airstrike [6] (6 kills)
/// - Bombing Run [7] (8 kills)
/// Segments illuminate white as kills are confirmed. Active ready streaks glow gold with inventory stack counts.
/// </summary>
public partial class ScorestreakWidget : Control
{
    private int _currentStreakKills;
    private int _uavCount;
    private int _carePackageCount;
    private int _airstrikeCount;
    private int _bombingRunCount;
    private Font? _cachedFont;

    public void SetStreakKills(int streakKills)
    {
        _currentStreakKills = streakKills;
        QueueRedraw();
    }

    public void SetStreakData(int streakKills, int uavCount, int carePackageCount, int airstrikeCount, int bombingRunCount = 0)
    {
        _currentStreakKills = streakKills;
        _uavCount = uavCount;
        _carePackageCount = carePackageCount;
        _airstrikeCount = airstrikeCount;
        _bombingRunCount = bombingRunCount;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(120, 142);
        Size = new Vector2(120, 142);
        _cachedFont = ThemeDB.FallbackFont;
    }

    public override void _Draw()
    {
        float slotHeight = 28.0f;
        float slotGap = 6.0f;
        float startY = 4.0f;

        float badgeX = 4.0f;
        float badgeWidth = 78.0f;
        float barX = badgeX + badgeWidth + 8.0f;
        float barWidth = 7.0f;

        Color darkSegColor = new Color(0.2f, 0.24f, 0.3f, 0.5f);
        Color activeSegColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);
        Color activeTextColor = new Color(1.0f, 0.88f, 0.25f, 1.0f);
        Color inactiveTextColor = new Color(0.6f, 0.68f, 0.8f, 0.8f);

        Color badgeBorderLocked = new Color(0.3f, 0.35f, 0.45f, 0.4f);
        Color badgeBorderActive = new Color(1.0f, 0.88f, 0.25f, 0.95f);
        Color badgeBg = new Color(0.06f, 0.08f, 0.12f, 0.50f);

        int cycleKills = _currentStreakKills % 8;
        if (cycleKills == 0 && _currentStreakKills > 0) cycleKills = 8;

        // 1. Bombing Run [7] (Kills 7 & 8) - Top Slot
        float bombTopY = startY;
        bool bombActive = _bombingRunCount > 0;
        DrawRect(new Rect2(badgeX, bombTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, bombTopY, badgeWidth, slotHeight), bombActive ? badgeBorderActive : badgeBorderLocked, false, 1.5f);

        if (_cachedFont != null)
        {
            string label = _bombingRunCount > 1 ? $"[7] BOMB x{_bombingRunCount}" : (_bombingRunCount > 0 ? "[7] BOMBING" : "BOMBING RUN");
            Vector2 textSz = _cachedFont.GetStringSize(label, HorizontalAlignment.Center, -1, 9);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, bombTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), label, HorizontalAlignment.Left, -1, 9, bombActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right for Bombing Run: 2 equal segments fitting in 28px (each 13px)
        float twoSegH = (slotHeight - 2.0f) * 0.5f; // 13px
        bool bombSeg8 = _bombingRunCount > 0 || (cycleKills >= 8);
        bool bombSeg7 = _bombingRunCount > 0 || (cycleKills >= 7);
        DrawRect(new Rect2(barX, bombTopY, barWidth, twoSegH), bombSeg8 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, bombTopY + twoSegH + 2.0f, barWidth, twoSegH), bombSeg7 ? activeSegColor : darkSegColor, true);

        // 2. Airstrike [6] (Kills 5 & 6) - Second Slot
        float airstrikeTopY = bombTopY + slotHeight + slotGap;
        bool airstrikeActive = _airstrikeCount > 0;
        DrawRect(new Rect2(badgeX, airstrikeTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, airstrikeTopY, badgeWidth, slotHeight), airstrikeActive ? badgeBorderActive : badgeBorderLocked, false, 1.5f);

        if (_cachedFont != null)
        {
            string label = _airstrikeCount > 1 ? $"[6] STRIKE x{_airstrikeCount}" : (_airstrikeCount > 0 ? "[6] STRIKE" : "AIRSTRIKE");
            Vector2 textSz = _cachedFont.GetStringSize(label, HorizontalAlignment.Center, -1, 9);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, airstrikeTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), label, HorizontalAlignment.Left, -1, 9, airstrikeActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right for Airstrike: 2 equal segments (kills 5 & 6)
        bool strikeSeg6 = _airstrikeCount > 0 || (cycleKills >= 6);
        bool strikeSeg5 = _airstrikeCount > 0 || (cycleKills >= 5);
        DrawRect(new Rect2(barX, airstrikeTopY, barWidth, twoSegH), strikeSeg6 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, airstrikeTopY + twoSegH + 2.0f, barWidth, twoSegH), strikeSeg5 ? activeSegColor : darkSegColor, true);

        // 3. Care Package [5] (Kill 4) - Third Slot
        float pkgTopY = airstrikeTopY + slotHeight + slotGap;
        bool pkgActive = _carePackageCount > 0;
        DrawRect(new Rect2(badgeX, pkgTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, pkgTopY, badgeWidth, slotHeight), pkgActive ? badgeBorderActive : badgeBorderLocked, false, 1.5f);

        if (_cachedFont != null)
        {
            string label = _carePackageCount > 1 ? $"[5] DROP x{_carePackageCount}" : (_carePackageCount > 0 ? "[5] PACKAGE" : "PACKAGE");
            Vector2 textSz = _cachedFont.GetStringSize(label, HorizontalAlignment.Center, -1, 9);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, pkgTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), label, HorizontalAlignment.Left, -1, 9, pkgActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right for Care Package: 1 single segment (kill 4)
        bool pkgSeg4 = _carePackageCount > 0 || (cycleKills >= 4);
        DrawRect(new Rect2(barX, pkgTopY, barWidth, slotHeight), pkgSeg4 ? activeSegColor : darkSegColor, true);

        // 4. UAV Recon [4] (Kills 1, 2, 3) - Bottom Slot
        float uavTopY = pkgTopY + slotHeight + slotGap;
        bool uavActive = _uavCount > 0;
        DrawRect(new Rect2(badgeX, uavTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, uavTopY, badgeWidth, slotHeight), uavActive ? badgeBorderActive : badgeBorderLocked, false, 1.5f);

        if (_cachedFont != null)
        {
            string label = _uavCount > 1 ? $"[4] UAV x{_uavCount}" : (_uavCount > 0 ? "[4] UAV" : "UAV");
            Vector2 textSz = _cachedFont.GetStringSize(label, HorizontalAlignment.Center, -1, 9);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, uavTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), label, HorizontalAlignment.Left, -1, 9, uavActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right for UAV: 3 equal segments (kills 1, 2, 3)
        float uavSegH = (slotHeight - 4.0f) / 3.0f; // 8px
        bool uavSeg3 = _uavCount > 0 || (cycleKills >= 3);
        bool uavSeg2 = _uavCount > 0 || (cycleKills >= 2);
        bool uavSeg1 = _uavCount > 0 || (cycleKills >= 1);
        DrawRect(new Rect2(barX, uavTopY, barWidth, uavSegH), uavSeg3 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, uavTopY + uavSegH + 2.0f, barWidth, uavSegH), uavSeg2 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, uavTopY + (uavSegH + 2.0f) * 2.0f, barWidth, uavSegH), uavSeg1 ? activeSegColor : darkSegColor, true);
    }
}
