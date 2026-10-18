using System;
using Godot;
using FPSGame.Entities.Player.Controllers;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// MQ-1 Predator UAV Reconnaissance Drone scorestreak.
/// Loiters in an orbital recon circle above the combat zone for 60 seconds (silent, zero sound effects).
/// Highlights all enemy combatants with an unshaded yellow tactical ESP border visible through terrain and walls.
/// </summary>
public partial class UavDrone : Node3D
{
    public long CallerPeerId { get; set; } = 1;
    public int TeamId { get; set; } = 1;

    private Node3D? _droneModel;
    private Vector3 _orbitCenter;
    private float _orbitAngle;
    private float _lifetime = 60.0f;
    private bool _isEgressing;
    private Vector3 _egressDirection;

    private const float OrbitRadius = 150.0f;
    private float _orbitAltitude = 185.0f;
    private const float OrbitSpeed = 18.0f; // Slower, realistic recon loitering
    private const float BankAngleDeg = 10.0f;

    private static PackedScene? _uavScene;

    private bool _initialized;

    public void Initialize(Vector3 spawnCenter, long callerPeerId, int teamId)
    {
        _orbitCenter = spawnCenter;
        CallerPeerId = callerPeerId;
        TeamId = teamId;
        _lifetime = 60.0f;
        _isEgressing = false;
        _orbitAngle = (float)GD.RandRange(0.0, Mathf.Tau);
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
        if (_droneModel != null) return;

        var tree = (IsInsideTree() ? GetTree() : null) ?? Engine.GetMainLoop() as SceneTree;
        float peak = ScorestreakManager.GetMapPeakHeight(tree);
        _orbitAltitude = Mathf.Max(peak + 75.0f, _orbitCenter.Y + 75.0f);

        _uavScene ??= ScorestreakManager.UavScene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/mq1_uav.glb");
        if (_uavScene != null)
        {
            _droneModel = _uavScene.Instantiate<Node3D>();
            _droneModel.Name = "ModelVisual";
            _droneModel.Scale = Vector3.One * 1.30f;
            _droneModel.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f); // Rotate 180 to face forward along flight direction
            AddChild(_droneModel);
        }

        UpdateDroneTransform(0.0f);
        ApplyEspHighlightToEnemies(true);
    }

    public override void _Process(double delta)
    {
        if (!_initialized || _droneModel == null) return;

        float dt = (float)delta;

        if (!_isEgressing)
        {
            _lifetime -= dt;

            // Advance orbit angle
            float angularSpeed = OrbitSpeed / OrbitRadius;
            _orbitAngle += angularSpeed * dt;

            UpdateDroneTransform(dt);

            // Re-apply highlight periodically (handles newly respawned players)
            ApplyEspHighlightToEnemies(true);

            if (_lifetime <= 0.0f)
            {
                StartEgress();
            }
        }
        else
        {
            // Fly straight into horizon to despawn
            GlobalPosition += _egressDirection * (OrbitSpeed * 2.2f) * dt;
            _lifetime -= dt;
            if (_lifetime <= -10.0f)
            {
                QueueFree();
            }
        }
    }

    private void UpdateDroneTransform(float dt)
    {
        float posX = _orbitCenter.X + Mathf.Cos(_orbitAngle) * OrbitRadius;
        float posZ = _orbitCenter.Z + Mathf.Sin(_orbitAngle) * OrbitRadius;
        float posY = _orbitAltitude;

        GlobalPosition = new Vector3(posX, posY, posZ);

        // Velocity tangent vector along orbit circle
        Vector3 tangent = new Vector3(-Mathf.Sin(_orbitAngle), 0.0f, Mathf.Cos(_orbitAngle)).Normalized();

        Vector3 up = Vector3.Up;
        // Bank into the turn
        Basis baseBasis = Basis.LookingAt(tangent, up);
        // Roll inward towards circle center
        baseBasis = baseBasis.Rotated(tangent, -Mathf.DegToRad(BankAngleDeg));
        GlobalBasis = baseBasis;
    }

    private void StartEgress()
    {
        _isEgressing = true;
        // Remove ESP outline from enemies
        ApplyEspHighlightToEnemies(false);

        // Fly out tangentially
        _egressDirection = new Vector3(-Mathf.Sin(_orbitAngle), 0.15f, Mathf.Cos(_orbitAngle)).Normalized();
    }

    private void ApplyEspHighlightToEnemies(bool enabled)
    {
        var tree = GetTree();
        if (tree == null) return;

        var players = tree.GetNodesInGroup("Players");
        if (players.Count == 0 && tree.CurrentScene != null)
        {
            players = tree.CurrentScene.FindChildren("*", nameof(PlayerController), true, false);
        }

        foreach (Node p in players)
        {
            if (p is PlayerController pc && !pc.IsDead)
            {
                // Highlight enemies (opposite team)
                bool isEnemy = pc.TeamId != TeamId;
                if (isEnemy)
                {
                    pc.SetEspHighlight(enabled);
                }
            }
        }
    }

    public override void _ExitTree()
    {
        ApplyEspHighlightToEnemies(false);
    }
}
