using Godot;

namespace FPSGame.UI.HUD.Scorestreaks;

/// <summary>
/// Tactical segmented scorestreak progression ladder.
/// Features 6 discrete kill segments split across UAV [4] (3 kills), Care Package [5] (4 kills), and Airstrike [6] (6 kills).
/// Segments illuminate white as kills are confirmed. Active ready streaks glow gold with inventory stack counts.
/// </summary>
public partial class ScorestreakWidget : Control
{
    private int _currentStreakKills;
    private int _uavCount;
    private int _carePackageCount;
    private int _airstrikeCount;
    private Font? _cachedFont;

    public void SetStreakKills(int streakKills)
    {
        _currentStreakKills = streakKills;
        QueueRedraw();
    }

    public void SetStreakData(int streakKills, int uavCount, int carePackageCount, int airstrikeCount)
    {
        _currentStreakKills = streakKills;
        _uavCount = uavCount;
        _carePackageCount = carePackageCount;
        _airstrikeCount = airstrikeCount;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(120, 105);
        Size = new Vector2(120, 105);
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

        int cycleKills = _currentStreakKills % 6;
        if (cycleKills == 0 && _currentStreakKills > 0) cycleKills = 6;

        // 1. Airstrike [6] (Kills 5 & 6) - Top Slot
        float airstrikeTopY = startY;
        bool airstrikeActive = _airstrikeCount > 0;
        DrawRect(new Rect2(badgeX, airstrikeTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, airstrikeTopY, badgeWidth, slotHeight), airstrikeActive ? badgeBorderActive : badgeBorderLocked, false, 1.5f);

        if (_cachedFont != null)
        {
            string label = _airstrikeCount > 1 ? $"[6] STRIKE x{_airstrikeCount}" : (_airstrikeCount > 0 ? "[6] STRIKE" : "AIRSTRIKE");
            Vector2 textSz = _cachedFont.GetStringSize(label, HorizontalAlignment.Center, -1, 9);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, airstrikeTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), label, HorizontalAlignment.Left, -1, 9, airstrikeActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right: 2 equal segments fitting in slotHeight (28px total)
        float sentrySegH = (slotHeight - 2.0f) * 0.5f; // 13px
        bool strikeSeg6 = _airstrikeCount > 0;
        bool strikeSeg5 = _airstrikeCount > 0 || (cycleKills == 5 && _airstrikeCount == 0);
        DrawRect(new Rect2(barX, airstrikeTopY, barWidth, sentrySegH), strikeSeg6 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, airstrikeTopY + sentrySegH + 2.0f, barWidth, sentrySegH), strikeSeg5 ? activeSegColor : darkSegColor, true);

        // 2. Care Package [5] (Kill 4) - Middle Slot
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

        // Segments on right: 1 single segment taking full slotHeight (28px)
        DrawRect(new Rect2(barX, pkgTopY, barWidth, slotHeight), _carePackageCount > 0 ? activeSegColor : darkSegColor, true);

        // 3. UAV Recon [4] (Kills 1, 2, 3) - Bottom Slot
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

        // Segments on right: 3 equal segments fitting in slotHeight (28px total: 8 + 2 + 8 + 2 + 8)
        float uavSegH = (slotHeight - 4.0f) / 3.0f; // 8px
        bool uavSeg3 = _uavCount > 0;
        bool uavSeg2 = _uavCount > 0 || (cycleKills == 2 && _uavCount == 0);
        bool uavSeg1 = _uavCount > 0 || (cycleKills >= 1 && cycleKills <= 2 && _uavCount == 0);
        DrawRect(new Rect2(barX, uavTopY, barWidth, uavSegH), uavSeg3 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, uavTopY + uavSegH + 2.0f, barWidth, uavSegH), uavSeg2 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, uavTopY + (uavSegH + 2.0f) * 2.0f, barWidth, uavSegH), uavSeg1 ? activeSegColor : darkSegColor, true);
    }
}
