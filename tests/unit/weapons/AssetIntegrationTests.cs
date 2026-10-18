using System;
using Godot;

namespace FPSGame.Tests.Unit.Weapons;

/// <summary>
/// Integration test verifying that the imported 3D weapons and animation library load cleanly.
/// </summary>
public static class AssetIntegrationTests
{
    public static bool VerifyAssets()
    {
        bool allPassed = true;

        // 1. Verify Character & Animation Rig
        var charScene = ResourceLoader.Load<PackedScene>("res://assets/models/characters/animations/AnimationLibrary_Godot_Standard.glb");
        if (charScene == null)
        {
            GD.PrintErr("[AssetTest] Failed to load AnimationLibrary_Godot_Standard.glb as PackedScene");
            allPassed = false;
        }
        else
        {
            var charInstance = charScene.Instantiate<Node3D>();
            GD.Print($"[AssetTest] Character model instantiated! Root children:");
            foreach (Node child in charInstance.GetChildren())
            {
                GD.Print($"  - {child.Name} ({child.GetClass()})");
                if (child is AnimationPlayer ap)
                {
                    GD.Print($"    Animations in {child.Name}: ({ap.GetAnimationList().Length} total)");
                    foreach (var animName in ap.GetAnimationList())
                    {
                        var anim = ap.GetAnimation(animName);
                        GD.Print($"    [ANIM] {animName} | Length: {anim.Length:F2}s | Step: {anim.Step:F2}s | Tracks: {anim.GetTrackCount()}");
                    }
                }
                foreach (Node grandchild in child.GetChildren())
                {
                    GD.Print($"    -> {grandchild.Name} ({grandchild.GetClass()})");
                    if (grandchild is Skeleton3D skel)
                    {
                        int headBone = skel.FindBone("DEF-head");
                        Transform3D headTransform = skel.GetBoneGlobalPose(headBone);
                        GD.Print($"       DEF-head global pos: {headTransform.Origin}");
                        int handBone = skel.FindBone("DEF-hand.R");
                        Transform3D handTransform = skel.GetBoneGlobalPose(handBone);
                        GD.Print($"       DEF-hand.R global pos: {handTransform.Origin}");
                    }
                }
            }
            charInstance.QueueFree();
        }

        // 2. Verify ZC57 Assault Rifle
        var zc57 = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/ZC57_Rigged.glb");
        if (zc57 == null)
        {
            GD.PrintErr("[AssetTest] Failed to load ZC57_Rigged.glb");
            allPassed = false;
        }
        else
        {
            var instance = zc57.Instantiate<Node3D>();
            var ap = instance.FindChild("*AnimationPlayer*", true, false) as AnimationPlayer;
            string anims = ap != null ? string.Join(", ", ap.GetAnimationList()) : "none";
            GD.Print($"[AssetTest] ZC57_Rigged direct children: {instance.GetChildCount()}, animations: {anims}");
            instance.QueueFree();
        }

        // 3. Verify MGP7 SMG
        var mgp7 = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGP7_Rigged.glb");
        if (mgp7 == null)
        {
            GD.PrintErr("[AssetTest] Failed to load MGP7_Rigged.glb");
            allPassed = false;
        }
        else
        {
            var instance = mgp7.Instantiate<Node3D>();
            var ap = instance.FindChild("*AnimationPlayer*", true, false) as AnimationPlayer;
            string anims = ap != null ? string.Join(", ", ap.GetAnimationList()) : "none";
            GD.Print($"[AssetTest] MGP7_Rigged direct children: {instance.GetChildCount()}, animations: {anims}");
            instance.QueueFree();
        }

        // 4. Verify Attachments
        var optic = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/attachments/ZC57_Attachment_OpticV1.glb");
        if (optic == null)
        {
            GD.PrintErr("[AssetTest] Failed to load ZC57_Attachment_OpticV1.glb");
            allPassed = false;
        }
        else
        {
            GD.Print("[AssetTest] ZC57_Attachment_OpticV1.glb loaded successfully.");
        }

        // 5. Verify Grenades
        var frag = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/grenades/M67V1_Rigged.glb");
        var flash = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/grenades/M84V1_Rigged.glb");
        if (frag == null || flash == null)
        {
            GD.PrintErr("[AssetTest] Failed to load grenade assets.");
            allPassed = false;
        }
        else
        {
            var instance1 = frag.Instantiate<Node3D>();
            var ap1 = instance1.FindChild("*AnimationPlayer*", true, false) as AnimationPlayer;
            string anims1 = ap1 != null ? string.Join(", ", ap1.GetAnimationList()) : "none";
            GD.Print($"[AssetTest] M67V1 animations: {anims1}");
            instance1.QueueFree();

            var instance2 = flash.Instantiate<Node3D>();
            var ap2 = instance2.FindChild("*AnimationPlayer*", true, false) as AnimationPlayer;
            string anims2 = ap2 != null ? string.Join(", ", ap2.GetAnimationList()) : "none";
            GD.Print($"[AssetTest] M84V1 animations: {anims2}");
            instance2.QueueFree();
        }

        // 6. Verify LMG Model
        var lmg = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/LMG.glb");
        if (lmg == null)
        {
            GD.PrintErr("[AssetTest] Failed to load LMG.glb");
            allPassed = false;
        }
        else
        {
            var instance = lmg.Instantiate<Node3D>();
            var ap = instance.FindChild("*AnimationPlayer*", true, false) as AnimationPlayer;
            string anims = ap != null ? string.Join(", ", ap.GetAnimationList()) : "none";
            GD.Print($"[AssetTest] LMG direct children: {instance.GetChildCount()}, animations: {anims}");
            // Calculate the actual transformed mesh bounds under instance
            Aabb meshBounds = new Aabb();
            bool hasMesh = false;
            foreach (Node c in instance.FindChildren("*", "MeshInstance3D"))
            {
                if (c is MeshInstance3D mi && mi.Mesh != null)
                {
                    Transform3D localToInst = mi.Transform;
                    Aabb b = localToInst * mi.Mesh.GetAabb();
                    if (!hasMesh) { meshBounds = b; hasMesh = true; }
                    else { meshBounds = meshBounds.Merge(b); }
                }
            }
            GD.Print($"    [LMG Bounds in Model Space] Size={meshBounds.Size}, Center={meshBounds.GetCenter()}");
            instance.QueueFree();
        }

        // 7. Verify Gunshot Audio
        var audio = ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/weapons/gunshot.mp3");
        if (audio == null)
        {
            GD.PrintErr("[AssetTest] Failed to load gunshot.mp3");
            allPassed = false;
        }
        else
        {
            GD.Print($"[AssetTest] gunshot.mp3 loaded successfully! Length: {audio.GetLength():F2}s");
        }

        return allPassed;
    }
}
