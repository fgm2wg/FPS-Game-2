using System;
using Godot;
using FPSGame.VFX.Impacts;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// Ballistic simulation of the US 2,000 lb MK-84 Low-Drag General-Purpose (LDGP) bomb.
/// Features high-fidelity aerodynamic ballistic arc:
/// - Inherits forward velocity from the B-52 Stratofortress.
/// - Accelerates under gravity, pitching nose down along its instantaneous velocity vector.
/// - Continuous swept raycast collision detection against terrain and structures.
/// - Massive 2,000 lb high-explosive detonation with devastating blast radius, seismic screen shake, and deep rumble.
/// </summary>
public partial class Mk84Bomb : Node3D
{
    private Node3D? _modelRoot;
    private Vector3 _velocity;
    private long _callerPeerId;
    private bool _detonated;
    private float _lifetime;
    private PhysicsRayQueryParameters3D? _rayQuery;
    private Godot.Collections.Array<Rid>? _excludedRids;

    private static PackedScene? _mk84Scene;

    public void Launch(Vector3 spawnPos, Vector3 initialVelocity, long callerPeerId)
    {
        GlobalPosition = spawnPos;
        _velocity = initialVelocity;
        _callerPeerId = callerPeerId;
        _detonated = false;
        _lifetime = 0.0f;

        _mk84Scene ??= ScorestreakManager.Mk84Scene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/mk84.glb");

        if (_mk84Scene != null)
        {
            _modelRoot = _mk84Scene.Instantiate<Node3D>();
            _modelRoot.Name = "ModelVisual";
            // 1:1 scale (3.87m length). Nose flipped 180 to face forward along flight trajectory.
            _modelRoot.Scale = Vector3.One;
            _modelRoot.RotationDegrees = new Vector3(0.0f, 90.0f, 0.0f);
            AddChild(_modelRoot);
        }

        if (_velocity.LengthSquared() > 0.1f)
        {
            GlobalBasis = Basis.LookingAt(_velocity.Normalized(), Vector3.Up);
        }

        _rayQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1 | 2);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_detonated) return;
        float dt = (float)delta;
        _lifetime += dt;

        // Gravity acceleration (18.0 m/s^2 for heavy fast-falling aerial ordinance)
        _velocity.Y -= 18.0f * dt;

        Vector3 curPos = GlobalPosition;
        Vector3 nextPos = curPos + _velocity * dt;

        // Smooth aerodynamic pitch: orient bomb nose directly along flight velocity vector
        if (_velocity.LengthSquared() > 1.0f)
        {
            Vector3 fwd = _velocity.Normalized();
            Vector3 up = Mathf.Abs(fwd.Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up;
            GlobalBasis = Basis.LookingAt(fwd, up);
        }

        // Swept raycast query against World_Static (1) and World_Dynamic (2)
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState != null && _rayQuery != null)
        {
            _rayQuery.From = curPos;
            _rayQuery.To = nextPos;

            while (true)
            {
                var hit = spaceState.IntersectRay(_rayQuery);
                if (hit.Count == 0) break;

                var collider = hit["collider"].AsGodotObject();
                if (collider is Node node && (node.Name.ToString().Contains("Boundary", StringComparison.OrdinalIgnoreCase) || node.GetParent()?.Name.ToString().Contains("Boundar", StringComparison.OrdinalIgnoreCase) == true))
                {
                    // Ignore invisible map boundary walls: add to exclusion and continue raycast
                    _excludedRids ??= new Godot.Collections.Array<Rid>();
                    if (hit.TryGetValue("rid", out var ridVal))
                    {
                        _excludedRids.Add(ridVal.AsRid());
                        _rayQuery.Exclude = _excludedRids;
                    }
                    continue;
                }

                Vector3 hitPos = (Vector3)hit["position"];
                Detonate(hitPos);
                return;
            }
        }

        GlobalPosition = nextPos;

        // Safety bounds
        if (GlobalPosition.Y < -80.0f || _lifetime > 14.0f)
        {
            Detonate(GlobalPosition);
        }
    }

    private void Detonate(Vector3 impactPos)
    {
        if (_detonated) return;
        _detonated = true;

        // Massive 2,000 lb high-explosive detonation
        ExplosionManager.Instance?.TriggerBombExplosion(impactPos, _callerPeerId, visualScale: 3.2f, damageRadius: 24.0f, maxDamage: 450.0f);

        QueueFree();
    }
}
