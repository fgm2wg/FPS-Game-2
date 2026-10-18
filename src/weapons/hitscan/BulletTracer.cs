using System;
using Godot;

namespace FPSGame.Weapons.Hitscan;

/// <summary>
/// High-speed visual bullet tracer.
/// Travels from weapon muzzle to impact target at supersonic velocity, rendering a luminous trail.
/// Optimized with shared static mesh and material for zero heap allocations per shot.
/// </summary>
public partial class BulletTracer : Node3D
{
    private static readonly CylinderMesh SharedMesh = new()
    {
        TopRadius = 0.014f,
        BottomRadius = 0.014f,
        Height = 1.10f,
        RadialSegments = 6,
        Rings = 1
    };

    private static readonly StandardMaterial3D SharedMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(1.0f, 0.90f, 0.40f, 0.95f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha
    };

    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private Vector3 _direction;
    private float _speed = 650.0f;
    private float _totalDistance;
    private float _traveledDistance;
    private float _lifetime;
    private float _maxLifetime;
    private MeshInstance3D? _tracerMesh;

    public static BulletTracer Create(Vector3 start, Vector3 target, float speed = 650.0f)
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
        _direction = _targetPosition - _startPosition;
        _totalDistance = _direction.Length();

        if (_totalDistance < 0.1f)
        {
            QueueFree();
            return;
        }

        _direction /= _totalDistance; // Normalized
        _maxLifetime = Mathf.Max(0.12f, (_totalDistance / _speed) + 0.08f);

        // Align tracer orientation along flight trajectory
        Vector3 up = Vector3.Up;
        if (Mathf.Abs(_direction.Dot(up)) > 0.98f) up = Vector3.Right;
        LookAt(_targetPosition, up);

        _tracerMesh = new MeshInstance3D
        {
            Mesh = SharedMesh,
            MaterialOverride = SharedMaterial,
            // Cylinder is along Y by default, rotate 90° on X to point along -Z (flight forward)
            Rotation = new Vector3(Mathf.DegToRad(90.0f), 0, 0),
            Position = new Vector3(0, 0, -0.55f)
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

        if (_traveledDistance >= _totalDistance || _lifetime >= _maxLifetime)
        {
            QueueFree();
        }
    }
}
