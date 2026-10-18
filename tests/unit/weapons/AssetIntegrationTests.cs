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

        // 6b. Verify MGL Model
        var mgl = ResourceLoader.Load<PackedScene>("res://assets/models/weapons/MGL.glb");
        if (mgl == null)
        {
            GD.PrintErr("[AssetTest] Failed to load MGL.glb");
            allPassed = false;
        }
        else
        {
            var instance = mgl.Instantiate<Node3D>();
            void Dump(Node n, int indent)
            {
                string info = n is Node3D n3 ? $" [Pos={n3.Position}, Rot={n3.RotationDegrees}, Scale={n3.Scale}]" : "";
                GD.Print($"{new string(' ', indent * 2)}- {n.Name} ({n.GetType().Name}){info}");
                foreach (Node child in n.GetChildren())
                    Dump(child, indent + 1);
            }
            GD.Print("[AssetTest] MGL hierarchy:");
            Dump(instance, 1);

            Aabb meshBounds = new Aabb();
            bool hasMesh = false;
            foreach (Node c in instance.FindChildren("*", "MeshInstance3D"))
            {
                if (c is MeshInstance3D mi && mi.Mesh != null)
                {
                    // Compute transform relative to instance root
                    Transform3D rel = Transform3D.Identity;
                    Node cur = mi;
                    while (cur != instance && cur is Node3D cur3)
                    {
                        rel = cur3.Transform * rel;
                        cur = cur.GetParent();
                    }
                    Aabb b = rel * mi.Mesh.GetAabb();
                    if (!hasMesh) { meshBounds = b; hasMesh = true; }
                    else { meshBounds = meshBounds.Merge(b); }
                    GD.Print($"    [Mesh] {mi.Name}: RelBounds Size={b.Size}, Center={b.GetCenter()}, Min={b.Position}, Max={b.End}");
                }
            }
            GD.Print($"    [MGL Total Bounds in Model Space] Size={meshBounds.Size}, Center={meshBounds.GetCenter()}, Min={meshBounds.Position}, Max={meshBounds.End}");
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

        var mglAudio = ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/weapons/mgl_shot.mp3");
        if (mglAudio == null)
        {
            GD.PrintErr("[AssetTest] Failed to load mgl_shot.mp3");
            allPassed = false;
        }
        else
        {
            GD.Print($"[AssetTest] mgl_shot.mp3 loaded successfully! Length: {mglAudio.GetLength():F2}s");
        }

        // 8. Verify Realistic Explosion Assets and Entities
        FPSGame.VFX.Impacts.ExplosionAssetGenerator.EnsureAssetsExist();

        var explosionAudio = ResourceLoader.Load<AudioStream>("res://assets/audio/sfx/weapons/explosion.mp3");
        if (explosionAudio == null)
        {
            GD.PrintErr("[AssetTest] Failed to load explosion.mp3");
            allPassed = false;
        }
        else
        {
            GD.Print($"[AssetTest] explosion.mp3 verified! Length: {explosionAudio.GetLength():F2}s");
        }

        var smokeTex = FPSGame.VFX.Impacts.ExplosionAssetGenerator.GetSmokeTexture();
        var sparkTex = FPSGame.VFX.Impacts.ExplosionAssetGenerator.GetSparkTexture();
        var scorchTex = FPSGame.VFX.Impacts.ExplosionAssetGenerator.GetScorchTexture();
        if (smokeTex == null || sparkTex == null || scorchTex == null)
        {
            GD.PrintErr("[AssetTest] Failed to load one or more explosion textures (smoke/spark/scorch)!");
            allPassed = false;
        }
        else
        {
            GD.Print($"[AssetTest] Explosion textures verified (Smoke: {smokeTex.GetSize()}, Spark: {sparkTex.GetSize()}, Scorch: {scorchTex.GetSize()})");
        }

        var grenade = new FPSGame.Weapons.Projectiles.GrenadeProjectile();
        if (grenade.Mass < 0.3f || grenade.Mass > 0.5f)
        {
            GD.PrintErr($"[AssetTest] Grenade mass unexpected: {grenade.Mass}");
            allPassed = false;
        }
        else
        {
            GD.Print($"[AssetTest] GrenadeProjectile verified (Mass={grenade.Mass}kg, CCD={grenade.ContinuousCd})");
        }
        grenade.QueueFree();

        var mglProj = new FPSGame.Weapons.Projectiles.MglProjectile();
        if (mglProj.Mass < 0.2f || mglProj.Mass > 0.3f)
        {
            GD.PrintErr($"[AssetTest] MglProjectile mass unexpected: {mglProj.Mass}");
            allPassed = false;
        }
        else
        {
            GD.Print($"[AssetTest] MglProjectile verified (Mass={mglProj.Mass}kg, CCD={mglProj.ContinuousCd})");
        }
        mglProj.QueueFree();

        var explosionEffect = new FPSGame.VFX.Impacts.ExplosionEffect();
        GD.Print("[AssetTest] ExplosionEffect created and verified.");
        explosionEffect.QueueFree();

        // 8. Verify Scorestreak Assets
        string[] streakModels = {
            "res://assets/models/scorestreaks/a10.glb",
            "res://assets/models/scorestreaks/c17.glb",
            "res://assets/models/scorestreaks/mq1_uav.glb",
            "res://assets/models/scorestreaks/supply_crate.glb",
            "res://assets/models/scorestreaks/parachute.glb",
            "res://assets/models/scorestreaks/b52.glb",
            "res://assets/models/scorestreaks/mk84.glb"
        };

        foreach (var path in streakModels)
        {
            var scene = ResourceLoader.Load<PackedScene>(path);
            if (scene == null)
            {
                GD.PrintErr($"[AssetTest] Failed to load scorestreak model: {path}");
                allPassed = false;
                continue;
            }
            var inst = scene.Instantiate<Node3D>();
            Aabb meshBounds = new Aabb();
            bool hasMesh = false;
            foreach (Node c in inst.FindChildren("*", "MeshInstance3D"))
            {
                if (c is MeshInstance3D mi && mi.Mesh != null)
                {
                    Transform3D rel = Transform3D.Identity;
                    Node cur = mi;
                    while (cur != inst && cur is Node3D cur3)
                    {
                        rel = cur3.Transform * rel;
                        cur = cur.GetParent();
                    }
                    Aabb b = rel * mi.Mesh.GetAabb();
                    if (!hasMesh) { meshBounds = b; hasMesh = true; }
                    else { meshBounds = meshBounds.Merge(b); }
                }
            }
            GD.Print($"[AssetTest] Model {System.IO.Path.GetFileName(path)}: Bounds Size={meshBounds.Size}, Center={meshBounds.GetCenter()}");
            inst.QueueFree();
        }

        string[] streakAudios = {
            "res://assets/audio/sfx/scorestreaks/a10_strafe.mp3",
            "res://assets/audio/sfx/scorestreaks/c17_flyby.mp3",
            "res://assets/audio/sfx/scorestreaks/radio_confirm.mp3"
        };
        foreach (var audioPath in streakAudios)
        {
            var st = ResourceLoader.Load<AudioStream>(audioPath);
            if (st == null)
            {
                GD.PrintErr($"[AssetTest] Failed to load scorestreak audio: {audioPath}");
                allPassed = false;
            }
            else
            {
                GD.Print($"[AssetTest] Audio {System.IO.Path.GetFileName(audioPath)} loaded! Length={st.GetLength():F2}s");
            }
        }

        return allPassed;
    }
}
