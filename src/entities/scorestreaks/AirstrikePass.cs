using System;
using Godot;
using FPSGame.VFX.Impacts;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// Dual A-10 Thunderbolt II strafe run scorestreak.
/// Spawns two A-10s executing tandem diving gun runs 3.0s apart.
/// Flies at safe altitude above terrain peak, smoothly dips nose down to aim front GAU-8 gun at target,
/// fires high-velocity visible 30mm tracer rounds with wide spread for 1.5s, smoothly pulls up,
/// and executes tandem egress with Jet 1 veering left and Jet 2 veering right.
/// </summary>
public partial class AirstrikePass : Node3D
{
    private class A10Jet
    {
        public int Index;
        public Node3D Root = null!;
        public Node3D Model = null!;
        public AudioStreamPlayer3D Audio = null!;
        public float Delay;
        public float Elapsed;
        public bool HasFiredAudio;
        public bool IsFiring;
        public float FireTimer;
        public bool IsFinished;
    }

    private readonly A10Jet[] _jets = new A10Jet[2];
    private Vector3 _strikeCenter;
    private Vector3 _flightDir;
    private long _callerPeerId;
    private StrikeTargetMarker? _targetMarker;

    // Cinematic flight parameters (~116 knots, extended approach and egress)
    private const float Speed = 60.0f;
    private const float ApproachDist = 800.0f;
    private const float EgressDist = 850.0f;

    private float _approachHeight = 220.0f;
    private float _egressHeight = 220.0f;
    private float _peakHeight = 110.0f;

    private const float StrafeDuration = 1.5f; // Exactly 1.5s firing window
    private const float ShotInterval = 0.080f; // ~12-13 shots/sec per jet
    private const float TargetRadius = 9.5f;

    private static PackedScene? _a10Scene;
    private static AudioStream? _a10Audio;
    private PhysicsRayQueryParameters3D? _cachedGroundQuery;
    private bool _initialized;

    public void Initialize(Vector3 strikeCenter, Vector3 flightDir, long callerPeerId, StrikeTargetMarker? marker)
    {
        _strikeCenter = strikeCenter;
        _flightDir = new Vector3(flightDir.X, 0.0f, flightDir.Z).Normalized();
        if (_flightDir.LengthSquared() < 0.01f) _flightDir = Vector3.Forward;
        _callerPeerId = callerPeerId;
        _targetMarker = marker;
        _initialized = true;

        if (IsInsideTree())
        {
            Setup();
        }
    }

    public override void _Ready()
    {
        if (_initialized)
        {
            Setup();
        }
    }

    private void Setup()
    {
        if (_jets[0] != null) return;

        if (_targetMarker == null)
        {
            _targetMarker = new StrikeTargetMarker { Name = "AirstrikeMarker", TargetRadius = TargetRadius, TopLevel = true };
            AddChild(_targetMarker);
            _targetMarker.Position = _strikeCenter;
        }
        _targetMarker.SetAutoRemove(5.0f, 1.0f);

        _a10Scene ??= ScorestreakManager.A10Scene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/a10.glb");
        _a10Audio ??= ScorestreakManager.A10Audio ?? ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/a10_strafe.mp3");

        // Maintain safe altitude as high as the C-17 cargo plane (~200m+ above sea level)
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        _peakHeight = ScorestreakManager.GetMapPeakHeight(tree);
        _approachHeight = Mathf.Max(_peakHeight + 110.0f, _strikeCenter.Y + 160.0f);
        _egressHeight = _approachHeight;

        _cachedGroundQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1 | 2);

        for (int i = 0; i < 2; i++)
        {
            var jet = new A10Jet
            {
                Index = i,
                Delay = i * 3.0f, // Second plane strikes exactly 3.0s behind the first
                Elapsed = 0.0f,
                HasFiredAudio = false,
                IsFiring = false,
                FireTimer = 0.0f,
                IsFinished = false,
                Root = new Node3D { Name = $"A10_Jet_{i + 1}" }
            };

            AddChild(jet.Root);

            if (_a10Scene != null)
            {
                jet.Model = _a10Scene.Instantiate<Node3D>();
                jet.Model.Name = "ModelVisual";
                // A-10 model is 10x scale; scale 0.10f and rotate +90 around Y so nose points forward along flight vector
                jet.Model.Scale = Vector3.One * 0.10f;
                jet.Model.RotationDegrees = new Vector3(0.0f, 90.0f, 0.0f);
                jet.Root.AddChild(jet.Model);
            }

            jet.Audio = new AudioStreamPlayer3D
            {
                Name = "JetAudio",
                Stream = _a10Audio,
                UnitSize = 50.0f,
                MaxDb = 0.0f,
                MaxDistance = 1100.0f,
                Bus = "Master",
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                AttenuationFilterCutoffHz = 4500.0f,
                AttenuationFilterDb = -18.0f
            };
            jet.Root.AddChild(jet.Audio);

            _jets[i] = jet;
            UpdateJetTransform(jet, -jet.Delay);
        }
    }

    private Vector3 ComputeJetPosition(int jetIndex, float d)
    {
        const float dipStartD = -560.0f;
        const float strafeStartD = -430.0f;
        const float strafeEndD = -340.0f;
        const float egressLevelD = 180.0f;
        const float egressStartD = 0.0f;

        // Shallow dive parameters:
        // Strafe run: -430m to -340m (90m distance = 1.50s at 60m/s).
        // Dive slope mDive = -0.38 (~20.8 degrees shallow dive angle).
        const float mDive = -0.38f;
        float yEnd = Mathf.Max(_peakHeight + 40.0f, _strikeCenter.Y + 130.0f);
        float yFire = yEnd - mDive * (strafeEndD - strafeStartD);
        float cruiseAlt = yFire + 30.0f;

        float height;
        if (d < dipStartD)
        {
            // Phase 1: High level cruise approach
            height = cruiseAlt;
        }
        else if (d < strafeStartD)
        {
            // Phase 2: Smooth C1 Hermite curve dipping nose down from level cruise (slope 0) to dive (slope mDive)
            float u = (d - dipStartD) / (strafeStartD - dipStartD);
            float deltaD = strafeStartD - dipStartD;
            float m1 = mDive * deltaD;
            height = (2.0f * u * u * u - 3.0f * u * u + 1.0f) * cruiseAlt
                   + (u * u * u - u * u) * m1
                   + (-2.0f * u * u * u + 3.0f * u * u) * yFire;
        }
        else if (d <= strafeEndD)
        {
            // Phase 3: Shallow linear dive directly facing and aiming at the target
            height = yFire + mDive * (d - strafeStartD);
        }
        else if (d < egressLevelD)
        {
            // Phase 4: Smooth C1 Hermite pull-up out of dive into climbing egress up to cruise altitude
            float u = (d - strafeEndD) / (egressLevelD - strafeEndD);
            float deltaD = egressLevelD - strafeEndD;
            float m0 = mDive * deltaD;
            height = (2.0f * u * u * u - 3.0f * u * u + 1.0f) * yEnd
                   + (u * u * u - 2.0f * u * u + u) * m0
                   + (-2.0f * u * u * u + 3.0f * u * u) * cruiseAlt;
        }
        else
        {
            // Phase 5: High level cruise egress
            height = cruiseAlt;
        }

        Vector3 rightVec = _flightDir.Cross(Vector3.Up).Normalized();
        Vector3 horizontalPos = _strikeCenter + _flightDir * d;

        if (d > egressStartD)
        {
            float distPast = d - egressStartD;
            float veerDir = jetIndex == 0 ? -1.0f : 1.0f;
            float veerProgress = Mathf.Clamp(distPast / 420.0f, 0.0f, 1.0f);
            float smoothVeer = veerProgress * veerProgress * (3.0f - 2.0f * veerProgress);
            float lateralOffset = distPast * 0.45f * smoothVeer * veerDir;
            horizontalPos += rightVec * lateralOffset;
        }

        return new Vector3(horizontalPos.X, height, horizontalPos.Z);
    }

    private void UpdateJetTransform(A10Jet jet, float t)
    {
        float targetTime = ApproachDist / Speed;
        float distFromTarget = (t - targetTime) * Speed;
        float d = distFromTarget;

        Vector3 pos = ComputeJetPosition(jet.Index, d);
        jet.Root.Position = pos;

        // Compute forward flight trajectory orientation with forward lookahead
        // Velocity vector and nose orientation are strictly aligned for realistic flight
        Vector3 nextPos = ComputeJetPosition(jet.Index, d + 2.0f);
        Vector3 fwd = (nextPos - pos).Normalized();

        if (fwd.LengthSquared() > 0.001f)
        {
            Vector3 up = Vector3.Up;
            if (Mathf.Abs(fwd.Dot(up)) > 0.98f) up = Vector3.Forward;
            Basis baseBasis = Basis.LookingAt(fwd, up);

            // Compute bank roll angle during egress turn
            const float egressStartD = 0.0f;
            if (d > egressStartD)
            {
                float distPast = d - egressStartD;
                float veerProgress = Mathf.Clamp(distPast / 420.0f, 0.0f, 1.0f);
                float bankFactor = Mathf.Sin(veerProgress * Mathf.Pi);
                float veerDir = jet.Index == 0 ? -1.0f : 1.0f;
                // Jet 0 veering left (veerDir = -1) banks left; Jet 1 veering right (veerDir = +1) banks right
                float bankAngle = veerDir * Mathf.DegToRad(26.0f) * bankFactor;
                baseBasis = baseBasis.Rotated(fwd, bankAngle);
            }

            jet.Root.Basis = baseBasis;
        }
    }

    public override void _Process(double delta)
    {
        if (!_initialized || _jets[0] == null) return;

        float dt = (float)delta;
        bool allFinished = true;

        float targetTime = ApproachDist / Speed;
        // Strafe run: exactly -430m to -340m (1.50s at 60m/s)
        float fireStartTime = targetTime - (430.0f / Speed);
        float fireEndTime = targetTime - (340.0f / Speed);

        for (int i = 0; i < _jets.Length; i++)
        {
            var jet = _jets[i];
            if (jet.IsFinished) continue;

            jet.Elapsed += dt;
            float t = jet.Elapsed - jet.Delay;

            if (t < -0.5f)
            {
                allFinished = false;
                jet.Root.Visible = false;
                continue;
            }

            jet.Root.Visible = true;
            UpdateJetTransform(jet, t);

            // 1. Audio trigger: plays immediately as firing run begins
            if (t >= fireStartTime && !jet.HasFiredAudio && jet.Audio != null && jet.Audio.IsInsideTree())
            {
                jet.HasFiredAudio = true;
                jet.Audio.Play();
            }

            // 2. 30mm gun firing window (1.50 seconds)
            if (t >= fireStartTime && t <= fireEndTime)
            {
                jet.IsFiring = true;
                jet.FireTimer -= dt;
                if (jet.FireTimer <= 0.0f)
                {
                    jet.FireTimer = ShotInterval;
                    FireStrafeRound(jet);
                }
            }
            else
            {
                jet.IsFiring = false;
            }

            // 3. Audio egress fadeout as jet climbs and veers away
            float distFromTarget = (t - targetTime) * Speed;
            if (distFromTarget > 200.0f && jet.Audio != null)
            {
                float fadeFraction = Mathf.Clamp((distFromTarget - 200.0f) / 450.0f, 0.0f, 1.0f);
                jet.Audio.VolumeDb = Mathf.Lerp(0.0f, -42.0f, fadeFraction);
            }

            // 4. Check egress completion
            if (distFromTarget >= EgressDist)
            {
                jet.IsFinished = true;
                jet.Root.Visible = false;
            }
            else
            {
                allFinished = false;
            }
        }

        if (allFinished)
        {
            if (IsInstanceValid(_targetMarker))
            {
                _targetMarker.FadeOutAndFree(1.0f);
            }
            QueueFree();
        }
    }

    private void FireStrafeRound(A10Jet jet)
    {
        // Spread across target circle
        float angle = (float)GD.RandRange(0.0, Mathf.Tau);
        float radius = (float)GD.RandRange(0.0, TargetRadius);
        Vector3 groundTarget = _strikeCenter + new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);

        // Raycast down to query exact terrain/obstacle height
        var space = GetWorld3D()?.DirectSpaceState;
        if (space != null && _cachedGroundQuery != null)
        {
            _cachedGroundQuery.From = groundTarget + Vector3.Up * 35.0f;
            _cachedGroundQuery.To = groundTarget + Vector3.Down * 35.0f;
            var hit = space.IntersectRay(_cachedGroundQuery);
            if (hit.Count > 0)
            {
                groundTarget = (Vector3)hit["position"];
            }
        }

        // Muzzle position at the nose of the A-10 (GAU-8 Avenger gun port)
        Vector3 fwd = -jet.Root.GlobalBasis.Z;
        Vector3 up = jet.Root.GlobalBasis.Y;
        Vector3 muzzlePos = jet.Root.GlobalPosition + fwd * 8.5f - up * 0.35f;

        // Launch visible high-velocity 30mm tracer round flying down to ground
        var tracer = new A10Tracer30mm();
        GetTree().Root.AddChild(tracer);
        tracer.Initialize(muzzlePos, groundTarget, _callerPeerId);
    }
}

/// <summary>
/// Visible high-velocity 30mm tracer round fired by A-10 Thunderbolt II.
/// Renders an unshaded, luminous emissive tracer traveling at 440 m/s towards ground target,
/// detonating a photorealistic scaled explosion upon impact.
/// </summary>
public partial class A10Tracer30mm : Node3D
{
    private Vector3 _start;
    private Vector3 _target;
    private long _callerPeerId;
    private float _totalDuration;
    private float _elapsed;

    public void Initialize(Vector3 start, Vector3 target, long callerPeerId)
    {
        _start = start;
        _target = target;
        _callerPeerId = callerPeerId;

        if (IsInsideTree())
        {
            GlobalPosition = start;
        }
        else
        {
            Position = start;
        }

        float dist = (target - start).Length();
        const float speed = 440.0f; // m/s
        _totalDuration = Mathf.Max(0.04f, dist / speed);
        _elapsed = 0.0f;

        Vector3 dir = (target - start).Normalized();
        if (dir.LengthSquared() > 0.001f)
        {
            Vector3 up = Vector3.Up;
            if (Mathf.Abs(dir.Dot(up)) > 0.98f) up = Vector3.Right;
            if (IsInsideTree())
            {
                GlobalBasis = Basis.LookingAt(dir, up);
            }
            else
            {
                Basis = Basis.LookingAt(dir, up);
            }
        }

        // Visual tracer mesh: bright luminous cylinder aligned with flight vector
        _sharedCylinderMesh ??= new CylinderMesh
        {
            TopRadius = 0.12f,
            BottomRadius = 0.12f,
            Height = 2.6f,
            RadialSegments = 6
        };

        _sharedTracerMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.85f, 0.35f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.70f, 0.20f),
            EmissionEnergyMultiplier = 6.0f
        };

        var meshInst = new MeshInstance3D
        {
            Name = "TracerMesh",
            Mesh = _sharedCylinderMesh,
            MaterialOverride = _sharedTracerMat,
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f)
        };
        AddChild(meshInst);
    }

    private static CylinderMesh? _sharedCylinderMesh;
    private static StandardMaterial3D? _sharedTracerMat;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _elapsed += dt;

        float progress = Mathf.Clamp(_elapsed / _totalDuration, 0.0f, 1.0f);
        GlobalPosition = _start.Lerp(_target, progress);

        if (progress >= 1.0f)
        {
            // Impact: trigger photorealistic scaled ground explosion with blast damage
            ExplosionManager.Instance?.TriggerScaledExplosion(_target, _callerPeerId, 0.55f, 4.8f, 75.0f);
            QueueFree();
        }
    }
}
