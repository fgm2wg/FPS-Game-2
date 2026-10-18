using System;
using Godot;
using FPSGame.Entities.Player.Controllers;

namespace FPSGame.VFX.Impacts;

/// <summary>
/// Cinematic, photorealistic explosion visual & acoustic effect.
/// Features a multi-stage explosion:
/// 1. Blinding incandescent omni-light flash with rapid exponential decay.
/// 2. Fast-expanding refractive shockwave ring.
/// 3. Volumetric rolling fireball with smooth noise texture and soft proximity depth fading (never blocky).
/// 4. Heavy dark billowing smoke column rising with buoyancy and atmospheric drag.
/// 5. Stretched incandescent shrapnel sparks shooting outward under gravity.
/// 6. Jagged tumbling soil & rock debris fragments (using non-box irregular geometry).
/// 7. Charred ground blast crater decal.
/// 8. Spatial 3D seismic explosion audio.
/// 9. Distance-attenuated camera trauma impulse for visceral screen shake.
/// </summary>
public partial class ExplosionEffect : Node3D
{
    public event Action<ExplosionEffect>? Finished;

    private OmniLight3D _flashLight = null!;
    private MeshInstance3D _shockwave = null!;
    private StandardMaterial3D _shockwaveMat = null!;
    private GpuParticles3D _fireballParticles = null!;
    private GpuParticles3D _smokeParticles = null!;
    private GpuParticles3D _sparksParticles = null!;
    private GpuParticles3D _debrisParticles = null!;
    private Decal _scorchDecal = null!;
    private AudioStreamPlayer3D _audioPlayer = null!;

    private float _lifeTimer;
    private float _shockwaveTimer;
    private float _lightEnergy;
    private float _scorchAlpha = 0.9f;
    private bool _isPlaying;

    public override void _Ready()
    {
        BuildExplosionRigs();
    }

    private void BuildExplosionRigs()
    {
        if (_flashLight != null) return;

        ExplosionAssetGenerator.EnsureAssetsExist();

        // 1. Blast light flash
        _flashLight = new OmniLight3D
        {
            Name = "FlashLight",
            LightColor = new Color(1.0f, 0.72f, 0.35f),
            LightEnergy = 0.0f,
            OmniRange = 18.0f,
            ShadowEnabled = false // Keep shadows disabled for max performance and zero stutter
        };
        AddChild(_flashLight);

        // 2. Shockwave expanding pressure ring
        var shockwaveMesh = new TorusMesh
        {
            InnerRadius = 0.85f,
            OuterRadius = 1.0f,
            Rings = 32,
            RingSegments = 16
        };
        _shockwaveMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1.0f, 0.85f, 0.55f, 0.85f)
        };
        _shockwave = new MeshInstance3D
        {
            Name = "ShockwaveRing",
            Mesh = shockwaveMesh,
            MaterialOverride = _shockwaveMat,
            Visible = false
        };
        AddChild(_shockwave);

        // Shared soft smoke texture
        var smokeTex = ExplosionAssetGenerator.GetSmokeTexture();
        var sparkTex = ExplosionAssetGenerator.GetSparkTexture();
        var scorchTex = ExplosionAssetGenerator.GetScorchTexture();

        // 3. Fireball Particles (Soft rolling incandescent cloud - ZERO blockiness)
        var fireballMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = smokeTex,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            ProximityFadeEnabled = true,
            ProximityFadeDistance = 0.45f // Smoothly blends into ground/walls without harsh intersection lines
        };

        var fireballProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.6f,
            Direction = Vector3.Up,
            Spread = 180.0f,
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 7.0f,
            LinearAccelMin = -3.0f,
            LinearAccelMax = -1.0f,
            DampingMin = 4.0f,
            DampingMax = 6.0f,
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -45.0f,
            AngularVelocityMax = 45.0f,
            ScaleMin = 1.0f,
            ScaleMax = 2.4f
        };

        // Fireball color ramp: white flash -> vivid orange -> deep ember red -> transparent soot
        var fireballGradient = new Gradient();
        fireballGradient.SetColor(0, new Color(1.0f, 1.0f, 0.95f, 1.0f));
        fireballGradient.AddPoint(0.18f, new Color(1.0f, 0.65f, 0.15f, 0.95f));
        fireballGradient.AddPoint(0.50f, new Color(0.85f, 0.25f, 0.05f, 0.80f));
        fireballGradient.AddPoint(0.80f, new Color(0.18f, 0.10f, 0.08f, 0.40f));
        fireballGradient.AddPoint(1.00f, new Color(0.08f, 0.05f, 0.05f, 0.0f));
        fireballProcess.ColorRamp = new GradientTexture1D { Gradient = fireballGradient };

        var fireballCurve = new Curve();
        fireballCurve.AddPoint(new Vector2(0.0f, 0.4f), 0.0f, 2.5f);
        fireballCurve.AddPoint(new Vector2(0.35f, 1.0f), 0.8f, -0.3f);
        fireballCurve.AddPoint(new Vector2(1.0f, 1.5f), 0.4f, 0.0f);
        fireballProcess.ScaleCurve = new CurveTexture { Curve = fireballCurve };

        _fireballParticles = new GpuParticles3D
        {
            Name = "FireballParticles",
            Amount = 16,
            Lifetime = 0.70f,
            OneShot = true,
            Explosiveness = 0.97f,
            ProcessMaterial = fireballProcess,
            DrawPass1 = new QuadMesh { Size = new Vector2(1.6f, 1.6f), Material = fireballMat }
        };
        AddChild(_fireballParticles);

        // 4. Volumetric Smoke Column (Dark churning soot rising with buoyancy)
        var smokeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = smokeTex,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            ProximityFadeEnabled = true,
            ProximityFadeDistance = 0.60f
        };

        var smokeProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.8f,
            Direction = Vector3.Up,
            Spread = 45.0f,
            InitialVelocityMin = 4.5f,
            InitialVelocityMax = 8.5f,
            Gravity = new Vector3(0.0f, 0.85f, 0.0f), // Buoyancy: hot air lifts smoke upward
            DampingMin = 2.8f,
            DampingMax = 4.2f,
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -30.0f,
            AngularVelocityMax = 30.0f,
            ScaleMin = 1.2f,
            ScaleMax = 2.6f
        };

        var smokeGradient = new Gradient();
        smokeGradient.SetColor(0, new Color(0.12f, 0.10f, 0.09f, 0.85f));
        smokeGradient.AddPoint(0.25f, new Color(0.20f, 0.18f, 0.16f, 0.75f));
        smokeGradient.AddPoint(0.65f, new Color(0.35f, 0.32f, 0.29f, 0.45f));
        smokeGradient.AddPoint(1.00f, new Color(0.45f, 0.42f, 0.38f, 0.0f));
        smokeProcess.ColorRamp = new GradientTexture1D { Gradient = smokeGradient };

        var smokeScaleCurve = new Curve();
        smokeScaleCurve.AddPoint(new Vector2(0.0f, 0.5f), 0.0f, 2.2f);
        smokeScaleCurve.AddPoint(new Vector2(0.4f, 1.2f), 0.8f, 0.4f);
        smokeScaleCurve.AddPoint(new Vector2(1.0f, 2.2f), 0.5f, 0.0f);
        smokeProcess.ScaleCurve = new CurveTexture { Curve = smokeScaleCurve };

        _smokeParticles = new GpuParticles3D
        {
            Name = "SmokeParticles",
            Amount = 20,
            Lifetime = 3.2f,
            OneShot = true,
            Explosiveness = 0.88f,
            ProcessMaterial = smokeProcess,
            DrawPass1 = new QuadMesh { Size = new Vector2(2.0f, 2.0f), Material = smokeMat }
        };
        AddChild(_smokeParticles);

        // 5. Shower of Incandescent Shrapnel Sparks
        var sparksMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = sparkTex,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true
        };

        var sparksProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f,
            Direction = Vector3.Up,
            Spread = 160.0f,
            InitialVelocityMin = 18.0f,
            InitialVelocityMax = 32.0f,
            Gravity = new Vector3(0.0f, -15.0f, 0.0f),
            DampingMin = 1.5f,
            DampingMax = 3.0f,
            ScaleMin = 0.3f,
            ScaleMax = 0.7f,
            ParticleFlagAlignY = true
        };

        var sparksGradient = new Gradient();
        sparksGradient.SetColor(0, new Color(1.0f, 1.0f, 0.85f, 1.0f));
        sparksGradient.AddPoint(0.4f, new Color(1.0f, 0.75f, 0.20f, 0.95f));
        sparksGradient.AddPoint(0.8f, new Color(0.95f, 0.30f, 0.05f, 0.65f));
        sparksGradient.AddPoint(1.0f, new Color(0.40f, 0.05f, 0.00f, 0.0f));
        sparksProcess.ColorRamp = new GradientTexture1D { Gradient = sparksGradient };

        _sparksParticles = new GpuParticles3D
        {
            Name = "SparksParticles",
            Amount = 24,
            Lifetime = 1.15f,
            OneShot = true,
            Explosiveness = 0.98f,
            ProcessMaterial = sparksProcess,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.08f, 0.32f), Material = sparksMat }
        };
        AddChild(_sparksParticles);

        // 6. Jagged Flying Rock & Dirt Debris (Irregular 3D geometry - NEVER blocky cubes!)
        var rockMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            AlbedoColor = new Color(0.24f, 0.20f, 0.17f),
            Roughness = 0.9f
        };

        // Realistic jagged triangular wedge fragment
        var rockMesh = new PrismMesh
        {
            Size = new Vector3(0.18f, 0.15f, 0.12f),
            LeftToRight = 0.3f,
            Material = rockMat
        };

        var debrisProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.4f,
            Direction = Vector3.Up,
            Spread = 65.0f,
            InitialVelocityMin = 10.0f,
            InitialVelocityMax = 20.0f,
            Gravity = new Vector3(0.0f, -22.0f, 0.0f),
            DampingMin = 0.8f,
            DampingMax = 1.8f,
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -180.0f,
            AngularVelocityMax = 180.0f,
            ScaleMin = 0.6f,
            ScaleMax = 1.6f
        };

        _debrisParticles = new GpuParticles3D
        {
            Name = "DebrisParticles",
            Amount = 10,
            Lifetime = 1.6f,
            OneShot = true,
            Explosiveness = 0.98f,
            ProcessMaterial = debrisProcess,
            DrawPass1 = rockMesh
        };
        AddChild(_debrisParticles);

        // 7. Charred Ground Scorch Decal
        _scorchDecal = new Decal
        {
            Name = "ScorchDecal",
            Size = new Vector3(4.5f, 2.0f, 4.5f),
            TextureAlbedo = scorchTex,
            Modulate = new Color(0.08f, 0.07f, 0.06f, 0.90f),
            LowerFade = 0.4f,
            UpperFade = 0.4f,
            Visible = false
        };
        AddChild(_scorchDecal);

        // 8. Spatial Audio Player
        var explosionStream = ExplosionAssetGenerator.GetExplosionAudio();
        _audioPlayer = new AudioStreamPlayer3D
        {
            Name = "AudioPlayer",
            Stream = explosionStream,
            VolumeDb = -1.0f, // Clean level without clipping
            UnitSize = 35.0f, // 0-35m full visceral blast punch; drops smoothly over distance
            MaxDb = 0.0f, // Clamps point-blank volume to prevent digital clipping/crispiness
            MaxDistance = 750.0f, // Clearly audible across full battlefield (spawns 300m away)
            Bus = "Master",
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            AttenuationFilterCutoffHz = 4000.0f, // Distant low-pass atmospheric air absorption
            AttenuationFilterDb = -18.0f
        };
        AddChild(_audioPlayer);
    }

    /// <summary>
    /// Triggers the explosion at the specified world position.
    /// Supports lightweight 30mm strafe mode (isStrafe = true) which disables dynamic omni lights,
    /// decals, and torus shockwaves to guarantee smooth 60+ FPS during rapid A-10 strafe runs.
    /// </summary>
    public void Explode(Vector3 worldPosition, bool playAudio = true, bool isStrafe = false)
    {
        if (_flashLight == null)
        {
            BuildExplosionRigs();
        }

        if (IsInsideTree())
        {
            GlobalPosition = worldPosition;
        }
        else
        {
            Position = worldPosition;
        }

        _isPlaying = true;

        if (isStrafe)
        {
            // Rapid 30mm High-Explosive Incendiary (HEI) impact:
            // 0.50s total lifetime recycles effects immediately,
            // dropping concurrently active explosions from 38 down to ~4-5.
            _lifeTimer = 0.50f;
            _shockwaveTimer = 0.0f;
            _lightEnergy = 0.0f;

            // Completely eliminate Forward+ light clustering stalls and decal fill-rate overdraw
            if (_flashLight != null) _flashLight.Visible = false;
            if (_shockwave != null) _shockwave.Visible = false;
            if (_scorchDecal != null) _scorchDecal.Visible = false;

            // Emit crisp 30mm explosive particle bursts
            _fireballParticles.Restart();
            _fireballParticles.Emitting = true;

            _smokeParticles.Restart();
            _smokeParticles.Emitting = true;

            _sparksParticles.Restart();
            _sparksParticles.Emitting = true;

            _debrisParticles.Restart();
            _debrisParticles.Emitting = true;

            if (playAudio && _audioPlayer != null)
            {
                _audioPlayer.PitchScale = (float)GD.RandRange(1.15, 1.35);
                _audioPlayer.Play();
            }

            ApplyCameraTrauma(worldPosition, isStrafe: true);
        }
        else
        {
            // Full grenade / MGL / standard explosion
            _lifeTimer = 1.9f;
            _shockwaveTimer = 0.28f;
            _scorchAlpha = 0.90f;

            // 1. Light flash
            _lightEnergy = 16.0f;
            if (_flashLight != null)
            {
                _flashLight.LightEnergy = _lightEnergy;
                _flashLight.Visible = true;
            }

            // 2. Shockwave
            if (_shockwave != null)
            {
                _shockwave.Visible = true;
                _shockwave.Scale = new Vector3(0.3f, 0.3f, 0.3f);
                _shockwaveMat.AlbedoColor = new Color(1.0f, 0.88f, 0.55f, 0.85f);
            }

            // 3. Emit particle bursts
            _fireballParticles.Restart();
            _fireballParticles.Emitting = true;

            _smokeParticles.Restart();
            _smokeParticles.Emitting = true;

            _sparksParticles.Restart();
            _sparksParticles.Emitting = true;

            _debrisParticles.Restart();
            _debrisParticles.Emitting = true;

            // 4. Scorch decal
            if (_scorchDecal != null)
            {
                _scorchDecal.Visible = true;
                _scorchDecal.Modulate = new Color(0.08f, 0.07f, 0.06f, _scorchAlpha);
            }

            // 5. Play spatial explosion sound with pitch scaling:
            // Heavy 2,000 lb bombs (Scale.X > 1.5) get deep bass rumble; grenades get punchy crack
            if (playAudio && _audioPlayer != null)
            {
                if (Scale.X > 1.5f)
                {
                    _audioPlayer.PitchScale = (float)GD.RandRange(0.70, 0.82);
                }
                else
                {
                    _audioPlayer.PitchScale = (float)GD.RandRange(0.95, 1.05);
                }
                _audioPlayer.Play();
            }

            ApplyCameraTrauma(worldPosition, isStrafe: false);
        }
    }

    private void ApplyCameraTrauma(Vector3 blastOrigin, bool isStrafe = false)
    {
        if (!IsInsideTree()) return;
        var tree = GetTree();
        if (tree == null) return;

        var players = tree.GetNodesInGroup("Players");
        foreach (Node node in players)
        {
            if (node is PlayerController player && player.IsLocalPlayer && !player.IsDead)
            {
                float dist = (player.GlobalPosition - blastOrigin).Length();
                float maxDist = isStrafe ? 18.0f : (Scale.X > 1.5f ? 75.0f : 35.0f);
                if (dist < maxDist)
                {
                    float factor = 1.0f - (dist / maxDist);
                    float trauma = isStrafe ? factor * 0.15f : (Scale.X > 1.5f ? factor * 1.35f : factor);
                    player.ApplyExplosionCameraShake(trauma);
                }
                break;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_isPlaying) return;

        float dt = (float)delta;
        _lifeTimer -= dt;

        // 1. Rapid exponential light decay
        if (_lightEnergy > 0.0f)
        {
            _lightEnergy = Mathf.MoveToward(_lightEnergy, 0.0f, 75.0f * dt);
            if (_flashLight != null)
            {
                _flashLight.LightEnergy = _lightEnergy;
                if (_lightEnergy <= 0.0f)
                {
                    _flashLight.Visible = false;
                }
            }
        }

        // 2. Fast-expanding shockwave ring
        if (_shockwaveTimer > 0.0f)
        {
            _shockwaveTimer -= dt;
            float progress = 1.0f - (_shockwaveTimer / 0.28f);
            float scale = Mathf.Lerp(0.3f, 11.0f, progress);
            if (_shockwave != null)
            {
                _shockwave.Scale = new Vector3(scale, 0.4f, scale);
                float alpha = (1.0f - progress) * 0.85f;
                _shockwaveMat.AlbedoColor = new Color(1.0f, 0.88f, 0.55f, alpha);

                if (_shockwaveTimer <= 0.0f)
                {
                    _shockwave.Visible = false;
                }
            }
        }

        // 3. Complete and return to pool
        if (_lifeTimer <= 0.0f)
        {
            _isPlaying = false;
            if (_scorchDecal != null) _scorchDecal.Visible = false;
            if (_shockwave != null) _shockwave.Visible = false;
            if (_flashLight != null) _flashLight.Visible = false;
            if (_fireballParticles != null) _fireballParticles.Emitting = false;
            if (_smokeParticles != null) _smokeParticles.Emitting = false;
            if (_sparksParticles != null) _sparksParticles.Emitting = false;
            if (_debrisParticles != null) _debrisParticles.Emitting = false;
            Scale = Vector3.One;
            Finished?.Invoke(this);
        }
    }
}
