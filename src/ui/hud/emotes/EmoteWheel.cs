using System;
using Godot;

namespace FPSGame.UI.HUD.Emotes;

public readonly record struct EmoteDefinition(string Name, string AnimationName);

/// <summary>
/// Radial Emote Selection Wheel (Hold 'T' to open, select with mouse, release to perform).
/// Features 8 tactical & fun emotes with visual radial sectors, hover highlights, and deadzone cancellation.
/// </summary>
public partial class EmoteWheel : Control
{
    public static readonly EmoteDefinition[] Emotes = new EmoteDefinition[]
    {
        new("Dance", "Dance"),
        new("Combat Roll", "Roll"),
        new("Power Cast", "Spell_Simple_Shoot"),
        new("Air Swim", "Swim_Fwd"),
        new("Katana", "Sword_Attack"),
        new("Jab Combo", "Punch_Jab"),
        new("Knockout", "Punch_Cross"),
        new("Sit & Chill", "Sitting_Idle")
    };

    public bool IsOpen { get; private set; }
    public int SelectedIndex { get; private set; } = -1;

    public event Action<string>? EmoteSelected;

    private Vector2 _mouseOffset = Vector2.Zero;
    private const float DeadzoneRadius = 50.0f;
    private const float InnerRadius = 68.0f;
    private const float OuterRadius = 192.0f;
    private const float SectorAngle = 360.0f / 8.0f; // 45 degrees
    private const float GapAngleDeg = 2.4f; // Clean visual gap between adjacent ring sectors

    public override void _Ready()
    {
        Visible = false;
        SetProcessInput(false);
    }

    public void Open()
    {
        IsOpen = true;
        Visible = true;
        _mouseOffset = Vector2.Zero;
        SelectedIndex = -1;
        SetProcessInput(true);
        QueueRedraw();
    }

    public void Close(bool triggerSelection = true)
    {
        if (!IsOpen) return;

        int chosenIndex = SelectedIndex;
        IsOpen = false;
        Visible = false;
        SetProcessInput(false);

        if (triggerSelection && chosenIndex >= 0 && chosenIndex < Emotes.Length)
        {
            EmoteSelected?.Invoke(Emotes[chosenIndex].AnimationName);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;

        if (@event is InputEventMouseMotion motion)
        {
            _mouseOffset += motion.Relative;
            float dist = _mouseOffset.Length();

            if (dist > OuterRadius + 25.0f)
            {
                _mouseOffset = _mouseOffset.Normalized() * (OuterRadius + 25.0f);
            }

            int prevSelected = SelectedIndex;
            if (dist < DeadzoneRadius + 8.0f)
            {
                SelectedIndex = -1; // Center deadzone (Cancel)
            }
            else
            {
                // Calculate angle from center (-180 to 180)
                float rad = Mathf.Atan2(_mouseOffset.Y, _mouseOffset.X);
                float deg = Mathf.RadToDeg(rad);
                if (deg < 0.0f) deg += 360.0f;

                // Offset by half sector so slice 0 is centered around Right (0 deg)
                float adjusted = (deg + SectorAngle * 0.5f) % 360.0f;
                SelectedIndex = Mathf.Clamp(Mathf.FloorToInt(adjusted / SectorAngle), 0, 7);
            }

            if (prevSelected != SelectedIndex)
            {
                QueueRedraw();
            }
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        if (!IsOpen) return;

        Vector2 center = Size * 0.5f;

        // Draw dark tinted backdrop
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.0f, 0.0f, 0.0f, 0.45f));

        float halfGapRad = Mathf.DegToRad(GapAngleDeg * 0.5f);

        // Draw 4-sided annular sectors (curved ring segments with 2 arcs and 2 connecting radial edges)
        for (int i = 0; i < Emotes.Length; i++)
        {
            bool isSelected = i == SelectedIndex;
            float startAngleDeg = i * SectorAngle - SectorAngle * 0.5f;
            float endAngleDeg = startAngleDeg + SectorAngle;

            float startRad = Mathf.DegToRad(startAngleDeg);
            float endRad = Mathf.DegToRad(endAngleDeg);
            float midRad = Mathf.DegToRad(i * SectorAngle);

            float segStartRad = startRad + halfGapRad;
            float segEndRad = endRad - halfGapRad;

            float curOuterR = isSelected ? OuterRadius + 12.0f : OuterRadius;
            float curInnerR = isSelected ? InnerRadius - 3.0f : InnerRadius;

            // Construct 4-sided polygon:
            // 1. Outer circular arc (segStartRad to segEndRad at curOuterR)
            // 2. Radial straight edge connecting outer arc to inner arc
            // 3. Inner circular arc (segEndRad back to segStartRad at curInnerR)
            // 4. Radial straight edge connecting inner arc back to outer arc
            const int arcSteps = 16;
            var points = new Vector2[(arcSteps + 1) * 2];

            // Outer arc vertices
            for (int p = 0; p <= arcSteps; p++)
            {
                float t = (float)p / arcSteps;
                float curRad = Mathf.Lerp(segStartRad, segEndRad, t);
                points[p] = center + new Vector2(Mathf.Cos(curRad), Mathf.Sin(curRad)) * curOuterR;
            }

            // Inner arc vertices
            for (int p = 0; p <= arcSteps; p++)
            {
                float t = (float)p / arcSteps;
                float curRad = Mathf.Lerp(segEndRad, segStartRad, t);
                points[arcSteps + 1 + p] = center + new Vector2(Mathf.Cos(curRad), Mathf.Sin(curRad)) * curInnerR;
            }

            // Sector fill color
            Color sectorColor = isSelected
                ? new Color(0.18f, 0.58f, 0.98f, 0.65f) // Glowing cyan-blue highlight
                : new Color(0.07f, 0.09f, 0.13f, 0.72f); // Tactical dark slate charcoal

            DrawColoredPolygon(points, sectorColor);

            // Sector outline on all 4 sides (2 arcs + 2 straight radial edges)
            Color borderColor = isSelected
                ? new Color(0.45f, 0.9f, 1.0f, 0.95f)
                : new Color(0.25f, 0.32f, 0.42f, 0.6f);

            for (int p = 0; p < points.Length; p++)
            {
                DrawLine(points[p], points[(p + 1) % points.Length], borderColor, isSelected ? 2.5f : 1.3f, true);
            }

            // Draw clean tactical emote name centered vertically and horizontally within the annular sector
            float midR = (curInnerR + curOuterR) * 0.5f;
            Vector2 sectorCenter = center + new Vector2(Mathf.Cos(midRad), Mathf.Sin(midRad)) * midR;

            string title = Emotes[i].Name.ToUpperInvariant();
            int titleFontSize = isSelected ? 15 : 13;
            Color textColor = isSelected ? Colors.White : new Color(0.85f, 0.90f, 0.96f, 0.92f);
            Vector2 titleSize = ThemeDB.FallbackFont.GetStringSize(title, HorizontalAlignment.Left, -1, titleFontSize);
            DrawString(ThemeDB.FallbackFont, sectorCenter - new Vector2(titleSize.X * 0.5f, -titleSize.Y * 0.35f), title, HorizontalAlignment.Left, -1, titleFontSize, textColor);
        }

        // Draw central deadzone circle
        Color centerCircleColor = SelectedIndex == -1 ? new Color(0.85f, 0.22f, 0.22f, 0.45f) : new Color(0.06f, 0.08f, 0.12f, 0.9f);
        Color centerBorderColor = SelectedIndex == -1 ? new Color(1.0f, 0.45f, 0.45f, 0.9f) : new Color(0.35f, 0.55f, 0.8f, 0.8f);
        DrawCircle(center, DeadzoneRadius, centerCircleColor);
        DrawArc(center, DeadzoneRadius, 0.0f, Mathf.Tau, 48, centerBorderColor, 2.0f, true);

        // Center label prompt
        string centerPrompt = SelectedIndex >= 0 ? Emotes[SelectedIndex].Name.ToUpperInvariant() : "CANCEL";
        int centerFontSize = SelectedIndex >= 0 ? 14 : 12;
        Color centerTextColor = SelectedIndex >= 0 ? new Color(0.35f, 0.85f, 1.0f) : new Color(0.9f, 0.75f, 0.75f, 0.9f);
        Vector2 promptSize = ThemeDB.FallbackFont.GetStringSize(centerPrompt, HorizontalAlignment.Left, -1, centerFontSize);
        DrawString(ThemeDB.FallbackFont, center - new Vector2(promptSize.X * 0.5f, -promptSize.Y * 0.35f), centerPrompt, HorizontalAlignment.Left, -1, centerFontSize, centerTextColor);
    }
}
