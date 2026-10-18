using System;
using System.Collections.Generic;
using Godot;
using FPSGame.Core.Pooling;
using FPSGame.Entities.Player.Controllers;
using FPSGame.Gamemodes.TDM;
using FPSGame.Network.Core;
using FPSGame.Common.Components.Health;
using FPSGame.Common.Components.Combat;

namespace FPSGame.VFX.Impacts;

/// <summary>
/// Authoritative explosion manager and zero-allocation effect pool.
/// Handles multi-client network replication of photorealistic explosions,
/// server-authoritative radial blast damage with obstacle occlusion checks,
/// and rigid body physics impulse waves.
/// </summary>
public partial class ExplosionManager : Node
{
    public static ExplosionManager? Instance { get; private set; }

    private NodePool<ExplosionEffect> _explosionPool = null!;
    private NodePool<StrafeImpactEffect> _strafeImpactPool = null!;
    private PhysicsRayQueryParameters3D _occlusionQuery = null!;
    private PhysicsShapeQueryParameters3D? _shapeQuery;

    public static ExplosionManager EnsureExists(SceneTree? tree)
    {
        if (Instance != null && Instance.IsInsideTree()) return Instance;

        if (tree?.Root != null)
        {
            var existing = tree.Root.FindChild("ExplosionManager", true, false) as ExplosionManager;
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var mgr = new ExplosionManager { Name = "ExplosionManager" };
            tree.Root.CallDeferred(Node.MethodName.AddChild, mgr);
            Instance = mgr;
            mgr.EnsurePoolInitialized();
            return mgr;
        }

        return Instance!;
    }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        EnsurePoolInitialized();
    }

    public void EnsurePoolInitialized()
    {
        if (_explosionPool != null) return;

        ExplosionAssetGenerator.EnsureAssetsExist();

        var container = GetNodeOrNull<Node3D>("ExplosionPoolContainer");
        if (container == null)
        {
            container = new Node3D { Name = "ExplosionPoolContainer" };
            AddChild(container);
        }

        _explosionPool = new NodePool<ExplosionEffect>(
            container,
            factory: () =>
            {
                var effect = new ExplosionEffect();
                effect.Finished += OnExplosionFinished;
                return effect;
            },
            prewarmCount: 50
        );

        var strafeContainer = GetNodeOrNull<Node3D>("StrafePoolContainer");
        if (strafeContainer == null)
        {
            strafeContainer = new Node3D { Name = "StrafePoolContainer" };
            AddChild(strafeContainer);
        }

        _strafeImpactPool = new NodePool<StrafeImpactEffect>(
            strafeContainer,
            factory: () =>
            {
                var effect = new StrafeImpactEffect();
                effect.Finished += OnStrafeImpactFinished;
                return effect;
            },
            prewarmCount: 40
        );

        if (_occlusionQuery == null)
        {
            // Pre-allocate raycast query for obstacle occlusion check (World_Static | World_Dynamic)
            _occlusionQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1 | 2);
            _occlusionQuery.CollideWithAreas = false;
            _occlusionQuery.CollideWithBodies = true;
        }

        if (_shapeQuery == null)
        {
            // Pre-allocate shape query for physics impulse wave against dynamic props
            _shapeQuery = new PhysicsShapeQueryParameters3D
            {
                Shape = new SphereShape3D { Radius = 8.5f },
                CollisionMask = 2 // World_Dynamic
            };
        }
    }

    private void OnExplosionFinished(ExplosionEffect effect)
    {
        effect.Scale = Vector3.One;
        _explosionPool?.Return(effect);
    }

    private void OnStrafeImpactFinished(StrafeImpactEffect effect)
    {
        _strafeImpactPool?.Return(effect);
    }

    /// <summary>
    /// Spawns an explosion effect and applies authoritative damage if server.
    /// </summary>
    public void TriggerExplosion(Vector3 worldPosition, long shooterPeerId, string weaponName = "Frag Grenade")
    {
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                ApplyAuthoritativeBlast(worldPosition, shooterPeerId, weaponName);
                Rpc(nameof(ReplicateExplosionRpc), worldPosition);
            }
            else
            {
                // Standalone or client-predicted preview
                PlayLocalExplosion(worldPosition);
            }
        }
        else
        {
            // Offline / gym mode
            ApplyAuthoritativeBlast(worldPosition, shooterPeerId, weaponName);
            PlayLocalExplosion(worldPosition);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateExplosionRpc(Vector3 worldPosition)
    {
        PlayLocalExplosion(worldPosition);
    }

    private void PlayLocalExplosion(Vector3 worldPosition)
    {
        EnsurePoolInitialized();
        var effect = _explosionPool.Checkout(worldPosition);
        effect.Explode(worldPosition);
    }

    private void ApplyAuthoritativeBlast(Vector3 blastOrigin, long shooterPeerId, string weaponName = "Frag Grenade")
    {
        EnsurePoolInitialized();

        const float blastRadius = 8.5f;
        const float maxDamage = 135.0f;
        const float minDamage = 20.0f;

        if (!IsInsideTree()) return;
        var tree = GetTree();
        if (tree == null) return;

        var spaceState = (tree.CurrentScene as Node3D)?.GetWorld3D()?.DirectSpaceState ?? tree.Root?.GetWorld3D()?.DirectSpaceState;
        var players = tree.GetNodesInGroup("Players");
        if (players.Count == 0 && tree.CurrentScene != null)
        {
            players = tree.CurrentScene.FindChildren("*", nameof(PlayerController), true, false);
        }

        int shooterTeam = 0;
        PlayerController? shooterController = null;
        if (NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(shooterPeerId, out var info))
        {
            shooterTeam = info.TeamId;
        }

        foreach (Node node in players)
        {
            if (node is PlayerController pc)
            {
                if (int.TryParse(pc.Name, out int peerId) && peerId == shooterPeerId)
                {
                    shooterController = pc;
                    if (shooterTeam == 0) shooterTeam = pc.TeamId;
                }
                else if (pc.IsLocalPlayer && (shooterPeerId <= 1 || pc.GetMultiplayerAuthority() == shooterPeerId))
                {
                    shooterController = pc;
                    if (shooterTeam == 0) shooterTeam = pc.TeamId;
                }
            }
        }

        bool anyHit = false;
        bool anyKill = false;

        foreach (Node node in players)
        {
            if (node is PlayerController player && !player.IsDead)
            {
                var health = player.GetNodeOrNull<HealthComponent>("HealthComponent");
                if (health == null || health.IsDead) continue;

                // Prevent damaging teammates if team IDs are set and not self-damage
                if (shooterTeam != 0 && player.TeamId == shooterTeam && player != shooterController)
                {
                    continue;
                }

                Vector3 targetPos = player.GlobalPosition + new Vector3(0.0f, 0.9f, 0.0f); // Center of mass
                Vector3 diff = targetPos - blastOrigin;
                float dist = diff.Length();

                if (dist > blastRadius) continue;

                // Check line of sight: ensure thick walls/terrain block blast wave
                if (spaceState != null && _occlusionQuery != null)
                {
                    _occlusionQuery.From = blastOrigin + new Vector3(0.0f, 0.15f, 0.0f);
                    _occlusionQuery.To = targetPos;
                    var hit = spaceState.IntersectRay(_occlusionQuery);
                    if (hit.Count > 0)
                    {
                        var collider = hit["collider"].AsGodotObject();
                        // If ray hit static world before player capsule, blast is occluded
                        if (collider is StaticBody3D)
                        {
                            continue;
                        }
                    }
                }

                // Smooth linear falloff
                float falloff = 1.0f - (dist / blastRadius);
                float damage = Mathf.Lerp(minDamage, maxDamage, falloff * falloff);

                // Apply blast damage
                bool wasDead = health.IsDead;
                Vector3 normal = dist > 0.001f ? diff.Normalized() : Vector3.Up;
                health.ApplyDamage(new DamageInfo(damage, shooterPeerId, 1, targetPos, normal, weaponName));

                    bool isKill = !wasDead && health.IsDead;
                    if (player != shooterController)
                    {
                        anyHit = true;
                        if (isKill) anyKill = true;
                    }

                    if (isKill && MatchManager.Instance != null)
                    {
                        int targetPeerId = int.TryParse(player.Name, out int parsedId) ? parsedId : (int)player.GetMultiplayerAuthority();
                        MatchManager.Instance.RecordKill(shooterPeerId, targetPeerId, weaponName);
                    }
            }
        }

        // Award hit feedback and scorestreak progression to shooter
        if (anyHit && shooterController != null)
        {
            if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && shooterPeerId != 1)
            {
                shooterController.RpcId(shooterPeerId, nameof(PlayerController.ConfirmHitRpc), false, anyKill);
                if (anyKill)
                {
                    shooterController.RpcId(shooterPeerId, nameof(PlayerController.AwardKillStreakRpc));
                }
            }
            else
            {
                shooterController.ConfirmHitRpc(false, anyKill);
                if (anyKill)
                {
                    shooterController.AwardKillStreakRpc();
                }
            }
        }

        // Apply physical explosive impulse to dynamic props (zero runtime allocations)
        if (spaceState != null && _shapeQuery != null)
        {
            _shapeQuery.Transform = new Transform3D(Basis.Identity, blastOrigin);
            var dynamicHits = spaceState.IntersectShape(_shapeQuery, 16);
            foreach (var hit in dynamicHits)
            {
                if (hit.TryGetValue("collider", out var colVariant) && colVariant.AsGodotObject() is RigidBody3D prop)
                {
                    if (prop.Freeze || prop is FPSGame.Weapons.Projectiles.MglProjectile || prop is FPSGame.Weapons.Projectiles.GrenadeProjectile || prop.IsQueuedForDeletion()) continue;
                    Vector3 diff = prop.GlobalPosition - blastOrigin;
                    float dist = diff.Length();
                    if (dist > 0.01f && dist <= blastRadius)
                    {
                        float impulseMag = (1.0f - (dist / blastRadius)) * 22.0f;
                        prop.ApplyCentralImpulse(diff.Normalized() * impulseMag);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Spawns a lightweight, zero-allocation 30mm strafe impact effect (sparks and dust puff) with authoritative blast damage.
    /// Does not saturate audio mixer, spawn dynamic lights, or cause frame drops during 38-round A-10 strafe runs.
    /// </summary>
    public void TriggerStrafeImpact(Vector3 worldPosition, long shooterPeerId)
    {
        EnsurePoolInitialized();
        PlayLocalStrafeImpact(worldPosition);

        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                ApplyCustomBlast(worldPosition, shooterPeerId, 4.8f, 70.0f, 15.0f, "Airstrike");
                Rpc(nameof(ReplicateStrafeImpactRpc), worldPosition);
            }
        }
        else
        {
            ApplyCustomBlast(worldPosition, shooterPeerId, 4.8f, 70.0f, 15.0f, "Airstrike");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReplicateStrafeImpactRpc(Vector3 worldPosition)
    {
        PlayLocalStrafeImpact(worldPosition);
    }

    private void PlayLocalStrafeImpact(Vector3 worldPosition)
    {
        EnsurePoolInitialized();
        var effect = _strafeImpactPool.Checkout(worldPosition);
        effect.Activate(worldPosition);
    }

    /// <summary>
    /// Spawns a scaled-down explosion with appropriate blast radius and damage.
    /// </summary>
    public void TriggerScaledExplosion(Vector3 worldPosition, long shooterPeerId, float visualScale = 0.60f, float damageRadius = 4.8f, float maxDamage = 75.0f)
    {
        EnsurePoolInitialized();
        var effect = _explosionPool.Checkout(worldPosition);
        effect.Scale = Vector3.One * visualScale;
        effect.Explode(worldPosition, playAudio: false, isStrafe: true);

        if (Multiplayer == null || !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
        {
            ApplyCustomBlast(worldPosition, shooterPeerId, damageRadius, maxDamage, 15.0f, "Airstrike");
        }
    }

    /// <summary>
    /// Spawns a massive 2,000 lb MK-84 high-explosive bomb blast with heavy shockwave,
    /// deep seismic bass acoustic punch, wide lethal radius, and distant screen shake.
    /// </summary>
    public void TriggerBombExplosion(Vector3 worldPosition, long shooterPeerId, float visualScale = 3.2f, float damageRadius = 24.0f, float maxDamage = 450.0f)
    {
        EnsurePoolInitialized();
        PlayLocalBombExplosion(worldPosition, visualScale);

        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer())
        {
            if (Multiplayer.IsServer())
            {
                ApplyCustomBlast(worldPosition, shooterPeerId, damageRadius, maxDamage, 50.0f, "Bombing Run");
                Rpc(nameof(ReplicateBombExplosionRpc), worldPosition, visualScale);
            }
        }
        else
        {
            ApplyCustomBlast(worldPosition, shooterPeerId, damageRadius, maxDamage, 50.0f, "Bombing Run");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateBombExplosionRpc(Vector3 worldPosition, float visualScale)
    {
        PlayLocalBombExplosion(worldPosition, visualScale);
    }

    private void PlayLocalBombExplosion(Vector3 worldPosition, float visualScale)
    {
        EnsurePoolInitialized();
        var effect = _explosionPool.Checkout(worldPosition);
        effect.Scale = Vector3.One * visualScale;
        effect.Explode(worldPosition, playAudio: true, isStrafe: false);
    }

    private void ApplyCustomBlast(Vector3 blastOrigin, long shooterPeerId, float blastRadius, float maxDamage, float minDamage, string weaponName)
    {
        EnsurePoolInitialized();
        if (!IsInsideTree()) return;
        var tree = GetTree();
        if (tree == null) return;

        var spaceState = (tree.CurrentScene as Node3D)?.GetWorld3D()?.DirectSpaceState ?? tree.Root?.GetWorld3D()?.DirectSpaceState;
        var players = tree.GetNodesInGroup("Players");
        if (players.Count == 0 && tree.CurrentScene != null)
        {
            players = tree.CurrentScene.FindChildren("*", nameof(PlayerController), true, false);
        }

        int shooterTeam = 0;
        PlayerController? shooterController = null;
        if (NetworkManager.Instance != null && NetworkManager.Instance.Players.TryGetValue(shooterPeerId, out var info))
        {
            shooterTeam = info.TeamId;
        }

        foreach (Node node in players)
        {
            if (node is PlayerController pc)
            {
                if (int.TryParse(pc.Name, out int peerId) && peerId == shooterPeerId)
                {
                    shooterController = pc;
                    if (shooterTeam == 0) shooterTeam = pc.TeamId;
                }
                else if (pc.IsLocalPlayer && (shooterPeerId <= 1 || pc.GetMultiplayerAuthority() == shooterPeerId))
                {
                    shooterController = pc;
                    if (shooterTeam == 0) shooterTeam = pc.TeamId;
                }
            }
        }

        bool anyHit = false;
        bool anyKill = false;

        foreach (Node node in players)
        {
            if (node is PlayerController player && !player.IsDead)
            {
                var health = player.GetNodeOrNull<HealthComponent>("HealthComponent");
                if (health == null || health.IsDead) continue;

                if (shooterTeam != 0 && player.TeamId == shooterTeam && player != shooterController) continue;

                Vector3 targetPos = player.GlobalPosition + new Vector3(0.0f, 0.9f, 0.0f);
                Vector3 diff = targetPos - blastOrigin;
                float dist = diff.Length();
                if (dist > blastRadius) continue;

                if (spaceState != null && _occlusionQuery != null)
                {
                    _occlusionQuery.From = blastOrigin + new Vector3(0.0f, 0.15f, 0.0f);
                    _occlusionQuery.To = targetPos;
                    var hit = spaceState.IntersectRay(_occlusionQuery);
                    if (hit.Count > 0 && hit["collider"].AsGodotObject() is StaticBody3D)
                    {
                        continue;
                    }
                }

                float falloff = 1.0f - (dist / blastRadius);
                float damage = Mathf.Lerp(minDamage, maxDamage, falloff * falloff);

                // Apply blast damage
                bool wasDead = health.IsDead;
                Vector3 normal = dist > 0.001f ? diff.Normalized() : Vector3.Up;
                health.ApplyDamage(new DamageInfo(damage, shooterPeerId, 1, targetPos, normal, weaponName));

                    bool isKill = !wasDead && health.IsDead;
                    if (player != shooterController)
                    {
                        anyHit = true;
                        if (isKill) anyKill = true;
                    }

                    if (isKill && MatchManager.Instance != null)
                    {
                        int targetPeerId = int.TryParse(player.Name, out int parsedId) ? parsedId : (int)player.GetMultiplayerAuthority();
                        MatchManager.Instance.RecordKill(shooterPeerId, targetPeerId, weaponName);
                    }
            }
        }

        if (anyHit && shooterController != null)
        {
            if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && shooterPeerId != 1)
            {
                shooterController.RpcId(shooterPeerId, nameof(PlayerController.ConfirmHitRpc), false, anyKill);
                if (anyKill)
                {
                    shooterController.RpcId(shooterPeerId, nameof(PlayerController.AwardKillStreakRpc));
                }
            }
            else
            {
                shooterController.ConfirmHitRpc(false, anyKill);
                if (anyKill)
                {
                    shooterController.AwardKillStreakRpc();
                }
            }
        }
    }
}

/// <summary>
/// Lightweight, zero-allocation 30mm strafe impact effect.
/// Emits a small, sharp spark/dust burst with 0.3s lifetime.
/// Prevents GPU particle and audio buffer saturation during rapid A-10 strafes.
/// </summary>
public partial class StrafeImpactEffect : Node3D
{
    public event Action<StrafeImpactEffect>? Finished;

    private GpuParticles3D _particles = null!;
    private float _lifeTimer;
    private bool _active;

    private static ParticleProcessMaterial? _sharedProcessMat;
    private static StandardMaterial3D? _sharedMat;
    private static QuadMesh? _sharedQuad;

    public override void _Ready()
    {
        if (_particles != null) return;

        var sparkTex = ExplosionAssetGenerator.GetSparkTexture();

        _sharedMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = sparkTex,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true
        };

        if (_sharedProcessMat == null)
        {
            var grad = new Gradient();
            grad.SetColor(0, new Color(1.0f, 1.0f, 0.85f, 1.0f));
            grad.AddPoint(0.4f, new Color(1.0f, 0.70f, 0.20f, 0.90f));
            grad.AddPoint(1.0f, new Color(0.40f, 0.05f, 0.00f, 0.0f));

            _sharedProcessMat = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.25f,
                Direction = Vector3.Up,
                Spread = 160.0f,
                InitialVelocityMin = 8.0f,
                InitialVelocityMax = 18.0f,
                Gravity = new Vector3(0.0f, -18.0f, 0.0f),
                DampingMin = 2.0f,
                DampingMax = 4.0f,
                ScaleMin = 0.25f,
                ScaleMax = 0.55f,
                ParticleFlagAlignY = true,
                ColorRamp = new GradientTexture1D { Gradient = grad }
            };
        }

        _sharedQuad ??= new QuadMesh { Size = new Vector2(0.07f, 0.24f), Material = _sharedMat };

        _particles = new GpuParticles3D
        {
            Name = "Sparks",
            Amount = 12,
            Lifetime = 0.26f,
            OneShot = true,
            Explosiveness = 0.98f,
            ProcessMaterial = _sharedProcessMat,
            DrawPass1 = _sharedQuad
        };
        AddChild(_particles);
    }

    public void Activate(Vector3 pos)
    {
        if (_particles == null)
        {
            _Ready();
        }

        GlobalPosition = pos;
        _lifeTimer = 0.30f;
        _active = true;
        Visible = true;
        if (_particles != null)
        {
            _particles.Restart();
            _particles.Emitting = true;
        }
    }

    public override void _Process(double delta)
    {
        if (!_active) return;
        _lifeTimer -= (float)delta;
        if (_lifeTimer <= 0.0f)
        {
            _active = false;
            Visible = false;
            Finished?.Invoke(this);
        }
    }
}
