using System;
using Godot;
using FPSGame.Entities.Player.Controllers;

namespace FPSGame.UI.HUD.Compass;

/// <summary>
/// Tactical 360-degree rotating horizontal tape compass.
/// Renders major cardinal headings (N, NE, E, SE, S, SW, W, NW), minor degree markers every 15°,
/// smooth left/right sliding, edge alpha fading, enemy diamond indicators, and digital heading readout below.
/// </summary>
public partial class CompassWidget : Control
{
    private float _currentYawDegrees;
    private PlayerController? _localPlayer;
    private Font? _cachedFont;

    public void BindLocalPlayer(PlayerController player)
    {
        _localPlayer = player;
    }

    public void SetHeading(float yawDegrees)
    {
        _currentYawDegrees = (yawDegrees % 360.0f + 360.0f) % 360.0f;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _cachedFont = ThemeDB.FallbackFont;
    }

    public override void _Draw()
    {
        float width = Size.X;
        float tapeHeight = 28.0f;
        float centerX = width * 0.5f;

        // Total visible angle span = 120 degrees
        float visibleSpan = 120.0f;
        float pixelsPerDegree = width / visibleSpan;
        float halfSpan = visibleSpan * 0.5f;

        // 1. Center pointer indicator (pointing down to the active heading)
        Vector2 ptrTip = new Vector2(centerX, 0.0f);
        Vector2 ptrLeft = new Vector2(centerX - 4.5f, -6.0f);
        Vector2 ptrRight = new Vector2(centerX + 4.5f, -6.0f);
        DrawColoredPolygon(new Vector2[] { ptrTip, ptrLeft, ptrRight }, new Color(1.0f, 0.88f, 0.25f, 0.95f));

        // 2. Compass tape ticks and markers every 15 degrees
        for (int deg = 0; deg < 360; deg += 15)
        {
            float diff = (deg - _currentYawDegrees + 540.0f) % 360.0f - 180.0f;

            if (Mathf.Abs(diff) > halfSpan + 5.0f) continue;

            float x = centerX + diff * pixelsPerDegree;
            float distRatio = Mathf.Abs(diff) / halfSpan;
            float alpha = Mathf.Clamp(1.0f - (distRatio * distRatio), 0.0f, 1.0f);

            bool isMajorCardinal = (deg % 90 == 0);
            bool isIntercardinal = (deg % 45 == 0 && !isMajorCardinal);

            // Tick line on top
            float tickLen = isMajorCardinal ? 10.0f : (isIntercardinal ? 7.0f : 5.0f);
            Color tickColor = isMajorCardinal
                ? new Color(1.0f, 0.9f, 0.35f, alpha)
                : new Color(0.75f, 0.82f, 0.9f, alpha * 0.8f);

            DrawLine(new Vector2(x, 2.0f), new Vector2(x, 2.0f + tickLen), tickColor, 1.5f);

            // Text label below tick
            string labelText = deg switch
            {
                0 => "N",
                45 => "NE",
                90 => "E",
                135 => "SE",
                180 => "S",
                225 => "SW",
                270 => "W",
                315 => "NW",
                _ => deg.ToString()
            };

            int fontSize = isMajorCardinal ? 12 : (isIntercardinal ? 11 : 9);
            Color textColor = isMajorCardinal
                ? new Color(1.0f, 0.9f, 0.35f, alpha)
                : (isIntercardinal ? new Color(0.9f, 0.94f, 1.0f, alpha) : new Color(0.65f, 0.72f, 0.82f, alpha * 0.75f));

            if (_cachedFont != null)
            {
                Vector2 textSize = _cachedFont.GetStringSize(labelText, HorizontalAlignment.Center, -1, fontSize);
                DrawString(_cachedFont, new Vector2(x - textSize.X * 0.5f, 2.0f + tickLen + textSize.Y + 2.0f), labelText, HorizontalAlignment.Left, -1, fontSize, textColor);
            }
        }

        // 3. Enemy radar diamonds on compass
        if (_localPlayer != null && _localPlayer.IsInsideTree())
        {
            Vector3 myPos = _localPlayer.GlobalPosition;
            int myTeam = _localPlayer.TeamId;

            var players = GetTree().GetNodesInGroup("Players");
            foreach (var node in players)
            {
                if (node is PlayerController other && other != _localPlayer && other.IsInsideTree() && !other.IsDead)
                {
                    // Only display enemies
                    if (other.TeamId == myTeam) continue;

                    Vector3 diffVec = other.GlobalPosition - myPos;
                    // North is -Z, East is +X
                    float enemyHeading = Mathf.RadToDeg(Mathf.Atan2(diffVec.X, -diffVec.Z));
                    enemyHeading = (enemyHeading % 360.0f + 360.0f) % 360.0f;

                    float diff = (enemyHeading - _currentYawDegrees + 540.0f) % 360.0f - 180.0f;
                    if (Mathf.Abs(diff) <= halfSpan)
                    {
                        float x = centerX + diff * pixelsPerDegree;
                        float distRatio = Mathf.Abs(diff) / halfSpan;
                        float alpha = Mathf.Clamp(1.0f - (distRatio * distRatio), 0.0f, 1.0f);

                        // Draw red diamond at y = -3px above tape
                        float diamondY = 0.0f;
                        Vector2 dTop = new Vector2(x, diamondY - 4.0f);
                        Vector2 dRight = new Vector2(x + 3.5f, diamondY);
                        Vector2 dBottom = new Vector2(x, diamondY + 4.0f);
                        Vector2 dLeft = new Vector2(x - 3.5f, diamondY);
                        DrawColoredPolygon(new Vector2[] { dTop, dRight, dBottom, dLeft }, new Color(1.0f, 0.22f, 0.22f, alpha * 0.95f));
                    }
                }
            }
        }

        // 4. Digital bearing readout below the tape
        string currentCardinal = _currentYawDegrees switch
        {
            >= 337.5f or < 22.5f => "N",
            >= 22.5f and < 67.5f => "NE",
            >= 67.5f and < 112.5f => "E",
            >= 112.5f and < 157.5f => "SE",
            >= 157.5f and < 202.5f => "S",
            >= 202.5f and < 247.5f => "SW",
            >= 247.5f and < 292.5f => "W",
            _ => "NW"
        };

        string readout = $"{Mathf.RoundToInt(_currentYawDegrees):D3}°  {currentCardinal}";
        if (_cachedFont != null)
        {
            Vector2 readoutSize = _cachedFont.GetStringSize(readout, HorizontalAlignment.Center, -1, 13);
            DrawString(_cachedFont, new Vector2(centerX - readoutSize.X * 0.5f, tapeHeight + 18.0f), readout, HorizontalAlignment.Left, -1, 13, new Color(1.0f, 0.88f, 0.25f, 0.95f));
        }
    }
}
