using Godot;

namespace FPSGame.Entities.Player.Viewmodel;

/// <summary>
/// First-person viewmodel controller.
/// Constructs the procedural weapon rig and provides fluid procedural recoil, mouse sway,
/// muzzle flash, and tactical reload animations with zero GC allocations in hot loops.
/// </summary>
public partial class ViewmodelController : Node3D
{
    private readonly Vector3 _restPosition = new Vector3(0.18f, -0.16f, -0.34f);
    private readonly Vector3 _restRotation = Vector3.Zero;

    // Recoil state
    private Vector3 _recoilPosition;
    private Vector3 _recoilRotation;
    private float _muzzleFlashTimer;

    // Sway state
    private Vector3 _swayRotation;

    // Reload animation state
    private bool _isReloading;
    private float _reloadTimer;
    private float _reloadDuration = 2.2f;

    // Rig nodes
    private Node3D _weaponRig = null!;
    private Node3D? _mp5Model;
    private Node3D? _lmgModel;
    private Node3D? _mglModel;
    private Node3D? _m82Model;
    private Node3D _muzzleMarker = null!;
    private MeshInstance3D _muzzleFlashMesh = null!;
    private OmniLight3D _muzzleFlashLight = null!;

    public Vector3 MuzzlePosition => _muzzleMarker.GlobalPosition;

    public override void _Ready()
    {
        Position = _restPosition;
        Rotation = _restRotation;

        BuildWeaponRig();
    }

    private void BuildWeaponRig()
    {
        _weaponRig = new Node3D { Name = "RifleRig" };
        AddChild(_weaponRig);

        // 1. Primary SMG: MP5
        var rifleScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGP7_Rigged.glb");
        if (rifleScene != null)
        {
            _mp5Model = rifleScene.Instantiate<Node3D>();
            _mp5Model.Name = "MGP7_Model";
            _mp5Model.Position = new Vector3(0.0f, -0.04f, 0.08f);
            _mp5Model.RotationDegrees = new Vector3(0.0f, -90.0f, 0.0f);
            _weaponRig.AddChild(_mp5Model);
        }

        // 2. Heavy Primary / Secondary: LMG (M249 SAW / Minimi)
        var lmgScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/LMG.glb");
        if (lmgScene != null)
        {
            _lmgModel = lmgScene.Instantiate<Node3D>();
            _lmgModel.Name = "LMG_Model";
            // Positioned lower-right of the viewport brought back closer to camera to accommodate long stock
            _lmgModel.Position = new Vector3(0.02f, -0.075f, -0.045f);
            _lmgModel.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
            _lmgModel.Scale = Vector3.One;
            _lmgModel.Visible = false;
            _weaponRig.AddChild(_lmgModel);
        }

        // 3. Heavy Special: MGL (Multiple Grenade Launcher)
        var mglScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGL.glb");
        if (mglScene != null)
        {
            _mglModel = mglScene.Instantiate<Node3D>();
            _mglModel.Name = "MGL_Model";
            // Rotated 180 degrees around Y so the barrel points forward away from the shooter
            _mglModel.Position = new Vector3(0.015f, -0.065f, -0.04f);
            _mglModel.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
            _mglModel.Scale = Vector3.One;
            _mglModel.Visible = false;
            _weaponRig.AddChild(_mglModel);
        }

        // 4. Heavy Sniper: M82 (.50 BMG Anti-Material Rifle)
        var m82Scene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/M82.glb");
        if (m82Scene != null)
        {
            _m82Model = m82Scene.Instantiate<Node3D>();
            _m82Model.Name = "M82_Model";
            _m82Model.Scale = new Vector3(0.007f, 0.007f, 0.007f);
            _m82Model.RotationDegrees = new Vector3(0.0f, 90.0f, 0.0f);
            _m82Model.Position = new Vector3(0.05f, -0.28f, -0.15f);
            _m82Model.Visible = false;
            _weaponRig.AddChild(_m82Model);
        }

        _muzzleMarker = new Node3D
        {
            Name = "MuzzleMarker",
            Position = new Vector3(0.0f, 0.04f, -0.36f)
        };
        _weaponRig.AddChild(_muzzleMarker);

        if (_mp5Model == null && _lmgModel == null && _mglModel == null && _m82Model == null)
        {
            BuildProceduralFallback();
        }

        var flashMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.88f, 0.45f, 0.95f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        _muzzleFlashMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.08f) },
            MaterialOverride = flashMat,
            Position = new Vector3(0, 0, -0.04f),
            Visible = false
        };
        _muzzleMarker.AddChild(_muzzleFlashMesh);

        _muzzleFlashLight = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.85f, 0.45f),
            LightEnergy = 2.5f,
            OmniRange = 4.0f,
            Visible = false
        };
        _muzzleMarker.AddChild(_muzzleFlashLight);
    }

    private void BuildProceduralFallback()
    {
        var receiver = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.065f, 0.11f, 0.48f) },
            Position = new Vector3(0.0f, 0.0f, -0.10f)
        };
        _weaponRig.AddChild(receiver);

        _muzzleMarker = new Node3D
        {
            Name = "MuzzleMarker",
            Position = new Vector3(0.0f, 0.02f, -0.42f)
        };
        _weaponRig.AddChild(_muzzleMarker);
    }

    public void SetEquippedWeapon(string weaponName)
    {
        if (weaponName == "LMG")
        {
            if (_mp5Model != null) _mp5Model.Visible = false;
            if (_lmgModel != null) _lmgModel.Visible = true;
            if (_mglModel != null) _mglModel.Visible = false;
            if (_m82Model != null) _m82Model.Visible = false;
            if (_muzzleMarker != null) _muzzleMarker.Position = new Vector3(0.02f, 0.04f, -0.725f);
        }
        else if (weaponName == "MGL")
        {
            if (_mp5Model != null) _mp5Model.Visible = false;
            if (_lmgModel != null) _lmgModel.Visible = false;
            if (_mglModel != null) _mglModel.Visible = true;
            if (_m82Model != null) _m82Model.Visible = false;
            if (_muzzleMarker != null) _muzzleMarker.Position = new Vector3(0.02f, -0.05f, -0.58f);
        }
        else if (weaponName == "M82")
        {
            if (_mp5Model != null) _mp5Model.Visible = false;
            if (_lmgModel != null) _lmgModel.Visible = false;
            if (_mglModel != null) _mglModel.Visible = false;
            if (_m82Model != null) _m82Model.Visible = true;
            if (_muzzleMarker != null) _muzzleMarker.Position = new Vector3(0.05f, -0.05f, -1.05f);
        }
        else
        {
            if (_mp5Model != null) _mp5Model.Visible = true;
            if (_lmgModel != null) _lmgModel.Visible = false;
            if (_mglModel != null) _mglModel.Visible = false;
            if (_m82Model != null) _m82Model.Visible = false;
            if (_muzzleMarker != null) _muzzleMarker.Position = new Vector3(0.0f, 0.04f, -0.36f);
        }
    }

    public void FireRecoil()
    {
        if (_m82Model != null && _m82Model.Visible)
        {
            // Heavy .50 BMG sniper kickback
            _recoilPosition = new Vector3(
                (float)GD.RandRange(-0.003, 0.003),
                0.035f,
                0.11f
            );
            _recoilRotation = new Vector3(
                0.24f,
                (float)GD.RandRange(-0.015, 0.015),
                (float)GD.RandRange(-0.015, 0.015)
            );
            _muzzleFlashTimer = 0.065f;
            _muzzleFlashMesh.Visible = true;
            _muzzleFlashLight.Visible = true;
            return;
        }

        // Punch weapon backward and up with clamping to prevent runaway recoil during 1200 RPM rapid fire
        _recoilPosition = new Vector3(
            Mathf.Clamp(_recoilPosition.X + (float)GD.RandRange(-0.002, 0.002), -0.015f, 0.015f),
            Mathf.Clamp(_recoilPosition.Y + 0.008f, -0.02f, 0.035f),
            Mathf.Clamp(_recoilPosition.Z + 0.032f, -0.01f, 0.065f)
        );
        _recoilRotation = new Vector3(
            Mathf.Clamp(_recoilRotation.X + 0.055f, -0.05f, 0.14f),
            Mathf.Clamp(_recoilRotation.Y + (float)GD.RandRange(-0.010, 0.010), -0.05f, 0.05f),
            Mathf.Clamp(_recoilRotation.Z + (float)GD.RandRange(-0.012, 0.012), -0.05f, 0.05f)
        );

        // Muzzle flash
        _muzzleFlashTimer = 0.035f;
        _muzzleFlashMesh.Visible = true;
        _muzzleFlashLight.Visible = true;
    }

    public void StartReload(float duration)
    {
        _isReloading = true;
        _reloadDuration = duration;
        _reloadTimer = duration;
    }

    public void UpdateSway(Vector2 mouseRelative)
    {
        _swayRotation.Y = Mathf.Clamp(_swayRotation.Y - mouseRelative.X * 0.0007f, -0.06f, 0.06f);
        _swayRotation.X = Mathf.Clamp(_swayRotation.X - mouseRelative.Y * 0.0007f, -0.06f, 0.06f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 1. Recover recoil spring
        _recoilPosition = _recoilPosition.Lerp(Vector3.Zero, 16.0f * dt);
        _recoilRotation = _recoilRotation.Lerp(Vector3.Zero, 18.0f * dt);

        // 2. Recover sway
        _swayRotation = _swayRotation.Lerp(Vector3.Zero, 8.0f * dt);

        // 3. Muzzle flash timer
        if (_muzzleFlashTimer > 0.0f)
        {
            _muzzleFlashTimer -= dt;
            if (_muzzleFlashTimer <= 0.0f)
            {
                _muzzleFlashMesh.Visible = false;
                _muzzleFlashLight.Visible = false;
            }
        }

        // 4. Procedural reload trajectory
        Vector3 reloadPosOffset = Vector3.Zero;
        Vector3 reloadRotOffset = Vector3.Zero;

        if (_isReloading)
        {
            _reloadTimer -= dt;
            float progress = 1.0f - Mathf.Clamp(_reloadTimer / _reloadDuration, 0.0f, 1.0f);

            if (progress < 0.35f)
            {
                // Phase 1: Lower rifle and roll inward
                float t = progress / 0.35f;
                reloadPosOffset = new Vector3(-0.02f * t, -0.14f * t, 0.03f * t);
                reloadRotOffset = new Vector3(-0.16f * t, 0.08f * t, 0.36f * t);
            }
            else if (progress < 0.68f)
            {
                // Phase 2: Mag insert tap / slap
                float t = (progress - 0.35f) / 0.33f;
                float magSlap = Mathf.Sin(t * Mathf.Pi) * 0.028f;
                reloadPosOffset = new Vector3(-0.02f, -0.14f + magSlap, 0.03f);
                reloadRotOffset = new Vector3(-0.16f, 0.08f, 0.36f);
            }
            else
            {
                // Phase 3: Return to rest position
                float t = 1.0f - ((progress - 0.68f) / 0.32f);
                reloadPosOffset = new Vector3(-0.02f * t, -0.14f * t, 0.03f * t);
                reloadRotOffset = new Vector3(-0.16f * t, 0.08f * t, 0.36f * t);
            }

            if (_reloadTimer <= 0.0f)
            {
                _isReloading = false;
            }
        }

        // 5. Apply final composed transform
        Position = _restPosition + _recoilPosition + reloadPosOffset;
        Rotation = _restRotation + _recoilRotation + _swayRotation + reloadRotOffset;
    }
}
