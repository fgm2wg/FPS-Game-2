using Godot;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// 3D translucent glowing tactical indicator for the B-52 Bombing Run scorestreak.
/// Features a center downward-pointing pivot arrow and two parallel corridor lines indicating
/// the exact swath and flight heading of the 2,000 lb bombing run.
/// Supports two-stage vector placement:
/// Stage 1: Cursor positions center pivot arrow on terrain.
/// Stage 2: Center is locked; mouse motion pivots the strike corridor direction.
/// </summary>
public partial class BombingRunMarker : Node3D
{
    private Node3D _arrowRoot = null!;
    private Node3D _corridorRoot = null!;
    private StandardMaterial3D _orangeMat = null!;
    private StandardMaterial3D _stripMat = null!;

    private float _animTime;
    private float _activeLifetime = -1.0f;
    private float _fadeTimer = -1.0f;
    private float _fadeDuration = 1.0f;

    public const float CorridorWidth = 20.0f;
    public const float CorridorLength = 220.0f;

    public bool IsCenterLocked { get; set; }
    private Vector3 _headingDir = Vector3.Forward;

    /// <summary>
    /// Sets an automatic timer to cleanly fade out and remove the marker after the specified duration (default 5.0s).
    /// </summary>
    public void SetAutoRemove(float totalDuration = 5.0f, float fadeDuration = 1.0f)
    {
        _fadeDuration = Mathf.Max(0.1f, fadeDuration);
        _activeLifetime = Mathf.Max(0.0f, totalDuration - _fadeDuration);
    }

    public override void _Ready()
    {
        // 1. Tactical glowing orange material
        _orangeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1.0f, 0.48f, 0.08f, 0.75f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        // 2. Translucent floor runway ribbon material
        _stripMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1.0f, 0.40f, 0.05f, 0.12f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        // 3. Center pivot arrow root
        _arrowRoot = new Node3D { Name = "ArrowRoot" };
        AddChild(_arrowRoot);

        // Arrow head (inverted cone pointing down at the center pivot)
        var cone = new CylinderMesh
        {
            TopRadius = 0.65f,
            BottomRadius = 0.0f,
            Height = 1.15f,
            RadialSegments = 16
        };
        var coneMesh = new MeshInstance3D
        {
            Name = "ArrowHead",
            Mesh = cone,
            MaterialOverride = _orangeMat,
            Position = new Vector3(0.0f, 0.58f, 0.0f)
        };
        _arrowRoot.AddChild(coneMesh);

        // Arrow shaft
        var shaft = new CylinderMesh
        {
            TopRadius = 0.20f,
            BottomRadius = 0.20f,
            Height = 1.25f,
            RadialSegments = 16
        };
        var shaftMesh = new MeshInstance3D
        {
            Name = "ArrowShaft",
            Mesh = shaft,
            MaterialOverride = _orangeMat,
            Position = new Vector3(0.0f, 1.75f, 0.0f)
        };
        _arrowRoot.AddChild(shaftMesh);

        // Center pivot target disc
        var centerDisc = new CylinderMesh
        {
            TopRadius = 1.6f,
            BottomRadius = 1.6f,
            Height = 0.04f,
            RadialSegments = 24
        };
        var centerDiscMesh = new MeshInstance3D
        {
            Name = "CenterDisc",
            Mesh = centerDisc,
            MaterialOverride = _stripMat,
            Position = new Vector3(0.0f, 0.02f, 0.0f)
        };
        AddChild(centerDiscMesh);

        // 4. Directional Corridor Root (rotates around Y according to chosen flight heading)
        _corridorRoot = new Node3D { Name = "CorridorRoot" };
        AddChild(_corridorRoot);

        // Left parallel guide rail
        float halfWidth = CorridorWidth * 0.5f;
        var railMesh = new BoxMesh
        {
            Size = new Vector3(0.35f, 0.08f, CorridorLength)
        };
        var leftRail = new MeshInstance3D
        {
            Name = "LeftRail",
            Mesh = railMesh,
            MaterialOverride = _orangeMat,
            Position = new Vector3(-halfWidth, 0.06f, 0.0f)
        };
        _corridorRoot.AddChild(leftRail);

        // Right parallel guide rail
        var rightRail = new MeshInstance3D
        {
            Name = "RightRail",
            Mesh = railMesh,
            MaterialOverride = _orangeMat,
            Position = new Vector3(halfWidth, 0.06f, 0.0f)
        };
        _corridorRoot.AddChild(rightRail);

        // Runway floor swath strip
        var stripMesh = new BoxMesh
        {
            Size = new Vector3(CorridorWidth, 0.03f, CorridorLength)
        };
        var swathStrip = new MeshInstance3D
        {
            Name = "SwathStrip",
            Mesh = stripMesh,
            MaterialOverride = _stripMat,
            Position = new Vector3(0.0f, 0.03f, 0.0f)
        };
        _corridorRoot.AddChild(swathStrip);

        // Add 9 forward-pointing chevrons along the 220m corridor to clearly indicate strike direction
        for (int i = -4; i <= 4; i++)
        {
            float zOffset = i * 24.0f;
            CreateChevronMesh(zOffset);
        }

        // Apply pre-configured flight heading
        SetHeading(_headingDir);
    }

    private void CreateChevronMesh(float zPos)
    {
        var chevronRoot = new Node3D
        {
            Name = $"Chevron_{zPos:F0}",
            Position = new Vector3(0.0f, 0.07f, zPos)
        };
        _corridorRoot.AddChild(chevronRoot);

        // Left wing of chevron
        var wingMesh = new BoxMesh { Size = new Vector3(0.25f, 0.05f, 3.5f) };
        var leftWing = new MeshInstance3D
        {
            Mesh = wingMesh,
            MaterialOverride = _orangeMat,
            Position = new Vector3(-1.4f, 0.0f, 1.0f),
            RotationDegrees = new Vector3(0.0f, 38.0f, 0.0f)
        };
        chevronRoot.AddChild(leftWing);

        // Right wing of chevron
        var rightWing = new MeshInstance3D
        {
            Mesh = wingMesh,
            MaterialOverride = _orangeMat,
            Position = new Vector3(1.4f, 0.0f, 1.0f),
            RotationDegrees = new Vector3(0.0f, -38.0f, 0.0f)
        };
        chevronRoot.AddChild(rightWing);
    }

    /// <summary>
    /// Orients the strike corridor heading vector towards the given direction.
    /// In local space, forward flight along the corridor is -Z.
    /// </summary>
    public void SetHeading(Vector3 flightDir)
    {
        Vector3 flatDir = new Vector3(flightDir.X, 0.0f, flightDir.Z).Normalized();
        if (flatDir.LengthSquared() < 0.01f) return;
        _headingDir = flatDir;

        if (_corridorRoot != null)
        {
            _corridorRoot.Transform = new Transform3D(Basis.LookingAt(_headingDir, Vector3.Up), Vector3.Zero);
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

        // Auto-remove countdown
        if (_activeLifetime > 0.0f)
        {
            _activeLifetime -= dt;
            if (_activeLifetime <= 0.0f)
            {
                FadeOutAndFree(_fadeDuration);
            }
        }

        // Bobbing & rotating animation for center arrow
        if (_arrowRoot != null)
        {
            float bob = Mathf.Sin(_animTime * 3.8f) * 0.22f;
            _arrowRoot.Position = new Vector3(0.0f, 0.45f + bob, 0.0f);
            _arrowRoot.RotateY(dt * 0.90f);
        }

        // Fade-out handling
        if (_fadeTimer > 0.0f)
        {
            _fadeTimer -= dt;
            float alpha = Mathf.Clamp(_fadeTimer / _fadeDuration, 0.0f, 1.0f);
            if (_orangeMat != null)
            {
                Color c = _orangeMat.AlbedoColor;
                c.A = alpha * 0.75f;
                _orangeMat.AlbedoColor = c;
            }
            if (_stripMat != null)
            {
                Color sc = _stripMat.AlbedoColor;
                sc.A = alpha * 0.12f;
                _stripMat.AlbedoColor = sc;
            }
            if (_fadeTimer <= 0.0f)
            {
                QueueFree();
            }
        }
    }
}
