using System;
using Godot;

namespace FPSGame.UI.HUD.Weapons;

public readonly record struct WeaponDefinition(string Id, string DisplayName, string Subtitle, string CaliberInfo);

/// <summary>
/// Radial Weapon Selection Wheel (Hold 'U' to open, aim toward weapon with mouse, release to equip).
/// Features 4 primary weapon slots (MP5, FN MINIMI, M32 MGL, M82) with tactical radial sectors,
/// hover highlights, ammo/caliber telemetry, and center deadzone cancellation.
/// </summary>
public partial class WeaponWheel : Control
{
    public static readonly WeaponDefinition[] Weapons = new WeaponDefinition[]
    {
        new("MP5", "MP5", "SUBMACHINE GUN", "9x19mm · 750 RPM"),
        new("LMG", "FN MINIMI", "LIGHT MACHINE GUN", "5.56x45mm · 1200 RPM"),
        new("MGL", "M32 MGL", "GRENADE LAUNCHER", "40mm HE · SEMI"),
        new("M82", "M82", ".50 CAL SNIPER", ".50 BMG · 1-SHOT KILL")
    };

    public bool IsOpen { get; private set; }
    public int SelectedIndex { get; private set; } = -1;

    public event Action<string>? WeaponSelected;

    private Vector2 _mouseOffset = Vector2.Zero;
    private const float DeadzoneRadius = 60.0f;
    private const float InnerRadius = 82.0f;
    private const float OuterRadius = 215.0f;
    private const float SectorAngle = 90.0f;
    private const float GapAngleDeg = 3.5f;

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

        if (triggerSelection && chosenIndex >= 0 && chosenIndex < Weapons.Length)
        {
            WeaponSelected?.Invoke(Weapons[chosenIndex].Id);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;

        if (@event is InputEventMouseMotion motion)
        {
            _mouseOffset += motion.Relative;
            float dist = _mouseOffset.Length();

            if (dist > OuterRadius + 30.0f)
            {
                _mouseOffset = _mouseOffset.Normalized() * (OuterRadius + 30.0f);
            }

            int prevSelected = SelectedIndex;
            if (dist < DeadzoneRadius + 8.0f)
            {
                SelectedIndex = -1; // Center deadzone (Cancel)
            }
            else
            {
                // Calculate angle from center (0 to 360 deg)
                float rad = Mathf.Atan2(_mouseOffset.Y, _mouseOffset.X);
                float deg = Mathf.RadToDeg(rad);
                if (deg < 0.0f) deg += 360.0f;

                // Sector 0 (Top / MP5): 225° to 315°
                // Sector 1 (Right / FN MINIMI): 315° to 45°
                // Sector 2 (Bottom / M32 MGL): 45° to 135°
                // Sector 3 (Left / M82): 135° to 225°
                if (deg >= 225.0f && deg < 315.0f)
                {
                    SelectedIndex = 0; // Top: MP5
                }
                else if (deg >= 315.0f || deg < 45.0f)
                {
                    SelectedIndex = 1; // Right: FN MINIMI
                }
                else if (deg >= 45.0f && deg < 135.0f)
                {
                    SelectedIndex = 2; // Bottom: M32 MGL
                }
                else
                {
                    SelectedIndex = 3; // Left: M82
                }
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

        // Dark tactical backdrop
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.0f, 0.0f, 0.0f, 0.52f));

        // Draw top header prompt
        const string headerText = "[ HOLD U ] WEAPON SELECTION WHEEL";
        Vector2 headerSize = ThemeDB.FallbackFont.GetStringSize(headerText, HorizontalAlignment.Left, -1, 15);
        DrawString(ThemeDB.FallbackFont, new Vector2(center.X - headerSize.X * 0.5f, center.Y - OuterRadius - 28.0f),
            headerText, HorizontalAlignment.Left, -1, 15, new Color(1.0f, 0.78f, 0.28f, 0.95f));

        float halfGapRad = Mathf.DegToRad(GapAngleDeg * 0.5f);

        // Mid-angles for the 4 sectors:
        // 0: Top (270°), 1: Right (0° / 360°), 2: Bottom (90°), 3: Left (180°)
        float[] midAnglesDeg = { 270.0f, 0.0f, 90.0f, 180.0f };

        for (int i = 0; i < Weapons.Length; i++)
        {
            bool isSelected = i == SelectedIndex;
            float midDeg = midAnglesDeg[i];
            float startAngleDeg = midDeg - SectorAngle * 0.5f;
            float endAngleDeg = midDeg + SectorAngle * 0.5f;

            float segStartRad = Mathf.DegToRad(startAngleDeg) + halfGapRad;
            float segEndRad = Mathf.DegToRad(endAngleDeg) - halfGapRad;

            float curOuterR = isSelected ? OuterRadius + 14.0f : OuterRadius;
            float curInnerR = isSelected ? InnerRadius - 4.0f : InnerRadius;

            const int arcSteps = 24;
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
            Color sectorColor;
            Color borderColor;
            if (isSelected)
            {
                // M82 gets a distinctive red/gold sniper highlight; others get cyber-amber
                if (i == 3)
                {
                    sectorColor = new Color(0.92f, 0.22f, 0.16f, 0.75f);
                    borderColor = new Color(1.0f, 0.65f, 0.35f, 0.98f);
                }
                else
                {
                    sectorColor = new Color(0.95f, 0.58f, 0.15f, 0.70f);
                    borderColor = new Color(1.0f, 0.85f, 0.45f, 0.95f);
                }
            }
            else
            {
                sectorColor = new Color(0.07f, 0.09f, 0.14f, 0.78f);
                borderColor = new Color(0.24f, 0.32f, 0.44f, 0.65f);
            }

            DrawColoredPolygon(points, sectorColor);

            for (int p = 0; p < points.Length; p++)
            {
                DrawLine(points[p], points[(p + 1) % points.Length], borderColor, isSelected ? 2.8f : 1.4f, true);
            }

            // Draw weapon labels
            float midR = (curInnerR + curOuterR) * 0.5f;
            float midRad = Mathf.DegToRad(midDeg);
            Vector2 sectorCenter = center + new Vector2(Mathf.Cos(midRad), Mathf.Sin(midRad)) * midR;

            string title = Weapons[i].DisplayName;
            string subtitle = Weapons[i].Subtitle;
            string caliber = Weapons[i].CaliberInfo;

            int titleFontSize = isSelected ? 17 : 14;
            int subFontSize = isSelected ? 11 : 10;

            Color titleColor = isSelected ? Colors.White : new Color(0.90f, 0.94f, 0.98f, 0.95f);
            Color subColor = isSelected ? new Color(1.0f, 0.95f, 0.80f, 0.95f) : new Color(0.65f, 0.72f, 0.82f, 0.75f);

            Vector2 titleSize = ThemeDB.FallbackFont.GetStringSize(title, HorizontalAlignment.Left, -1, titleFontSize);
            Vector2 subSize = ThemeDB.FallbackFont.GetStringSize(subtitle, HorizontalAlignment.Left, -1, subFontSize);

            float totalH = titleSize.Y + subSize.Y + 4.0f;
            Vector2 titlePos = sectorCenter - new Vector2(titleSize.X * 0.5f, totalH * 0.5f - titleSize.Y);
            Vector2 subPos = sectorCenter - new Vector2(subSize.X * 0.5f, totalH * 0.5f - titleSize.Y - subSize.Y - 2.0f);

            DrawString(ThemeDB.FallbackFont, titlePos, title, HorizontalAlignment.Left, -1, titleFontSize, titleColor);
            DrawString(ThemeDB.FallbackFont, subPos, subtitle, HorizontalAlignment.Left, -1, subFontSize, subColor);
        }

        // Draw central deadzone circle
        Color centerCircleColor = SelectedIndex == -1
            ? new Color(0.85f, 0.20f, 0.20f, 0.40f)
            : new Color(0.06f, 0.08f, 0.12f, 0.92f);
        Color centerBorderColor = SelectedIndex == -1
            ? new Color(1.0f, 0.45f, 0.45f, 0.90f)
            : new Color(0.95f, 0.65f, 0.25f, 0.85f);

        DrawCircle(center, DeadzoneRadius, centerCircleColor);
        DrawArc(center, DeadzoneRadius, 0.0f, Mathf.Tau, 48, centerBorderColor, 2.2f, true);

        // Center telemetry text
        if (SelectedIndex >= 0)
        {
            var def = Weapons[SelectedIndex];
            string centerName = def.DisplayName;
            string centerInfo = def.CaliberInfo;

            int nameFontSize = 15;
            int infoFontSize = 10;

            Vector2 nameSize = ThemeDB.FallbackFont.GetStringSize(centerName, HorizontalAlignment.Left, -1, nameFontSize);
            Vector2 infoSize = ThemeDB.FallbackFont.GetStringSize(centerInfo, HorizontalAlignment.Left, -1, infoFontSize);

            DrawString(ThemeDB.FallbackFont, center - new Vector2(nameSize.X * 0.5f, 4.0f),
                centerName, HorizontalAlignment.Left, -1, nameFontSize, new Color(1.0f, 0.85f, 0.45f));
            DrawString(ThemeDB.FallbackFont, center - new Vector2(infoSize.X * 0.5f, -14.0f),
                centerInfo, HorizontalAlignment.Left, -1, infoFontSize, new Color(0.80f, 0.85f, 0.92f, 0.85f));
        }
        else
        {
            const string cancelPrompt = "CANCEL";
            int cancelFontSize = 13;
            Vector2 promptSize = ThemeDB.FallbackFont.GetStringSize(cancelPrompt, HorizontalAlignment.Left, -1, cancelFontSize);
            DrawString(ThemeDB.FallbackFont, center - new Vector2(promptSize.X * 0.5f, -promptSize.Y * 0.35f),
                cancelPrompt, HorizontalAlignment.Left, -1, cancelFontSize, new Color(0.95f, 0.65f, 0.65f, 0.95f));
        }
    }
}
