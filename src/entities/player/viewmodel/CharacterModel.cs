using System;
using Godot;

namespace FPSGame.Entities.Player.Viewmodel;

/// <summary>
/// Third-person animated character model for multiplayer player bodies.
/// Replaces primitive pill capsules with a fully rigged, animated humanoid character
/// wielding a 3D tactical assault rifle, with network-synced locomotion, aiming pitch,
/// firing recoil, reloads, and team materials.
/// </summary>
public partial class CharacterModel : Node3D
{
    private AnimationPlayer? _animPlayer;
    private Skeleton3D? _skeleton;
    private MeshInstance3D? _mannequinMesh;
    private BoneAttachment3D? _handAttachment;

    private Node3D? _weaponModel;
    private Node3D? _muzzleMarker;
    private MeshInstance3D? _muzzleFlashMesh;
    private OmniLight3D? _muzzleFlashLight;
    private float _muzzleFlashTimer;

    private int _spineBone2 = -1;
    private int _spineBone3 = -1;
    private int _headBoneIndex = -1;
    private float _aimPitch;

    private string _currentAnim = "";
    private bool _isDead;
    private int _teamId = 1;

    public Vector3 MuzzlePosition => _muzzleMarker != null ? _muzzleMarker.GlobalPosition : GlobalPosition + Vector3.Up * 1.3f;

    public Vector3 GetHeadGlobalPosition()
    {
        if (_skeleton != null && _headBoneIndex != -1)
        {
            return _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_headBoneIndex).Origin;
        }
        return GlobalPosition + Vector3.Up * 1.55f;
    }

    public override void _Ready()
    {
        BuildCharacter();
        ApplyTeamMaterial();
    }

    private void BuildCharacter()
    {
        var charScene = ResourceLoader.Load<PackedScene>("res://assets/models/characters/animations/AnimationLibrary_Godot_Standard.glb");
        if (charScene == null)
        {
            GD.PrintErr("[CharacterModel] Could not load character glb!");
            return;
        }

        var charInstance = charScene.Instantiate<Node3D>();
        charInstance.Name = "RiggedMannequin";
        // Soles rest flush on ground plane (y = 0), and rotate 180 deg to face Godot forward (-Z)
        charInstance.Position = Vector3.Zero;
        charInstance.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
        AddChild(charInstance);

        _animPlayer = charInstance.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        _skeleton = charInstance.GetNodeOrNull<Skeleton3D>("Rig/Skeleton3D");

        if (_skeleton != null)
        {
            _mannequinMesh = _skeleton.GetNodeOrNull<MeshInstance3D>("Mannequin");
            _spineBone2 = _skeleton.FindBone("DEF-spine.002");
            _spineBone3 = _skeleton.FindBone("DEF-spine.003");
            _headBoneIndex = _skeleton.FindBone("DEF-head");

            // Attach 3D weapon to character's right hand bone
            int handBoneIndex = _skeleton.FindBone("DEF-hand.R");
            if (handBoneIndex != -1)
            {
                _handAttachment = new BoneAttachment3D
                {
                    Name = "RightHandGrip",
                    BoneName = "DEF-hand.R"
                };
                _skeleton.AddChild(_handAttachment);

                MountWeaponToHand(_handAttachment);
            }
        }

        // Configure default animation playback
        if (_animPlayer != null)
        {
            PlayAnimation("Pistol_Idle", 0.1f);
        }
    }

    private static readonly Vector3 WeaponRestPosition = new Vector3(0.01f, 0.04f, -0.02f);
    private static readonly Vector3 WeaponRestRotation = new Vector3(-1.24f, -92.68f, -79.32f);

    private void MountWeaponToHand(BoneAttachment3D parent)
    {
        var rifleScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGP7_Rigged.glb");
        if (rifleScene == null) return;

        _weaponModel = rifleScene.Instantiate<Node3D>();
        _weaponModel.Name = "MGP7_Rifle";

        // Align MP5 grip directly into humanoid hand palm with barrel pointing straight forward down -Z
        _weaponModel.Position = WeaponRestPosition;
        _weaponModel.RotationDegrees = WeaponRestRotation;
        _weaponModel.Scale = new Vector3(1.0f, 1.0f, 1.0f);
        parent.AddChild(_weaponModel);

        // Muzzle marker and dynamic flash light at weapon barrel tip (-X in MP5 model space)
        _muzzleMarker = new Node3D
        {
            Name = "MuzzleMarker",
            Position = new Vector3(-0.41f, 0.08f, 0.0f)
        };
        _weaponModel.AddChild(_muzzleMarker);

        var flashMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.9f, 0.45f, 0.95f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        _muzzleFlashMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) },
            MaterialOverride = flashMat,
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

    public void SetTeam(int teamId)
    {
        _teamId = teamId;
        ApplyTeamMaterial();
    }

    private void ApplyTeamMaterial()
    {
        if (_mannequinMesh == null) return;

        var mat = new StandardMaterial3D();
        mat.Roughness = 0.45f;
        mat.Metallic = 0.2f;

        if (_teamId == 2)
        {
            // Team Bravo: Tactical Crimson / Carbon Red
            mat.AlbedoColor = new Color(0.85f, 0.18f, 0.18f, 1.0f);
        }
        else
        {
            // Team Alpha: Tactical Cobalt / Navy Blue
            mat.AlbedoColor = new Color(0.18f, 0.48f, 0.95f, 1.0f);
        }

        _mannequinMesh.SetSurfaceOverrideMaterial(0, mat);
    }

    public void UpdateAnimationState(float horizontalSpeed, bool isOnFloor, bool isDead, bool isMovingBackward = false)
    {
        if (isDead)
        {
            if (!_isDead)
            {
                _isDead = true;
                PlayAnimation("Death01", 0.15f);
                if (_weaponModel != null)
                {
                    _weaponModel.Visible = false;
                }
            }
            return;
        }

        _isDead = false;

        string targetAnim;
        float speedScale = 1.0f;
        if (!isOnFloor)
        {
            targetAnim = "Jump";
        }
        else if (_currentAnim == "Sprint")
        {
            // Hysteresis: only leave sprint if dropping below 4.8 m/s
            if (horizontalSpeed > 4.8f)
            {
                targetAnim = "Sprint";
            }
            else if (horizontalSpeed > 0.25f)
            {
                targetAnim = "Walk";
                if (isMovingBackward) speedScale = -1.0f;
            }
            else
            {
                targetAnim = "Pistol_Idle";
            }
        }
        else if (_currentAnim == "Walk")
        {
            // Hysteresis: stay in walk unless speeding up above 5.8 m/s or slowing down below 0.15 m/s
            if (horizontalSpeed > 5.8f)
            {
                targetAnim = "Sprint";
            }
            else if (horizontalSpeed > 0.15f)
            {
                targetAnim = "Walk";
                if (isMovingBackward) speedScale = -1.0f;
            }
            else
            {
                targetAnim = "Pistol_Idle";
            }
        }
        else // Currently in Pistol_Idle or other
        {
            if (horizontalSpeed > 5.5f)
            {
                targetAnim = "Sprint";
            }
            else if (horizontalSpeed > 0.35f)
            {
                targetAnim = "Walk";
                if (isMovingBackward) speedScale = -1.0f;
            }
            else
            {
                targetAnim = "Pistol_Idle";
            }
        }

        if (targetAnim != _currentAnim)
        {
            PlayAnimation(targetAnim, 0.25f, speedScale);
        }
        else if (_animPlayer != null)
        {
            if (Mathf.Abs(_animPlayer.SpeedScale - speedScale) > 0.05f)
            {
                _animPlayer.SpeedScale = speedScale;
            }
        }
    }

    private void PlayAnimation(string animName, float blendTime = 0.2f, float speedScale = 1.0f)
    {
        if (_animPlayer == null) return;
        if (!_animPlayer.HasAnimation(animName)) return;

        _currentAnim = animName;
        _animPlayer.SpeedScale = speedScale;
        _animPlayer.Play(animName, customBlend: blendTime);
    }

    public void FireRecoil()
    {
        _muzzleFlashTimer = 0.05f;
        if (_muzzleFlashMesh != null) _muzzleFlashMesh.Visible = true;
        if (_muzzleFlashLight != null) _muzzleFlashLight.Visible = true;

        if (_weaponModel != null)
        {
            _weaponModel.Position = WeaponRestPosition + new Vector3(0.0f, -0.025f, 0.0f); // Quick recoil punch back along hand
        }
    }

    public void StartReload(float duration)
    {
        if (_animPlayer != null && _animPlayer.HasAnimation("Pistol_Reload"))
        {
            _animPlayer.Play("Pistol_Reload", customBlend: 0.15f);
        }
    }

    public void SetAimPitch(float pitch)
    {
        _aimPitch = pitch;
    }

    public void ResetCharacter()
    {
        _isDead = false;
        if (_weaponModel != null)
        {
            _weaponModel.Visible = true;
            _weaponModel.Position = WeaponRestPosition;
            _weaponModel.RotationDegrees = WeaponRestRotation;
        }
        PlayAnimation("Pistol_Idle", 0.1f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Muzzle flash timer recovery
        if (_muzzleFlashTimer > 0.0f)
        {
            _muzzleFlashTimer -= dt;
            if (_muzzleFlashTimer <= 0.0f)
            {
                if (_muzzleFlashMesh != null) _muzzleFlashMesh.Visible = false;
                if (_muzzleFlashLight != null) _muzzleFlashLight.Visible = false;
            }
        }

        // Recover weapon kick back to hand rest
        if (_weaponModel != null)
        {
            _weaponModel.Position = _weaponModel.Position.Lerp(WeaponRestPosition, 14.0f * dt);
        }

        // Apply upper body spine aim pitch so the torso bends to match where player aims
        if (_skeleton != null && !_isDead)
        {
            if (_spineBone2 != -1)
            {
                var q = new Quaternion(Vector3.Right, _aimPitch * 0.35f);
                _skeleton.SetBonePoseRotation(_spineBone2, q);
            }
            if (_spineBone3 != -1)
            {
                var q = new Quaternion(Vector3.Right, _aimPitch * 0.35f);
                _skeleton.SetBonePoseRotation(_spineBone3, q);
            }
        }
    }
}
