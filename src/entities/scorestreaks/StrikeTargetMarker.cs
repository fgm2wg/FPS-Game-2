using Godot;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// 3D translucent glowing tactical target indicator for Airstrikes and Care Packages.
/// Features a glowing orange ground perimeter ring and a hovering downward-pointing arrow.
/// </summary>
public partial class StrikeTargetMarker : Node3D
{
    private MeshInstance3D _ringMesh = null!;
    private MeshInstance3D _discMesh = null!;
    private Node3D _arrowRoot = null!;
    private StandardMaterial3D _orangeMat = null!;

    private float _animTime;
    private float _fadeTimer = -1.0f;
    private float _fadeDuration = 1.0f;
    private float _targetRadius = 9.0f;

    public float TargetRadius
    {
        get => _targetRadius;
        set
        {
            _targetRadius = value;
            UpdateDimensions();
        }
    }

    public override void _Ready()
    {
        _orangeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1.0f, 0.48f, 0.08f, 0.65f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        // 1. Concentric tactical perimeter ring
        var torus = new TorusMesh
        {
            InnerRadius = _targetRadius - 0.16f,
            OuterRadius = _targetRadius + 0.16f,
            Rings = 48,
            RingSegments = 16
        };
        _ringMesh = new MeshInstance3D
        {
            Name = "PerimeterRing",
            Mesh = torus,
            MaterialOverride = _orangeMat,
            Position = new Vector3(0.0f, 0.06f, 0.0f)
        };
        AddChild(_ringMesh);

        // 2. Translucent floor zone disc
        var discMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1.0f, 0.40f, 0.05f, 0.16f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var cylinder = new CylinderMesh
        {
            TopRadius = _targetRadius,
            BottomRadius = _targetRadius,
            Height = 0.04f,
            RadialSegments = 36
        };
        _discMesh = new MeshInstance3D
        {
            Name = "ZoneDisc",
            Mesh = cylinder,
            MaterialOverride = discMat,
            Position = new Vector3(0.0f, 0.02f, 0.0f)
        };
        AddChild(_discMesh);

        // 3. 3D downward pointing tactical arrow
        _arrowRoot = new Node3D { Name = "ArrowRoot" };
        AddChild(_arrowRoot);

        // Arrow head (inverted cone pointing down)
        var cone = new CylinderMesh
        {
            TopRadius = 0.50f,
            BottomRadius = 0.0f,
            Height = 0.90f,
            RadialSegments = 16
        };
        var coneMesh = new MeshInstance3D
        {
            Name = "ArrowHead",
            Mesh = cone,
            MaterialOverride = _orangeMat,
            Position = new Vector3(0.0f, 0.45f, 0.0f)
        };
        _arrowRoot.AddChild(coneMesh);

        // Arrow shaft
        var shaft = new CylinderMesh
        {
            TopRadius = 0.16f,
            BottomRadius = 0.16f,
            Height = 0.95f,
            RadialSegments = 16
        };
        var shaftMesh = new MeshInstance3D
        {
            Name = "ArrowShaft",
            Mesh = shaft,
            MaterialOverride = _orangeMat,
            Position = new Vector3(0.0f, 1.35f, 0.0f)
        };
        _arrowRoot.AddChild(shaftMesh);
    }

    private void UpdateDimensions()
    {
        if (_ringMesh?.Mesh is TorusMesh torus)
        {
            torus.InnerRadius = Mathf.Max(0.5f, _targetRadius - 0.16f);
            torus.OuterRadius = _targetRadius + 0.16f;
        }
        if (_discMesh?.Mesh is CylinderMesh cyl)
        {
            cyl.TopRadius = _targetRadius;
            cyl.BottomRadius = _targetRadius;
        }
    }

    public void FadeOutAndFree(float duration = 1.0f)
    {
        _fadeDuration = Mathf.Max(0.1f, duration);
        _fadeTimer = _fadeDuration;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _animTime += dt;

        // Subtle vertical bobbing and rotation for dynamic tactical visualization
        if (_arrowRoot != null)
        {
            float bob = Mathf.Sin(_animTime * 3.6f) * 0.20f;
            _arrowRoot.Position = new Vector3(0.0f, 0.40f + bob, 0.0f);
            _arrowRoot.RotateY(dt * 0.85f);
        }

        // Fading out sequence
        if (_fadeTimer > 0.0f)
        {
            _fadeTimer -= dt;
            float alpha = Mathf.Clamp(_fadeTimer / _fadeDuration, 0.0f, 1.0f);
            if (_orangeMat != null)
            {
                Color c = _orangeMat.AlbedoColor;
                c.A = alpha * 0.65f;
                _orangeMat.AlbedoColor = c;
            }
            if (_fadeTimer <= 0.0f)
            {
                QueueFree();
            }
        }
    }
}
