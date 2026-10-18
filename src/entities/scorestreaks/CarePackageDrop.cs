using System;
using Godot;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// C-17 Globemaster airdrop scorestreak delivery sequence.
/// Spawns a C-17 transport aircraft high above, flies across the sky playing heavy turbofan flyby audio,
/// releases the supply crate accurately over the designated landing zone, and egresses smoothly into the horizon.
/// </summary>
public partial class CarePackageDrop : Node3D
{
    private Node3D? _planeRoot;
    private AudioStreamPlayer3D? _planeAudio;
    private Vector3 _dropTarget;
    private Vector3 _flightDir;
    private long _crateId;
    private long _callerPeerId;
    private int _teamId;
    private StrikeTargetMarker? _targetMarker;

    private const float Speed = 95.0f; // ~185 knots
    private const float ApproachDist = 380.0f;
    private const float EgressDist = 550.0f;
    private float _altitude = 195.0f;

    private float _elapsed;
    private bool _hasDroppedCrate;
    private bool _isComplete;
    private bool _hasPlayedAudio;
    private bool _initialized;

    private static PackedScene? _c17Scene;
    private static AudioStream? _c17Audio;

    public void Initialize(long crateId, Vector3 dropTarget, Vector3 flightDir, long callerPeerId, int teamId, StrikeTargetMarker? marker)
    {
        _crateId = crateId;
        _dropTarget = dropTarget;
        _flightDir = new Vector3(flightDir.X, 0.0f, flightDir.Z).Normalized();
        if (_flightDir.LengthSquared() < 0.01f) _flightDir = Vector3.Forward;
        _callerPeerId = callerPeerId;
        _teamId = teamId;
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

        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        float peak = ScorestreakManager.GetMapPeakHeight(tree);
        _altitude = Mathf.Max(peak + 85.0f, _dropTarget.Y + 85.0f);

        if (_targetMarker == null)
        {
            _targetMarker = new StrikeTargetMarker { Name = "CarePackageMarker", TargetRadius = 2.0f, TopLevel = true };
            AddChild(_targetMarker);
            _targetMarker.Position = _dropTarget;
        }
        _targetMarker.SetAutoRemove(5.0f, 1.0f);

        _c17Scene ??= ScorestreakManager.C17Scene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/c17.glb");
        _c17Audio ??= ScorestreakManager.C17Audio ?? ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/c17_flyby.wav");

        _planeRoot = new Node3D { Name = "C17_Plane" };
        AddChild(_planeRoot);

        if (_c17Scene != null)
        {
            var model = _c17Scene.Instantiate<Node3D>();
            model.Name = "ModelVisual";
            model.Scale = Vector3.One * 0.80f;
            model.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f); // Rotate 180 to face forward along flight path
            _planeRoot.AddChild(model);
        }

        _planeAudio = new AudioStreamPlayer3D
        {
            Name = "C17Audio",
            Stream = _c17Audio,
            UnitSize = 50.0f,
            MaxDb = 0.0f,
            MaxDistance = 1100.0f,
            Bus = "Master",
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            AttenuationFilterCutoffHz = 4000.0f,
            AttenuationFilterDb = -18.0f
        };
        _planeRoot.AddChild(_planeAudio);

        UpdatePlanePosition(0.0f);
    }

    private void UpdatePlanePosition(float t)
    {
        if (_planeRoot == null) return;

        float dropTime = ApproachDist / Speed;
        float distFromTarget = (t - dropTime) * Speed;

        Vector3 pos = new Vector3(
            _dropTarget.X + _flightDir.X * distFromTarget,
            _altitude,
            _dropTarget.Z + _flightDir.Z * distFromTarget
        );
        _planeRoot.Position = pos;

        // Face forward along flight direction
        Vector3 forward = _flightDir;
        Vector3 up = Vector3.Up;
        _planeRoot.Basis = Basis.LookingAt(forward, up);
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

        float dropTime = ApproachDist / Speed;
        UpdatePlanePosition(_elapsed);

        // Check if aircraft is directly above the designated drop point
        if (_elapsed >= dropTime && !_hasDroppedCrate)
        {
            _hasDroppedCrate = true;
            ReleaseSupplyCrate();
        }

        // Fade out plane audio during egress
        float distFromDrop = (_elapsed - dropTime) * Speed;
        if (distFromDrop > 180.0f && _planeAudio != null)
        {
            float fade = Mathf.Clamp((distFromDrop - 180.0f) / 320.0f, 0.0f, 1.0f);
            _planeAudio.VolumeDb = Mathf.Lerp(0.0f, -36.0f, fade);
        }

        // Despawn aircraft once past egress horizon
        if (distFromDrop >= EgressDist)
        {
            _isComplete = true;
            QueueFree();
        }
    }

    private void ReleaseSupplyCrate()
    {
        var crate = new CarePackageCrate
        {
            Name = $"CarePackageCrate_{_crateId}",
            CrateNetId = _crateId,
            CallerPeerId = _callerPeerId,
            TeamId = _teamId
        };
        crate.AddToGroup("CarePackageCrates");

        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        tree?.Root?.AddChild(crate);
        Vector3 releasePos = new Vector3(_dropTarget.X, _altitude - 5.0f, _dropTarget.Z);
        crate.DropFromAltitude(releasePos);

        // Target marker fades out smoothly as crate deploys (if still around)
        if (IsInstanceValid(_targetMarker))
        {
            _targetMarker.FadeOutAndFree(1.0f);
        }
    }
}
