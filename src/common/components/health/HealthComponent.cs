using System;
using Godot;
using FPSGame.Common.Components.Combat;

namespace FPSGame.Common.Components.Health;

/// <summary>
/// Authoritative entity health component.
/// Governs hitpoints, damage reception, death state, and health replication across network peers.
/// </summary>
public partial class HealthComponent : Node
{
    [Export] public float MaxHealth { get; set; } = 100.0f;

    private float _currentHealth = 100.0f;

    [Export]
    public float CurrentHealth
    {
        get => _currentHealth;
        set
        {
            _currentHealth = Mathf.Max(0.0f, value);
            HealthChanged?.Invoke(_currentHealth, MaxHealth);
        }
    }

    public bool IsDead => CurrentHealth <= 0.0f;

    public event Action<float, float>? HealthChanged;
    public event Action<DamageInfo>? Damaged;
    public event Action<DamageInfo>? Died;

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
    }

    /// <summary>
    /// Applies authoritative damage on the server.
    /// </summary>
    public void ApplyDamage(DamageInfo damage)
    {
        if (IsDead) return;

        _currentHealth = Mathf.Max(0.0f, _currentHealth - damage.Amount);
        Damaged?.Invoke(damage);
        HealthChanged?.Invoke(_currentHealth, MaxHealth);

        // Broadcast health update to all peers
        Rpc(nameof(SyncHealthRpc), _currentHealth);

        if (_currentHealth <= 0.0f)
        {
            Died?.Invoke(damage);
            Rpc(nameof(NotifyDeathRpc), damage.AttackerPeerId, damage.WeaponName, damage.BoneType);
        }
    }

    /// <summary>
    /// Restores full health and revives entity.
    /// </summary>
    public void ResetHealth()
    {
        _currentHealth = MaxHealth;
        HealthChanged?.Invoke(_currentHealth, MaxHealth);
        if (Multiplayer != null && Multiplayer.IsServer())
        {
            Rpc(nameof(SyncHealthRpc), _currentHealth);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncHealthRpc(float newHealth)
    {
        if (Multiplayer != null && Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer()) return;

        _currentHealth = Mathf.Max(0.0f, newHealth);
        HealthChanged?.Invoke(_currentHealth, MaxHealth);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void NotifyDeathRpc(long killerPeerId, string weaponName, int boneType)
    {
        if (Multiplayer != null && Multiplayer.GetRemoteSenderId() != 1 && !Multiplayer.IsServer()) return;

        var dummyDamage = new DamageInfo(100f, killerPeerId, boneType, Vector3.Zero, Vector3.Up, weaponName);
        Died?.Invoke(dummyDamage);
    }
}
