using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using FPSGame.Network.Core;
using FPSGame.UI.Menus.Pause;
using FPSGame.UI.HUD;
using FPSGame.UI.HUD.Scoreboard;
using FPSGame.Gamemodes.TDM;
using FPSGame.Common.Components.Health;
using FPSGame.Common.Components.Combat;
using FPSGame.Weapons.Hitscan;
using FPSGame.Entities.Player.Viewmodel;
using FPSGame.UI.HUD.Emotes;

namespace FPSGame.Entities.Player.Controllers;

/// <summary>
/// Authoritative and client-predicted networked player controller.
/// Drives player movement, hitscan combat, health/respawn loops, HUD telemetry, and streak rewards.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 5.5f;
    [Export] public float SprintSpeed { get; set; } = 8.5f;
    [Export] public float CrouchSpeed { get; set; } = 2.8f;
    [Export] public float JumpVelocity { get; set; } = 3.15f;
    [Export] public float MouseSensitivity { get; set; } = 0.0022f;
    [Export] public float Acceleration { get; set; } = 15.0f;
    [Export] public float Friction { get; set; } = 10.0f;

    private bool _isCrouched;

    [Export]
    public bool IsCrouched
    {
        get => _isCrouched;
        set
        {
            if (_isCrouched == value) return;
            _isCrouched = value;
            UpdateStanceColliders(value);
        }
    }

    [Export] public string CurrentEmote { get; set; } = "";

    [Export] public NodePath CameraPivotPath { get; set; } = new("CameraPivot");
    [Export] public NodePath CameraPath { get; set; } = new("CameraPivot/Camera3D");
    [Export] public NodePath WorldMeshPath { get; set; } = new("WorldModel");

    private int _teamId = 1;

    [Export]
    public int TeamId
    {
        get => _teamId;
        set
        {
            _teamId = value;
            ApplyTeamAppearance();
        }
    }

    public bool IsLocalPlayer => IsMultiplayerAuthority();
    public bool IsDead => _isDead;

    private bool _isDead;

    private Node3D _cameraPivot = null!;
    private Camera3D _camera = null!;
    private Node3D _worldMesh = null!;
    private PauseMenu? _pauseMenu;
    private Scoreboard? _scoreboard;
    private MatchHUD? _matchHud;
    private EmoteWheel? _emoteWheel;
    private HealthComponent? _healthComponent;
    private ViewmodelController? _viewmodel;
    private WorldWeapon? _worldWeapon;
    private CharacterModel? _characterModel;
    private CollisionShape3D? _bodyCollider;
    private Area3D? _headHurtbox;
    private Area3D? _bodyHurtbox;
    private CollisionShape3D? _bodyHurtboxCol;
    private float _currentCameraHeight = 1.6f;
    private bool _isEmoting;
    private Vector3 _lastRemotePosition;
    private float _smoothedRemoteSpeed;
    private float _remoteMoveStopTimer;
    private bool _remoteMovingBackward;
    private float _remoteBobCycle;
    private float _spawnStabilizationTimer = 0.4f;
    private float _powerCastBarrageTimer;

    private static readonly (float YawDeg, float PitchDeg)[] PowerCastWaveOffsets = new (float, float)[]
    {
        (-18f, 0f), (-15f, 2.5f), (-13f, -2.5f), (-10f, 1f),
        (-7f, -1.5f), (-4f, 3f), (-2f, -2f), (0f, 0f),
        (2f, 2f), (4f, -3f), (7f, 1.5f), (10f, -1f),
        (13f, 2.5f), (15f, -2.5f), (18f, 0f), (0f, 3.5f)
    };

    private float _cameraPitch;
    private float _gravity = 9.81f;
    private ulong _tabPressTime;

    // Weapon & Combat Stats
    public string WeaponName { get; private set; } = "MP5";
    public string FireMode { get; private set; } = "AUTO"; // AUTO, SEMI, BURST
    public int CurrentMagAmmo { get; private set; } = 30;
    public int ReserveAmmo { get; private set; } = 120;
    public const int MagCapacity = 30;
    public int FragCount { get; private set; } = 2;
    public int FlashCount { get; private set; } = 2;
    public int CurrentStreakKills { get; private set; }

    private float _fireCooldownTimer;
    private float _reloadTimer;
    private float _respawnTimer;
    private const float FireRateInterval = 0.08f; // ~750 RPM
    private const float ReloadDuration = 1.8f;
    private const float RespawnDuration = 3.0f;

    // Pre-allocated physics raycast queries for zero heap allocations
    private PhysicsRayQueryParameters3D _rayQuery = null!;
    private PhysicsRayQueryParameters3D _groundRayQuery = null!;
    private readonly Array<Rid> _excludedRids = new();
    private readonly Array<Rid> _groundExcludedRids = new();

    public override void _EnterTree()
    {
        if (int.TryParse(Name, out int peerId))
        {
            SetMultiplayerAuthority(peerId);
        }
    }

    public override void _Ready()
    {
        _cameraPivot = GetNode<Node3D>(CameraPivotPath);
        _camera = GetNode<Camera3D>(CameraPath);
        _worldMesh = GetNode<Node3D>(WorldMeshPath);
        _characterModel = _worldMesh.GetNodeOrNull<CharacterModel>("CharacterModel");
        _viewmodel = _camera.GetNodeOrNull<ViewmodelController>("ViewmodelRig");
        _worldWeapon = _cameraPivot.GetNodeOrNull<WorldWeapon>("WorldWeapon");
        _lastRemotePosition = GlobalPosition;
        _healthComponent = GetNodeOrNull<HealthComponent>("HealthComponent");
        _pauseMenu = GetNodeOrNull<PauseMenu>("UI/PauseMenu");
        _scoreboard = GetNodeOrNull<Scoreboard>("UI/Scoreboard");
        _matchHud = GetNodeOrNull<MatchHUD>("UI/MatchHUD");

        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.81);

        // Pre-allocate cached raycast query
        _rayQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1 | 2 | 4 | 8 | 16 | 32);
        _rayQuery.CollideWithAreas = true;
        _rayQuery.CollideWithBodies = true;

        // Cache self RIDs to exclude shooter from own rays
        _excludedRids.Add(GetRid());
        var headHurtbox = GetNodeOrNull<CollisionObject3D>("Hurtboxes/HeadHurtbox");
        if (headHurtbox != null) _excludedRids.Add(headHurtbox.GetRid());
        var bodyHurtbox = GetNodeOrNull<CollisionObject3D>("Hurtboxes/BodyHurtbox");
        if (bodyHurtbox != null) _excludedRids.Add(bodyHurtbox.GetRid());
        _rayQuery.Exclude = _excludedRids;

        // Pre-allocate ground check query for remote proxy players (World_Static | World_Dynamic)
        _groundRayQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1 | 2);
        _groundRayQuery.CollideWithAreas = false;
        _groundRayQuery.CollideWithBodies = true;
        _groundExcludedRids.Add(GetRid());
        if (headHurtbox != null) _groundExcludedRids.Add(headHurtbox.GetRid());
        if (bodyHurtbox != null) _groundExcludedRids.Add(bodyHurtbox.GetRid());
        _groundRayQuery.Exclude = _groundExcludedRids;

        // Fetch authoritative team if available in NetworkManager
        if (int.TryParse(Name, out int peerId) && NetworkManager.Instance != null)
        {
            if (NetworkManager.Instance.Players.TryGetValue(peerId, out var info))
            {
                _teamId = info.TeamId;
            }
        }

        ApplyTeamAppearance();

        _bodyCollider = GetNodeOrNull<CollisionShape3D>("BodyCollider");
        _headHurtbox = GetNodeOrNull<Area3D>("Hurtboxes/HeadHurtbox");
        _bodyHurtbox = GetNodeOrNull<Area3D>("Hurtboxes/BodyHurtbox");
        _bodyHurtboxCol = _bodyHurtbox?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

        // Duplicate collider shapes to ensure instance isolation during crouching
        if (_bodyCollider?.Shape != null)
        {
            _bodyCollider.Shape = (Shape3D)_bodyCollider.Shape.Duplicate();
        }
        if (_bodyHurtboxCol?.Shape != null)
        {
            _bodyHurtboxCol.Shape = (Shape3D)_bodyHurtboxCol.Shape.Duplicate();
        }

        _emoteWheel = GetNodeOrNull<EmoteWheel>("UI/EmoteWheel");
        if (_emoteWheel != null)
        {
            _emoteWheel.EmoteSelected += OnEmoteSelected;
        }

        if (_characterModel != null)
        {
            _characterModel.EmoteFinished += OnEmoteFinished;
        }

        if (_healthComponent != null)
        {
            _healthComponent.HealthChanged += OnHealthChanged;
            _healthComponent.Died += OnDied;
        }

        if (IsLocalPlayer)
        {
            _camera.Current = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _worldMesh.Visible = false; // Prevent clipping local camera into own body
            if (_characterModel != null) _characterModel.Visible = false;
            if (_viewmodel != null) _viewmodel.Visible = true;
            if (_worldWeapon != null) _worldWeapon.Visible = false;

            if (_matchHud != null)
            {
                _matchHud.BindLocalPlayer(this);
                _matchHud.UpdateHealth(100.0f, 100.0f);
                _matchHud.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);
                _matchHud.UpdateEquipment(FragCount, FlashCount);
                _matchHud.UpdateScorestreakProgress(CurrentStreakKills);
            }
        }
        else
        {
            _camera.Current = false;
            _worldMesh.Visible = true;
            if (_characterModel != null) _characterModel.Visible = true;
            if (_viewmodel != null) _viewmodel.Visible = false;
            if (_worldWeapon != null) _worldWeapon.Visible = false;

            // Remove UI on remote proxy nodes
            var ui = GetNodeOrNull<CanvasLayer>("UI");
            if (ui != null)
            {
                ui.QueueFree();
            }
            _pauseMenu = null;
            _scoreboard = null;
            _matchHud = null;
            _emoteWheel = null;
        }
    }

    public override void _ExitTree()
    {
        if (_healthComponent != null)
        {
            _healthComponent.HealthChanged -= OnHealthChanged;
            _healthComponent.Died -= OnDied;
        }

        if (_emoteWheel != null)
        {
            _emoteWheel.EmoteSelected -= OnEmoteSelected;
        }

        if (_characterModel != null)
        {
            _characterModel.EmoteFinished -= OnEmoteFinished;
        }
    }

    public void ApplyTeamAppearance()
    {
        _characterModel?.SetTeam(TeamId);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsLocalPlayer) return;

        // Escape: dismiss emote wheel, dismiss scoreboard, or toggle pause menu
        if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && !escapeEvent.Echo && escapeEvent.Keycode == Key.Escape)
        {
            if (_emoteWheel != null && _emoteWheel.IsOpen)
            {
                _emoteWheel.Close(triggerSelection: false);
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_scoreboard != null && _scoreboard.IsOpen)
            {
                _scoreboard.Close();
                GetViewport().SetInputAsHandled();
                return;
            }

            TogglePauseMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Hold T for Emote Radial Wheel
        if (@event.IsAction("emote_wheel"))
        {
            if (@event.IsActionPressed("emote_wheel") && !@event.IsEcho())
            {
                if (!IsDead && (_pauseMenu == null || !_pauseMenu.IsOpen) && (_scoreboard == null || !_scoreboard.IsOpen))
                {
                    _emoteWheel?.Open();
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
            else if (@event.IsActionReleased("emote_wheel"))
            {
                if (_emoteWheel != null && _emoteWheel.IsOpen)
                {
                    _emoteWheel.Close(triggerSelection: true);
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }

        if (_emoteWheel != null && _emoteWheel.IsOpen) return;

        // Tab: toggle/hold scoreboard
        if (@event is InputEventKey tabEvent && !tabEvent.Echo && tabEvent.Keycode == Key.Tab)
        {
            if (_pauseMenu != null && _pauseMenu.IsOpen) return;

            if (tabEvent.Pressed)
            {
                if (_scoreboard != null)
                {
                    if (_scoreboard.IsOpen) _scoreboard.Close();
                    else
                    {
                        _scoreboard.Open();
                        _tabPressTime = Time.GetTicksMsec();
                    }
                    GetViewport().SetInputAsHandled();
                }
            }
            else
            {
                if (_scoreboard != null && _scoreboard.IsOpen)
                {
                    if (Time.GetTicksMsec() - _tabPressTime > 250)
                    {
                        _scoreboard.Close();
                        GetViewport().SetInputAsHandled();
                    }
                }
            }
            return;
        }

        // Key V: Cycle fire mode
        if (@event is InputEventKey keyV && keyV.Pressed && !keyV.Echo && keyV.Keycode == Key.V && !IsDead)
        {
            if (_isEmoting) Rpc(nameof(StopEmoteRpc));
            CycleFireMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key R: Reload
        if (@event is InputEventKey keyR && keyR.Pressed && !keyR.Echo && keyR.Keycode == Key.R && !IsDead)
        {
            if (_isEmoting) Rpc(nameof(StopEmoteRpc));
            StartReload();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_pauseMenu != null && _pauseMenu.IsOpen) return;

        // Mouse look
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured && !IsDead)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);

            _cameraPitch = Mathf.Clamp(_cameraPitch - motion.Relative.Y * MouseSensitivity, -Mathf.DegToRad(89.0f), Mathf.DegToRad(89.0f));
            _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);

            _viewmodel?.UpdateSway(motion.Relative);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Respawn countdown and body despawn handling across all peers
        if (IsDead)
        {
            _respawnTimer -= dt;

            // Server authority triggers respawn when countdown finishes
            if (Multiplayer.IsServer() && _respawnTimer <= 0.0f)
            {
                ServerExecuteRespawn();
            }

            // Despawn body after death animation finishes (~1.2s into 3.0s respawn window)
            if (_respawnTimer <= 1.8f && _respawnTimer > 0.0f)
            {
                if (_worldMesh != null && _worldMesh.Visible)
                {
                    _worldMesh.Visible = false;
                }
            }
        }

        if (!IsLocalPlayer)
        {
            UpdateRemoteVisuals(dt);
            return;
        }

        // Camera death follow: track head bone down to the floor
        if (IsDead)
        {
            if (_characterModel != null && _respawnTimer > 1.8f)
            {
                Vector3 headPos = _characterModel.GetHeadGlobalPosition();
                Vector3 targetCamPos = headPos + Vector3.Up * 0.12f;
                _cameraPivot.GlobalPosition = _cameraPivot.GlobalPosition.Lerp(targetCamPos, 12.0f * dt);
                _cameraPivot.Rotation = new Vector3(Mathf.Lerp(_cameraPivot.Rotation.X, -0.25f, 4.0f * dt), 0.0f, 0.0f);
                _camera.Rotation = new Vector3(0.0f, 0.0f, Mathf.Lerp(_camera.Rotation.Z, 0.35f, 3.5f * dt));
            }

            if (_matchHud != null)
            {
                _matchHud.SetRespawning(true, _respawnTimer);
            }
            return;
        }

        // Smooth camera height between standing (1.6m) and crouching (1.05m)
        float targetCamHeight = IsCrouched ? 1.05f : 1.6f;
        _currentCameraHeight = Mathf.Lerp(_currentCameraHeight, targetCamHeight, 14.0f * dt);

        if (_isEmoting)
        {
            // Show 3D rig and hide first-person viewmodel so player can see themselves performing the emote
            if (_worldMesh != null && !_worldMesh.Visible) _worldMesh.Visible = true;
            if (_characterModel != null && !_characterModel.Visible) _characterModel.Visible = true;
            if (_viewmodel != null && _viewmodel.Visible) _viewmodel.Visible = false;

            // Update character model animation
            Vector2 curInputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            bool isMovingBackward = curInputDir.Y > 0.1f;
            _characterModel?.UpdateAnimationState(Velocity.Length(), IsOnFloor(), IsDead, isMovingBackward, IsCrouched, CurrentEmote);

            // Pull camera back into third-person perspective
            _cameraPivot.Position = new Vector3(0.0f, _currentCameraHeight + 0.15f, 0.0f);
            _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);

            Vector3 desiredOffset = new Vector3(0.0f, 0.2f, 2.2f);
            var spaceState = GetWorld3D()?.DirectSpaceState;
            if (spaceState != null)
            {
                _rayQuery.From = _cameraPivot.GlobalPosition;
                _rayQuery.To = _cameraPivot.GlobalTransform * desiredOffset;
                var hit = spaceState.IntersectRay(_rayQuery);
                if (hit.Count > 0)
                {
                    float hitDist = ((Vector3)hit["position"] - _cameraPivot.GlobalPosition).Length();
                    float clampedDist = Mathf.Max(0.5f, hitDist - 0.25f);
                    _camera.Position = desiredOffset.Normalized() * clampedDist;
                }
                else
                {
                    _camera.Position = _camera.Position.Lerp(desiredOffset, 12.0f * dt);
                }
            }
            else
            {
                _camera.Position = desiredOffset;
            }
            _camera.Rotation = Vector3.Zero;
        }
        else
        {
            // Standard first-person FPS view
            _cameraPivot.Position = new Vector3(0.0f, _currentCameraHeight, 0.0f);
            _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);
            _camera.Position = Vector3.Zero;
            _camera.Rotation = Vector3.Zero;

            if (_worldMesh != null && _worldMesh.Visible)
            {
                _worldMesh.Visible = false;
            }
            if (_characterModel != null && _characterModel.Visible)
            {
                _characterModel.Visible = false;
            }
            if (_viewmodel != null && !_viewmodel.Visible)
            {
                _viewmodel.Visible = true;
            }
        }

        // Update compass heading on HUD (North is -Z in Godot 3D)
        if (_matchHud != null)
        {
            float yawDegrees = Mathf.RadToDeg(-Rotation.Y);
            _matchHud.UpdateCompass(yawDegrees);
        }

        // Reload countdown
        if (_reloadTimer > 0.0f)
        {
            _reloadTimer -= dt;
            if (_reloadTimer <= 0.0f)
            {
                CompleteReload();
            }
        }

        // Weapon fire cooldown
        if (_fireCooldownTimer > 0.0f)
        {
            _fireCooldownTimer -= dt;
        }

        // Easter egg: Power Cast bullet wave timer countdown
        if (_powerCastBarrageTimer > 0.0f)
        {
            _powerCastBarrageTimer -= dt;
            if (_powerCastBarrageTimer <= 0.0f)
            {
                if (IsLocalPlayer && !IsDead && CurrentEmote == "Spell_Simple_Shoot")
                {
                    ExecutePowerCastBarrage();
                }
            }
        }

        // Handle firing input
        HandleWeaponFiring();
    }

    private void UpdateRemoteVisuals(float dt)
    {
        if (_worldMesh == null) return;

        UpdateStanceColliders(IsCrouched);

        if (_spawnStabilizationTimer > 0.0f)
        {
            _spawnStabilizationTimer -= dt;
        }

        // Ensure remote character mesh visibility matches alive/dead state
        if (!IsDead)
        {
            if (!_worldMesh.Visible) _worldMesh.Visible = true;
            if (_characterModel != null && !_characterModel.Visible) _characterModel.Visible = true;
        }
        else if (_respawnTimer <= 1.8f && _respawnTimer > 0.0f)
        {
            if (_worldMesh.Visible) _worldMesh.Visible = false;
        }

        // Calculate remote player displacement velocity
        Vector3 currentPos = GlobalPosition;
        Vector3 displacement = currentPos - _lastRemotePosition;
        _lastRemotePosition = currentPos;

        float horizontalDist = new Vector2(displacement.X, displacement.Z).Length();

        // Discard teleportation / initial spawn jumps (greater than 1.5m in a single frame or during spawn stabilization)
        if (horizontalDist > 1.5f || _spawnStabilizationTimer > 0.0f)
        {
            _smoothedRemoteSpeed = 0.0f;
            _remoteMoveStopTimer = 0.0f;
            _remoteMovingBackward = false;
            _characterModel?.UpdateAnimationState(0.0f, true, IsDead, false, IsCrouched, CurrentEmote);
            _characterModel?.SetAimPitch(_cameraPivot.Rotation.X);
            return;
        }

        if (horizontalDist > 0.001f)
        {
            float instantSpeed = horizontalDist / Mathf.Max(dt, 0.001f);
            instantSpeed = Mathf.Min(instantSpeed, SprintSpeed);
            _smoothedRemoteSpeed = Mathf.Lerp(_smoothedRemoteSpeed, instantSpeed, 14.0f * dt);
            _remoteMoveStopTimer = 0.22f; // Sustain movement across network replication gaps

            Vector3 forwardDir = -GlobalTransform.Basis.Z;
            Vector3 moveDir = new Vector3(displacement.X, 0.0f, displacement.Z);
            _remoteMovingBackward = moveDir.Normalized().Dot(forwardDir) < -0.2f;
        }
        else
        {
            _remoteMoveStopTimer -= dt;
            if (_remoteMoveStopTimer <= 0.0f)
            {
                _smoothedRemoteSpeed = Mathf.Lerp(_smoothedRemoteSpeed, 0.0f, 10.0f * dt);
                if (_smoothedRemoteSpeed < 0.05f)
                {
                    _smoothedRemoteSpeed = 0.0f;
                }
            }
        }

        // Remote proxy players don't run MoveAndSlide(), so IsOnFloor() is always false.
        // We use a zero-allocation downward raycast to accurately detect grounded state.
        bool isGrounded = CheckRemoteGrounded();

        // Drive rigged character animations: Idle, Walk, Sprint, Jump, Crouch, Death, Backpedal, Emotes
        _characterModel?.UpdateAnimationState(_smoothedRemoteSpeed, isGrounded, IsDead, _remoteMovingBackward, IsCrouched, CurrentEmote);

        // Drive torso aim pitch to match where remote player is aiming
        _characterModel?.SetAimPitch(_cameraPivot.Rotation.X);
    }

    private bool CheckRemoteGrounded()
    {
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return true;

        Vector3 pos = GlobalPosition;
        _groundRayQuery.From = pos + new Vector3(0, 0.35f, 0);
        _groundRayQuery.To = pos + new Vector3(0, -0.35f, 0);

        var hit = spaceState.IntersectRay(_groundRayQuery);
        return hit.Count > 0;
    }

    private void HandleWeaponFiring()
    {
        if (IsDead || (_pauseMenu != null && _pauseMenu.IsOpen) ||
            (MatchManager.Instance != null && MatchManager.Instance.IsMovementLocked))
        {
            return;
        }

        bool fireRequested = FireMode == "AUTO"
            ? Input.IsActionPressed("fire_primary")
            : Input.IsActionJustPressed("fire_primary");

        if (!fireRequested) return;

        if (_isEmoting)
        {
            if (_powerCastBarrageTimer <= 0.0f)
            {
                Rpc(nameof(StopEmoteRpc));
            }
            return;
        }

        if (_reloadTimer > 0.0f) return;

        if (CurrentMagAmmo <= 0)
        {
            StartReload();
            return;
        }

        if (_fireCooldownTimer <= 0.0f)
        {
            ExecuteFire();
        }
    }

    private void ExecuteFire()
    {
        CurrentMagAmmo--;
        _fireCooldownTimer = FireRateInterval;

        _matchHud?.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);

        // Procedural camera kick
        _cameraPitch = Mathf.Clamp(_cameraPitch + 0.008f, -Mathf.DegToRad(89.0f), Mathf.DegToRad(89.0f));
        _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);

        // Procedural viewmodel recoil animation & muzzle flash
        _viewmodel?.FireRecoil();

        Vector3 origin = _camera.GlobalPosition;
        Vector3 direction = -_camera.GlobalBasis.Z;

        // Query impact point for local tracer
        var spaceState = GetWorld3D().DirectSpaceState;
        _rayQuery.From = origin;
        _rayQuery.To = origin + direction * 150.0f;
        var result = spaceState.IntersectRay(_rayQuery);
        Vector3 hitPos = result.Count > 0 ? result["position"].AsVector3() : origin + direction * 150.0f;

        // Spawn immediate local bullet tracer
        Vector3 tracerStart = _viewmodel != null ? _viewmodel.MuzzlePosition : origin + direction * 0.5f;
        var tracer = BulletTracer.Create(tracerStart, hitPos);
        GetTree().Root.AddChild(tracer);

        // Send fire intent to authoritative server
        RpcId(1, nameof(RequestFireRpc), origin, direction, WeaponName);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void RequestFireRpc(Vector3 origin, Vector3 direction, string weaponName)
    {
        if (!Multiplayer.IsServer()) return;

        long senderId = Multiplayer.GetRemoteSenderId();
        if (senderId == 0) senderId = Multiplayer.GetUniqueId();

        int senderTeam = 1;
        if (NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(senderId, out var senderInfo))
        {
            senderTeam = senderInfo.TeamId;
        }

        var spaceState = GetWorld3D().DirectSpaceState;
        _rayQuery.From = origin;
        _rayQuery.To = origin + direction * 150.0f;

        var result = spaceState.IntersectRay(_rayQuery);
        Vector3 serverHitPos = result.Count > 0 ? result["position"].AsVector3() : origin + direction * 150.0f;

        // Broadcast tracer replication to other peers
        Rpc(nameof(ReplicateFireRpc), senderId, origin, serverHitPos);

        if (result.Count == 0) return;

        var collider = result["collider"].AsGodotObject();

        HurtboxComponent? targetHurtbox = collider as HurtboxComponent;
        PlayerController? targetPlayer = collider as PlayerController;

        if (targetHurtbox == null && targetPlayer == null && collider is Node colNode)
        {
            targetHurtbox = colNode.GetParentOrNull<HurtboxComponent>() ?? colNode.GetNodeOrNull<HurtboxComponent>("Hurtboxes/BodyHurtbox");
            targetPlayer = colNode.GetOwnerOrNull<PlayerController>() ?? colNode.GetParentOrNull<PlayerController>();
        }

        if (targetHurtbox != null)
        {
            // Reject self
            if (targetHurtbox.OwnerPeerId == senderId) return;
            // Reject friendly fire
            if (targetHurtbox.TeamId == senderTeam) return;

            Vector3 hitPos = result["position"].AsVector3();
            Vector3 hitNormal = result["normal"].AsVector3();

            bool wasDeadBefore = targetHurtbox.IsDead;
            targetHurtbox.ReceiveDamage(new DamageInfo(28.0f, senderId, targetHurtbox.BoneType, hitPos, hitNormal, weaponName));

            bool isHeadshot = targetHurtbox.BoneType == 0;
            bool isKill = !wasDeadBefore && targetHurtbox.IsDead;

            // Confirm hit feedback to attacker (supports both host and clients)
            if (senderId == Multiplayer.GetUniqueId())
            {
                ConfirmHitRpc(isHeadshot, isKill);
            }
            else
            {
                RpcId(senderId, nameof(ConfirmHitRpc), isHeadshot, isKill);
            }

            if (isKill && MatchManager.Instance != null)
            {
                MatchManager.Instance.RecordKill(senderId, targetHurtbox.OwnerPeerId, weaponName);
                if (senderId == Multiplayer.GetUniqueId())
                {
                    AwardKillStreakRpc();
                }
                else
                {
                    RpcId(senderId, nameof(AwardKillStreakRpc));
                }
            }
        }
        else if (targetPlayer != null)
        {
            long targetPeerId = targetPlayer.GetMultiplayerAuthority();
            if (targetPeerId == senderId) return;
            if (targetPlayer.TeamId == senderTeam) return;

            var targetHealth = targetPlayer.GetNodeOrNull<HealthComponent>("HealthComponent");
            if (targetHealth != null && !targetHealth.IsDead)
            {
                bool wasDeadBefore = targetHealth.IsDead;
                Vector3 hitPos = result["position"].AsVector3();
                Vector3 hitNormal = result["normal"].AsVector3();
                targetHealth.ApplyDamage(new DamageInfo(28.0f, senderId, 1, hitPos, hitNormal, weaponName));

                bool isKill = !wasDeadBefore && targetHealth.IsDead;
                if (senderId == Multiplayer.GetUniqueId())
                {
                    ConfirmHitRpc(false, isKill);
                }
                else
                {
                    RpcId(senderId, nameof(ConfirmHitRpc), false, isKill);
                }

                if (isKill && MatchManager.Instance != null)
                {
                    MatchManager.Instance.RecordKill(senderId, targetPeerId, weaponName);
                    if (senderId == Multiplayer.GetUniqueId())
                    {
                        AwardKillStreakRpc();
                    }
                    else
                    {
                        RpcId(senderId, nameof(AwardKillStreakRpc));
                    }
                }
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReplicateFireRpc(long shooterPeerId, Vector3 origin, Vector3 hitPos)
    {
        if (Multiplayer == null || (Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer())) return;

        // Don't duplicate tracer or recoil for local shooter (already executed client-side in ExecuteFire)
        if (IsLocalPlayer && shooterPeerId == Multiplayer.GetUniqueId()) return;

        // Trigger 3D world weapon recoil and muzzle flash on remote character
        _characterModel?.FireRecoil();
        _worldWeapon?.FireRecoil();

        Vector3 tracerStart = _characterModel != null
            ? _characterModel.MuzzlePosition
            : (_worldWeapon != null ? _worldWeapon.MuzzlePosition : GlobalPosition + Vector3.Up * 1.3f);
        var tracer = BulletTracer.Create(tracerStart, hitPos);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, tracer);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ConfirmHitRpc(bool isHeadshot, bool isKill)
    {
        if (Multiplayer == null || (Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer())) return;

        if (IsLocalPlayer)
        {
            _matchHud?.ShowHitmarker(isHeadshot, isKill);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AwardKillStreakRpc()
    {
        if (Multiplayer == null || (Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer())) return;

        if (IsLocalPlayer)
        {
            CurrentStreakKills++;
            _matchHud?.UpdateScorestreakProgress(CurrentStreakKills);
        }
    }

    private void CycleFireMode()
    {
        FireMode = FireMode switch
        {
            "AUTO" => "SEMI",
            "SEMI" => "BURST",
            _ => "AUTO"
        };

        _matchHud?.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);
    }

    private void StartReload()
    {
        if (_reloadTimer > 0.0f) return;
        if (CurrentMagAmmo >= MagCapacity) return;
        if (ReserveAmmo <= 0) return;

        _reloadTimer = ReloadDuration;
        _viewmodel?.StartReload(ReloadDuration);

        // Replicate reload animation to all other peers
        Rpc(nameof(ReplicateReloadRpc));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateReloadRpc()
    {
        if (IsLocalPlayer) return;
        _characterModel?.StartReload(ReloadDuration);
        _worldWeapon?.StartReload(ReloadDuration);
    }

    private void CompleteReload()
    {
        int needed = MagCapacity - CurrentMagAmmo;
        int take = Mathf.Min(needed, ReserveAmmo);
        CurrentMagAmmo += take;
        ReserveAmmo -= take;

        _matchHud?.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);
    }

    private void OnHealthChanged(float current, float max)
    {
        if (IsLocalPlayer)
        {
            _matchHud?.UpdateHealth(current, max);
        }
    }

    private void OnDied(DamageInfo damage)
    {
        _isDead = true;
        _isEmoting = false;
        CurrentEmote = "";
        _powerCastBarrageTimer = 0.0f;

        if (_emoteWheel != null && _emoteWheel.IsOpen)
        {
            _emoteWheel.Close(triggerSelection: false);
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        CurrentStreakKills = 0;
        _respawnTimer = RespawnDuration;

        // Drive rigged character elimination animation
        _characterModel?.UpdateAnimationState(0.0f, true, true);

        if (_worldWeapon != null)
        {
            _worldWeapon.Visible = false;
        }

        if (IsLocalPlayer)
        {
            _matchHud?.UpdateScorestreakProgress(0);
            _matchHud?.SetRespawning(true, RespawnDuration);

            // Hide viewmodel so player isn't holding gun upright while dead, and reveal 3D ragdoll rig
            if (_viewmodel != null) _viewmodel.Visible = false;
            if (_worldMesh != null) _worldMesh.Visible = true;
            if (_characterModel != null)
            {
                _characterModel.Visible = true;
                _characterModel.UpdateAnimationState(0.0f, true, true);
            }
        }
    }

    private void ServerExecuteRespawn()
    {
        if (!Multiplayer.IsServer()) return;

        // Spawn flush on floor plane (y = 0.02f) so player does not fall from the air
        Vector3 spawnPos = TeamId == 2 ? new Vector3(20, 0.02f, 0) : new Vector3(-20, 0.02f, 0);

        GlobalPosition = spawnPos;
        _healthComponent?.ResetHealth();

        Rpc(nameof(ClientRespawnRpc), spawnPos);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientRespawnRpc(Vector3 spawnPos)
    {
        if (Multiplayer != null && Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer()) return;

        _isDead = false;
        IsCrouched = false;
        CurrentEmote = "";
        _isEmoting = false;
        _powerCastBarrageTimer = 0.0f;
        _currentCameraHeight = 1.6f;
        UpdateStanceColliders(false);

        GlobalPosition = spawnPos;
        Velocity = Vector3.Zero;

        // Reset remote position tracker and smoothing to spawnPos to eliminate false displacement spike
        _lastRemotePosition = spawnPos;
        _smoothedRemoteSpeed = 0.0f;
        _remoteMoveStopTimer = 0.0f;
        _remoteMovingBackward = false;
        _spawnStabilizationTimer = 0.4f;

        // Restore health component immediately on all peers so IsDead flips to false instantly
        _healthComponent?.ResetHealth();

        CurrentMagAmmo = MagCapacity;
        ReserveAmmo = 120;
        _reloadTimer = 0.0f;
        _fireCooldownTimer = 0.0f;
        _respawnTimer = 0.0f;

        // Restore upright character orientation
        if (_worldMesh != null)
        {
            _worldMesh.Rotation = Vector3.Zero;
            _worldMesh.Position = Vector3.Zero;
        }

        if (!IsLocalPlayer && _characterModel != null)
        {
            if (_worldMesh != null) _worldMesh.Visible = true;
            _characterModel.Visible = true;
            _characterModel.ResetCharacter();
            _characterModel.UpdateAnimationState(0.0f, true, false, false);
        }

        if (IsLocalPlayer)
        {
            _cameraPitch = 0.0f;
            _cameraPivot.Position = new Vector3(0.0f, 1.6f, 0.0f);
            _cameraPivot.Rotation = Vector3.Zero;
            _camera.Position = Vector3.Zero;
            _camera.Rotation = Vector3.Zero;

            if (_viewmodel != null) _viewmodel.Visible = true;
            if (_worldMesh != null) _worldMesh.Visible = false;
            if (_characterModel != null)
            {
                _characterModel.Visible = false;
                _characterModel.ResetCharacter();
            }

            _matchHud?.SetRespawning(false);
            _matchHud?.UpdateHealth(100.0f, 100.0f);
            _matchHud?.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (!IsLocalPlayer) return;

        bool isMovementFrozen = IsDead ||
                                (_pauseMenu != null && _pauseMenu.IsOpen) ||
                                (MatchManager.Instance != null && MatchManager.Instance.IsMovementLocked);

        if (isMovementFrozen)
        {
            Velocity = new Vector3(0, Velocity.Y, 0);
            if (!IsOnFloor())
            {
                Velocity = new Vector3(0, Velocity.Y - _gravity * dt, 0);
            }
            MoveAndSlide();
            return;
        }

        bool wantsCrouch = Input.IsActionPressed("crouch");
        if (IsCrouched != wantsCrouch)
        {
            IsCrouched = wantsCrouch;
            UpdateStanceColliders(IsCrouched);
        }

        Vector3 vel = Velocity;

        if (!IsOnFloor())
        {
            vel.Y -= _gravity * dt;
        }

        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            if (IsCrouched)
            {
                IsCrouched = false;
                UpdateStanceColliders(false);
            }
            vel.Y = JumpVelocity;
            if (_isEmoting && _powerCastBarrageTimer <= 0.0f)
            {
                Rpc(nameof(StopEmoteRpc));
            }
        }

        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 wishDir = (GlobalBasis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        if (_isEmoting && wishDir != Vector3.Zero && _powerCastBarrageTimer <= 0.0f)
        {
            Rpc(nameof(StopEmoteRpc));
        }

        bool isSprinting = !IsCrouched && Input.IsActionPressed("sprint") && inputDir.Y < 0;
        float targetSpeed = IsCrouched ? CrouchSpeed : (isSprinting ? SprintSpeed : WalkSpeed);

        if (wishDir != Vector3.Zero)
        {
            vel.X = Mathf.MoveToward(vel.X, wishDir.X * targetSpeed, Acceleration * dt);
            vel.Z = Mathf.MoveToward(vel.Z, wishDir.Z * targetSpeed, Acceleration * dt);
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0.0f, Friction * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0.0f, Friction * dt);
        }

        Velocity = vel;
        MoveAndSlide();
    }

    private void UpdateStanceColliders(bool crouched)
    {
        if (_bodyCollider?.Shape is CapsuleShape3D bodyCap)
        {
            bodyCap.Height = crouched ? 1.2f : 1.8f;
            _bodyCollider.Position = new Vector3(0.0f, crouched ? 0.6f : 0.9f, 0.0f);
        }
        if (_headHurtbox != null)
        {
            _headHurtbox.Position = new Vector3(0.0f, crouched ? 1.05f : 1.6f, 0.0f);
        }
        if (_bodyHurtbox != null && _bodyHurtboxCol?.Shape is CapsuleShape3D bodyHurtCap)
        {
            bodyHurtCap.Height = crouched ? 0.85f : 1.3f;
            _bodyHurtbox.Position = new Vector3(0.0f, crouched ? 0.55f : 0.85f, 0.0f);
        }
    }

    private void OnEmoteSelected(string animName)
    {
        if (IsDead) return;
        Rpc(nameof(PlayEmoteRpc), animName);
    }

    private void OnEmoteFinished()
    {
        if (IsLocalPlayer && _isEmoting)
        {
            Rpc(nameof(StopEmoteRpc));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PlayEmoteRpc(string animName)
    {
        CurrentEmote = animName;
        _isEmoting = true;

        if (animName == "Spell_Simple_Shoot")
        {
            _powerCastBarrageTimer = 0.18f; // Delay to match hands thrusting forward in the spell cast animation
        }
        else
        {
            _powerCastBarrageTimer = 0.0f;
        }

        if (_characterModel != null)
        {
            _characterModel.UpdateAnimationState(0.0f, true, IsDead, false, IsCrouched, CurrentEmote);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StopEmoteRpc()
    {
        CurrentEmote = "";
        _isEmoting = false;
        _powerCastBarrageTimer = 0.0f;

        if (_characterModel != null)
        {
            _characterModel.UpdateAnimationState(0.0f, true, IsDead, false, IsCrouched, "");
        }
    }

    private static Vector3 ComputeBarrageDirection(Vector3 forward, Vector3 up, Vector3 right, float pitchDeg, float yawDeg)
    {
        Vector3 fwd = forward.Normalized();
        Vector3 u = up.Normalized();
        Vector3 r = right.Normalized();

        if (fwd.LengthSquared() < 0.0001f) fwd = -Vector3.Forward;
        if (u.LengthSquared() < 0.0001f) u = Vector3.Up;
        if (r.LengthSquared() < 0.0001f) r = Vector3.Right;

        Vector3 dir = fwd;
        if (Mathf.Abs(pitchDeg) > 0.001f)
        {
            dir = dir.Rotated(r, Mathf.DegToRad(pitchDeg));
        }
        if (Mathf.Abs(yawDeg) > 0.001f)
        {
            dir = dir.Rotated(u, Mathf.DegToRad(yawDeg));
        }
        return dir.Normalized();
    }

    private void ExecutePowerCastBarrage()
    {
        Vector3 origin = _cameraPivot.GlobalPosition + Vector3.Down * 0.15f;
        Vector3 forward = (-_cameraPivot.GlobalBasis.Z).Normalized();
        Vector3 up = _cameraPivot.GlobalBasis.Y.Normalized();
        Vector3 right = _cameraPivot.GlobalBasis.X.Normalized();
        Vector3 spawnOrigin = origin + forward * 0.5f;

        // Camera recoil jolt
        _cameraPitch = Mathf.Clamp(_cameraPitch + 0.025f, -Mathf.DegToRad(89.0f), Mathf.DegToRad(89.0f));
        _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);

        // Immediate local bullet tracers for instant client feedback
        var spaceState = GetWorld3D()?.DirectSpaceState;
        for (int i = 0; i < PowerCastWaveOffsets.Length; i++)
        {
            var offset = PowerCastWaveOffsets[i];
            Vector3 bulletDir = ComputeBarrageDirection(forward, up, right, offset.PitchDeg, offset.YawDeg);

            Vector3 endPos = spawnOrigin + bulletDir * 120.0f;
            if (spaceState != null)
            {
                _rayQuery.From = spawnOrigin;
                _rayQuery.To = endPos;
                var result = spaceState.IntersectRay(_rayQuery);
                if (result.Count > 0)
                {
                    endPos = result["position"].AsVector3();
                }
            }

            var tracer = BulletTracer.Create(spawnOrigin, endPos);
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, tracer);
        }

        // Authoritative server simulation
        if (Multiplayer.IsServer())
        {
            RequestPowerCastBarrageRpc(spawnOrigin, forward, up, right);
        }
        else
        {
            RpcId(1, nameof(RequestPowerCastBarrageRpc), spawnOrigin, forward, up, right);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestPowerCastBarrageRpc(Vector3 origin, Vector3 forward, Vector3 up, Vector3 right)
    {
        if (!Multiplayer.IsServer()) return;

        long senderId = Multiplayer.GetRemoteSenderId();
        if (senderId == 0) senderId = Multiplayer.GetUniqueId();

        int senderTeam = 1;
        if (NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(senderId, out var senderInfo))
        {
            senderTeam = senderInfo.TeamId;
        }

        // Broadcast tracer wave to all other peers
        Rpc(nameof(ReplicatePowerCastBarrageRpc), senderId, origin, forward, up, right);

        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        int hitCount = 0;
        bool hasKill = false;
        bool hasHeadshot = false;

        for (int i = 0; i < PowerCastWaveOffsets.Length; i++)
        {
            var offset = PowerCastWaveOffsets[i];
            Vector3 bulletDir = ComputeBarrageDirection(forward, up, right, offset.PitchDeg, offset.YawDeg);

            _rayQuery.From = origin;
            _rayQuery.To = origin + bulletDir * 120.0f;
            var result = spaceState.IntersectRay(_rayQuery);

            if (result.Count == 0) continue;

            var collider = result["collider"].AsGodotObject();
            HurtboxComponent? targetHurtbox = collider as HurtboxComponent;
            PlayerController? targetPlayer = collider as PlayerController;

            if (targetHurtbox == null && targetPlayer == null && collider is Node colNode)
            {
                targetHurtbox = colNode.GetParentOrNull<HurtboxComponent>() ?? colNode.GetNodeOrNull<HurtboxComponent>("Hurtboxes/BodyHurtbox");
                targetPlayer = colNode.GetOwnerOrNull<PlayerController>() ?? colNode.GetParentOrNull<PlayerController>();
            }

            if (targetHurtbox != null)
            {
                if (targetHurtbox.OwnerPeerId == senderId) continue;
                if (senderTeam != 0 && targetHurtbox.TeamId != 0 && targetHurtbox.TeamId == senderTeam) continue;

                Vector3 hitPos = result["position"].AsVector3();
                Vector3 hitNormal = result["normal"].AsVector3();

                bool wasDeadBefore = targetHurtbox.IsDead;
                targetHurtbox.ReceiveDamage(new DamageInfo(32.0f, senderId, targetHurtbox.BoneType, hitPos, hitNormal, "Power Cast"));

                hitCount++;
                if (targetHurtbox.BoneType == 0) hasHeadshot = true;

                if (!wasDeadBefore && targetHurtbox.IsDead)
                {
                    hasKill = true;
                    if (MatchManager.Instance != null)
                    {
                        MatchManager.Instance.RecordKill(senderId, targetHurtbox.OwnerPeerId, "Power Cast");
                        if (senderId == Multiplayer.GetUniqueId())
                        {
                            AwardKillStreakRpc();
                        }
                        else
                        {
                            RpcId(senderId, nameof(AwardKillStreakRpc));
                        }
                    }
                }
            }
            else if (targetPlayer != null)
            {
                long targetPeerId = targetPlayer.GetMultiplayerAuthority();
                if (targetPeerId == senderId) continue;
                if (senderTeam != 0 && targetPlayer.TeamId != 0 && targetPlayer.TeamId == senderTeam) continue;

                var targetHealth = targetPlayer.GetNodeOrNull<HealthComponent>("HealthComponent");
                if (targetHealth != null && !targetHealth.IsDead)
                {
                    bool wasDeadBefore = targetHealth.IsDead;
                    Vector3 hitPos = result["position"].AsVector3();
                    Vector3 hitNormal = result["normal"].AsVector3();
                    targetHealth.ApplyDamage(new DamageInfo(32.0f, senderId, 1, hitPos, hitNormal, "Power Cast"));

                    hitCount++;
                    if (!wasDeadBefore && targetHealth.IsDead)
                    {
                        hasKill = true;
                        if (MatchManager.Instance != null)
                        {
                            MatchManager.Instance.RecordKill(senderId, targetPeerId, "Power Cast");
                            if (senderId == Multiplayer.GetUniqueId())
                            {
                                AwardKillStreakRpc();
                            }
                            else
                            {
                                RpcId(senderId, nameof(AwardKillStreakRpc));
                            }
                        }
                    }
                }
            }
        }

        if (hitCount > 0)
        {
            if (senderId == Multiplayer.GetUniqueId())
            {
                ConfirmHitRpc(hasHeadshot, hasKill);
            }
            else
            {
                RpcId(senderId, nameof(ConfirmHitRpc), hasHeadshot, hasKill);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReplicatePowerCastBarrageRpc(long casterId, Vector3 origin, Vector3 forward, Vector3 up, Vector3 right)
    {
        if (Multiplayer == null || (Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer())) return;
        if (IsLocalPlayer && casterId == Multiplayer.GetUniqueId()) return;

        var spaceState = GetWorld3D()?.DirectSpaceState;
        for (int i = 0; i < PowerCastWaveOffsets.Length; i++)
        {
            var offset = PowerCastWaveOffsets[i];
            Vector3 bulletDir = ComputeBarrageDirection(forward, up, right, offset.PitchDeg, offset.YawDeg);

            Vector3 endPos = origin + bulletDir * 120.0f;
            if (spaceState != null)
            {
                _rayQuery.From = origin;
                _rayQuery.To = endPos;
                var result = spaceState.IntersectRay(_rayQuery);
                if (result.Count > 0)
                {
                    endPos = result["position"].AsVector3();
                }
            }

            var tracer = BulletTracer.Create(origin, endPos);
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, tracer);
        }
    }

    private void TogglePauseMenu()
    {
        if (_pauseMenu == null) return;

        if (_pauseMenu.IsOpen)
        {
            _pauseMenu.Resume();
        }
        else
        {
            _scoreboard?.Close();

            string operatorName = "Operator";
            if (int.TryParse(Name, out int peerId) && NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(peerId, out var info))
            {
                operatorName = info.Name;
            }

            _pauseMenu.Open(TeamId, operatorName);
        }
    }
}
