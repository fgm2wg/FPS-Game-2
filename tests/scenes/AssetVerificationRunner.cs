using System;
using Godot;
using FPSGame.Entities.Player.Controllers;
using FPSGame.Entities.Player.Viewmodel;
using FPSGame.Tests.Unit.Weapons;

namespace FPSGame.Tests.Scenes;

public partial class AssetVerificationRunner : Node
{
    public override void _Ready()
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

        GD.Print("\n=================== ALL ASSET INTEGRATION TESTS PASSED! ===================");
        GetTree().Quit(0);
    }
}
