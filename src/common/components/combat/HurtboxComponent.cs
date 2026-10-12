using Godot;
using FPSGame.Common.Components.Health;
using FPSGame.Entities.Player.Controllers;

namespace FPSGame.Common.Components.Combat;

/// <summary>
/// Precision hurtbox volume attached to character body and head.
/// Intercepts projectile and hitscan queries on Layer 5/6, applies bone multipliers,
/// and forwards authoritative damage to the associated HealthComponent.
/// </summary>
public partial class HurtboxComponent : Area3D
{
    [Export] public int BoneType { get; set; } = 1; // 0 = Head (2.5x), 1 = Torso (1.0x), 2 = Limbs (0.7x)
    [Export] public float DamageMultiplier { get; set; } = 1.0f;
    [Export] public NodePath? HealthComponentPath { get; set; }

    private HealthComponent? _health;
    private PlayerController? _player;

    public long OwnerPeerId
    {
        get
        {
            if (_player != null) return _player.GetMultiplayerAuthority();
            var p = GetNodeOrNull<PlayerController>("../..") ?? GetOwnerOrNull<PlayerController>();
            return p != null ? p.GetMultiplayerAuthority() : 1;
        }
    }

    public int TeamId
    {
        get
        {
            if (_player != null) return _player.TeamId;
            var p = GetNodeOrNull<PlayerController>("../..") ?? GetOwnerOrNull<PlayerController>();
            return p != null ? p.TeamId : 1;
        }
    }

    public bool IsDead => _health != null && _health.IsDead;

    public override void _Ready()
    {
        if (HealthComponentPath != null && HasNode(HealthComponentPath))
        {
            _health = GetNode<HealthComponent>(HealthComponentPath);
        }
        else
        {
            _health = GetParentOrNull<HealthComponent>() ?? GetOwnerOrNull<HealthComponent>() ?? GetNodeOrNull<HealthComponent>("../../HealthComponent");
        }

        _player = GetOwnerOrNull<PlayerController>() ?? GetParentOrNull<PlayerController>() ?? GetNodeOrNull<PlayerController>("../..");

        // Ensure hurtbox is on Layer 5 (Player_Hitbox) and Layer 6 (Enemy_Hitbox)
        CollisionLayer = (1 << 4) | (1 << 5); // Layers 5 and 6 (0-indexed: 4 and 5)
        CollisionMask = 0; // Hurtboxes only receive raycasts, do not collide with world
    }

    public void ReceiveDamage(DamageInfo damage)
    {
        if (_health == null)
        {
            _health = GetNodeOrNull<HealthComponent>("../../HealthComponent") ?? _player?.GetNodeOrNull<HealthComponent>("HealthComponent");
        }

        if (_health == null || _health.IsDead) return;

        float scaledAmount = damage.Amount * DamageMultiplier;
        var modifiedDamage = new DamageInfo(
            scaledAmount,
            damage.AttackerPeerId,
            BoneType,
            damage.HitPosition,
            damage.HitNormal,
            damage.WeaponName
        );

        _health.ApplyDamage(modifiedDamage);
    }
}
