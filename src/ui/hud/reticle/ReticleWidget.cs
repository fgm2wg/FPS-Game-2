using Godot;

namespace FPSGame.UI.HUD.Reticle;

/// <summary>
/// Pixel-perfect procedural dynamic FPS reticle and hitmarker renderer.
/// Guarantees exact sub-pixel center alignment and diagonal hitmarker confirmation.
/// </summary>
public partial class ReticleWidget : Control
{
    private float _hitmarkerTimer;
    private bool _isCriticalHit;
    private const float HitmarkerDuration = 0.16f;

    public bool IsScoped { get; private set; }
    public float ScopeMagnification { get; private set; } = 6.0f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetScoped(bool scoped, float magnification = 6.0f)
    {
        if (IsScoped == scoped && Mathf.IsEqualApprox(ScopeMagnification, magnification)) return;
        IsScoped = scoped;
        ScopeMagnification = magnification;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_hitmarkerTimer > 0.0f)
        {
            _hitmarkerTimer -= (float)delta;
            QueueRedraw();
        }
    }

    public void FlashHitmarker(bool isHeadshot, bool isKill)
    {
        _isCriticalHit = isHeadshot || isKill;
        _hitmarkerTimer = HitmarkerDuration;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 screenSize = Size;
        if (screenSize.X <= 64.0f || screenSize.Y <= 64.0f)
        {
            screenSize = GetViewportRect().Size;
        }
        Vector2 center = screenSize * 0.5f;

        if (IsScoped)
        {
            DrawSniperScope(center, screenSize);
        }
        else
        {
            DrawStandardReticle(center);
        }

        // 3. Diagonal hitmarker ticks
        if (_hitmarkerTimer > 0.0f)
        {
            float alpha = Mathf.Clamp(_hitmarkerTimer / HitmarkerDuration, 0.0f, 1.0f);
            Color hitColor = _isCriticalHit
                ? new Color(1.0f, 0.22f, 0.22f, alpha)
                : new Color(1.0f, 1.0f, 1.0f, alpha);

            float hitInner = IsScoped ? 7.0f : 4.5f;
            float hitOuter = IsScoped ? 16.0f : 10.0f;
            float hitThickness = 2.0f;

            DrawLine(center + new Vector2(-hitInner, -hitInner), center + new Vector2(-hitOuter, -hitOuter), hitColor, hitThickness);
            DrawLine(center + new Vector2(hitInner, -hitInner), center + new Vector2(hitOuter, -hitOuter), hitColor, hitThickness);
            DrawLine(center + new Vector2(-hitInner, hitInner), center + new Vector2(-hitOuter, hitOuter), hitColor, hitThickness);
            DrawLine(center + new Vector2(hitInner, hitInner), center + new Vector2(hitOuter, hitOuter), hitColor, hitThickness);
        }
    }

    private void DrawStandardReticle(Vector2 center)
    {
        // 1. Center dot
        DrawCircle(center, 1.5f, new Color(1.0f, 1.0f, 1.0f, 0.9f));

        // 2. Symmetrical 4-line reticle
        float gap = 5.0f;
        float len = 7.0f;
        float thickness = 1.5f;
        Color reticleColor = new Color(1.0f, 1.0f, 1.0f, 0.8f);

        DrawLine(center + new Vector2(0, -gap), center + new Vector2(0, -gap - len), reticleColor, thickness);
        DrawLine(center + new Vector2(0, gap), center + new Vector2(0, gap + len), reticleColor, thickness);
        DrawLine(center + new Vector2(-gap, 0), center + new Vector2(-gap - len, 0), reticleColor, thickness);
        DrawLine(center + new Vector2(gap, 0), center + new Vector2(gap + len, 0), reticleColor, thickness);
    }

    private Vector2 _lastSize = Vector2.Zero;
    private Vector2[] _cornerTL = System.Array.Empty<Vector2>();
    private Vector2[] _cornerTR = System.Array.Empty<Vector2>();
    private Vector2[] _cornerBR = System.Array.Empty<Vector2>();
    private Vector2[] _cornerBL = System.Array.Empty<Vector2>();

    private void UpdateScopeCorners(Vector2 size, Vector2 center, float radius)
    {
        if (size == _lastSize && _cornerTL.Length > 0) return;
        _lastSize = size;

        const int segments = 32;
        _cornerTL = new Vector2[segments + 2];
        _cornerTR = new Vector2[segments + 2];
        _cornerBR = new Vector2[segments + 2];
        _cornerBL = new Vector2[segments + 2];

        float left = center.X - radius;
        float right = center.X + radius;
        float top = center.Y - radius;
        float bottom = center.Y + radius;

        // Top-Left Corner: from (left, top) along arc from PI to 1.5*PI
        _cornerTL[0] = new Vector2(left, top);
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Pi + t * (Mathf.Pi * 0.5f);
            _cornerTL[i + 1] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        // Top-Right Corner: from (right, top) along arc from 1.5*PI to 2*PI
        _cornerTR[0] = new Vector2(right, top);
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Pi * 1.5f + t * (Mathf.Pi * 0.5f);
            _cornerTR[i + 1] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        // Bottom-Right Corner: from (right, bottom) along arc from 0 to 0.5*PI
        _cornerBR[0] = new Vector2(right, bottom);
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = 0.0f + t * (Mathf.Pi * 0.5f);
            _cornerBR[i + 1] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        // Bottom-Left Corner: from (left, bottom) along arc from 0.5*PI to PI
        _cornerBL[0] = new Vector2(left, bottom);
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Pi * 0.5f + t * (Mathf.Pi * 0.5f);
            _cornerBL[i + 1] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }

    private void DrawSniperScope(Vector2 center, Vector2 screenSize)
    {
        // Big open circular aperture spanning ~92% of screen height
        float radius = Mathf.Min(screenSize.X, screenSize.Y) * 0.46f;
        UpdateScopeCorners(screenSize, center, radius);

        float left = center.X - radius;
        float right = center.X + radius;
        float top = center.Y - radius;
        float bottom = center.Y + radius;

        // 1. Solid pitch black mask covering entire screen outside the scope aperture
        // Use extra margin to guarantee full coverage on ultrawide monitors and letterboxed viewports
        float bleed = 3000.0f;
        DrawRect(new Rect2(-bleed, -bleed, screenSize.X + bleed * 2.0f, top + bleed), Colors.Black);
        DrawRect(new Rect2(-bleed, bottom, screenSize.X + bleed * 2.0f, screenSize.Y - bottom + bleed), Colors.Black);
        DrawRect(new Rect2(-bleed, top, left + bleed, radius * 2.0f), Colors.Black);
        DrawRect(new Rect2(right, top, screenSize.X - right + bleed, radius * 2.0f), Colors.Black);

        // Fill corner wedges outside the circle
        DrawColoredPolygon(_cornerTL, Colors.Black);
        DrawColoredPolygon(_cornerTR, Colors.Black);
        DrawColoredPolygon(_cornerBR, Colors.Black);
        DrawColoredPolygon(_cornerBL, Colors.Black);

        // Smooth circular rim edge
        DrawArc(center, radius, 0, Mathf.Tau, 128, Colors.Black, 2.5f);

        // 2. Fine, clean, non-invasive sniper crosshair
        Color crosshairColor = Colors.Black;

        // Subtle outer stadia posts starting far out at 72% radius so the center view remains wide open
        float postStart = radius * 0.72f;
        float postThickness = 2.0f;
        DrawLine(new Vector2(left, center.Y), new Vector2(center.X - postStart, center.Y), crosshairColor, postThickness);
        DrawLine(new Vector2(center.X + postStart, center.Y), new Vector2(right, center.Y), crosshairColor, postThickness);
        DrawLine(new Vector2(center.X, top), new Vector2(center.X, center.Y - postStart), crosshairColor, postThickness);
        DrawLine(new Vector2(center.X, center.Y + postStart), new Vector2(center.X, bottom), crosshairColor, postThickness);

        // Ultra-fine 1.0px hairline traversing the full aperture
        float fineThickness = 1.0f;
        DrawLine(new Vector2(left, center.Y), new Vector2(right, center.Y), crosshairColor, fineThickness);
        DrawLine(new Vector2(center.X, top), new Vector2(center.X, bottom), crosshairColor, fineThickness);

        // Subtle non-invasive micro range ticks along hairline
        for (int i = 1; i <= 4; i++)
        {
            float dist = i * (radius * 0.12f);
            float tick = (i % 2 == 0) ? 3.5f : 2.0f;

            // Horizontal windage ticks
            DrawLine(new Vector2(center.X + dist, center.Y - tick), new Vector2(center.X + dist, center.Y + tick), crosshairColor, 1.0f);
            DrawLine(new Vector2(center.X - dist, center.Y - tick), new Vector2(center.X - dist, center.Y + tick), crosshairColor, 1.0f);

            // Vertical elevation ticks
            DrawLine(new Vector2(center.X - tick, center.Y + dist), new Vector2(center.X + tick, center.Y + dist), crosshairColor, 1.0f);
            DrawLine(new Vector2(center.X - tick, center.Y - dist), new Vector2(center.X + tick, center.Y - dist), crosshairColor, 1.0f);
        }
    }
}

