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

        var rifleScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGP7_Rigged.glb");
        if (rifleScene != null)
        {
            var weaponInstance = rifleScene.Instantiate<Node3D>();
            weaponInstance.Name = "MGP7_Model";
            weaponInstance.Position = new Vector3(0.0f, -0.04f, 0.08f);
            weaponInstance.RotationDegrees = new Vector3(0.0f, -90.0f, 0.0f);
            _weaponRig.AddChild(weaponInstance);

            _muzzleMarker = new Node3D
            {
                Name = "MuzzleMarker",
                Position = new Vector3(0.0f, 0.04f, -0.36f)
            };
            _weaponRig.AddChild(_muzzleMarker);
        }
        else
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

    public void FireRecoil()
    {
        // Punch weapon backward and up
        _recoilPosition += new Vector3(0.004f, 0.012f, 0.048f);
        _recoilRotation += new Vector3(
            0.085f,
            (float)GD.RandRange(-0.012, 0.012),
            (float)GD.RandRange(-0.016, 0.016)
        );

        // Muzzle flash
        _muzzleFlashTimer = 0.045f;
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
