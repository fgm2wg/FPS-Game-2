using Godot;

namespace FPSGame.Weapons.Hitscan;

/// <summary>
/// High-speed visual bullet tracer.
/// Travels from weapon muzzle to impact target at high velocity, rendering a luminous trail.
/// </summary>
public partial class BulletTracer : Node3D
{
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private Vector3 _direction;
    private float _speed = 260.0f;
    private float _totalDistance;
    private float _traveledDistance;
    private float _lifetime;
    private MeshInstance3D? _tracerMesh;

    public static BulletTracer Create(Vector3 start, Vector3 target, float speed = 260.0f)
    {
        var tracer = new BulletTracer();
        tracer._startPosition = start;
        tracer._targetPosition = target;
        tracer._speed = speed;
        return tracer;
    }

    public override void _Ready()
    {
        GlobalPosition = _startPosition;
        _direction = (_targetPosition - _startPosition);
        _totalDistance = _direction.Length();

        if (_totalDistance < 0.1f)
        {
            QueueFree();
            return;
        }

        _direction /= _totalDistance; // Normalized

        // Align tracer orientation along flight trajectory
        LookAt(_targetPosition, Vector3.Up);

        // Build luminous tracer geometry (stretched capsule)
        var mesh = new CylinderMesh
        {
            TopRadius = 0.012f,
            BottomRadius = 0.012f,
            Height = 0.85f,
            RadialSegments = 6,
            Rings = 1
        };

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.92f, 0.4f, 0.95f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        _tracerMesh = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            // Cylinder is along Y by default, rotate 90° on X to point along -Z
            Rotation = new Vector3(Mathf.DegToRad(90.0f), 0, 0),
            Position = new Vector3(0, 0, -0.42f)
        };

        AddChild(_tracerMesh);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _lifetime += dt;

        float step = _speed * dt;
        _traveledDistance += step;
        GlobalPosition += _direction * step;

        if (_traveledDistance >= _totalDistance || _lifetime > 0.8f)
        {
            SpawnImpactSpark(_targetPosition);
            QueueFree();
        }
    }

    private void SpawnImpactSpark(Vector3 pos)
    {
        var tree = GetTree();
        if (tree == null) return;

        var spark = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.8f, 0.3f),
            LightEnergy = 1.8f,
            OmniRange = 1.5f
        };
        tree.Root.AddChild(spark);
        spark.GlobalPosition = pos;

        var timer = tree.CreateTimer(0.06);
        timer.Timeout += () =>
        {
            if (IsInstanceValid(spark))
            {
                spark.QueueFree();
            }
        };
    }
}
