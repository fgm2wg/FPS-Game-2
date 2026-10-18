using System;
using Godot;
using FPSGame.VFX.Impacts;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// B-52 Stratofortress heavy carpet bombing run scorestreak.
/// Spawns a massive B-52 bomber cruising at high altitude (~195m+ above sea level),
/// roaring across the sky playing heavy jet flyby audio.
/// Drops 20 US MK-84 2,000 lb LDGP bombs sequentially along the 120m tactical corridor,
/// calculated with true ballistic parabolic arcs to impact precisely within the designated swath.
/// </summary>
public partial class BombingRunPass : Node3D
{
    private Node3D? _planeRoot;
    private AudioStreamPlayer3D? _planeAudio;
    private Vector3 _strikeCenter;
    private Vector3 _flightDir;
    private long _callerPeerId;
    private BombingRunMarker? _targetMarker;

    // Heavy bomber flight parameters
    private const float Speed = 80.0f; // ~155 knots
    private const float ApproachDist = 880.0f;
    private const float EgressDist = 980.0f;
    private float _altitude = 195.0f;

    // Ordinance drop parameters (40 bombs across 220m corridor)
    private const int TotalBombs = 40;
    private const float CorridorLength = 220.0f; // -110m to +110m along flight axis
    private float _leadDistance;
    private float _dropStartDist;
    private float _dropEndDist;
    private float _bombIntervalDist;
    private int _bombsDropped;

    private float _elapsed;
    private bool _isComplete;
    private bool _hasPlayedAudio;
    private bool _initialized;

    private static PackedScene? _b52Scene;
    private static AudioStream? _flybyAudio;

    public void Initialize(Vector3 strikeCenter, Vector3 flightDir, long callerPeerId, BombingRunMarker? marker)
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
        if (_planeRoot != null) return;

        // Cruise safe above map peak altitude, matching C-17 cargo plane
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        float peak = ScorestreakManager.GetMapPeakHeight(tree);
        _altitude = Mathf.Max(peak + 95.0f, _strikeCenter.Y + 95.0f);

        // Marker setup
        if (_targetMarker == null)
        {
            _targetMarker = new BombingRunMarker { Name = "BombingRunMarker", TopLevel = true };
            AddChild(_targetMarker);
            _targetMarker.Position = _strikeCenter;
            _targetMarker.SetHeading(_flightDir);
        }
        _targetMarker.SetAutoRemove(5.0f, 1.0f);

        // Preload assets
        _b52Scene ??= ScorestreakManager.B52Scene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/b52.glb");
        _flybyAudio ??= ScorestreakManager.C17Audio ?? ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/c17_flyby.mp3");

        // Calculate ballistic lead compensation:
        // Free fall time under gravity g = 18 m/s^2 from cruise altitude to strike center elevation
        float hFall = Mathf.Max(20.0f, _altitude - _strikeCenter.Y);
        float tFall = Mathf.Sqrt(2.0f * hFall / 18.0f);
        _leadDistance = Speed * tFall;

        // Corridor is from +110m to -110m relative to strike center.
        // Moving from +ApproachDist towards -EgressDist in direction of current orientation (-_flightDir).
        // Therefore bomber must release bombs between (+110 + lead) and (-110 + lead).
        _dropStartDist = (CorridorLength * 0.5f) + _leadDistance;
        _dropEndDist = (-CorridorLength * 0.5f) + _leadDistance;
        _bombIntervalDist = (_dropStartDist - _dropEndDist) / (TotalBombs - 1);

        // Spawn B-52 Root
        _planeRoot = new Node3D { Name = "B52_Bomber" };
        AddChild(_planeRoot);

        if (_b52Scene != null)
        {
            var model = _b52Scene.Instantiate<Node3D>();
            model.Name = "ModelVisual";
            // 1:1 real-world scale (56m wingspan). Rotate 180 so nose faces forward along flight direction.
            model.Scale = Vector3.One;
            model.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
            _planeRoot.AddChild(model);
        }

        // Heavy jet flyby audio with distant spatial propagation
        _planeAudio = new AudioStreamPlayer3D
        {
            Name = "B52Audio",
            Stream = _flybyAudio,
            UnitSize = 65.0f,
            MaxDb = 0.0f,
            MaxDistance = 1600.0f,
            Bus = "Master",
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            AttenuationFilterCutoffHz = 3800.0f,
            AttenuationFilterDb = -18.0f
        };
        _planeRoot.AddChild(_planeAudio);

        UpdatePlaneTransform(ApproachDist);
    }

    private void UpdatePlaneTransform(float distFromCenter)
    {
        if (_planeRoot == null) return;

        Vector3 planePos = new Vector3(
            _strikeCenter.X + _flightDir.X * distFromCenter,
            _altitude,
            _strikeCenter.Z + _flightDir.Z * distFromCenter
        );
        _planeRoot.Position = planePos;

        // Orient aircraft forward along flight direction
        _planeRoot.Basis = Basis.LookingAt(_flightDir, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (!_initialized || _planeRoot == null || _isComplete) return;

        float dt = (float)delta;
        _elapsed += dt;

        if (!_hasPlayedAudio && _planeAudio != null && _planeAudio.IsInsideTree())
        {
            _hasPlayedAudio = true;
            _planeAudio.Play();
        }

        float currentDist = ApproachDist - _elapsed * Speed;
        UpdatePlaneTransform(currentDist);

        // Check ordinance drop sequence (while loop ensures no dropped frames skip bombs)
        while (_bombsDropped < TotalBombs && currentDist <= _dropStartDist - _bombsDropped * _bombIntervalDist)
        {
            DropNextBomb();
        }

        // Egress audio attenuation fade
        if (currentDist < -400.0f && _planeAudio != null)
        {
            float egressProgress = Mathf.Clamp((-currentDist - 400.0f) / (EgressDist - 400.0f), 0.0f, 1.0f);
            _planeAudio.VolumeDb = Mathf.Lerp(0.0f, -40.0f, egressProgress);
        }

        // Clean despawn once past egress horizon
        if (currentDist <= -EgressDist)
        {
            _isComplete = true;
            QueueFree();
        }
    }

    private void DropNextBomb()
    {
        _bombsDropped++;
        if (_planeRoot == null) return;

        // Spawn position: bomb bay underneath B-52 fuselage
        // Add subtle lateral variation across the 20m corridor for realistic carpet bombing pattern
        Vector3 rightVec = _flightDir.Cross(Vector3.Up).Normalized();
        float lateralOffset = (float)GD.RandRange(-7.0, 7.0);
        Vector3 spawnPos = _planeRoot.GlobalPosition + new Vector3(0.0f, -4.5f, 0.0f) + rightVec * lateralOffset;

        // Forward velocity inherited from B-52 + slight downward push from bomb bay release
        Vector3 bombVelocity = -_flightDir * Speed + new Vector3(0.0f, -3.0f, 0.0f);

        var bomb = new Mk84Bomb();
        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        tree?.Root?.AddChild(bomb);
        bomb.Launch(spawnPos, bombVelocity, _callerPeerId);
    }
}
