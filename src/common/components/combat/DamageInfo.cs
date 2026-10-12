using Godot;

namespace FPSGame.Common.Components.Combat;

/// <summary>
/// Immutable damage payload passed during weapon impacts and explosions.
/// </summary>
public readonly record struct DamageInfo(
    float Amount,
    long AttackerPeerId,
    int BoneType,          // 0 = Head (2.5x), 1 = Torso (1.0x), 2 = Limbs (0.7x)
    Vector3 HitPosition,
    Vector3 HitNormal,
    string WeaponName = "M4A1"
);
