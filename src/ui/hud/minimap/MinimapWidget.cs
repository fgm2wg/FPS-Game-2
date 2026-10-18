using Godot;
using FPSGame.Entities.Player.Controllers;

namespace FPSGame.UI.HUD.Minimap;

/// <summary>
/// Square tactical minimap with unshaded bird's-eye terrain rendering.
/// Renders terrain underneath and overlays real-time friendly directional blue arrows and enemy red radar dots.
/// </summary>
public partial class MinimapWidget : Control
{
    [Export] public float WorldViewSizeMeters { get; set; } = 100.0f;

    private PlayerController? _localPlayer;
    private SubViewport? _viewport;
    private Camera3D? _minimapCamera;
    private Control _overlay = null!;
    private Font? _font;

    public void BindLocalPlayer(PlayerController player)
    {
        _localPlayer = player;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(150, 150);
        Size = new Vector2(150, 150);

        _font = ThemeDB.FallbackFont;
        _viewport = GetNodeOrNull<SubViewport>("SubViewportContainer/SubViewport");
        _minimapCamera = GetNodeOrNull<Camera3D>("SubViewportContainer/SubViewport/MinimapCamera");
        _overlay = GetNodeOrNull<Control>("RadarOverlay") ?? this;

        if (_viewport != null)
        {
            _viewport.DebugDraw = SubViewport.DebugDrawEnum.Unshaded;
            _viewport.AudioListenerEnable3D = false;
        }

        if (_minimapCamera != null)
        {
            _minimapCamera.Size = WorldViewSizeMeters;
            _minimapCamera.Near = 1.0f;
            _minimapCamera.Far = 1000.0f;
        }

        if (_overlay != this)
        {
            _overlay.Draw += DrawRadarOverlay;
        }
    }

    public override void _Process(double delta)
    {
        if (_localPlayer != null && _localPlayer.IsInsideTree() && _minimapCamera != null)
        {
            Vector3 p = _localPlayer.GlobalPosition;
            // Place minimap camera 250m directly above the player's current height looking down
            _minimapCamera.GlobalPosition = new Vector3(p.X, p.Y + 250.0f, p.Z);
            if (_minimapCamera.Size != WorldViewSizeMeters)
            {
                _minimapCamera.Size = WorldViewSizeMeters;
            }
        }

        if (Visible)
        {
            if (_overlay != this)
            {
                _overlay.QueueRedraw();
            }
            else
            {
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        if (_overlay == this)
        {
            DrawRadarOverlay();
        }
    }

    private void DrawRadarOverlay()
    {
        CanvasItem target = _overlay;
        Vector2 size = Size;
        Vector2 center = size * 0.5f;

        // 1. Square tactical frame
        target.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.2f, 0.45f, 0.75f, 0.9f), false, 1.5f);

        // 2. Corner tactical brackets
        float corner = 10.0f;
        Color accentColor = new Color(0.35f, 0.75f, 1.0f, 0.95f);
        target.DrawLine(Vector2.Zero, new Vector2(corner, 0), accentColor, 2.0f);
        target.DrawLine(Vector2.Zero, new Vector2(0, corner), accentColor, 2.0f);
        target.DrawLine(new Vector2(size.X, 0), new Vector2(size.X - corner, 0), accentColor, 2.0f);
        target.DrawLine(new Vector2(size.X, 0), new Vector2(size.X, corner), accentColor, 2.0f);
        target.DrawLine(new Vector2(0, size.Y), new Vector2(corner, size.Y), accentColor, 2.0f);
        target.DrawLine(new Vector2(0, size.Y), new Vector2(0, size.Y - corner), accentColor, 2.0f);
        target.DrawLine(size, new Vector2(size.X - corner, size.Y), accentColor, 2.0f);
        target.DrawLine(size, new Vector2(size.X, size.Y - corner), accentColor, 2.0f);

        if (_localPlayer == null || !_localPlayer.IsInsideTree())
        {
            target.DrawCircle(center, 4.0f, new Color(0.2f, 0.72f, 1.0f, 1.0f));
            return;
        }

        Vector3 myPos = _localPlayer.GlobalPosition;
        int myTeam = _localPlayer.TeamId;
        float pixelsPerMeter = size.X / WorldViewSizeMeters;

        Color friendlyBlue = new Color(0.22f, 0.72f, 1.0f, 1.0f);
        Color friendlyBlueBorder = new Color(0.9f, 0.95f, 1.0f, 0.9f);
        Color enemyRed = new Color(1.0f, 0.25f, 0.25f, 0.95f);

        // 3. Draw other teammates (blue directional arrows) and enemies (red dots with distance indicator)
        var players = GetTree().GetNodesInGroup("Players");
        foreach (var node in players)
        {
            if (node is PlayerController other && other != _localPlayer && other.IsInsideTree() && !other.IsDead)
            {
                Vector3 diff = other.GlobalPosition - myPos;
                float distMeters = diff.Length();

                // In Godot 3D: North is -Z (up on screen), East is +X (right on screen)
                Vector2 blipPos = center + new Vector2(diff.X * pixelsPerMeter, diff.Z * pixelsPerMeter);

                // Clamp within square minimap bounds
                blipPos.X = Mathf.Clamp(blipPos.X, 7.0f, size.X - 7.0f);
                blipPos.Y = Mathf.Clamp(blipPos.Y, 7.0f, size.Y - 7.0f);

                bool isTeammate = other.TeamId == myTeam;

                if (isTeammate)
                {
                    // Friendlies: blue directional arrow pointing in their look direction
                    float rot = other.Rotation.Y;
                    Vector2 fwd = new Vector2(Mathf.Sin(-rot), -Mathf.Cos(-rot)).Normalized();
                    Vector2 rgt = new Vector2(-fwd.Y, fwd.X);

                    Vector2 tip = blipPos + fwd * 7.0f;
                    Vector2 left = blipPos - fwd * 4.0f - rgt * 4.0f;
                    Vector2 right = blipPos - fwd * 4.0f + rgt * 4.0f;
                    Vector2 indent = blipPos - fwd * 2.0f;

                    target.DrawColoredPolygon(new Vector2[] { tip, right, indent, left }, friendlyBlue);
                    target.DrawPolyline(new Vector2[] { tip, right, indent, left, tip }, friendlyBlueBorder, 1.0f);
                }
                else
                {
                    // Enemies: red dot with white outline ring
                    target.DrawCircle(blipPos, 4.0f, enemyRed);
                    target.DrawArc(blipPos, 4.5f, 0, Mathf.Tau, 12, new Color(1.0f, 0.85f, 0.85f, 0.75f), 1.0f);

                    // Distance indicator: #m or #km if >= 1000m
                    string distText = distMeters >= 1000.0f
                        ? $"{(distMeters * 0.001f):F1}km"
                        : $"{Mathf.RoundToInt(distMeters)}m";

                    if (_font != null)
                    {
                        Vector2 textPos = blipPos + new Vector2(6.0f, 3.0f);
                        if (textPos.X > size.X - 32.0f)
                        {
                            textPos.X = blipPos.X - 34.0f;
                        }
                        if (textPos.Y > size.Y - 4.0f)
                        {
                            textPos.Y = blipPos.Y - 6.0f;
                        }
                        if (textPos.Y < 12.0f)
                        {
                            textPos.Y = blipPos.Y + 12.0f;
                        }

                        target.DrawString(_font, textPos + new Vector2(1, 1), distText, HorizontalAlignment.Left, -1, 9, new Color(0, 0, 0, 0.85f));
                        target.DrawString(_font, textPos, distText, HorizontalAlignment.Left, -1, 9, new Color(1.0f, 0.88f, 0.88f, 0.95f));
                    }
                }
            }
        }

        // 4. Local player: prominent centered blue arrow pointing in forward look direction
        float rotY = _localPlayer.Rotation.Y;
        Vector2 forward = new Vector2(Mathf.Sin(-rotY), -Mathf.Cos(-rotY)).Normalized();
        Vector2 rightVec = new Vector2(-forward.Y, forward.X);

        Vector2 pTip = center + forward * 9.0f;
        Vector2 pLeft = center - forward * 5.0f - rightVec * 5.5f;
        Vector2 pRight = center - forward * 5.0f + rightVec * 5.5f;
        Vector2 pIndent = center - forward * 2.5f;

        target.DrawColoredPolygon(new Vector2[] { pTip, pRight, pIndent, pLeft }, friendlyBlue);
        target.DrawPolyline(new Vector2[] { pTip, pRight, pIndent, pLeft, pTip }, new Color(1.0f, 1.0f, 1.0f, 0.95f), 1.5f);
    }
}
