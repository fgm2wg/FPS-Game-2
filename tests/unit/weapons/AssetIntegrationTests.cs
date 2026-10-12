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
                    GD.Print($"    Animations in {child.Name}: {string.Join(", ", ap.GetAnimationList())}");
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
            GD.Print($"[AssetTest] ZC57_Rigged instantiated successfully with {instance.GetChildCount()} direct children.");
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
            GD.Print($"[AssetTest] MGP7_Rigged instantiated successfully with {instance.GetChildCount()} direct children.");
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
            GD.Print("[AssetTest] Grenade models loaded successfully.");
        }

        return allPassed;
    }
}
