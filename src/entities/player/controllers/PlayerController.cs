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
using FPSGame.Core.Pooling;
using FPSGame.Weapons.Projectiles;
using FPSGame.VFX.Impacts;
using FPSGame.Entities.Scorestreaks;

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
    public MatchHUD? MatchHud => _matchHud;
    public Camera3D? Camera => _camera;

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

    // Independent ammo pools per weapon slot
    private int _mp5MagAmmo = 30;
    private int _mp5ReserveAmmo = 120;
    private int _lmgMagAmmo = 200;
    private int _lmgReserveAmmo = 400;
    private int _mglMagAmmo = 6;
    private int _mglReserveAmmo = 18;

    // Independent fire modes per weapon slot
    private string _mp5FireMode = "AUTO";
    private string _lmgFireMode = "AUTO";
    private string _mglFireMode = "SEMI";

    public int CurrentMagAmmo { get; private set; } = 30;
    public int ReserveAmmo { get; private set; } = 120;
    public int MagCapacity => WeaponName switch
    {
        "MGL" => 6,
        "LMG" => 200,
        _ => 30
    };
    public int MaxReserveAmmo => WeaponName switch
    {
        "MGL" => 18,
        "LMG" => 400,
        _ => 120
    };
    public float FireRateInterval => WeaponName switch
    {
        "MGL" => 0.35f, // MGL semi-auto ~170 RPM
        "LMG" => 0.05f, // LMG 1200 RPM
        _ => 0.08f      // MP5 750 RPM
    };
    public float ReloadDuration => WeaponName switch
    {
        "MGL" => 2.8f,
        "LMG" => 3.0f,
        _ => 1.8f
    };
    public float WeaponDamage => WeaponName switch
    {
        "MGL" => 140.0f,
        "LMG" => 32.0f,
        _ => 28.0f
    };

    public int FragCount { get; private set; } = 2;
    public int FlashCount { get; private set; } = 2;
    public int CurrentStreakKills { get; private set; }
    public int UavCount { get; private set; }
    public int CarePackageCount { get; private set; }
    public int AirstrikeCount { get; private set; }
    public int BombingRunCount { get; private set; }

    private enum StreakPlacementType { None, Airstrike, CarePackage, BombingRun }
    private StreakPlacementType _activePlacement = StreakPlacementType.None;
    private StrikeTargetMarker? _placementMarker;
    private BombingRunMarker? _bombingRunMarker;
    private bool _bombingRunCenterLocked;
    private Vector3 _bombingRunCenterPos;
    private Vector3 _bombingRunHeadingDir = Vector3.Forward;
    private AudioStream? _radioConfirmSound;
    private AudioStreamPlayer? _radioAudioPlayer;

    private float _fireCooldownTimer;
    private float _reloadTimer;
    private float _respawnTimer;
    private float _weaponUnholsterTimer;
    private bool _mustReleaseFireAfterPlacement;
    private const float RespawnDuration = 3.0f;
    private const float VoidKillThresholdY = -350.0f;

    // Grenade & Projectile physical pooling
    private float _grenadeCooldownTimer;
    private bool _gKeyWasPressed;
    private NodePool<GrenadeProjectile>? _grenadePool;
    private NodePool<MglProjectile>? _mglPool;

    // Cinematic camera trauma and shake
    private float _cameraTrauma;
    private float _shakeTime;

    // Zero-allocation audio voice pool (expanded to 8 players to handle 1200 RPM without voice stealing)
    private AudioStream? _gunshotSound;
    private AudioStream? _mglShotSound;
    private readonly AudioStreamPlayer[] _audioPlayers = new AudioStreamPlayer[8];
    private int _audioIndex;
    private readonly AudioStreamPlayer3D[] _spatialAudioPlayers = new AudioStreamPlayer3D[6];
    private int _spatialAudioIndex;

    // Pre-allocated physics raycast queries for zero heap allocations
    private const float MaxBulletRange = 550.0f;
    private PhysicsRayQueryParameters3D _rayQuery = null!;
    private PhysicsRayQueryParameters3D _groundRayQuery = null!;
    private PhysicsRayQueryParameters3D _groundPlacementQuery = null!;
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
        AddToGroup("Players");
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

        // Pre-allocate ground query for streak placement marker
        _groundPlacementQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1 | 2);
        _groundPlacementQuery.CollideWithAreas = false;
        _groundPlacementQuery.CollideWithBodies = true;

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

        // Initialize pre-allocated gunshot audio voice pool
        _gunshotSound = ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/weapons/gunshot.mp3");
        _mglShotSound = ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/weapons/mgl_shot.mp3");
        for (int i = 0; i < _audioPlayers.Length; i++)
        {
            _audioPlayers[i] = new AudioStreamPlayer
            {
                Stream = _gunshotSound,
                Bus = "Master"
            };
            AddChild(_audioPlayers[i]);
        }
        for (int i = 0; i < _spatialAudioPlayers.Length; i++)
        {
            _spatialAudioPlayers[i] = new AudioStreamPlayer3D
            {
                Name = $"SpatialGunshotPlayer_{i}",
                TopLevel = true,
                Stream = _gunshotSound,
                UnitSize = 25.0f,
                MaxDb = 0.0f,
                MaxDistance = 600.0f,
                Bus = "Master",
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                AttenuationFilterCutoffHz = 4500.0f,
                AttenuationFilterDb = -18.0f
            };
            AddChild(_spatialAudioPlayers[i]);
        }

        _radioConfirmSound = ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/scorestreaks/radio_confirm.mp3");
        _radioAudioPlayer = new AudioStreamPlayer
        {
            Stream = _radioConfirmSound,
            Bus = "Master"
        };
        AddChild(_radioAudioPlayer);

        _placementMarker = new StrikeTargetMarker
        {
            Name = "LocalStreakTargetMarker",
            TopLevel = true,
            Visible = false
        };
        AddChild(_placementMarker);

        _bombingRunMarker = new BombingRunMarker
        {
            Name = "LocalBombingRunMarker",
            TopLevel = true,
            Visible = false
        };
        AddChild(_bombingRunMarker);

        ExplosionManager.EnsureExists(GetTree());
        ScorestreakManager.EnsureExists(GetTree());

        Node3D? grenadeContainer = null;
        if (ExplosionManager.Instance != null && ExplosionManager.Instance.IsInsideTree())
        {
            grenadeContainer = ExplosionManager.Instance.GetNodeOrNull<Node3D>("GrenadePoolContainer");
            if (grenadeContainer == null)
            {
                grenadeContainer = new Node3D { Name = "GrenadePoolContainer", TopLevel = true };
                ExplosionManager.Instance.AddChild(grenadeContainer);
            }
        }
        if (grenadeContainer == null)
        {
            grenadeContainer = new Node3D { Name = "GrenadePoolContainer", TopLevel = true };
            AddChild(grenadeContainer);
        }

        _grenadePool = new NodePool<GrenadeProjectile>(
            grenadeContainer,
            factory: () =>
            {
                var grenade = new GrenadeProjectile();
                grenade.Exploded += OnGrenadeExploded;
                return grenade;
            },
            prewarmCount: 4
        );

        Node3D? mglContainer = null;
        if (ExplosionManager.Instance != null && ExplosionManager.Instance.IsInsideTree())
        {
            mglContainer = ExplosionManager.Instance.GetNodeOrNull<Node3D>("MglPoolContainer");
            if (mglContainer == null)
            {
                mglContainer = new Node3D { Name = "MglPoolContainer", TopLevel = true };
                ExplosionManager.Instance.AddChild(mglContainer);
            }
        }
        if (mglContainer == null)
        {
            mglContainer = new Node3D { Name = "MglPoolContainer", TopLevel = true };
            AddChild(mglContainer);
        }

        _mglPool = new NodePool<MglProjectile>(
            mglContainer,
            factory: () =>
            {
                var proj = new MglProjectile();
                proj.Exploded += OnMglProjectileExploded;
                return proj;
            },
            prewarmCount: 6
        );

        if (IsLocalPlayer)
        {
            // If the local player spawned at scene origin (0,0,0) before receiving server spawn transform,
            // immediately reposition to the safe team spawn point to avoid falling or spawning 1.8km away.
            if (GlobalPosition.LengthSquared() < 25.0f)
            {
                Vector3 safeSpawn = GetSafeSpawnPosition();
                TeleportToSpawn(safeSpawn);
            }

            _camera.Current = true;

            // Ensure 3D audio listener is attached to local player camera and made current
            var listener = _camera.GetNodeOrNull<AudioListener3D>("AudioListener3D");
            if (listener == null)
            {
                listener = new AudioListener3D { Name = "AudioListener3D" };
                _camera.AddChild(listener);
            }
            listener.MakeCurrent();
            GetViewport().AudioListenerEnable3D = true;

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
                _matchHud.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
            }

            // Bind player camera to Terrain3D if map uses Terrain3D
            var terrains = GetTree().CurrentScene?.FindChildren("*", "Terrain3D");
            if (terrains != null && terrains.Count > 0)
            {
                terrains[0].Call("set_camera", _camera);
            }
        }
        else
        {
            _camera.Current = false;
            var listener = _camera.GetNodeOrNull<AudioListener3D>("AudioListener3D");
            listener?.ClearCurrent();
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
        RemoveFromGroup("Players");

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

        // Key 1: Switch to MP5
        if (@event is InputEventKey key1 && key1.Pressed && !key1.Echo && (key1.Keycode == Key.Key1 || key1.Keycode == Key.Kp1) && !IsDead)
        {
            if (_activePlacement != StreakPlacementType.None) CancelStreakPlacement();
            EquipWeapon("MP5");
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key 2: Switch to LMG
        if (@event is InputEventKey key2 && key2.Pressed && !key2.Echo && (key2.Keycode == Key.Key2 || key2.Keycode == Key.Kp2) && !IsDead)
        {
            if (_activePlacement != StreakPlacementType.None) CancelStreakPlacement();
            EquipWeapon("LMG");
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key 3: Switch to MGL (Multiple Grenade Launcher)
        if (@event is InputEventKey key3 && key3.Pressed && !key3.Echo && (key3.Keycode == Key.Key3 || key3.Keycode == Key.Kp3) && !IsDead)
        {
            if (_activePlacement != StreakPlacementType.None) CancelStreakPlacement();
            EquipWeapon("MGL");
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key 4: UAV Recon Scorestreak
        if (@event is InputEventKey key4 && key4.Pressed && !key4.Echo && (key4.Keycode == Key.Key4 || key4.Keycode == Key.Kp4) && !IsDead)
        {
            if (_activePlacement != StreakPlacementType.None)
            {
                CancelStreakPlacement();
            }

            if (UavCount > 0)
            {
                UavCount--;
                PlayRadioConfirmSound();
                _matchHud?.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
                ScorestreakManager.Instance?.CallUav(Multiplayer?.GetUniqueId() ?? 1, TeamId, GlobalPosition);
                _matchHud?.ShowAnnouncement("UAV RECON INBOUND (60S)", 2.2f);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key 5: Care Package Scorestreak
        if (@event is InputEventKey key5 && key5.Pressed && !key5.Echo && (key5.Keycode == Key.Key5 || key5.Keycode == Key.Kp5) && !IsDead)
        {
            if (_activePlacement == StreakPlacementType.CarePackage)
            {
                CancelStreakPlacement();
            }
            else if (CarePackageCount > 0)
            {
                if (_activePlacement != StreakPlacementType.None) CancelStreakPlacement();
                StartStreakPlacement(StreakPlacementType.CarePackage, 2.0f);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key 6: Airstrike Scorestreak
        if (@event is InputEventKey key6 && key6.Pressed && !key6.Echo && (key6.Keycode == Key.Key6 || key6.Keycode == Key.Kp6) && !IsDead)
        {
            if (_activePlacement == StreakPlacementType.Airstrike)
            {
                CancelStreakPlacement();
            }
            else if (AirstrikeCount > 0)
            {
                if (_activePlacement != StreakPlacementType.None) CancelStreakPlacement();
                StartStreakPlacement(StreakPlacementType.Airstrike, 9.0f);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key 7: Bombing Run Scorestreak
        if (@event is InputEventKey key7 && key7.Pressed && !key7.Echo && (key7.Keycode == Key.Key7 || key7.Keycode == Key.Kp7) && !IsDead)
        {
            if (_activePlacement == StreakPlacementType.BombingRun)
            {
                CancelStreakPlacement();
            }
            else if (BombingRunCount > 0)
            {
                if (_activePlacement != StreakPlacementType.None) CancelStreakPlacement();
                StartStreakPlacement(StreakPlacementType.BombingRun, 0.0f);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Mouse clicks for streak placement confirmation / cancellation
        if (@event is InputEventMouseButton mb && mb.Pressed && !IsDead && _activePlacement != StreakPlacementType.None)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                CancelStreakPlacement();
                GetViewport().SetInputAsHandled();
                return;
            }
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (_activePlacement == StreakPlacementType.BombingRun)
                {
                    if (!_bombingRunCenterLocked)
                    {
                        // Click 1: Lock center pivot arrow on terrain
                        UpdatePlacementMarkerPosition();
                        _bombingRunCenterLocked = true;
                        if (_bombingRunMarker != null)
                        {
                            _bombingRunMarker.IsCenterLocked = true;
                        }
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                    else
                    {
                        // Click 2: Confirm bombing run along chosen flight heading
                        UpdatePlacementMarkerPosition();
                        ConfirmStreakPlacement();
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                }
                else
                {
                    ConfirmStreakPlacement();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }

        // Key V: Cycle fire mode
        if (@event is InputEventKey keyV && keyV.Pressed && !keyV.Echo && keyV.Keycode == Key.V && !IsDead)
        {
            if (_activePlacement != StreakPlacementType.None) return;
            if (_isEmoting) Rpc(nameof(StopEmoteRpc));
            CycleFireMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Key R: Reload
        if (@event is InputEventKey keyR && keyR.Pressed && !keyR.Echo && keyR.Keycode == Key.R && !IsDead)
        {
            if (_activePlacement != StreakPlacementType.None) return;
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

            // Server authority (or offline/solo) triggers respawn when countdown finishes
            if ((Multiplayer == null || !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer()) && _respawnTimer <= 0.0f)
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

        // Server authoritative (or offline/solo) void protection for all player entities
        if ((Multiplayer == null || !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer()) && GlobalPosition.Y < VoidKillThresholdY)
        {
            HandleVoidFall();
            return;
        }

        if (!IsLocalPlayer)
        {
            UpdateRemoteVisuals(dt);
            return;
        }

        // Scorestreak confirm weapon unholster delay
        if (_weaponUnholsterTimer > 0.0f)
        {
            _weaponUnholsterTimer -= dt;
        }

        if (_activePlacement != StreakPlacementType.None)
        {
            UpdatePlacementMarkerPosition();
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

            if (_cameraTrauma > 0.0f)
            {
                _shakeTime += dt * 32.0f;
                float shakeAmt = _cameraTrauma * _cameraTrauma;
                float pitchShake = (Mathf.Sin(_shakeTime * 1.3f) * 0.040f + (float)GD.RandRange(-0.012, 0.012)) * shakeAmt;
                float yawShake = (Mathf.Cos(_shakeTime * 1.1f) * 0.035f + (float)GD.RandRange(-0.012, 0.012)) * shakeAmt;
                float rollShake = (Mathf.Sin(_shakeTime * 1.7f) * 0.035f) * shakeAmt;

                _cameraPivot.Rotation = new Vector3(_cameraPitch + pitchShake, yawShake, rollShake);
                _cameraTrauma = Mathf.MoveToward(_cameraTrauma, 0.0f, 1.25f * dt);
            }
            else
            {
                _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);
            }

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
            if (_viewmodel != null)
            {
                bool shouldShowViewmodel = _activePlacement == StreakPlacementType.None && _weaponUnholsterTimer <= 0.0f;
                if (_viewmodel.Visible != shouldShowViewmodel)
                {
                    _viewmodel.Visible = shouldShowViewmodel;
                }
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

        // Grenade throw cooldown countdown
        if (_grenadeCooldownTimer > 0.0f)
        {
            _grenadeCooldownTimer -= dt;
        }

        // Handle grenade throw input (edge-triggered detection to avoid holding repeat)
        bool gActionPressed = Input.IsActionJustPressed("throw_grenade");
        bool gRawDown = Input.IsKeyPressed(Key.G);
        bool gJustPressed = gActionPressed || (gRawDown && !_gKeyWasPressed);
        _gKeyWasPressed = gRawDown;

        if (IsLocalPlayer && !IsDead && (_pauseMenu == null || !_pauseMenu.IsOpen) &&
            (MatchManager.Instance == null || !MatchManager.Instance.IsMovementLocked) &&
            _activePlacement == StreakPlacementType.None)
        {
            if (gJustPressed && _grenadeCooldownTimer <= 0.0f)
            {
                TryThrowGrenade();
            }
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
            (MatchManager.Instance != null && MatchManager.Instance.IsMovementLocked) ||
            _activePlacement != StreakPlacementType.None ||
            _weaponUnholsterTimer > 0.0f)
        {
            return;
        }

        if (_mustReleaseFireAfterPlacement)
        {
            if (Input.IsActionPressed("fire_primary") || Input.IsMouseButtonPressed(MouseButton.Left))
            {
                return;
            }
            _mustReleaseFireAfterPlacement = false;
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

        // Procedural camera kick (beefy recoil for 40mm launcher, tuned recoil for 1200 RPM LMG)
        float kick = WeaponName == "MGL" ? 0.024f : (WeaponName == "LMG" ? 0.0036f : 0.008f);
        _cameraPitch = Mathf.Clamp(_cameraPitch + kick, -Mathf.DegToRad(89.0f), Mathf.DegToRad(89.0f));
        _cameraPivot.Rotation = new Vector3(_cameraPitch, 0.0f, 0.0f);

        // Procedural viewmodel recoil animation & muzzle flash
        _viewmodel?.FireRecoil();

        // Audio gunfire report
        PlayLocalGunshotSound();

        Vector3 origin = _camera.GlobalPosition;
        Vector3 direction = -_camera.GlobalBasis.Z;

        // Special handling for MGL: launches physical 40mm HEDP ballistic projectile
        if (WeaponName == "MGL")
        {
            Vector3 spawnOrigin = _viewmodel != null ? _viewmodel.MuzzlePosition : origin + direction * 0.5f;
            // Prevent spawning inside wall when standing point-blank against geometry
            var space = GetWorld3D()?.DirectSpaceState;
            if (space != null)
            {
                var query = PhysicsRayQueryParameters3D.Create(origin, spawnOrigin, 1 | 2);
                var hit = space.IntersectRay(query);
                if (hit.Count > 0)
                {
                    spawnOrigin = (Vector3)hit["position"] - direction * 0.05f;
                }
            }

            Vector3 projectileVelocity = direction * 76.0f + (Velocity * 0.35f);
            long myId = Multiplayer != null && Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1;

            if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
            {
                Rpc(nameof(SpawnMglProjectileRpc), spawnOrigin, projectileVelocity, myId);
            }
            else
            {
                ExecuteSpawnMglProjectile(spawnOrigin, projectileVelocity, myId);
            }
            return;
        }

        // Query impact point for local tracer
        var spaceState = GetWorld3D().DirectSpaceState;
        _rayQuery.From = origin;
        _rayQuery.To = origin + direction * MaxBulletRange;
        var result = spaceState.IntersectRay(_rayQuery);
        Vector3 hitPos = result.Count > 0 ? result["position"].AsVector3() : origin + direction * MaxBulletRange;

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
        _rayQuery.To = origin + direction * MaxBulletRange;

        var result = spaceState.IntersectRay(_rayQuery);
        Vector3 serverHitPos = result.Count > 0 ? result["position"].AsVector3() : origin + direction * MaxBulletRange;

        // Broadcast tracer replication and gunfire audio to other peers
        Rpc(nameof(ReplicateFireRpc), senderId, origin, serverHitPos, weaponName);

        if (result.Count == 0) return;

        var collider = result["collider"].AsGodotObject();

        HurtboxComponent? targetHurtbox = collider as HurtboxComponent;
        PlayerController? targetPlayer = collider as PlayerController;

        if (targetHurtbox == null && targetPlayer == null && collider is Node colNode)
        {
            targetHurtbox = colNode.GetParentOrNull<HurtboxComponent>() ?? colNode.GetNodeOrNull<HurtboxComponent>("Hurtboxes/BodyHurtbox");
            targetPlayer = colNode.GetOwnerOrNull<PlayerController>() ?? colNode.GetParentOrNull<PlayerController>();
        }

        float damageAmount = weaponName == "LMG" ? 36.0f : 28.0f;

        if (targetHurtbox != null)
        {
            // Reject self
            if (targetHurtbox.OwnerPeerId == senderId) return;
            // Reject friendly fire
            if (targetHurtbox.TeamId == senderTeam) return;
            // Reject already dead target
            if (targetHurtbox.IsDead) return;
            var ownerPlayer = targetHurtbox.GetNodeOrNull<PlayerController>("../..") ?? targetHurtbox.GetOwnerOrNull<PlayerController>();
            if (ownerPlayer != null && ownerPlayer.IsDead) return;

            Vector3 hitPos = result["position"].AsVector3();
            Vector3 hitNormal = result["normal"].AsVector3();

            bool wasDeadBefore = targetHurtbox.IsDead;
            targetHurtbox.ReceiveDamage(new DamageInfo(damageAmount, senderId, targetHurtbox.BoneType, hitPos, hitNormal, weaponName));

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
            if (targetPlayer.IsDead) return;

            var targetHealth = targetPlayer.GetNodeOrNull<HealthComponent>("HealthComponent");
            if (targetHealth != null && !targetHealth.IsDead)
            {
                bool wasDeadBefore = targetHealth.IsDead;
                Vector3 hitPos = result["position"].AsVector3();
                Vector3 hitNormal = result["normal"].AsVector3();
                targetHealth.ApplyDamage(new DamageInfo(damageAmount, senderId, 1, hitPos, hitNormal, weaponName));

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
    private void ReplicateFireRpc(long shooterPeerId, Vector3 origin, Vector3 hitPos, string weaponName = "MP5")
    {
        if (Multiplayer == null || (Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer())) return;

        // Don't duplicate tracer or recoil for local shooter (already executed client-side in ExecuteFire)
        if (IsLocalPlayer && shooterPeerId == Multiplayer.GetUniqueId()) return;

        // Play spatial 3D gunfire audio at shooter position
        PlaySpatialGunshotSound(origin, weaponName);

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
    public void ConfirmHitRpc(bool isHeadshot, bool isKill)
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer()) return;

        if (IsLocalPlayer)
        {
            _matchHud?.ShowHitmarker(isHeadshot, isKill);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void AwardKillStreakRpc()
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer()) return;

        if (IsLocalPlayer)
        {
            CurrentStreakKills++;
            int cycle = CurrentStreakKills % 8;
            if (cycle == 3)
            {
                UavCount++;
                _matchHud?.ShowAnnouncement("UAV READY [PRESS 4]", 2.2f);
            }
            else if (cycle == 4)
            {
                CarePackageCount++;
                _matchHud?.ShowAnnouncement("CARE PACKAGE READY [PRESS 5]", 2.2f);
            }
            else if (cycle == 6)
            {
                AirstrikeCount++;
                _matchHud?.ShowAnnouncement("AIRSTRIKE READY [PRESS 6]", 2.2f);
            }
            else if (cycle == 0)
            {
                BombingRunCount++;
                _matchHud?.ShowAnnouncement("BOMBING RUN READY [PRESS 7]", 2.2f);
            }

            _matchHud?.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
        }
    }

    private void CycleFireMode()
    {
        if (WeaponName == "MGL")
        {
            FireMode = "SEMI"; // MGL is strictly semi-automatic
            _mglFireMode = "SEMI";
            return;
        }
        else if (WeaponName == "LMG")
        {
            FireMode = FireMode == "AUTO" ? "SEMI" : "AUTO"; // LMG only AUTO or SEMI
            _lmgFireMode = FireMode;
        }
        else
        {
            FireMode = FireMode switch
            {
                "AUTO" => "SEMI",
                "SEMI" => "BURST",
                _ => "AUTO"
            };
            _mp5FireMode = FireMode;
        }

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

        if (WeaponName == "MP5")
        {
            _mp5MagAmmo = CurrentMagAmmo;
            _mp5ReserveAmmo = ReserveAmmo;
        }
        else if (WeaponName == "LMG")
        {
            _lmgMagAmmo = CurrentMagAmmo;
            _lmgReserveAmmo = ReserveAmmo;
        }
        else if (WeaponName == "MGL")
        {
            _mglMagAmmo = CurrentMagAmmo;
            _mglReserveAmmo = ReserveAmmo;
        }

        _matchHud?.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);
    }

    public void EquipWeapon(string newWeapon)
    {
        if (WeaponName == newWeapon) return;
        if (IsDead) return;

        // Save current ammo pool and fire mode for current weapon
        if (WeaponName == "MP5")
        {
            _mp5MagAmmo = CurrentMagAmmo;
            _mp5ReserveAmmo = ReserveAmmo;
            _mp5FireMode = FireMode;
        }
        else if (WeaponName == "LMG")
        {
            _lmgMagAmmo = CurrentMagAmmo;
            _lmgReserveAmmo = ReserveAmmo;
            _lmgFireMode = FireMode;
        }
        else if (WeaponName == "MGL")
        {
            _mglMagAmmo = CurrentMagAmmo;
            _mglReserveAmmo = ReserveAmmo;
            _mglFireMode = FireMode;
        }

        // Cancel active reload or emote
        if (_isEmoting) Rpc(nameof(StopEmoteRpc));
        _reloadTimer = 0.0f;
        _fireCooldownTimer = 0.15f; // Quick swap transition delay

        WeaponName = newWeapon;
        if (WeaponName == "MP5")
        {
            FireMode = _mp5FireMode;
        }
        else if (WeaponName == "LMG")
        {
            FireMode = _lmgFireMode;
        }
        else if (WeaponName == "MGL")
        {
            FireMode = _mglFireMode;
        }

        // Restore ammo pool for newly equipped weapon
        if (WeaponName == "MP5")
        {
            CurrentMagAmmo = _mp5MagAmmo;
            ReserveAmmo = _mp5ReserveAmmo;
        }
        else if (WeaponName == "LMG")
        {
            CurrentMagAmmo = _lmgMagAmmo;
            ReserveAmmo = _lmgReserveAmmo;
        }
        else if (WeaponName == "MGL")
        {
            CurrentMagAmmo = _mglMagAmmo;
            ReserveAmmo = _mglReserveAmmo;
        }

        _matchHud?.UpdateWeapon(WeaponName, FireMode, CurrentMagAmmo, ReserveAmmo);
        _viewmodel?.SetEquippedWeapon(WeaponName);
        _characterModel?.SetEquippedWeapon(WeaponName);

        // Replicate weapon change across network peers
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            Rpc(nameof(SyncEquippedWeaponRpc), WeaponName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncEquippedWeaponRpc(string weaponName)
    {
        WeaponName = weaponName;
        _characterModel?.SetEquippedWeapon(weaponName);
    }

    private void PlayLocalGunshotSound()
    {
        if (_audioPlayers.Length == 0) return;
        var player = _audioPlayers[_audioIndex];
        if (player == null) return;
        _audioIndex = (_audioIndex + 1) % _audioPlayers.Length;

        if (WeaponName == "MGL")
        {
            player.Stream = _mglShotSound ?? _gunshotSound;
            player.PitchScale = (float)GD.RandRange(0.97, 1.03);
            player.VolumeDb = 0.0f;
        }
        else
        {
            player.Stream = _gunshotSound;
            float basePitch = WeaponName == "LMG" ? 0.82f : 1.0f;
            player.PitchScale = basePitch * (float)GD.RandRange(0.97, 1.03);
            player.VolumeDb = WeaponName == "LMG" ? 0.5f : 0.0f;
        }

        player.Play();
    }

    private void PlaySpatialGunshotSound(Vector3 origin, string weapon)
    {
        if (_spatialAudioPlayers.Length == 0) return;
        var player = _spatialAudioPlayers[_spatialAudioIndex];
        if (player == null) return;
        _spatialAudioIndex = (_spatialAudioIndex + 1) % _spatialAudioPlayers.Length;

        player.GlobalPosition = origin;
        if (weapon == "MGL")
        {
            player.Stream = _mglShotSound ?? _gunshotSound;
            player.PitchScale = (float)GD.RandRange(0.96, 1.04);
            player.VolumeDb = 0.0f;
        }
        else
        {
            player.Stream = _gunshotSound;
            float basePitch = weapon == "LMG" ? 0.82f : 1.0f;
            player.PitchScale = basePitch * (float)GD.RandRange(0.96, 1.04);
            player.VolumeDb = weapon == "LMG" ? 1.0f : 0.0f;
        }

        player.Play();
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

        // Immediately disable hurtboxes and physical body collision so dead/invisible player cannot be hit
        if (_headHurtbox != null)
        {
            _headHurtbox.SetDeferred(Area3D.PropertyName.Monitorable, false);
            _headHurtbox.SetDeferred(Area3D.PropertyName.Monitoring, false);
            _headHurtbox.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0);
        }
        if (_bodyHurtbox != null)
        {
            _bodyHurtbox.SetDeferred(Area3D.PropertyName.Monitorable, false);
            _bodyHurtbox.SetDeferred(Area3D.PropertyName.Monitoring, false);
            _bodyHurtbox.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0);
        }
        if (_bodyCollider != null)
        {
            _bodyCollider.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        }
        SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0);

        if (_emoteWheel != null && _emoteWheel.IsOpen)
        {
            _emoteWheel.Close(triggerSelection: false);
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        CancelStreakPlacement();
        CurrentStreakKills = 0;
        UavCount = 0;
        CarePackageCount = 0;
        AirstrikeCount = 0;
        BombingRunCount = 0;
        _respawnTimer = RespawnDuration;

        // Drive rigged character elimination animation
        _characterModel?.UpdateAnimationState(0.0f, true, true);

        if (_worldWeapon != null)
        {
            _worldWeapon.Visible = false;
        }

        if (IsLocalPlayer)
        {
            _matchHud?.UpdateScorestreakProgress(0, 0, 0, 0, 0);
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

    public Vector3 GetSafeSpawnPosition()
    {
        Vector3 spawnPos = TeamId == 2 ? new Vector3(280.0f, 3.5f, -1529.0f) : new Vector3(215.0f, 106.0f, -1822.0f);

        var spawnContainer = GetTree().CurrentScene?.GetNodeOrNull<Node3D>("SpawnPoints")
            ?? (GetTree().Root?.FindChild("SpawnPoints", true, false) as Node3D);

        if (spawnContainer != null)
        {
            var matchingSpawns = new System.Collections.Generic.List<Marker3D>();
            foreach (Node child in spawnContainer.GetChildren())
            {
                if (child is Marker3D marker)
                {
                    bool isBravo = marker.Name.ToString().Contains("Bravo", System.StringComparison.OrdinalIgnoreCase);
                    if ((TeamId == 2 && isBravo) || (TeamId != 2 && !isBravo))
                    {
                        matchingSpawns.Add(marker);
                    }
                }
            }

            if (matchingSpawns.Count > 0)
            {
                int idx = (int)(GD.Randi() % (uint)matchingSpawns.Count);
                spawnPos = matchingSpawns[idx].GlobalPosition;
            }
        }

        return spawnPos;
    }

    public void TeleportToSpawn(Vector3 spawnPos)
    {
        GlobalPosition = spawnPos;
        Velocity = Vector3.Zero;
        _lastRemotePosition = spawnPos;
        _smoothedRemoteSpeed = 0.0f;
        _remoteMoveStopTimer = 0.0f;
        _spawnStabilizationTimer = 0.4f;
    }

    private void HandleVoidFall()
    {
        Vector3 safePos = GetSafeSpawnPosition();
        TeleportToSpawn(safePos);

        if (Multiplayer == null || !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
        {
            _healthComponent?.ApplyDamage(new DamageInfo(9999.0f, 0, 1, safePos, Vector3.Up, "Void"));
            ServerExecuteRespawn();
        }
        else
        {
            RpcId(1, nameof(RequestVoidRespawnRpc));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestVoidRespawnRpc()
    {
        if (Multiplayer == null || !Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer()) return;
        long senderId = Multiplayer.GetRemoteSenderId();
        if (int.TryParse(Name, out int peerId) && senderId != peerId) return;

        _healthComponent?.ApplyDamage(new DamageInfo(9999.0f, 0, 1, GlobalPosition, Vector3.Up, "Void"));
        ServerExecuteRespawn();
    }

    private void ServerExecuteRespawn()
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer()) return;

        Vector3 spawnPos = GetSafeSpawnPosition();
        TeleportToSpawn(spawnPos);
        _healthComponent?.ResetHealth();

        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            Rpc(nameof(ClientRespawnRpc), spawnPos);
        }
        else
        {
            ClientRespawnRpc(spawnPos);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientRespawnRpc(Vector3 spawnPos)
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer()) return;

        _isDead = false;
        IsCrouched = false;
        CurrentEmote = "";
        _isEmoting = false;
        _powerCastBarrageTimer = 0.0f;
        _currentCameraHeight = 1.6f;
        UpdateStanceColliders(false);

        TeleportToSpawn(spawnPos);

        // Re-enable hurtboxes and body collider upon respawning
        if (_headHurtbox != null)
        {
            _headHurtbox.SetDeferred(Area3D.PropertyName.Monitorable, true);
            _headHurtbox.SetDeferred(Area3D.PropertyName.Monitoring, true);
            _headHurtbox.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, (1 << 4) | (1 << 5));
        }
        if (_bodyHurtbox != null)
        {
            _bodyHurtbox.SetDeferred(Area3D.PropertyName.Monitorable, true);
            _bodyHurtbox.SetDeferred(Area3D.PropertyName.Monitoring, true);
            _bodyHurtbox.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, (1 << 4) | (1 << 5));
        }
        if (_bodyCollider != null)
        {
            _bodyCollider.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
        }
        SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 4);

        // Restore health component immediately on all peers so IsDead flips to false instantly
        _healthComponent?.ResetHealth();

        // Reset weapon ammo pools for all weapons
        _mp5MagAmmo = 30;
        _mp5ReserveAmmo = 120;
        _lmgMagAmmo = 200;
        _lmgReserveAmmo = 400;
        _mglMagAmmo = 6;
        _mglReserveAmmo = 18;
        CurrentMagAmmo = MagCapacity;
        ReserveAmmo = MaxReserveAmmo;
        _reloadTimer = 0.0f;
        _fireCooldownTimer = 0.0f;
        _respawnTimer = 0.0f;

        // Reset lethal & tactical equipment
        FragCount = 2;
        FlashCount = 2;
        _grenadeCooldownTimer = 0.0f;
        _cameraTrauma = 0.0f;
        _matchHud?.UpdateEquipment(FragCount, FlashCount);

        // Reset fire modes and scorestreak progress
        _mp5FireMode = "AUTO";
        _lmgFireMode = "AUTO";
        _mglFireMode = "SEMI";
        FireMode = WeaponName == "MGL" ? _mglFireMode : (WeaponName == "LMG" ? _lmgFireMode : _mp5FireMode);
        CurrentStreakKills = 0;
        UavCount = 0;
        CarePackageCount = 0;
        AirstrikeCount = 0;
        BombingRunCount = 0;
        _matchHud?.UpdateScorestreakProgress(0, 0, 0, 0, 0);

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
            _characterModel.SetEquippedWeapon(WeaponName);
            _characterModel.UpdateAnimationState(0.0f, true, false, false);
        }

        if (IsLocalPlayer)
        {
            _cameraPitch = 0.0f;
            _cameraPivot.Position = new Vector3(0.0f, 1.6f, 0.0f);
            _cameraPivot.Rotation = Vector3.Zero;
            _camera.Position = Vector3.Zero;
            _camera.Rotation = Vector3.Zero;

            if (_viewmodel != null)
            {
                _viewmodel.Visible = true;
                _viewmodel.SetEquippedWeapon(WeaponName);
            }
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

        // Void threshold protection: catch player falling through map and immediately teleport/kill
        if (GlobalPosition.Y < VoidKillThresholdY)
        {
            HandleVoidFall();
            return;
        }

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

            Vector3 endPos = spawnOrigin + bulletDir * MaxBulletRange;
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
            _rayQuery.To = origin + bulletDir * MaxBulletRange;
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
                if (targetHurtbox.IsDead) continue;

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
                if (targetPlayer.IsDead) continue;

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

            Vector3 endPos = origin + bulletDir * MaxBulletRange;
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

    private void OnGrenadeExploded(GrenadeProjectile grenade)
    {
        CallDeferred(nameof(DeferredReturnGrenadeProjectile), grenade);
    }

    private void DeferredReturnGrenadeProjectile(GrenadeProjectile grenade)
    {
        _grenadePool?.Return(grenade);
    }

    public void ApplyExplosionCameraShake(float trauma)
    {
        _cameraTrauma = Mathf.Clamp(_cameraTrauma + trauma, 0.0f, 1.0f);
    }

    private void TryThrowGrenade()
    {
        if (FragCount <= 0 || _grenadeCooldownTimer > 0.0f) return;

        FragCount--;
        _grenadeCooldownTimer = 1.0f;
        _matchHud?.UpdateEquipment(FragCount, FlashCount);

        Vector3 lookDir = -_camera.GlobalTransform.Basis.Z;
        Vector3 camPos = _camera.GlobalPosition;
        Vector3 targetSpawn = camPos + lookDir * 0.70f;
        Vector3 origin = targetSpawn;

        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState != null)
        {
            var query = PhysicsRayQueryParameters3D.Create(camPos, targetSpawn, 1 | 2);
            var hit = spaceState.IntersectRay(query);
            if (hit.Count > 0)
            {
                Vector3 hitPoint = (Vector3)hit["position"];
                Vector3 hitNormal = (Vector3)hit["normal"];
                origin = hitPoint + hitNormal * 0.08f;
            }
        }

        Vector3 throwVelocity = lookDir * 19.0f + Vector3.Up * 2.8f + (Velocity * 0.50f);
        Vector3 spin = new Vector3(
            (float)GD.RandRange(8.0, 16.0),
            (float)GD.RandRange(-4.0, 4.0),
            (float)GD.RandRange(-6.0, 6.0)
        );

        long myId = Multiplayer != null && Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1;

        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            Rpc(nameof(SpawnGrenadeRpc), origin, throwVelocity, spin, myId);
        }
        else
        {
            ExecuteSpawnGrenade(origin, throwVelocity, spin, myId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnGrenadeRpc(Vector3 origin, Vector3 velocity, Vector3 spin, long shooterId)
    {
        ExecuteSpawnGrenade(origin, velocity, spin, shooterId);
    }

    private void ExecuteSpawnGrenade(Vector3 origin, Vector3 velocity, Vector3 spin, long shooterId)
    {
        if (_grenadePool == null) return;
        var grenade = _grenadePool.Checkout(origin);
        grenade.Launch(origin, velocity, spin, shooterId);
    }

    private void OnMglProjectileExploded(MglProjectile proj)
    {
        CallDeferred(nameof(DeferredReturnMglProjectile), proj);
    }

    private void DeferredReturnMglProjectile(MglProjectile proj)
    {
        _mglPool?.Return(proj);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnMglProjectileRpc(Vector3 origin, Vector3 velocity, long shooterId)
    {
        ExecuteSpawnMglProjectile(origin, velocity, shooterId);

        // Remote visual and sound feedback
        if (shooterId != (Multiplayer?.GetUniqueId() ?? 1))
        {
            PlaySpatialGunshotSound(origin, "MGL");
            _characterModel?.FireRecoil();
            _worldWeapon?.FireRecoil();
        }
    }

    private void ExecuteSpawnMglProjectile(Vector3 origin, Vector3 velocity, long shooterId)
    {
        if (_mglPool == null) return;
        var proj = _mglPool.Checkout(origin);
        proj.Launch(origin, velocity, shooterId);
    }

    private void StartStreakPlacement(StreakPlacementType type, float radius)
    {
        _activePlacement = type;
        if (_viewmodel != null) _viewmodel.Visible = false;

        if (type == StreakPlacementType.BombingRun)
        {
            _bombingRunCenterLocked = false;
            _bombingRunHeadingDir = -_cameraPivot.GlobalBasis.Z;
            if (_placementMarker != null) _placementMarker.Visible = false;
            if (_bombingRunMarker != null)
            {
                _bombingRunMarker.IsCenterLocked = false;
                _bombingRunMarker.Visible = true;
                UpdatePlacementMarkerPosition();
            }
        }
        else
        {
            if (_bombingRunMarker != null) _bombingRunMarker.Visible = false;
            if (_placementMarker != null)
            {
                _placementMarker.TargetRadius = radius;
                _placementMarker.Visible = true;
                UpdatePlacementMarkerPosition();
            }
        }
    }

    private void CancelStreakPlacement()
    {
        _activePlacement = StreakPlacementType.None;
        _bombingRunCenterLocked = false;
        _weaponUnholsterTimer = 0.0f;
        _fireCooldownTimer = Mathf.Max(_fireCooldownTimer, 0.35f);
        _mustReleaseFireAfterPlacement = true;
        if (!_isEmoting && _viewmodel != null)
        {
            _viewmodel.Visible = true;
        }
        if (_placementMarker != null)
        {
            _placementMarker.Visible = false;
        }
        if (_bombingRunMarker != null)
        {
            _bombingRunMarker.Visible = false;
        }
    }

    private void ConfirmStreakPlacement()
    {
        if (_activePlacement == StreakPlacementType.None) return;

        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        long myId = Multiplayer != null && Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1;

        if (_activePlacement == StreakPlacementType.BombingRun)
        {
            BombingRunCount = Mathf.Max(0, BombingRunCount - 1);
            _matchHud?.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
            _matchHud?.ShowAnnouncement("BOMBING RUN INBOUND", 2.2f);

            ScorestreakManager.Instance?.CallBombingRun(myId, TeamId, _bombingRunCenterPos, _bombingRunHeadingDir, null);
            PlayRadioConfirmSound();

            if (_bombingRunMarker != null)
            {
                _bombingRunMarker.Visible = false;
            }
            _bombingRunCenterLocked = false;
            _activePlacement = StreakPlacementType.None;
            _weaponUnholsterTimer = 0.25f;
            _fireCooldownTimer = Mathf.Max(_fireCooldownTimer, 0.45f);
            _mustReleaseFireAfterPlacement = true;
            return;
        }

        Vector3 camPos = _camera.GlobalPosition;
        Vector3 lookDir = -_camera.GlobalTransform.Basis.Z;
        Vector3 targetPos = _placementMarker != null && _placementMarker.Visible ? _placementMarker.GlobalPosition : (camPos + lookDir * 40.0f);

        PlayRadioConfirmSound();

        Vector3 flightDir = new Vector3(lookDir.X, 0.0f, lookDir.Z).Normalized();
        if (flightDir.LengthSquared() < 0.01f) flightDir = -_cameraPivot.GlobalBasis.Z;

        if (_activePlacement == StreakPlacementType.CarePackage)
        {
            CarePackageCount = Mathf.Max(0, CarePackageCount - 1);
            _matchHud?.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
            _matchHud?.ShowAnnouncement("CARE PACKAGE INBOUND", 2.2f);

            ScorestreakManager.Instance?.CallCarePackage(myId, TeamId, targetPos, flightDir, null);
        }
        else if (_activePlacement == StreakPlacementType.Airstrike)
        {
            AirstrikeCount = Mathf.Max(0, AirstrikeCount - 1);
            _matchHud?.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
            _matchHud?.ShowAnnouncement("AIRSTRIKE INBOUND", 2.2f);

            ScorestreakManager.Instance?.CallAirstrike(myId, TeamId, targetPos, flightDir, null);
        }

        if (_placementMarker != null)
        {
            _placementMarker.Visible = false;
        }
        _activePlacement = StreakPlacementType.None;
        _weaponUnholsterTimer = 0.25f;
        _fireCooldownTimer = Mathf.Max(_fireCooldownTimer, 0.45f);
        _mustReleaseFireAfterPlacement = true;
    }

    private void UpdatePlacementMarkerPosition()
    {
        if (_activePlacement == StreakPlacementType.None) return;

        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        Vector3 camPos = _camera.GlobalPosition;
        Vector3 lookDir = -_camera.GlobalTransform.Basis.Z;
        Vector3 targetPos = camPos + lookDir * 40.0f;
        Vector3 normal = Vector3.Up;

        const float MaxPlacementRange = 2500.0f;
        _rayQuery.From = camPos;
        _rayQuery.To = camPos + lookDir * MaxPlacementRange;

        while (true)
        {
            var hit = spaceState.IntersectRay(_rayQuery);
            if (hit.Count == 0) break;

            var collider = hit["collider"].AsGodotObject();
            if (collider is Node node && (node.Name.ToString().Contains("Boundary", StringComparison.OrdinalIgnoreCase) || node.GetParent()?.Name.ToString().Contains("Boundar", StringComparison.OrdinalIgnoreCase) == true))
            {
                // Ignore invisible map edge barriers and continue raycast
                if (hit.TryGetValue("rid", out var ridVal))
                {
                    _groundExcludedRids.Add(ridVal.AsRid());
                    _rayQuery.Exclude = _groundExcludedRids;
                }
                continue;
            }

            targetPos = (Vector3)hit["position"];
            normal = (Vector3)hit["normal"];
            break;
        }

        // Fallback for looking directly into empty sky above all mountain peaks
        if (targetPos == camPos + lookDir * 40.0f)
        {
            _groundPlacementQuery.From = camPos + lookDir * 120.0f + Vector3.Up * 120.0f;
            _groundPlacementQuery.To = camPos + lookDir * 120.0f + Vector3.Down * 350.0f;
            var groundHit = spaceState.IntersectRay(_groundPlacementQuery);
            if (groundHit.Count > 0)
            {
                targetPos = (Vector3)groundHit["position"];
            }
        }

        if (_activePlacement == StreakPlacementType.BombingRun)
        {
            if (_bombingRunMarker == null || !_bombingRunMarker.IsInsideTree()) return;

            if (!_bombingRunCenterLocked)
            {
                // Stage 1: Cursor positions center pivot arrow anywhere on terrain
                _bombingRunCenterPos = targetPos;
                _bombingRunMarker.GlobalPosition = targetPos;
                _bombingRunMarker.GlobalBasis = Basis.Identity;

                Vector3 defaultDir = new Vector3(lookDir.X, 0.0f, lookDir.Z).Normalized();
                if (defaultDir.LengthSquared() < 0.01f) defaultDir = -_cameraPivot.GlobalBasis.Z;
                _bombingRunHeadingDir = defaultDir;
                _bombingRunMarker.SetHeading(defaultDir);
            }
            else
            {
                // Stage 2: Center is locked; looking around smoothly rotates corridor heading around center
                _bombingRunMarker.GlobalPosition = _bombingRunCenterPos;
                _bombingRunMarker.GlobalBasis = Basis.Identity;

                Vector3 lookHeading = new Vector3(lookDir.X, 0.0f, lookDir.Z).Normalized();
                if (lookHeading.LengthSquared() > 0.01f)
                {
                    _bombingRunHeadingDir = lookHeading;
                }
                _bombingRunMarker.SetHeading(_bombingRunHeadingDir);
            }
            return;
        }

        if (_placementMarker == null || !_placementMarker.IsInsideTree()) return;

        // Keep tactical circle indicator always completely flat on ground (never tilt to slope)
        _placementMarker.GlobalPosition = targetPos;
        _placementMarker.GlobalBasis = Basis.Identity;
    }

    public void PlayRadioConfirmSound()
    {
        if (_radioAudioPlayer != null && _radioConfirmSound != null)
        {
            _radioAudioPlayer.Stream = _radioConfirmSound;
            _radioAudioPlayer.Play();
        }
    }

    public void AwardScorestreak(string streakName)
    {
        if (!IsLocalPlayer) return;

        if (streakName.Equals("UAV", StringComparison.OrdinalIgnoreCase))
        {
            UavCount++;
            _matchHud?.ShowAnnouncement("ACQUIRED: UAV RECON [PRESS 4]", 2.5f);
        }
        else if (streakName.Equals("Airstrike", StringComparison.OrdinalIgnoreCase))
        {
            AirstrikeCount++;
            _matchHud?.ShowAnnouncement("ACQUIRED: AIRSTRIKE [PRESS 6]", 2.5f);
        }
        else if (streakName.Equals("Care Package", StringComparison.OrdinalIgnoreCase))
        {
            CarePackageCount++;
            _matchHud?.ShowAnnouncement("ACQUIRED: CARE PACKAGE [PRESS 5]", 2.5f);
        }
        else if (streakName.Equals("Bombing Run", StringComparison.OrdinalIgnoreCase) || streakName.Equals("BOMBING_RUN", StringComparison.OrdinalIgnoreCase))
        {
            BombingRunCount++;
            _matchHud?.ShowAnnouncement("ACQUIRED: BOMBING RUN [PRESS 7]", 2.5f);
        }

        _matchHud?.UpdateScorestreakProgress(CurrentStreakKills, UavCount, CarePackageCount, AirstrikeCount, BombingRunCount);
    }

    public void SetEspHighlight(bool enabled)
    {
        _characterModel?.SetEspHighlight(enabled);
    }
}
