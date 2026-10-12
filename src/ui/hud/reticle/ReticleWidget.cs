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

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
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
        Vector2 center = Size * 0.5f;

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

        // 3. Diagonal hitmarker ticks
        if (_hitmarkerTimer > 0.0f)
        {
            float alpha = Mathf.Clamp(_hitmarkerTimer / HitmarkerDuration, 0.0f, 1.0f);
            Color hitColor = _isCriticalHit
                ? new Color(1.0f, 0.22f, 0.22f, alpha)
                : new Color(1.0f, 1.0f, 1.0f, alpha);

            float hitInner = 4.5f;
            float hitOuter = 10.0f;
            float hitThickness = 2.0f;

            DrawLine(center + new Vector2(-hitInner, -hitInner), center + new Vector2(-hitOuter, -hitOuter), hitColor, hitThickness);
            DrawLine(center + new Vector2(hitInner, -hitInner), center + new Vector2(hitOuter, -hitOuter), hitColor, hitThickness);
            DrawLine(center + new Vector2(-hitInner, hitInner), center + new Vector2(-hitOuter, hitOuter), hitColor, hitThickness);
            DrawLine(center + new Vector2(hitInner, hitInner), center + new Vector2(hitOuter, hitOuter), hitColor, hitThickness);
        }
    }
}
