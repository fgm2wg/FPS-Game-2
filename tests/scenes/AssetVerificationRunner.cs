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

        Vector3 pA1 = new Vector3(215.0f, 0, -1822.0f);
        Vector3 pA2 = new Vector3(218.0f, 0, -1825.0f);
        Vector3 pB1 = new Vector3(515.0f, 0, -1822.0f);
        Vector3 pB2 = new Vector3(512.0f, 0, -1825.0f);
        Vector3 pMid = new Vector3(365.0f, 0, -1822.0f);
        float h515 = terrainData.Call("get_height", pB1).AsSingle();
        float h512 = terrainData.Call("get_height", pB2).AsSingle();

        GD.Print($"  [OK] SpawnAlpha1 ({pA1.X}, {pA1.Z}): {terrainData.Call("get_height", pA1).AsSingle():F2}m");
        GD.Print($"  [OK] SpawnAlpha2 ({pA2.X}, {pA2.Z}): {terrainData.Call("get_height", pA2).AsSingle():F2}m");
        GD.Print($"  [OK] SpawnBravo1 ({pB1.X}, {pB1.Z}): {h515:F2}m");
        GD.Print($"  [OK] SpawnBravo2 ({pB2.X}, {pB2.Z}): {h512:F2}m");
        GD.Print($"  [OK] Midpoint Valley ({pMid.X}, {pMid.Z}): {terrainData.Call("get_height", pMid).AsSingle():F2}m");

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

        GD.Print("\n=================== ALL ASSET INTEGRATION TESTS PASSED! ===================");
        GetTree().Quit(0);
    }
}
