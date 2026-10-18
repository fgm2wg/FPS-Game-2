using System;
using Godot;
using Godot.Collections;
using FPSGame.VFX.Impacts;

namespace FPSGame.Weapons.Projectiles;

/// <summary>
/// Physical 40mm High-Explosive Dual-Purpose (HEDP) grenade projectile for the MGL.
/// Simulates authentic low-velocity ballistics (~76 m/s muzzle velocity, realistic gravity arc),
/// aerodynamics, swept collision detection to prevent tunneling, and instant detonation
/// upon impact with terrain, obstacles, or combatants.
/// </summary>
public partial class MglProjectile : RigidBody3D
{
    public event Action<MglProjectile>? Exploded;

    public long ShooterPeerId { get; set; } = 1;

    private float _lifetime;
    private const float MaxLifetime = 8.0f;
    private bool _hasDetonated;
    private Vector3 _prevGlobalPos;
    private PhysicsRayQueryParameters3D? _sweepQuery;
    private readonly Array<Rid> _excludedRids = new();
    private CollisionShape3D? _colShape;

    public MglProjectile()
    {
        Mass = 0.23f; // 230g authentic 40x46mm M433 HEDP grenade weight
        ContinuousCd = true;
    }

    public override void _Ready()
    {
        Mass = 0.23f;
        ContinuousCd = true;
        ContactMonitor = true;
        MaxContactsReported = 4;

        // Layer 7: Projectiles_Player
        CollisionLayer = 64;
        // Collide with Layer 1 (World_Static), 2 (World_Dynamic), 3 (Player_Body), 4 (Enemy_Body),
        // 5 (Player_Hitbox), 6 (Enemy_Hitbox), 16 (Environment_Destructible)
        CollisionMask = 1 | 2 | 4 | 8 | 16 | 32 | 32768;

        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Bounce = 0.05f,
            Friction = 0.85f
        };

        // Collision sphere (diameter ~45mm matching 40mm caliber)
        _colShape = new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = 0.025f }
        };
        AddChild(_colShape);

        BuildVisualRound();

        // Setup pre-allocated swept raycast query for guaranteed zero-tunneling
        _excludedRids.Clear();
        _excludedRids.Add(GetRid());
        _sweepQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, CollisionMask);
        _sweepQuery.CollideWithAreas = true;
        _sweepQuery.CollideWithBodies = true;
        _sweepQuery.Exclude = _excludedRids;

        BodyEntered += OnBodyEntered;
    }

    private void BuildVisualRound()
    {
        var visualRoot = new Node3D { Name = "40mm_Visual" };
        AddChild(visualRoot);

        // Cylindrical body casing (brass / bronze)
        var bodyMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.58f, 0.28f),
            Metallic = 0.85f,
            Roughness = 0.25f
        };

        var bodyMesh = new MeshInstance3D
        {
            Name = "Casing",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.020f,
                BottomRadius = 0.020f,
                Height = 0.055f,
                RadialSegments = 16
            },
            MaterialOverride = bodyMat,
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
            Position = new Vector3(0.0f, 0.0f, 0.015f)
        };
        visualRoot.AddChild(bodyMesh);

        // Aerodynamic ogive warhead cap (olive drab military finish)
        var capMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.24f, 0.30f, 0.20f),
            Metallic = 0.45f,
            Roughness = 0.35f
        };

        var capMesh = new MeshInstance3D
        {
            Name = "Warhead",
            Mesh = new SphereMesh
            {
                Radius = 0.020f,
                Height = 0.035f,
                RadialSegments = 16,
                Rings = 8
            },
            MaterialOverride = capMat,
            Position = new Vector3(0.0f, 0.0f, -0.020f)
        };
        visualRoot.AddChild(capMesh);

        // Gold rotating band
        var bandMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.72f, 0.18f),
            Metallic = 0.95f,
            Roughness = 0.20f
        };

        var bandMesh = new MeshInstance3D
        {
            Name = "DrivingBand",
            Mesh = new TorusMesh
            {
                InnerRadius = 0.019f,
                OuterRadius = 0.0215f,
                RingSegments = 12,
                Rings = 8
            },
            MaterialOverride = bandMat,
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
            Position = new Vector3(0.0f, 0.0f, 0.025f)
        };
        visualRoot.AddChild(bandMesh);

        // Subtle flight smoke puff / vapor tracer trail
        var trailParticles = new GpuParticles3D
        {
            Name = "SmokeTracer",
            Amount = 24,
            Lifetime = 0.35,
            Explosiveness = 0.0f,
            Randomness = 0.2f,
            LocalCoords = false,
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth
        };

        var trailMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.90f, 0.88f, 0.82f, 0.28f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles
        };

        var quad = new QuadMesh { Size = new Vector2(0.06f, 0.06f), Material = trailMat };
        trailParticles.DrawPass1 = quad;

        var procMat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 0, 1),
            Spread = 12.0f,
            InitialVelocityMin = 0.5f,
            InitialVelocityMax = 1.5f,
            Gravity = new Vector3(0, 0.4f, 0),
            ScaleMin = 0.5f,
            ScaleMax = 1.2f
        };
        trailParticles.ProcessMaterial = procMat;
        trailParticles.Position = new Vector3(0.0f, 0.0f, 0.045f);
        visualRoot.AddChild(trailParticles);
    }

    public void Launch(Vector3 origin, Vector3 velocity, long shooterId)
    {
        _lifetime = 0.0f;
        _hasDetonated = false;
        ShooterPeerId = shooterId;

        Visible = true;
        Freeze = false;
        Sleeping = false;
        if (_colShape != null) _colShape.Disabled = false;
        CollisionLayer = 64;
        CollisionMask = 1 | 2 | 4 | 8 | 16 | 32 | 32768;

        Vector3 fwd = velocity.LengthSquared() > 0.01f ? velocity.Normalized() : -Vector3.Back;
        Vector3 up = Mathf.Abs(fwd.Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up;
        Transform3D newTransform = new Transform3D(Basis.LookingAt(fwd, up), origin);

        if (IsInsideTree())
        {
            GlobalTransform = newTransform;
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.Transform, newTransform);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.LinearVelocity, velocity);
            PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.AngularVelocity, Vector3.Zero);
        }
        else
        {
            Position = origin;
        }

        LinearVelocity = velocity;
        AngularVelocity = Vector3.Zero;
        _prevGlobalPos = origin;
        SetPhysicsProcess(true);
    }

    private void OnBodyEntered(Node body)
    {
        if (_hasDetonated) return;
        Vector3 hitPos = IsInsideTree() ? GlobalPosition : _prevGlobalPos;
        Detonate(hitPos);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_hasDetonated) return;

        float dt = (float)delta;
        _lifetime += dt;

        Vector3 curPos = IsInsideTree() ? GlobalPosition : _prevGlobalPos;

        // Sub-tick continuous swept raycast to prevent tunneling at high velocity
        if (_sweepQuery != null && IsInsideTree() && (_prevGlobalPos - curPos).LengthSquared() > 0.001f)
        {
            var spaceState = GetWorld3D()?.DirectSpaceState;
            if (spaceState != null)
            {
                _sweepQuery.From = _prevGlobalPos;
                _sweepQuery.To = curPos;
                var hit = spaceState.IntersectRay(_sweepQuery);
                if (hit.Count > 0)
                {
                    Vector3 hitPos = (Vector3)hit["position"];
                    Detonate(hitPos);
                    return;
                }
            }
        }

        _prevGlobalPos = curPos;

        // Orient projectile along aerodynamic flight velocity
        if (LinearVelocity.LengthSquared() > 1.0f)
        {
            Vector3 fwd = LinearVelocity.Normalized();
            Vector3 up = Mathf.Abs(fwd.Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up;
            GlobalBasis = Basis.LookingAt(fwd, up);
        }

        // Safety timeout fuse (e.g. fired straight up or off-map)
        if (_lifetime >= MaxLifetime || curPos.Y < -350.0f)
        {
            Detonate(curPos);
        }
    }

    public void Detonate(Vector3 impactPoint)
    {
        if (_hasDetonated) return;
        _hasDetonated = true;

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
            ExplosionManager.Instance.TriggerExplosion(impactPoint, ShooterPeerId, "MGL");
        }

        Exploded?.Invoke(this);
    }
}
