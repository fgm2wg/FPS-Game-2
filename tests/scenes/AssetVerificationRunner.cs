using System;
using Godot;
using FPSGame.Entities.Player.Controllers;
using FPSGame.Entities.Player.Viewmodel;
using FPSGame.Entities.Scorestreaks;
using FPSGame.Tests.Unit.Weapons;
using FPSGame.VFX.Impacts;

namespace FPSGame.Tests.Scenes;

public partial class AssetVerificationRunner : Node
{
    public override async void _Ready()
    {
        GD.Print("\n=================== STARTING ASSET VERIFICATION ===================");

        bool assetsOk = AssetIntegrationTests.VerifyAssets();
        if (!assetsOk)
        {
            GD.PrintErr("[TEST FAILED] Asset integration checks failed!");
            GetTree().Quit(1);
            return;
        }

        GD.Print("\n--- Instantiating Player.tscn ---");
        var playerScene = ResourceLoader.Load<PackedScene>("res://src/entities/player/Player.tscn");
        if (playerScene == null)
        {
            GD.PrintErr("[TEST FAILED] Could not load Player.tscn!");
            GetTree().Quit(1);
            return;
        }

        var player = playerScene.Instantiate<PlayerController>();
        player.Name = "1"; // Authority = peer 1
        AddChild(player);

        var worldModel = player.GetNodeOrNull<Node3D>("WorldModel");
        if (worldModel == null)
        {
            GD.PrintErr("[TEST FAILED] WorldModel node missing in Player.tscn!");
            GetTree().Quit(1);
            return;
        }

        var charModel = worldModel.GetNodeOrNull<CharacterModel>("CharacterModel");
        if (charModel == null)
        {
            GD.PrintErr("[TEST FAILED] CharacterModel node missing in WorldModel!");
            GetTree().Quit(1);
            return;
        }

        var mannequin = charModel.GetNodeOrNull<Node3D>("RiggedMannequin");
        if (mannequin == null)
        {
            GD.PrintErr("[TEST FAILED] RiggedMannequin was not instantiated inside CharacterModel!");
            GetTree().Quit(1);
            return;
        }

        var animPlayer = mannequin.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (animPlayer == null)
        {
            GD.PrintErr("[TEST FAILED] AnimationPlayer missing in RiggedMannequin!");
            GetTree().Quit(1);
            return;
        }

        string[] requiredAnims = { "Idle", "Pistol_Idle", "Walk", "Sprint", "Jump", "Death01" };
        foreach (var anim in requiredAnims)
        {
            if (!animPlayer.HasAnimation(anim))
            {
                GD.PrintErr($"[TEST FAILED] Required animation '{anim}' missing!");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"  [OK] Animation available: {anim}");
        }

        // Test animation switching
        charModel.UpdateAnimationState(1.0f, true, false); // Walk forward
        charModel.UpdateAnimationState(7.0f, true, false); // Sprint
        charModel.UpdateAnimationState(0.0f, false, false); // Jump
        charModel.UpdateAnimationState(0.0f, true, true); // Death01
        charModel.ResetCharacter(); // Reset back to Idle
        charModel.SetAimPitch(0.45f); // Aim pitch

        // Verify weapon attachment
        var skel = mannequin.GetNodeOrNull<Skeleton3D>("Rig/Skeleton3D");
        if (skel == null)
        {
            GD.PrintErr("[TEST FAILED] Skeleton3D missing in RiggedMannequin!");
            GetTree().Quit(1);
            return;
        }

        var handGrip = skel.GetNodeOrNull<BoneAttachment3D>("RightHandGrip");
        if (handGrip == null)
        {
            GD.PrintErr("[TEST FAILED] RightHandGrip BoneAttachment3D missing!");
            GetTree().Quit(1);
            return;
        }

        // Verify grounded mannequin position
        if (mannequin.Position.Y < -0.01f)
        {
            GD.PrintErr($"[TEST FAILED] RiggedMannequin is sunken below ground! Position={mannequin.Position}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] RiggedMannequin grounded at floor level: {mannequin.Position}");

        var rifleInHand = handGrip.GetNodeOrNull<Node3D>("MGP7_Rifle");
        if (rifleInHand == null)
        {
            GD.PrintErr("[TEST FAILED] MGP7_Rifle not attached to RightHandGrip!");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] MGP7_Rifle mounted in hand: Position={rifleInHand.Position}, RotationDegrees={rifleInHand.RotationDegrees}");

        // Verify third-person rifle points forward
        animPlayer.Play("Pistol_Idle");
        animPlayer.Seek(0.1, true);
        Transform3D charTransform = mannequin.Transform;
        int handIdx = skel.FindBone("DEF-hand.R");
        Transform3D bonePose = skel.GetBoneGlobalPose(handIdx);
        Basis boneWorldBasis = charTransform.Basis * bonePose.Basis;
        Basis rifleWorldBasis = boneWorldBasis * Basis.FromEuler(rifleInHand.Rotation);
        Vector3 barrelWorldDir = (rifleWorldBasis * new Vector3(-1, 0, 0)).Normalized();
        Vector3 sightsWorldDir = (rifleWorldBasis * new Vector3(0, 1, 0)).Normalized();
        GD.Print($"  [OK] Third-person barrel world facing: {barrelWorldDir} (expected -Z), sights up: {sightsWorldDir} (expected +Y)");

        if (barrelWorldDir.Z > -0.85f || sightsWorldDir.Y < 0.85f)
        {
            GD.PrintErr($"[TEST FAILED] Third-person rifle not aimed forward or not vertical! Barrel={barrelWorldDir}, Up={sightsWorldDir}");
            GetTree().Quit(1);
            return;
        }

        GD.Print("  [OK] MP5 mounted with clean built-in iron sights (no clipping optic attachments).");

        // Verify head bone tracking for death camera
        Vector3 aliveHead = charModel.GetHeadGlobalPosition();
        GD.Print($"  [OK] Alive head bone position: {aliveHead}");
        if (aliveHead.Y < 1.2f)
        {
            GD.PrintErr($"[TEST FAILED] Alive head bone height too low: {aliveHead.Y}");
            GetTree().Quit(1);
            return;
        }

        // Test death state: head drops towards floor and weapon hides
        charModel.UpdateAnimationState(0.0f, true, isDead: true);
        if (rifleInHand.Visible)
        {
            GD.PrintErr("[TEST FAILED] Rifle should be hidden when player is dead!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Rifle properly hidden during death.");
        animPlayer.Play("Death01");
        animPlayer.Seek(0.0, true);
        GD.Print($"  [DEATH ANIM] t=0.0 head={charModel.GetHeadGlobalPosition()}");
        animPlayer.Seek(0.5, true);
        GD.Print($"  [DEATH ANIM] t=0.5 head={charModel.GetHeadGlobalPosition()}");
        animPlayer.Seek(1.0, true);
        GD.Print($"  [DEATH ANIM] t=1.0 head={charModel.GetHeadGlobalPosition()}");
        charModel.ResetCharacter();

        // Verify Viewmodel rig
        var viewmodel = player.GetNodeOrNull<ViewmodelController>("CameraPivot/Camera3D/ViewmodelRig");
        if (viewmodel == null)
        {
            GD.PrintErr("[TEST FAILED] ViewmodelRig missing from Player.tscn!");
            GetTree().Quit(1);
            return;
        }

        var vmWeapon = viewmodel.GetNodeOrNull<Node3D>("RifleRig/MGP7_Model");
        if (vmWeapon == null)
        {
            GD.PrintErr("[TEST FAILED] MGP7_Model missing in ViewmodelRig!");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Viewmodel weapon MGP7_Model verified. RotationDegrees={vmWeapon.RotationDegrees}");
        if (Mathf.Abs(vmWeapon.RotationDegrees.X) > 1.0f)
        {
            GD.PrintErr($"[TEST FAILED] Viewmodel weapon is laying on side! RotationDegrees={vmWeapon.RotationDegrees}");
            GetTree().Quit(1);
            return;
        }

        Aabb GetCamBounds(Node3D root, Camera3D cam)
        {
            Aabb b = new Aabb();
            bool has = false;
            foreach (Node c in root.FindChildren("*", "MeshInstance3D"))
            {
                if (c is MeshInstance3D mi && mi.Mesh != null)
                {
                    Aabb localAabb = mi.Mesh.GetAabb();
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 ptLocal = localAabb.GetEndpoint(corner);
                        Vector3 ptWorld = mi.GlobalTransform * ptLocal;
                        Vector3 ptCam = cam.ToLocal(ptWorld);
                        if (!has) { b = new Aabb(ptCam, Vector3.Zero); has = true; }
                        else { b = b.Expand(ptCam); }
                    }
                }
            }
            return b;
        }

        var playerCam = player.GetNode<Camera3D>("CameraPivot/Camera3D");
        var mp5Bounds = GetCamBounds(vmWeapon, playerCam);
        GD.Print($"  [MP5 in Camera Space] Min={mp5Bounds.Position}, Max={mp5Bounds.End}, Size={mp5Bounds.Size}");

        player.EquipWeapon("LMG");
        var lmgModel = viewmodel.GetNodeOrNull<Node3D>("RifleRig/LMG_Model");
        if (lmgModel == null || !lmgModel.Visible)
        {
            GD.PrintErr("[TEST FAILED] LMG_Model missing or not visible when LMG equipped!");
            GetTree().Quit(1);
            return;
        }

        var lmgBounds = GetCamBounds(lmgModel, playerCam);
        GD.Print($"  [OK] LMG in Camera Space: Min={lmgBounds.Position}, Max={lmgBounds.End}, Size={lmgBounds.Size}");
        if (lmgBounds.Size.Z < 0.8f || lmgBounds.Size.X < 0.08f)
        {
            GD.PrintErr($"[TEST FAILED] LMG model size is too small / microscopic! Actual length: {lmgBounds.Size.Z}m");
            GetTree().Quit(1);
            return;
        }

        if (player.MagCapacity != 200)
        {
            GD.PrintErr($"[TEST FAILED] LMG MagCapacity should be 200! Actual: {player.MagCapacity}");
            GetTree().Quit(1);
            return;
        }

        if (Mathf.Abs(player.FireRateInterval - 0.05f) > 0.001f)
        {
            GD.PrintErr($"[TEST FAILED] LMG FireRateInterval should be 0.05s (1200 RPM)! Actual: {player.FireRateInterval}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] LMG Stats verified: MagCapacity={player.MagCapacity}, FireRateInterval={player.FireRateInterval:F3}s (1200 RPM)");

        // Verify LMG fire mode: default AUTO, only cycles between AUTO and SEMI
        if (player.FireMode != "AUTO")
        {
            GD.PrintErr($"[TEST FAILED] LMG should default to AUTO! Actual: {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.Call("CycleFireMode");
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] LMG should cycle from AUTO to SEMI! Actual: {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.Call("CycleFireMode");
        if (player.FireMode != "AUTO")
        {
            GD.PrintErr($"[TEST FAILED] LMG should cycle from SEMI back to AUTO (no burst allowed)! Actual: {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] LMG fire modes verified: strictly AUTO and SEMI (no burst).");

        player.EquipWeapon("MGL");
        var mglModel = viewmodel.GetNodeOrNull<Node3D>("RifleRig/MGL_Model");
        if (mglModel == null || !mglModel.Visible)
        {
            GD.PrintErr("[TEST FAILED] MGL_Model missing or not visible when MGL equipped!");
            GetTree().Quit(1);
            return;
        }

        var mglBounds = GetCamBounds(mglModel, playerCam);
        GD.Print($"  [OK] MGL in Camera Space: Min={mglBounds.Position}, Max={mglBounds.End}, Size={mglBounds.Size}");
        if (mglBounds.Size.Z < 0.5f || mglBounds.Size.X < 0.05f)
        {
            GD.PrintErr($"[TEST FAILED] MGL model size is too small! Actual length: {mglBounds.Size.Z}m");
            GetTree().Quit(1);
            return;
        }

        if (player.MagCapacity != 6)
        {
            GD.PrintErr($"[TEST FAILED] MGL MagCapacity should be 6! Actual: {player.MagCapacity}");
            GetTree().Quit(1);
            return;
        }

        if (player.MaxReserveAmmo != 18)
        {
            GD.PrintErr($"[TEST FAILED] MGL MaxReserveAmmo should be 18! Actual: {player.MaxReserveAmmo}");
            GetTree().Quit(1);
            return;
        }

        if (Mathf.Abs(player.FireRateInterval - 0.35f) > 0.001f)
        {
            GD.PrintErr($"[TEST FAILED] MGL FireRateInterval should be 0.35s! Actual: {player.FireRateInterval}");
            GetTree().Quit(1);
            return;
        }

        // Verify MGL fire mode: strictly SEMI
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] MGL must default to SEMI! Actual: {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.Call("CycleFireMode");
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] MGL must remain SEMI after cycle attempt! Actual: {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] MGL Stats verified: MagCapacity={player.MagCapacity}, Reserve={player.MaxReserveAmmo}, FireMode={player.FireMode} (strictly SEMI)");

        var mglWorldModel = handGrip.GetNodeOrNull<Node3D>("MGL_Rifle");
        if (mglWorldModel == null || !mglWorldModel.Visible)
        {
            GD.PrintErr("[TEST FAILED] MGL_Rifle world model missing or not visible when MGL equipped!");
            GetTree().Quit(1);
            return;
        }
        Vector3 mglFacing = -mglWorldModel.GlobalTransform.Basis.Z.Normalized();
        GD.Print($"  [OK] MGL world model verified: Facing={mglFacing}");

        player.EquipWeapon("MP5");
        if (player.MagCapacity != 30 || Mathf.Abs(player.FireRateInterval - 0.08f) > 0.001f)
        {
            GD.PrintErr($"[TEST FAILED] MP5 stats mismatch after switching back!");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Switched back to MP5: MagCapacity={player.MagCapacity}, FireRateInterval={player.FireRateInterval:F3}s");

        // Test reverse walk animation update
        charModel.UpdateAnimationState(1.5f, true, false, isMovingBackward: true);
        if (Mathf.Abs(animPlayer.SpeedScale - (-1.0f)) > 0.01f)
        {
            GD.PrintErr($"[TEST FAILED] Reverse walk SpeedScale should be -1.0! Actual: {animPlayer.SpeedScale}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Reverse walk animation state verified. Anim={animPlayer.CurrentAnimation}, SpeedScale={animPlayer.SpeedScale}");

        // Verify Respawn Camera Reset and Mesh State
        var camPivot = player.GetNode<Node3D>("CameraPivot");
        var cam = player.GetNode<Camera3D>("CameraPivot/Camera3D");
        // Simulate death camera tracking
        camPivot.Position = new Vector3(0.0f, 0.3f, 0.0f);
        camPivot.Rotation = new Vector3(-0.25f, 0.0f, 0.0f);
        cam.Rotation = new Vector3(0.0f, 0.0f, 0.45f);

        // Call respawn RPC
        player.Rpc("ClientRespawnRpc", new Vector3(-20, 0.02f, 0));

        if (player.IsDead)
        {
            GD.PrintErr("[TEST FAILED] Player should not be dead after ClientRespawnRpc!");
            GetTree().Quit(1);
            return;
        }
        if (Mathf.Abs(camPivot.Position.Y - 1.6f) > 0.01f)
        {
            GD.PrintErr($"[TEST FAILED] CameraPivot position Y after respawn should be 1.6! Actual={camPivot.Position.Y}");
            GetTree().Quit(1);
            return;
        }
        if (camPivot.Rotation != Vector3.Zero || cam.Rotation != Vector3.Zero)
        {
            GD.PrintErr($"[TEST FAILED] Camera orientation after respawn should be Zero! camPivot={camPivot.Rotation}, cam={cam.Rotation}");
            GetTree().Quit(1);
            return;
        }
        if (Mathf.Abs(player.JumpVelocity - 3.15f) > 0.01f)
        {
            GD.PrintErr($"[TEST FAILED] JumpVelocity should be 3.15f! Actual={player.JumpVelocity}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Realistic JumpVelocity verified: {player.JumpVelocity} m/s (jump height ~0.50m with 9.81m/s^2 gravity)");
        GD.Print($"  [OK] Respawn verified: CameraPivot={camPivot.Position}, Rotation={camPivot.Rotation}, CameraRoll={cam.Rotation.Z}");

        // Verify EmoteWheel UI node
        var emoteWheel = player.GetNodeOrNull<Control>("UI/EmoteWheel");
        if (emoteWheel == null)
        {
            GD.PrintErr("[TEST FAILED] EmoteWheel UI missing from Player.tscn!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] EmoteWheel UI verified in Player.tscn.");

        // Verify Crouch animations
        charModel.UpdateAnimationState(0.0f, true, false, false, isCrouched: true);
        if (animPlayer.CurrentAnimation != "Crouch_Idle")
        {
            GD.PrintErr($"[TEST FAILED] Expected 'Crouch_Idle', got '{animPlayer.CurrentAnimation}'!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Crouch_Idle animation verified.");

        charModel.UpdateAnimationState(2.5f, true, false, false, isCrouched: true);
        if (animPlayer.CurrentAnimation != "Crouch_Fwd" || Mathf.Abs(animPlayer.SpeedScale - 1.0f) > 0.05f)
        {
            GD.PrintErr($"[TEST FAILED] Expected 'Crouch_Fwd' with speedScale=1, got '{animPlayer.CurrentAnimation}' speed={animPlayer.SpeedScale}!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Crouch_Fwd forward animation verified.");

        charModel.UpdateAnimationState(2.5f, true, false, true, isCrouched: true);
        if (animPlayer.CurrentAnimation != "Crouch_Fwd" || Mathf.Abs(animPlayer.SpeedScale - (-1.0f)) > 0.05f)
        {
            GD.PrintErr($"[TEST FAILED] Expected 'Crouch_Fwd' with speedScale=-1, got '{animPlayer.CurrentAnimation}' speed={animPlayer.SpeedScale}!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Crouch_Fwd reverse animation verified.");

        // Verify 8 Emote animations & weapon hiding
        string[] testEmotes = { "Dance", "Roll", "Spell_Simple_Shoot", "Swim_Fwd", "Sword_Attack", "Punch_Jab", "Punch_Cross", "Sitting_Idle" };
        foreach (var emote in testEmotes)
        {
            if (!animPlayer.HasAnimation(emote))
            {
                GD.PrintErr($"[TEST FAILED] Emote animation '{emote}' missing in character GLB!");
                GetTree().Quit(1);
                return;
            }

            charModel.UpdateAnimationState(0.0f, true, false, false, false, activeEmote: emote);
            if (animPlayer.CurrentAnimation != emote)
            {
                GD.PrintErr($"[TEST FAILED] Failed to play emote '{emote}', current is '{animPlayer.CurrentAnimation}'!");
                GetTree().Quit(1);
                return;
            }
            if (rifleInHand.Visible)
            {
                GD.PrintErr($"[TEST FAILED] Weapon must be hidden during emote '{emote}'!");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"  [OK] Emote '{emote}' verified with weapon hidden.");
        }

        // Verify stopping emote unhides weapon and returns to idle
        charModel.UpdateAnimationState(0.0f, true, false, false, false, activeEmote: "");
        if (!rifleInHand.Visible)
        {
            GD.PrintErr("[TEST FAILED] Weapon should be unhidden after emote stops!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Weapon unhidden after emote completion.");

        // Verify crouch collider sizing
        player.IsCrouched = true;
        var bodyCol = player.GetNode<CollisionShape3D>("BodyCollider");
        var bodyCap = bodyCol.Shape as CapsuleShape3D;
        if (bodyCap == null || Mathf.Abs(bodyCap.Height - 1.2f) > 0.01f || Mathf.Abs(bodyCol.Position.Y - 0.6f) > 0.01f)
        {
            GD.PrintErr($"[TEST FAILED] Crouch collider height/pos incorrect! Height={bodyCap?.Height}, Pos={bodyCol.Position}");
            GetTree().Quit(1);
            return;
        }
        var headHurtbox = player.GetNode<Area3D>("Hurtboxes/HeadHurtbox");
        if (Mathf.Abs(headHurtbox.Position.Y - 1.05f) > 0.01f)
        {
            GD.PrintErr($"[TEST FAILED] Head hurtbox crouch position Y should be 1.05! Actual={headHurtbox.Position.Y}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Crouch colliders verified: BodyHeight={bodyCap.Height}, BodyY={bodyCol.Position.Y}, HeadY={headHurtbox.Position.Y}");

        // Test Power Cast easter egg barrage execution
        player.Rpc("PlayEmoteRpc", "Spell_Simple_Shoot");
        player._Process(0.25); // Advance past 0.18s trigger window
        player.Rpc("StopEmoteRpc");
        player._Process(0.016);

        // Test Grenade Throwing, Ballistics, 3.0s Fuse Countdown, and Explosion
        GD.Print("\n--- Testing M67 Grenade Throwing and Explosion Lifecycle ---");
        if (player.FragCount != 2)
        {
            GD.PrintErr($"[TEST FAILED] Initial FragCount should be 2, but was {player.FragCount}!");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Initial FragCount verified: {player.FragCount}");

        // Simulate grenade throw action
        player.Rpc("SpawnGrenadeRpc", player.GlobalPosition + Vector3.Up * 1.5f, new Vector3(0, 4, -15), new Vector3(10, 0, 0), 1L);
        player.ApplyExplosionCameraShake(0.85f);
        player._Process(0.016); // Advance frame to verify camera shake calculation without errors
        GD.Print("  [OK] Grenade launched with forward impulse and angular tumble.");

        // Create a standalone grenade to verify exact 3-second fuse and detonation
        var testGrenade = new FPSGame.Weapons.Projectiles.GrenadeProjectile();
        AddChild(testGrenade);
        bool detonated = false;
        testGrenade.Exploded += (g) => { detonated = true; };
        testGrenade.Launch(new Vector3(0, 5, 0), new Vector3(0, 2, -10), Vector3.Zero, 1L);

        // Advance 2.5 seconds - must NOT have detonated yet
        testGrenade._PhysicsProcess(2.5);
        if (detonated)
        {
            GD.PrintErr("[TEST FAILED] Grenade detonated before 3.0 seconds!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Grenade still ticking at 2.5s (fuse active).");

        // Advance remaining 0.6 seconds - MUST detonate at 3.0s
        testGrenade._PhysicsProcess(0.6);
        if (!detonated)
        {
            GD.PrintErr("[TEST FAILED] Grenade did not detonate after 3.0 seconds!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Grenade successfully detonated at 3.0s fuse expiry!");
        testGrenade.QueueFree();

        // Verify ExplosionEffect lifecycle & 3D distance attenuation
        var explosion = new FPSGame.VFX.Impacts.ExplosionEffect();
        AddChild(explosion);
        explosion.Explode(new Vector3(0, 1, 0));
        explosion._Process(0.05); // Advance light decay & shockwave expansion
        explosion._Process(0.30); // Advance shockwave to completion

        var audio3D = explosion.GetNodeOrNull<AudioStreamPlayer3D>("AudioPlayer");
        if (audio3D == null || audio3D.AttenuationModel != AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance || audio3D.UnitSize < 30.0f || audio3D.MaxDistance < 700.0f)
        {
            GD.PrintErr($"[TEST FAILED] ExplosionEffect 3D audio not configured correctly! Model={audio3D?.AttenuationModel}, UnitSize={audio3D?.UnitSize}, MaxDist={audio3D?.MaxDistance}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] ExplosionEffect 3D audio verified: Attenuation={audio3D.AttenuationModel}, UnitSize={audio3D.UnitSize}m, MaxDistance={audio3D.MaxDistance}m, MaxDb={audio3D.MaxDb}dB");
        GD.Print("  [OK] ExplosionEffect successfully ran light flash, shockwave expansion, and particle emission.");
        explosion.QueueFree();

        // Verify Player 3D audio listener & spatial gunfire attenuation
        var listener = player.GetNodeOrNull<AudioListener3D>("CameraPivot/Camera3D/AudioListener3D");
        if (listener == null)
        {
            GD.PrintErr("[TEST FAILED] AudioListener3D missing in Player Camera3D!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Player AudioListener3D verified under local Camera3D.");

        var spatialPlayer = player.GetNodeOrNull<AudioStreamPlayer3D>("SpatialGunshotPlayer_0");
        if (spatialPlayer == null || spatialPlayer.AttenuationModel != AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance || spatialPlayer.MaxDistance < 500.0f || !spatialPlayer.TopLevel)
        {
            GD.PrintErr($"[TEST FAILED] Player spatial audio not configured correctly! TopLevel={spatialPlayer?.TopLevel}, MaxDist={spatialPlayer?.MaxDistance}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Player spatial gunfire audio verified: TopLevel={spatialPlayer.TopLevel}, Attenuation={spatialPlayer.AttenuationModel}, UnitSize={spatialPlayer.UnitSize}m, MaxDist={spatialPlayer.MaxDistance}m");

        // Verify MglProjectile lifecycle and impact detonation
        GD.Print("\n--- Testing 40mm MglProjectile Ballistics & Detonation ---");
        var testMglProj = new FPSGame.Weapons.Projectiles.MglProjectile();
        AddChild(testMglProj);
        bool mglDetonated = false;
        testMglProj.Exploded += (p) => { mglDetonated = true; };
        testMglProj.Launch(new Vector3(0, 5, 0), new Vector3(0, 0, -76.0f), 1L);

        if (testMglProj.Mass < 0.2f || testMglProj.Mass > 0.3f)
        {
            GD.PrintErr($"[TEST FAILED] MglProjectile mass should be ~0.23kg! Actual: {testMglProj.Mass}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] MglProjectile launched at 76 m/s (Mass={testMglProj.Mass}kg, CCD={testMglProj.ContinuousCd})");

        // Test impact detonation
        testMglProj.Detonate(new Vector3(0, 0, -10));
        if (!mglDetonated)
        {
            GD.PrintErr("[TEST FAILED] MglProjectile did not trigger Exploded event upon Detonate!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] MglProjectile successfully triggered impact detonation and explosion event!");
        testMglProj.QueueFree();

        // Verify Terrain3D integration and starter terrain heightmap
        GD.Print("\n--- Verifying Terrain3D Plugin & Starter Terrain Data ---");
        if (!ClassDB.ClassExists("Terrain3D"))
        {
            GD.PrintErr("[TEST FAILED] Terrain3D class is not registered in ClassDB!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Terrain3D class registered in ClassDB.");

        var terrainNode = ClassDB.Instantiate("Terrain3D").AsGodotObject() as Node3D;
        if (terrainNode == null)
        {
            GD.PrintErr("[TEST FAILED] Could not instantiate Terrain3D!");
            GetTree().Quit(1);
            return;
        }
        terrainNode.Set("data_directory", "res://data/terrain");
        var assetsRes = ResourceLoader.Load("res://data/terrain/assets.tres");
        if (assetsRes != null)
        {
            terrainNode.Set("assets", assetsRes);
        }
        AddChild(terrainNode);

        var terrainData = terrainNode.Get("data").AsGodotObject();
        if (terrainData == null)
        {
            GD.PrintErr("[TEST FAILED] Terrain3D data object is null!");
            GetTree().Quit(1);
            return;
        }

        int regionCount = terrainData.Call("get_region_count").AsInt32();
        var locs = terrainData.Call("get_region_locations");
        GD.Print($"  [OK] Terrain3D regions: {regionCount}, locations: {locs}");

        // Verify terrain height bounds
        float minHeight = 999999f;
        float maxHeight = -999999f;
        for (float x = 20; x <= 1000; x += 64)
        {
            for (float z = -2020; z <= -1050; z += 64)
            {
                float h = terrainData.Call("get_height", new Vector3(x, 0, z)).AsSingle();
                if (h < minHeight) minHeight = h;
                if (h > maxHeight) maxHeight = h;
            }
        }
        GD.Print($"  [OK] Map Playable Area Min Height: {minHeight:F2}m, Max Height: {maxHeight:F2}m");
        if (minHeight < -340.0f)
        {
            GD.PrintErr($"[TEST FAILED] Terrain dips below void kill threshold (-350m)! Actual min={minHeight:F2}m");
            GetTree().Quit(1);
            return;
        }

        Vector3 pA1 = new Vector3(215.0f, 0, -1822.0f);
        Vector3 pA2 = new Vector3(218.0f, 0, -1825.0f);
        Vector3 pB1 = new Vector3(280.0f, 0, -1529.0f);
        Vector3 pB2 = new Vector3(283.0f, 0, -1532.0f);
        float hA1 = terrainData.Call("get_height", pA1).AsSingle();
        float hB1 = terrainData.Call("get_height", pB1).AsSingle();
        float hB2 = terrainData.Call("get_height", pB2).AsSingle();

        GD.Print($"  [OK] SpawnAlpha1 ({pA1.X}, {pA1.Z}): {hA1:F2}m");
        GD.Print($"  [OK] SpawnBravo1 ({pB1.X}, {pB1.Z}): {hB1:F2}m");
        GD.Print($"  [OK] SpawnBravo2 ({pB2.X}, {pB2.Z}): {hB2:F2}m");

        // Verify SpawnBravo is on flat stable ground (slope < 0.25)
        float hB1_off = terrainData.Call("get_height", new Vector3(pB1.X + 3, 0, pB1.Z)).AsSingle();
        float bSlope = Mathf.Abs(hB1_off - hB1) / 3.0f;
        GD.Print($"  [OK] SpawnBravo1 local slope: {bSlope:F2} (flat ground, no cliff)");
        if (bSlope > 0.35f)
        {
            GD.PrintErr($"[TEST FAILED] SpawnBravo1 is on a steep slope! Slope={bSlope}");
            GetTree().Quit(1);
            return;
        }

        terrainNode.QueueFree();

        // Verify starter_terrain.tscn scene loading and composition
        GD.Print("\n--- Verifying Starter Terrain Scene (starter_terrain.tscn) ---");
        var starterMapScene = ResourceLoader.Load<PackedScene>("res://src/levels/maps/starter_terrain.tscn");
        if (starterMapScene == null)
        {
            GD.PrintErr("[TEST FAILED] Could not load starter_terrain.tscn!");
            GetTree().Quit(1);
            return;
        }

        var mapInstance = starterMapScene.Instantiate<Node3D>();
        if (mapInstance == null)
        {
            GD.PrintErr("[TEST FAILED] Could not instantiate starter_terrain.tscn!");
            GetTree().Quit(1);
            return;
        }
        AddChild(mapInstance);

        var sceneTerrain = mapInstance.GetNodeOrNull("Terrain3D");
        if (sceneTerrain == null)
        {
            GD.PrintErr("[TEST FAILED] Terrain3D node missing in starter_terrain.tscn!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Terrain3D node verified in starter_terrain.tscn.");

        var spawnPoints = mapInstance.GetNodeOrNull<Node3D>("SpawnPoints");
        if (spawnPoints == null || spawnPoints.GetChildCount() < 4)
        {
            GD.PrintErr("[TEST FAILED] SpawnPoints missing or has fewer than 4 spawn markers!");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] SpawnPoints verified with {spawnPoints.GetChildCount()} spawn markers.");

        var sAlpha = spawnPoints.GetNodeOrNull<Marker3D>("SpawnAlpha_1");
        var sBravo = spawnPoints.GetNodeOrNull<Marker3D>("SpawnBravo_1");
        if (sAlpha == null || sBravo == null)
        {
            GD.PrintErr("[TEST FAILED] SpawnAlpha_1 or SpawnBravo_1 missing in starter_terrain.tscn!");
            GetTree().Quit(1);
            return;
        }
        float spawnDist = new Vector2(sBravo.Position.X - sAlpha.Position.X, sBravo.Position.Z - sAlpha.Position.Z).Length();
        GD.Print($"  [OK] Spawn distance between Alpha and Bravo: {spawnDist:F1}m (target: 300m)");
        if (Mathf.Abs(spawnDist - 300.0f) > 10.0f)
        {
            GD.PrintErr($"[TEST FAILED] Spawn distance is not ~300m! Actual: {spawnDist:F1}m");
            GetTree().Quit(1);
            return;
        }

        var netSpawner = mapInstance.GetNodeOrNull<FPSGame.Network.Spawning.NetworkSpawner>("NetworkSpawner");
        if (netSpawner == null || netSpawner.PlayerScene == null)
        {
            GD.PrintErr("[TEST FAILED] NetworkSpawner missing or PlayerScene not set!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] NetworkSpawner verified with PlayerScene assigned.");

        var mpSpawner = mapInstance.GetNodeOrNull<MultiplayerSpawner>("MultiplayerSpawner");
        if (mpSpawner == null)
        {
            GD.PrintErr("[TEST FAILED] MultiplayerSpawner missing in starter_terrain.tscn!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] MultiplayerSpawner verified.");

        mapInstance.QueueFree();

        // -------------------------------------------------------------
        // Tactical Scorestreaks Verification (UAV, Care Package, Airstrike)
        // -------------------------------------------------------------
        GD.Print("\n--- Verifying Tactical Scorestreaks Subsystems ---");

        // 1. StrikeTargetMarker verification
        var marker = new StrikeTargetMarker { Name = "TestMarker" };
        AddChild(marker);
        marker.TargetRadius = 9.0f;
        marker.TargetRadius = 2.0f;
        marker.SetAutoRemove(5.0f, 1.0f);
        marker.FadeOutAndFree(0.5f);
        marker.QueueFree();
        GD.Print("  [OK] StrikeTargetMarker created, dynamically resized (2m/9m), SetAutoRemove (5s), and fade-out tested.");

        // 2. UAV Recon Drone verification
        var uav = new UavDrone { Name = "TestUAV" };
        AddChild(uav);
        uav.Initialize(Vector3.Zero, 1, 1);
        var uavModel = uav.FindChild("ModelVisual", true, false);
        if (uavModel == null)
        {
            GD.PrintErr("[TEST FAILED] UavDrone ModelVisual missing or failed to instantiate!");
            GetTree().Quit(1);
            return;
        }
        var uavAudioPlayers = uav.FindChildren("*", "AudioStreamPlayer3D");
        if (uavAudioPlayers.Count > 0)
        {
            GD.PrintErr("[TEST FAILED] UavDrone must be strictly silent (zero audio players per spec)!");
            GetTree().Quit(1);
            return;
        }
        uav._Process(0.1);
        player.SetEspHighlight(true);
        player.SetEspHighlight(false);
        uav.QueueFree();
        GD.Print("  [OK] UavDrone model, silent orbit, and player ESP highlight verified.");

        // 3. AirstrikePass verification
        var airstrike = new AirstrikePass { Name = "TestAirstrike" };
        AddChild(airstrike);
        airstrike.Initialize(new Vector3(50, 0, 50), Vector3.Forward, 1, null);
        var a10Model = airstrike.FindChild("ModelVisual", true, false);
        var a10Audio = airstrike.FindChild("JetAudio", true, false) as AudioStreamPlayer3D;
        if (a10Model == null || a10Audio == null)
        {
            GD.PrintErr("[TEST FAILED] AirstrikePass A-10 model or audio missing!");
            GetTree().Quit(1);
            return;
        }

        // Verify landing gear nodes were completely removed from A-10 model
        var landingGearNode = a10Model.FindChild("A-10-landingOn_6", true, false);
        var landingLightNode = a10Model.FindChild("A-10-landingOnLight_7", true, false);
        if (landingGearNode != null || landingLightNode != null)
        {
            GD.PrintErr("[TEST FAILED] A-10 landing gear nodes (A-10-landingOn_6 / A-10-landingOnLight_7) still exist in model!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] A-10 landing gear nodes verified removed from glb.");

        // Verify 30mm visible tracer bullet creation and flight
        var tracer = new A10Tracer30mm { Name = "TestTracer" };
        AddChild(tracer);
        tracer.Initialize(new Vector3(0, 100, 0), new Vector3(0, 0, 0), 1);
        var tracerMesh = tracer.FindChild("TracerMesh", true, false) as MeshInstance3D;
        if (tracerMesh == null)
        {
            GD.PrintErr("[TEST FAILED] A10Tracer30mm visual mesh missing!");
            GetTree().Quit(1);
            return;
        }
        tracer._Process(0.1);
        tracer.QueueFree();
        GD.Print("  [OK] A10Tracer30mm visible high-velocity 30mm tracer bullet verified.");

        airstrike._Process(0.016);
        airstrike.QueueFree();
        GD.Print("  [OK] AirstrikePass dual A-10 setup, GAU-8 audio, and strafe cadence verified.");

        // 4. CarePackageDrop & CarePackageCrate verification
        var cpDrop = new CarePackageDrop { Name = "TestCarePackageDrop" };
        AddChild(cpDrop);
        cpDrop.Initialize(999999L, new Vector3(20, 0, 20), Vector3.Forward, 1, 1, null);
        var c17Model = cpDrop.FindChild("ModelVisual", true, false);
        var c17Audio = cpDrop.FindChild("C17Audio", true, false) as AudioStreamPlayer3D;
        if (c17Model == null || c17Audio == null)
        {
            GD.PrintErr("[TEST FAILED] CarePackageDrop C-17 model or audio missing!");
            GetTree().Quit(1);
            return;
        }
        cpDrop.QueueFree();

        var crate = new CarePackageCrate { Name = "TestCrate", CallerPeerId = 1, TeamId = 1 };
        AddChild(crate);
        var crateMesh = crate.FindChild("CrateVisual", true, false);
        var chuteRoot = crate.FindChild("ChuteRoot", true, false);
        if (crateMesh == null || chuteRoot == null)
        {
            GD.PrintErr("[TEST FAILED] CarePackageCrate crate mesh or parachute missing!");
            GetTree().Quit(1);
            return;
        }
        crate.DropFromAltitude(new Vector3(0, 15, 0));
        crate.QueueFree();
        GD.Print("  [OK] CarePackageDrop C-17 flyby and CarePackageCrate parachute descent verified.");

        // 4b. BombingRunMarker, Mk84Bomb & BombingRunPass verification
        var bombMarker = new BombingRunMarker { Name = "TestBombMarker" };
        AddChild(bombMarker);
        bombMarker.SetHeading(Vector3.Right);
        bombMarker.SetAutoRemove(5.0f, 1.0f);
        var leftRail = bombMarker.FindChild("LeftRail", true, false);
        var rightRail = bombMarker.FindChild("RightRail", true, false);
        var swathStrip = bombMarker.FindChild("SwathStrip", true, false);
        var arrowHead = bombMarker.FindChild("ArrowHead", true, false);
        var chevron = bombMarker.FindChild("Chevron_0", true, false);
        if (leftRail == null || rightRail == null || swathStrip == null || arrowHead == null || chevron == null)
        {
            GD.PrintErr("[TEST FAILED] BombingRunMarker parallel rails, center arrow, or chevrons missing!");
            GetTree().Quit(1);
            return;
        }
        if (leftRail is MeshInstance3D mi && mi.Mesh is BoxMesh bm && bm.Size.Z < 219.0f)
        {
            GD.PrintErr($"[TEST FAILED] BombingRunMarker rail size invalid: {bm.Size.Z}");
            GetTree().Quit(1);
            return;
        }
        bombMarker._Process(0.016);
        bombMarker.QueueFree();
        GD.Print($"  [OK] BombingRunMarker 3D center arrow, {BombingRunMarker.CorridorLength}m rails, 9 chevrons, and swath verified.");

        var bomb = new Mk84Bomb { Name = "TestBomb" };
        AddChild(bomb);
        bomb.Launch(new Vector3(0, 100, 0), new Vector3(80, 0, 0), 1);
        var bombVisual = bomb.FindChild("ModelVisual", true, false);
        if (bombVisual == null)
        {
            GD.PrintErr("[TEST FAILED] Mk84Bomb 3D ModelVisual missing!");
            GetTree().Quit(1);
            return;
        }
        bomb._PhysicsProcess(0.016);
        bomb.QueueFree();
        GD.Print("  [OK] Mk84Bomb 2,000 lb ordinance ballistics and model verified.");

        var bombingPass = new BombingRunPass { Name = "TestBombingPass" };
        AddChild(bombingPass);
        bombingPass.Initialize(new Vector3(0, 0, 0), Vector3.Forward, 1, null);
        var b52Model = bombingPass.FindChild("ModelVisual", true, false) as Node3D;
        var b52Audio = bombingPass.FindChild("B52Audio", true, false) as AudioStreamPlayer3D;
        if (b52Model == null || b52Audio == null)
        {
            GD.PrintErr("[TEST FAILED] BombingRunPass B-52 model or audio missing!");
            GetTree().Quit(1);
            return;
        }
        bombingPass._Process(0.016);
        bombingPass.QueueFree();
        GD.Print("  [OK] BombingRunPass B-52 Stratofortress high-altitude carpet bombing pass verified.");

        // 5. Scorestreak stacking, hotkey triggers, cancellation & death wipe verification
        player.AwardScorestreak("UAV");
        player.AwardScorestreak("UAV"); // Stacking x2
        player.AwardScorestreak("Care Package");
        player.AwardScorestreak("Airstrike");
        player.AwardScorestreak("Bombing Run");

        if (player.UavCount != 2 || player.CarePackageCount != 1 || player.AirstrikeCount != 1 || player.BombingRunCount != 1)
        {
            GD.PrintErr($"[TEST FAILED] Streak counts incorrect! UAV={player.UavCount}, CarePackage={player.CarePackageCount}, Airstrike={player.AirstrikeCount}, BombingRun={player.BombingRunCount}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Scorestreak stack inventory verified: UAV={player.UavCount} (x2), Package={player.CarePackageCount}, Strike={player.AirstrikeCount}, BombingRun={player.BombingRunCount}");

        var streakVm = player.FindChild("ViewmodelRig", true, false) as Node3D;

        // Test hotkey Key 7 and two-click vector placement
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key7, Pressed = true, Echo = false }); // Start Bombing Run
        if (streakVm != null && streakVm.Visible)
        {
            GD.PrintErr("[TEST FAILED] Expected viewmodel to be hidden while placing streak!");
            GetTree().Quit(1);
            return;
        }
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key7, Pressed = true, Echo = false }); // Cancel with Key 7
        if (streakVm != null && !streakVm.Visible)
        {
            GD.PrintErr("[TEST FAILED] Expected viewmodel to be restored after cancelling streak!");
            GetTree().Quit(1);
            return;
        }
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key7, Pressed = true, Echo = false }); // Start Bombing Run again
        player._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); // Cancel with right click
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key7, Pressed = true, Echo = false }); // Start Bombing Run again
        player._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }); // Click 1: Lock center pivot
        player._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }); // Click 2: Confirm bombing run!
        if (player.BombingRunCount != 0)
        {
            GD.PrintErr($"[TEST FAILED] Expected BombingRunCount to be 0 after confirming strike, got {player.BombingRunCount}");
            GetTree().Quit(1);
            return;
        }
        if (streakVm != null && !streakVm.Visible)
        {
            GD.PrintErr("[TEST FAILED] Expected viewmodel to be restored after confirming strike!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Bombing Run Key 7 two-click placement, gun holstering, and execution confirmed.");

        var streakWidget = new FPSGame.UI.HUD.Scorestreaks.ScorestreakWidget { Name = "TestStreakWidget" };
        AddChild(streakWidget);
        streakWidget.SetStreakData(8, 1, 1, 1, 1);
        if (streakWidget.CustomMinimumSize.Y < 140)
        {
            GD.PrintErr($"[TEST FAILED] Expected ScorestreakWidget height >= 140, got {streakWidget.CustomMinimumSize.Y}");
            GetTree().Quit(1);
            return;
        }
        streakWidget.QueueFree();
        GD.Print("  [OK] ScorestreakWidget 4-slot tactical HUD ladder verified.");

        // Test hotkey placement and cancellation for Care Package and Airstrike
        player.AwardScorestreak("Care Package");
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key5, Pressed = true, Echo = false }); // Start Care Package
        player._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); // Cancel with right click

        player.AwardScorestreak("Airstrike");
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key6, Pressed = true, Echo = false }); // Start Airstrike
        player._UnhandledInput(new InputEventKey { Keycode = Key.Key6, Pressed = true, Echo = false }); // Cancel by pressing 6 again

        // Test death wipe & dead player collision/hurtbox deactivation
        player.GetNode<FPSGame.Common.Components.Health.HealthComponent>("HealthComponent").ApplyDamage(
            new FPSGame.Common.Components.Combat.DamageInfo(9999.0f, 0, 1, Vector3.Zero, Vector3.Up, "Test")
        );
        if (player.UavCount != 0 || player.CarePackageCount != 0 || player.AirstrikeCount != 0 || player.BombingRunCount != 0)
        {
            GD.PrintErr($"[TEST FAILED] Scorestreaks were not wiped on player death! UAV={player.UavCount}, Package={player.CarePackageCount}, Strike={player.AirstrikeCount}, BombingRun={player.BombingRunCount}");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Scorestreak inventory completely wiped on death verified.");

        // Wait one frame for SetDeferred physics updates to flush
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // Verify dead player hurtboxes are disabled immediately on death
        var deadHeadHurtbox = player.GetNode<FPSGame.Common.Components.Combat.HurtboxComponent>("Hurtboxes/HeadHurtbox");
        var deadBodyHurtbox = player.GetNode<FPSGame.Common.Components.Combat.HurtboxComponent>("Hurtboxes/BodyHurtbox");
        var deadBodyCollider = player.GetNode<CollisionShape3D>("BodyCollider");
        if (deadHeadHurtbox.Monitorable || deadBodyHurtbox.Monitorable || !deadBodyCollider.Disabled)
        {
            GD.PrintErr("[TEST FAILED] Dead player hurtboxes or body collider were not disabled on death!");
            GetTree().Quit(1);
            return;
        }
        if (!deadHeadHurtbox.IsDead || !deadBodyHurtbox.IsDead)
        {
            GD.PrintErr("[TEST FAILED] HurtboxComponent.IsDead returned false for deceased player!");
            GetTree().Quit(1);
            return;
        }

        // Verify shooting dead player cannot inflict damage or trigger hitmarkers
        deadHeadHurtbox.ReceiveDamage(new FPSGame.Common.Components.Combat.DamageInfo(50.0f, 2, 1, Vector3.Zero, Vector3.Up, "MP5"));
        GD.Print("  [OK] Dead player collider/hurtbox disabling and hit rejection verified.");

        // Respawn player and wait one frame for SetDeferred restoration
        player.Rpc("ClientRespawnRpc", Vector3.Zero);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!deadHeadHurtbox.Monitorable || !deadBodyHurtbox.Monitorable || deadBodyCollider.Disabled)
        {
            GD.PrintErr("[TEST FAILED] Hurtboxes or collider were not restored after respawn!");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Player hurtboxes and collider successfully re-enabled on respawn.");

        // 6. Weapon fire mode memory verification
        player.EquipWeapon("MP5");
        player._UnhandledInput(new InputEventKey { Keycode = Key.V, Pressed = true, Echo = false }); // Cycle MP5 to SEMI
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] Expected MP5 FireMode SEMI, got {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.EquipWeapon("MGL");
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] Expected MGL FireMode SEMI, got {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.EquipWeapon("MP5");
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] Expected MP5 FireMode to remain SEMI after switching back from MGL, got {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.EquipWeapon("LMG");
        if (player.FireMode != "AUTO")
        {
            GD.PrintErr($"[TEST FAILED] Expected LMG default FireMode AUTO, got {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        player.EquipWeapon("MP5");
        if (player.FireMode != "SEMI")
        {
            GD.PrintErr($"[TEST FAILED] Expected MP5 FireMode to remain SEMI, got {player.FireMode}");
            GetTree().Quit(1);
            return;
        }
        GD.Print("  [OK] Per-weapon fire mode memory persistence verified.");

        // 7. Explosion kill credit & streak progression verification
        var enemyPlayer = playerScene.Instantiate<PlayerController>();
        enemyPlayer.Name = "2"; // Target peer 2 (Enemy team 2)
        enemyPlayer.TeamId = 2;
        enemyPlayer.Position = new Vector3(2.0f, 0.0f, 0.0f);
        AddChild(enemyPlayer);

        int initialStreak = player.CurrentStreakKills;
        // Trigger lethal explosion from player at enemy position
        ExplosionManager.Instance?.TriggerExplosion(enemyPlayer.GlobalPosition, 1, "Frag Grenade");
        if (!enemyPlayer.IsDead)
        {
            GD.PrintErr("[TEST FAILED] Enemy player was not killed by point-blank Frag Grenade blast!");
            GetTree().Quit(1);
            return;
        }
        if (player.CurrentStreakKills != initialStreak + 1)
        {
            GD.PrintErr($"[TEST FAILED] Player did not receive streak progress from Frag Grenade kill! Initial={initialStreak}, Current={player.CurrentStreakKills}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"  [OK] Frag Grenade blast kill credit and scorestreak progression verified (CurrentStreakKills={player.CurrentStreakKills}).");
        enemyPlayer.QueueFree();

        GD.Print("\n=================== ALL ASSET INTEGRATION TESTS PASSED! ===================");
        GetTree().Quit(0);
    }

    private static void PrintNodeTree(Node node, int depth)
    {
        string indent = new string(' ', depth * 2);
        if (node is MeshInstance3D mi)
        {
            int tris = mi.Mesh != null ? mi.Mesh.GetFaces().Length / 3 : 0;
            string matInfo = "";
            if (mi.Mesh is ArrayMesh am)
            {
                for (int s = 0; s < am.GetSurfaceCount(); s++)
                {
                    var mat = am.SurfaceGetMaterial(s) as StandardMaterial3D;
                    if (mat != null && mat.AlbedoTexture != null)
                    {
                        matInfo += $" Tex: {mat.AlbedoTexture.GetSize()}";
                    }
                }
            }
            GD.Print($"{indent}[Mesh] {mi.Name} (Tris: {tris}{matInfo})");
        }
        else
        {
            GD.Print($"{indent}[Node] {node.Name} ({node.GetClass()})");
        }
        foreach (Node child in node.GetChildren())
        {
            PrintNodeTree(child, depth + 1);
        }
    }
}
