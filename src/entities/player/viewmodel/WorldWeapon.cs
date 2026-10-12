using Godot;

namespace FPSGame.Entities.Player.Viewmodel;

/// <summary>
/// Third-person world weapon attached to the player's CameraPivot.
/// Displays the player's held weapon to other players in multiplayer, syncing aim pitch,
/// firing recoil, muzzle flashes, and reload animations.
/// </summary>
public partial class WorldWeapon : Node3D
{
    private readonly Vector3 _restPosition = new Vector3(0.24f, -0.22f, -0.38f);
    private readonly Vector3 _restRotation = Vector3.Zero;

    private Vector3 _recoilPosition;
    private Vector3 _recoilRotation;
    private float _muzzleFlashTimer;

    private bool _isReloading;
    private float _reloadTimer;
    private float _reloadDuration = 1.8f;

    private Node3D _weaponRig = null!;
    private Node3D _muzzleMarker = null!;
    private MeshInstance3D _muzzleFlashMesh = null!;
    private OmniLight3D _muzzleFlashLight = null!;
    private Node3D _leftArmRig = null!;

    public Vector3 MuzzlePosition => _muzzleMarker.GlobalPosition;

    public override void _Ready()
    {
        Position = _restPosition;
        Rotation = _restRotation;

        BuildWorldWeaponRig();
    }

    private void BuildWorldWeaponRig()
    {
        _weaponRig = new Node3D { Name = "RifleMesh" };
        AddChild(_weaponRig);

        var gunMetalMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.20f, 0.23f),
            Roughness = 0.35f,
            Metallic = 0.85f
        };

        var darkSteelMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.11f, 0.13f),
            Roughness = 0.25f,
            Metallic = 0.95f
        };

        var polymerMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.06f, 0.07f, 0.08f),
            Roughness = 0.85f,
            Metallic = 0.1f
        };

        // 1. Receiver
        var receiver = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, 0.32f) },
            MaterialOverride = gunMetalMat,
            Position = new Vector3(0, 0, 0)
        };
        _weaponRig.AddChild(receiver);

        // 2. Barrel & Handguard
        var barrel = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.045f, 0.28f) },
            MaterialOverride = darkSteelMat,
            Position = new Vector3(0, 0.005f, -0.28f)
        };
        _weaponRig.AddChild(barrel);

        var barrelTip = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 0.07f },
            MaterialOverride = darkSteelMat,
            Rotation = new Vector3(Mathf.DegToRad(90.0f), 0, 0),
            Position = new Vector3(0, 0.005f, -0.44f)
        };
        _weaponRig.AddChild(barrelTip);

        // 3. Magazine
        var mag = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.16f, 0.07f) },
            MaterialOverride = polymerMat,
            Position = new Vector3(0, -0.10f, -0.07f),
            Rotation = new Vector3(Mathf.DegToRad(12.0f), 0, 0)
        };
        _weaponRig.AddChild(mag);

        // 4. Stock
        var stock = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.045f, 0.09f, 0.20f) },
            MaterialOverride = polymerMat,
            Position = new Vector3(0, -0.02f, 0.24f)
        };
        _weaponRig.AddChild(stock);

        // 5. Sight
        var sight = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.08f) },
            MaterialOverride = darkSteelMat,
            Position = new Vector3(0, 0.055f, -0.06f)
        };
        _weaponRig.AddChild(sight);

        // 6. Muzzle Marker & Flash
        _muzzleMarker = new Node3D
        {
            Name = "MuzzleMarker",
            Position = new Vector3(0, 0.005f, -0.48f)
        };
        _weaponRig.AddChild(_muzzleMarker);

        var flashMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.88f, 0.45f, 0.95f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        _muzzleFlashMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) },
            MaterialOverride = flashMat,
            Position = new Vector3(0, 0, -0.05f),
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

        // 7. Tactical Pistol Grip
        var pistolGrip = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.038f, 0.12f, 0.05f) },
            MaterialOverride = polymerMat,
            Position = new Vector3(0, -0.07f, 0.06f),
            Rotation = new Vector3(Mathf.DegToRad(-20.0f), 0, 0)
        };
        _weaponRig.AddChild(pistolGrip);

        var sleeveMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.14f, 0.16f, 0.20f),
            Roughness = 0.8f
        };

        var gloveMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.09f, 0.10f),
            Roughness = 0.7f
        };

        // 8. Right Arm & Firing Hand (Holding Pistol Grip)
        var rightHand = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.07f, 0.08f) },
            MaterialOverride = gloveMat,
            Position = new Vector3(0.01f, -0.07f, 0.06f)
        };
        _weaponRig.AddChild(rightHand);

        var rightForearm = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.26f) },
            MaterialOverride = sleeveMat,
            Position = new Vector3(0.05f, -0.08f, 0.22f),
            Rotation = new Vector3(Mathf.DegToRad(-15.0f), Mathf.DegToRad(12.0f), 0)
        };
        _weaponRig.AddChild(rightForearm);

        // 9. Left Arm & Support Hand Rig (Holding Handguard, drops to mag during reload)
        _leftArmRig = new Node3D { Name = "LeftArmRig" };
        _weaponRig.AddChild(_leftArmRig);

        var leftHand = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.09f) },
            MaterialOverride = gloveMat,
            Position = new Vector3(-0.02f, 0.01f, -0.25f)
        };
        _leftArmRig.AddChild(leftHand);

        var leftForearm = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.30f) },
            MaterialOverride = sleeveMat,
            Position = new Vector3(-0.12f, -0.07f, -0.06f),
            Rotation = new Vector3(Mathf.DegToRad(-12.0f), Mathf.DegToRad(-35.0f), Mathf.DegToRad(18.0f))
        };
        _leftArmRig.AddChild(leftForearm);
    }

    public void FireRecoil()
    {
        _recoilPosition += new Vector3(0.005f, 0.012f, 0.05f);
        _recoilRotation += new Vector3(
            0.08f,
            (float)GD.RandRange(-0.01, 0.01),
            (float)GD.RandRange(-0.015, 0.015)
        );

        _muzzleFlashTimer = 0.05f;
        _muzzleFlashMesh.Visible = true;
        _muzzleFlashLight.Visible = true;
    }

    public void StartReload(float duration)
    {
        _isReloading = true;
        _reloadDuration = duration;
        _reloadTimer = duration;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Recover recoil
        _recoilPosition = _recoilPosition.Lerp(Vector3.Zero, 15.0f * dt);
        _recoilRotation = _recoilRotation.Lerp(Vector3.Zero, 16.0f * dt);

        // Muzzle flash timer
        if (_muzzleFlashTimer > 0.0f)
        {
            _muzzleFlashTimer -= dt;
            if (_muzzleFlashTimer <= 0.0f)
            {
                _muzzleFlashMesh.Visible = false;
                _muzzleFlashLight.Visible = false;
            }
        }

        // Reload trajectory
        Vector3 reloadPosOffset = Vector3.Zero;
        Vector3 reloadRotOffset = Vector3.Zero;

        if (_isReloading)
        {
            _reloadTimer -= dt;
            float progress = 1.0f - Mathf.Clamp(_reloadTimer / _reloadDuration, 0.0f, 1.0f);

            if (progress < 0.35f)
            {
                float t = progress / 0.35f;
                reloadPosOffset = new Vector3(-0.02f * t, -0.15f * t, 0.03f * t);
                reloadRotOffset = new Vector3(-0.16f * t, 0.08f * t, 0.35f * t);
                _leftArmRig.Position = new Vector3(0.04f * t, -0.10f * t, 0.18f * t);
            }
            else if (progress < 0.68f)
            {
                float t = (progress - 0.35f) / 0.33f;
                float magSlap = Mathf.Sin(t * Mathf.Pi) * 0.03f;
                reloadPosOffset = new Vector3(-0.02f, -0.15f + magSlap, 0.03f);
                reloadRotOffset = new Vector3(-0.16f, 0.08f, 0.35f);
                _leftArmRig.Position = new Vector3(0.04f, -0.10f + magSlap, 0.18f);
            }
            else
            {
                float t = 1.0f - ((progress - 0.68f) / 0.32f);
                reloadPosOffset = new Vector3(-0.02f * t, -0.15f * t, 0.03f * t);
                reloadRotOffset = new Vector3(-0.16f * t, 0.08f * t, 0.35f * t);
                _leftArmRig.Position = new Vector3(0.04f * t, -0.10f * t, 0.18f * t);
            }

            if (_reloadTimer <= 0.0f)
            {
                _isReloading = false;
                _leftArmRig.Position = Vector3.Zero;
            }
        }
        else
        {
            _leftArmRig.Position = _leftArmRig.Position.Lerp(Vector3.Zero, 12.0f * dt);
        }

        Position = _restPosition + _recoilPosition + reloadPosOffset;
        Rotation = _restRotation + _recoilRotation + reloadRotOffset;
    }
}
