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
    private Node3D? _mp5WorldModel;
    private Node3D? _lmgWorldModel;
    private string _currentEquippedWeapon = "MP5";
    private Node3D? _muzzleMarker;
    private MeshInstance3D? _muzzleFlashMesh;
    private OmniLight3D? _muzzleFlashLight;
    private float _muzzleFlashTimer;

    private int _spineBone2 = -1;
    private int _spineBone3 = -1;
    private int _headBoneIndex = -1;
    private float _aimPitch;

    private string _currentAnim = "";
    private string _activeEmote = "";
    private bool _isDead;
    private int _teamId = 1;

    public event Action? EmoteFinished;

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

    public override void _ExitTree()
    {
        if (_animPlayer != null)
        {
            _animPlayer.AnimationFinished -= OnAnimationFinished;
        }
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

        // Configure animation playback & looping modes
        if (_animPlayer != null)
        {
            _animPlayer.AnimationFinished += OnAnimationFinished;

            var animList = _animPlayer.GetAnimationList();
            foreach (var animName in animList)
            {
                var anim = _animPlayer.GetAnimation(animName);
                if (anim != null)
                {
                    if (animName == "Crouch_Idle" ||
                        animName == "Crouch_Fwd" ||
                        animName == "Pistol_Idle" ||
                        animName == "Walk" ||
                        animName == "Sprint" ||
                        animName == "Dance" ||
                        animName == "Swim_Fwd" ||
                        animName == "Sitting_Idle")
                    {
                        anim.LoopMode = Animation.LoopModeEnum.Linear;
                    }
                }
            }

            PlayAnimation("Pistol_Idle", 0.1f);
        }
    }

    private static readonly Vector3 WeaponRestPosition = new Vector3(0.01f, 0.04f, -0.02f);
    private static readonly Vector3 WeaponRestRotation = new Vector3(-1.24f, -92.68f, -79.32f);

    private void MountWeaponToHand(BoneAttachment3D parent)
    {
        var rifleScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGP7_Rigged.glb");
        if (rifleScene != null)
        {
            _mp5WorldModel = rifleScene.Instantiate<Node3D>();
            _mp5WorldModel.Name = "MGP7_Rifle";
            _mp5WorldModel.Position = WeaponRestPosition;
            _mp5WorldModel.RotationDegrees = WeaponRestRotation;
            _mp5WorldModel.Scale = new Vector3(1.0f, 1.0f, 1.0f);
            parent.AddChild(_mp5WorldModel);
            _weaponModel = _mp5WorldModel;

            // Muzzle marker and dynamic flash light at weapon barrel tip (-X in MP5 model space)
            _muzzleMarker = new Node3D
            {
                Name = "MuzzleMarker",
                Position = new Vector3(-0.41f, 0.08f, 0.0f)
            };
            _mp5WorldModel.AddChild(_muzzleMarker);

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

        var lmgScene = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/LMG.glb");
        if (lmgScene != null)
        {
            _lmgWorldModel = lmgScene.Instantiate<Node3D>();
            _lmgWorldModel.Name = "LMG_Rifle";
            _lmgWorldModel.Position = new Vector3(0.015f, 0.045f, -0.02f);
            _lmgWorldModel.RotationDegrees = new Vector3(-1.24f, 177.32f, -79.32f);
            _lmgWorldModel.Scale = Vector3.One;
            _lmgWorldModel.Visible = false;
            parent.AddChild(_lmgWorldModel);
        }
    }

    public void SetEquippedWeapon(string weaponName)
    {
        _currentEquippedWeapon = weaponName;
        _weaponModel = weaponName == "LMG" ? _lmgWorldModel : _mp5WorldModel;
        bool shouldBeVisible = !_isDead && string.IsNullOrEmpty(_activeEmote);
        UpdateWeaponVisibility(shouldBeVisible);
    }

    public void UpdateWeaponVisibility(bool visible)
    {
        if (!visible)
        {
            if (_mp5WorldModel != null) _mp5WorldModel.Visible = false;
            if (_lmgWorldModel != null) _lmgWorldModel.Visible = false;
        }
        else
        {
            if (_currentEquippedWeapon == "LMG")
            {
                if (_lmgWorldModel != null) _lmgWorldModel.Visible = true;
                if (_mp5WorldModel != null) _mp5WorldModel.Visible = false;
            }
            else
            {
                if (_mp5WorldModel != null) _mp5WorldModel.Visible = true;
                if (_lmgWorldModel != null) _lmgWorldModel.Visible = false;
            }
        }
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

    public void UpdateAnimationState(float horizontalSpeed, bool isOnFloor, bool isDead, bool isMovingBackward = false, bool isCrouched = false, string activeEmote = "")
    {
        _activeEmote = activeEmote;
        if (isDead)
        {
            if (!_isDead)
            {
                _isDead = true;
                PlayAnimation("Death01", 0.15f);
                UpdateWeaponVisibility(false);
            }
            return;
        }

        _isDead = false;

        // If an emote is active, hide weapon and play the emote animation
        if (!string.IsNullOrEmpty(activeEmote))
        {
            UpdateWeaponVisibility(false);

            if (_currentAnim != activeEmote)
            {
                PlayAnimation(activeEmote, 0.2f);
            }
            return;
        }

        // Ensure weapon model is visible when not emoting or dead
        UpdateWeaponVisibility(true);

        string targetAnim;
        float speedScale = 1.0f;
        if (!isOnFloor)
        {
            targetAnim = "Jump";
        }
        else if (isCrouched)
        {
            if (horizontalSpeed > 0.2f)
            {
                targetAnim = "Crouch_Fwd";
                if (isMovingBackward) speedScale = -1.0f;
            }
            else
            {
                targetAnim = "Crouch_Idle";
            }
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

    private void OnAnimationFinished(StringName animName)
    {
        if (animName == "Roll" ||
            animName == "Spell_Simple_Shoot" ||
            animName == "Sword_Attack" ||
            animName == "Punch_Jab" ||
            animName == "Punch_Cross")
        {
            EmoteFinished?.Invoke();
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
        _activeEmote = "";
        UpdateWeaponVisibility(true);
        if (_mp5WorldModel != null)
        {
            _mp5WorldModel.Position = WeaponRestPosition;
            _mp5WorldModel.RotationDegrees = WeaponRestRotation;
        }
        if (_lmgWorldModel != null)
        {
            _lmgWorldModel.Position = new Vector3(0.015f, 0.045f, -0.02f);
            _lmgWorldModel.RotationDegrees = new Vector3(-1.24f, 177.32f, -79.32f);
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
        bool isEmoting = !string.IsNullOrEmpty(_currentAnim) &&
                         (_currentAnim == "Dance" || _currentAnim == "Roll" ||
                          _currentAnim == "Spell_Simple_Shoot" || _currentAnim == "Swim_Fwd" ||
                          _currentAnim == "Sword_Attack" || _currentAnim == "Punch_Jab" ||
                          _currentAnim == "Punch_Cross" || _currentAnim == "Sitting_Idle");

        if (_skeleton != null && !_isDead && !isEmoting)
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
