using System;
using Godot;
using FPSGame.VFX.Impacts;

namespace FPSGame.Weapons.Projectiles;

/// <summary>
/// Physical M67 Frag Grenade entity.
/// Simulates authentic ballistics with continuous collision detection, ground bouncing,
/// aerodynamic tumbling, and a precise 3.0-second timed fuse before detonating.
/// </summary>
public partial class GrenadeProjectile : RigidBody3D
{
    public event Action<GrenadeProjectile>? Exploded;

    public long ShooterPeerId { get; set; } = 1;

    private float _fuseTimer = 3.0f;
    private bool _hasDetonated;
    private Node3D? _modelInstance;
    private CollisionShape3D? _colShape;

    public GrenadeProjectile()
    {
        Mass = 0.40f; // 400g realistic M67 weight
        ContinuousCd = true;
    }

    public override void _Ready()
    {
        Mass = 0.40f; // 400g realistic M67 weight
        ContinuousCd = true; // Prevents tunneling through high-velocity collisions
        CollisionLayer = 64; // Layer 7: Projectiles_Player
        CollisionMask = 1 | 2; // Layer 1 (World_Static) | Layer 2 (World_Dynamic)

        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Bounce = 0.35f,
            Friction = 0.65f
        };

        // Collision shape (8.5cm sphere matching M67 body)
        _colShape = new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = 0.055f }
        };
        AddChild(_colShape);

        // Visual model
        var scene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/grenades/M67V1_Rigged.glb");
        if (scene != null)
        {
            _modelInstance = scene.Instantiate<Node3D>();
            _modelInstance.Name = "M67_Model";
            _modelInstance.Scale = Vector3.One;
            AddChild(_modelInstance);
        }
    }

    public void Launch(Vector3 origin, Vector3 impulseVelocity, Vector3 spinVelocity, long shooterId)
    {
        _fuseTimer = 3.0f;
        _hasDetonated = false;
        ShooterPeerId = shooterId;

        Visible = true;
        Freeze = false;
        Sleeping = false;
        if (_colShape != null) _colShape.Disabled = false;
        CollisionLayer = 64;
        CollisionMask = 1 | 2;

        Transform3D newTransform = new Transform3D(Basis.Identity, origin);
        if (IsInsideTree())
        {
            GlobalTransform = newTransform;
            // Directly update physics server state to guarantee wake-up and correct velocity injection
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.Transform, newTransform);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.LinearVelocity, impulseVelocity);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.AngularVelocity, spinVelocity);
        }
        else
        {
            Position = origin;
        }

        LinearVelocity = impulseVelocity;
        AngularVelocity = spinVelocity;
        SetPhysicsProcess(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_hasDetonated) return;

        float dt = (float)delta;
        _fuseTimer -= dt;

        if (_fuseTimer <= 0.0f)
        {
            Detonate();
        }
    }

    private void Detonate()
    {
        if (_hasDetonated) return;
        _hasDetonated = true;

        Vector3 detonatePos = IsInsideTree() ? GlobalPosition : Position;
        Freeze = true;
        Visible = false;
        SetPhysicsProcess(false);
        SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0);
        SetDeferred(CollisionObject3D.PropertyName.CollisionMask, 0);
        if (_colShape != null)
        {
            _colShape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        }
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;

        if (IsInsideTree())
        {
            GlobalPosition = new Vector3(0.0f, -9999.0f, 0.0f);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.LinearVelocity, Vector3.Zero);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.AngularVelocity, Vector3.Zero);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.Transform, new Transform3D(Basis.Identity, new Vector3(0.0f, -9999.0f, 0.0f)));
        }

        if (ExplosionManager.Instance != null)
        {
            ExplosionManager.Instance.TriggerExplosion(detonatePos, ShooterPeerId, "Frag Grenade");
        }

        Exploded?.Invoke(this);
    }
}
