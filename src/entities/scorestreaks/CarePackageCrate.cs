using System;
using Godot;
using FPSGame.Entities.Player.Controllers;
using FPSGame.UI.HUD;

namespace FPSGame.Entities.Scorestreaks;

/// <summary>
/// Airdropped supply crate physical entity.
/// Manages parachute descent, ground touchdown, parachute detachment,
/// and interactive hold-to-claim capture progression awarding random scorestreaks.
/// </summary>
public partial class CarePackageCrate : CharacterBody3D
{
    public long CrateNetId { get; set; }
    public long CallerPeerId { get; set; } = 1;
    public int TeamId { get; set; } = 1;

    private Node3D? _crateModel;
    private Node3D? _chuteRoot;
    private CollisionShape3D? _colShape;
    private AudioStreamPlayer3D? _audioPlayer;

    private bool _isChuteOpen;
    private bool _hasLanded;
    private bool _isClaimed;
    private float _fallTimer;
    private float _chuteScale = 3.5f;

    // Capture interaction state
    private const float RequiredHoldTime = 1.8f;
    private float _currentHoldProgress;
    private PlayerController? _capturingPlayer;

    private static PackedScene? _crateScene;
    private static PackedScene? _chuteScene;
    private static AudioStream? _radioConfirmAudio;

    public override void _Ready()
    {
        _crateScene ??= ScorestreakManager.CrateScene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/supply_crate.glb");
        _chuteScene ??= ScorestreakManager.ChuteScene ?? ResourceLoader.Load<PackedScene>("res://assets/models/scorestreaks/parachute.glb");
        _radioConfirmAudio ??= ScorestreakManager.RadioAudio ?? ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/radio_confirm.mp3");

        // Physics body setup
        CollisionLayer = 2; // World_Dynamic
        CollisionMask = 1 | 2; // Collides with World_Static and World_Dynamic

        _colShape = new CollisionShape3D
        {
            Name = "CrateCollider",
            Shape = new BoxShape3D { Size = new Vector3(1.0f, 1.0f, 1.0f) },
            Position = new Vector3(0.0f, 0.5f, 0.0f)
        };
        AddChild(_colShape);

        // Visual crate
        if (_crateScene != null)
        {
            _crateModel = _crateScene.Instantiate<Node3D>();
            _crateModel.Name = "CrateVisual";
            AddChild(_crateModel);
        }

        // Parachute canopy: position cords connected to top of supply crate
        if (_chuteScene != null)
        {
            _chuteRoot = new Node3D { Name = "ChuteRoot" };
            var chuteModel = _chuteScene.Instantiate<Node3D>();
            chuteModel.Scale = Vector3.One * _chuteScale;
            // Position canopy ~4.5m above crate center so harness cords attach to top of crate
            chuteModel.Position = new Vector3(0.0f, 4.5f, 0.0f);
            _chuteRoot.AddChild(chuteModel);
            _chuteRoot.Visible = false;
            AddChild(_chuteRoot);
        }

        _audioPlayer = new AudioStreamPlayer3D
        {
            Name = "CrateAudio",
            Stream = _radioConfirmAudio,
            UnitSize = 15.0f,
            MaxDistance = 100.0f,
            Bus = "Master"
        };
        AddChild(_audioPlayer);
    }

    public void DropFromAltitude(Vector3 spawnPosition)
    {
        GlobalPosition = spawnPosition;
        Velocity = new Vector3(0.0f, -6.0f, 0.0f);
        _hasLanded = false;
        _isChuteOpen = false;
        _fallTimer = 0.0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (!_hasLanded)
        {
            _fallTimer += dt;

            // Open chute after 1.0 second of freefall
            if (_fallTimer >= 1.0f && !_isChuteOpen)
            {
                _isChuteOpen = true;
                if (_chuteRoot != null) _chuteRoot.Visible = true;
            }

            Vector3 vel = Velocity;
            if (_isChuteOpen)
            {
                // Terminal descent speed under parachute (~9.5 m/s)
                vel.Y = Mathf.MoveToward(vel.Y, -9.5f, 18.0f * dt);
                if (_chuteRoot != null)
                {
                    // Gentle wind sway
                    float sway = Mathf.Sin(_fallTimer * 2.8f) * 0.06f;
                    _chuteRoot.Rotation = new Vector3(sway, 0.0f, sway * 0.5f);
                }
            }
            else
            {
                // Freefall under gravity
                vel.Y -= 9.81f * dt;
            }

            Velocity = vel;
            MoveAndSlide();

            // Check if touchdown occurred
            if (IsOnFloor() || CheckTouchdown(out Vector3 groundPoint, out Vector3 groundNormal))
            {
                Touchdown();
            }
        }
        else
        {
            // Process player capture interaction
            ProcessCaptureInteraction(dt);
        }
    }

    private bool CheckTouchdown(out Vector3 groundPoint, out Vector3 groundNormal)
    {
        groundPoint = GlobalPosition;
        groundNormal = Vector3.Up;

        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;

        // Raycast down through bottom of crate
        var query = PhysicsRayQueryParameters3D.Create(
            GlobalPosition + Vector3.Up * 0.8f,
            GlobalPosition + Vector3.Down * 0.8f,
            1 | 2
        );
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

        var hit = space.IntersectRay(query);
        if (hit.Count > 0)
        {
            groundPoint = (Vector3)hit["position"];
            groundNormal = ((Vector3)hit["normal"]).Normalized();

            // Check if bottom of crate is at or very close to surface
            float diffY = GlobalPosition.Y - groundPoint.Y;
            if (diffY <= 0.35f && diffY >= -0.30f)
            {
                return true;
            }
        }

        return false;
    }

    private void Touchdown()
    {
        _hasLanded = true;
        Velocity = Vector3.Zero;

        // Query ground position and normal directly beneath crate to snap flush and angle to slope
        var space = GetWorld3D()?.DirectSpaceState;
        if (space != null)
        {
            var query = PhysicsRayQueryParameters3D.Create(
                GlobalPosition + Vector3.Up * 1.5f,
                GlobalPosition + Vector3.Down * 3.0f,
                1 | 2
            );
            query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

            var hit = space.IntersectRay(query);
            if (hit.Count > 0)
            {
                Vector3 groundPoint = (Vector3)hit["position"];
                Vector3 groundNormal = ((Vector3)hit["normal"]).Normalized();

                // Snap crate bottom flush onto terrain
                GlobalPosition = groundPoint;

                // Orient crate basis to match the terrain slope normal
                Vector3 currentForward = -GlobalBasis.Z;
                if (Mathf.Abs(currentForward.Dot(groundNormal)) > 0.95f)
                {
                    currentForward = Vector3.Forward;
                }
                Vector3 right = currentForward.Cross(groundNormal).Normalized();
                if (right.LengthSquared() < 0.001f)
                {
                    right = Vector3.Right.Cross(groundNormal).Normalized();
                }
                Vector3 forward = groundNormal.Cross(right).Normalized();
                GlobalBasis = new Basis(right, groundNormal, -forward).Orthonormalized();
            }
        }

        // Parachute detaches and disappears
        if (_chuteRoot != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_chuteRoot, "scale", Vector3.Zero, 0.45f);
            tween.TweenCallback(Callable.From(() =>
            {
                if (IsInstanceValid(_chuteRoot))
                {
                    _chuteRoot.QueueFree();
                    _chuteRoot = null;
                }
            }));
        }
    }

    private void ProcessCaptureInteraction(float dt)
    {
        var tree = GetTree();
        if (tree == null) return;

        Vector3 crateCenter = GlobalPosition + Vector3.Up * 0.5f;

        // Find nearest living local player in interaction range looking at the crate
        PlayerController? localPlayer = null;
        var players = tree.GetNodesInGroup("Players");
        if (players.Count == 0 && tree.CurrentScene != null)
        {
            players = tree.CurrentScene.FindChildren("*", nameof(PlayerController), true, false);
        }

        foreach (Node p in players)
        {
            if (p is PlayerController pc && pc.IsLocalPlayer && !pc.IsDead)
            {
                Vector3 camPos = pc.Camera?.GlobalPosition ?? (pc.GlobalPosition + Vector3.Up * 1.6f);
                Vector3 toCrate = crateCenter - camPos;
                float dist = toCrate.Length();

                // Within ~1m-1.5m horizontal proximity
                if (dist <= 2.8f)
                {
                    Vector3 camFwd = pc.Camera != null ? -pc.Camera.GlobalBasis.Z : -pc.GlobalBasis.Z;
                    float lookDot = camFwd.Dot(toCrate.Normalized());

                    // Verify player crosshair is aimed towards the crate
                    if (lookDot > 0.58f)
                    {
                        localPlayer = pc;
                        break;
                    }
                }
            }
        }

        MatchHUD? hud = localPlayer?.MatchHud ?? _capturingPlayer?.MatchHud ?? MatchHUD.Instance;

        if (localPlayer != null)
        {
            _capturingPlayer = localPlayer;

            // Check if player is holding Key F (or interact action)
            bool wantsCapture = Input.IsKeyPressed(Key.F) || Input.IsActionPressed("interact");

            if (wantsCapture)
            {
                _currentHoldProgress += dt / RequiredHoldTime;
                float progress01 = Mathf.Clamp(_currentHoldProgress, 0.0f, 1.0f);
                hud?.ShowInteractionPrompt("HOLD [F] TO CLAIM", progress01);

                if (_currentHoldProgress >= 1.0f)
                {
                    CompleteCapture(localPlayer);
                }
            }
            else
            {
                _currentHoldProgress = 0.0f;
                hud?.ShowInteractionPrompt("HOLD [F] TO CLAIM", 0.0f);
            }
        }
        else
        {
            if (_capturingPlayer != null)
            {
                _currentHoldProgress = 0.0f;
                hud?.HideInteractionPrompt();
                _capturingPlayer = null;
            }
        }
    }

    private void CompleteCapture(PlayerController player)
    {
        if (_isClaimed) return;
        _isClaimed = true;

        MatchHUD? hud = player.MatchHud ?? MatchHUD.Instance;
        hud?.HideInteractionPrompt();

        // Sound feedback
        _audioPlayer?.Play();

        // Broadcast crate claiming across network so it is despawned across all peers
        if (ScorestreakManager.Instance != null)
        {
            long myPeerId = (Multiplayer != null && Multiplayer.HasMultiplayerPeer()) ? Multiplayer.GetUniqueId() : player.GetMultiplayerAuthority();
            ScorestreakManager.Instance.ClaimCrate(CrateNetId, myPeerId);
        }
        else
        {
            string reward = GD.Randf() < 0.5f ? "UAV" : "AIRSTRIKE";
            player.AwardScorestreak(reward);
            Despawn();
        }
    }

    public void Despawn()
    {
        _isClaimed = true;
        _capturingPlayer?.MatchHud?.HideInteractionPrompt();
        MatchHUD.Instance?.HideInteractionPrompt();
        QueueFree();
    }

    public override void _ExitTree()
    {
        _capturingPlayer?.MatchHud?.HideInteractionPrompt();
        MatchHUD.Instance?.HideInteractionPrompt();
    }
}
