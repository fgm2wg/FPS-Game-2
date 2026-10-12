using Godot;

namespace FPSGame.UI.HUD.Scorestreaks;

/// <summary>
/// Tactical segmented scorestreak progression ladder.
/// Features 6 discrete kill segments split across UAV (3), Care Package (+1 = 4), and Sentry (+2 = 6).
/// Segments illuminate white as kills are confirmed.
/// </summary>
public partial class ScorestreakWidget : Control
{
    private int _currentStreakKills;
    private Font? _cachedFont;

    public void SetStreakKills(int streakKills)
    {
        _currentStreakKills = streakKills;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(100, 100);
        Size = new Vector2(100, 100);
        _cachedFont = ThemeDB.FallbackFont;
    }

    public override void _Draw()
    {
        float slotHeight = 28.0f;
        float slotGap = 6.0f;
        float startY = 4.0f;

        float badgeX = 4.0f;
        float badgeWidth = 62.0f;
        float barX = badgeX + badgeWidth + 8.0f;
        float barWidth = 7.0f;

        Color darkSegColor = new Color(0.2f, 0.24f, 0.3f, 0.5f);
        Color activeSegColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);
        Color activeTextColor = new Color(1.0f, 0.88f, 0.25f, 1.0f);
        Color inactiveTextColor = new Color(0.6f, 0.68f, 0.8f, 0.8f);

        Color badgeBorderLocked = new Color(0.3f, 0.35f, 0.45f, 0.4f);
        Color badgeBorderActive = new Color(1.0f, 0.88f, 0.25f, 0.85f);
        Color badgeBg = new Color(0.06f, 0.08f, 0.12f, 0.45f);

        // 1. Sentry Turret (Kills 5 & 6) - Top Slot
        float sentryTopY = startY;
        bool sentryActive = _currentStreakKills >= 6;
        DrawRect(new Rect2(badgeX, sentryTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, sentryTopY, badgeWidth, slotHeight), sentryActive ? badgeBorderActive : badgeBorderLocked, false, 1.0f);

        if (_cachedFont != null)
        {
            Vector2 textSz = _cachedFont.GetStringSize("SENTRY", HorizontalAlignment.Center, -1, 10);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, sentryTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), "SENTRY", HorizontalAlignment.Left, -1, 10, sentryActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right: 2 equal segments fitting in slotHeight (28px total)
        float sentrySegH = (slotHeight - 2.0f) * 0.5f; // 13px
        DrawRect(new Rect2(barX, sentryTopY, barWidth, sentrySegH), _currentStreakKills >= 6 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, sentryTopY + sentrySegH + 2.0f, barWidth, sentrySegH), _currentStreakKills >= 5 ? activeSegColor : darkSegColor, true);

        // 2. Care Package (Kill 4) - Middle Slot
        float pkgTopY = sentryTopY + slotHeight + slotGap;
        bool pkgActive = _currentStreakKills >= 4;
        DrawRect(new Rect2(badgeX, pkgTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, pkgTopY, badgeWidth, slotHeight), pkgActive ? badgeBorderActive : badgeBorderLocked, false, 1.0f);

        if (_cachedFont != null)
        {
            Vector2 textSz = _cachedFont.GetStringSize("PACKAGE", HorizontalAlignment.Center, -1, 10);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, pkgTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), "PACKAGE", HorizontalAlignment.Left, -1, 10, pkgActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right: 1 single segment taking full slotHeight (28px)
        DrawRect(new Rect2(barX, pkgTopY, barWidth, slotHeight), _currentStreakKills >= 4 ? activeSegColor : darkSegColor, true);

        // 3. UAV Recon (Kills 1, 2, 3) - Bottom Slot
        float uavTopY = pkgTopY + slotHeight + slotGap;
        bool uavActive = _currentStreakKills >= 3;
        DrawRect(new Rect2(badgeX, uavTopY, badgeWidth, slotHeight), badgeBg, true);
        DrawRect(new Rect2(badgeX, uavTopY, badgeWidth, slotHeight), uavActive ? badgeBorderActive : badgeBorderLocked, false, 1.0f);

        if (_cachedFont != null)
        {
            Vector2 textSz = _cachedFont.GetStringSize("UAV", HorizontalAlignment.Center, -1, 10);
            DrawString(_cachedFont, new Vector2(badgeX + (badgeWidth - textSz.X) * 0.5f, uavTopY + (slotHeight + textSz.Y) * 0.5f - 2.0f), "UAV", HorizontalAlignment.Left, -1, 10, uavActive ? activeTextColor : inactiveTextColor);
        }

        // Segments on right: 3 equal segments fitting in slotHeight (28px total: 8 + 2 + 8 + 2 + 8)
        float uavSegH = (slotHeight - 4.0f) / 3.0f; // 8px
        DrawRect(new Rect2(barX, uavTopY, barWidth, uavSegH), _currentStreakKills >= 3 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, uavTopY + uavSegH + 2.0f, barWidth, uavSegH), _currentStreakKills >= 2 ? activeSegColor : darkSegColor, true);
        DrawRect(new Rect2(barX, uavTopY + (uavSegH + 2.0f) * 2.0f, barWidth, uavSegH), _currentStreakKills >= 1 ? activeSegColor : darkSegColor, true);
    }
}
