using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

// Điều kiện cho các nhánh chiến thuật SWAT 4 của nghi phạm (EnemyTactics). Self chưa nối = GameObject chạy graph.

/// <summary>
/// Nên bỏ chạy: morale Panicked, đang giao chiến, cảnh sát / người chơi ở trong FleeTriggerDistance, chưa bỏ chạy
/// trong FleeCooldown giây. Chưa có EnemyTactics thì tự thêm (mặc định).
/// </summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Should Flee", story: "[Self] should flee", category: "Enemy", id: "3b6f0f4e8a2c4d6b9e1f7a5c2d8b4e60")]
public partial class ShouldFleeCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        NPC npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (npc == null || npc.IsDead || npc.IsSurrendered || !npc.IsAlerted || npc.Target == null) return false;
        if (npc.MoraleState != MoraleState.Panicked) return false;
        EnemyTactics t = EnemyNodeUtils.TacticsOf(npc);
        if (!t.CanFlee || t.FleeOnCooldown) return false;
        return Vector3.Distance(npc.transform.position, npc.ThreatPosition) <= t.FleeTriggerDistance;
    }
}

/// <summary>
/// Khống chế con tin: CanTakeHostage, đang giữ con tin (chưa đến lúc buông) hoặc có con tin trong tầm + đang thấy
/// cảnh sát + morale Aggressive.
/// </summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Can Threaten Hostage", story: "[Self] can threaten a hostage", category: "Enemy", id: "7c2e9a1d4f6b4a8e8c3d5b7f1e9a2c41")]
public partial class CanThreatenHostageCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        NPC npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (npc == null || npc.IsDead || npc.IsSurrendered) return false;
        EnemyTactics t = EnemyNodeUtils.TacticsOf(npc);
        if (!t.CanTakeHostage) return false;
        if (t.HeldHostage != null)
            return t.HeldHostage.IsHeld && !t.ShouldReleaseHostage && npc.MoraleState == MoraleState.Aggressive;
        if (!t.CanGrabAgain || !npc.IsAlerted || !npc.CanSeeTarget || npc.MoraleState != MoraleState.Aggressive) return false;
        NPCWeaponHolder holder = npc.GetComponentInChildren<NPCWeaponHolder>();
        if (holder == null || !holder.HasWeapon) return false;
        return t.FindHostageToGrab() != null;
    }
}

/// <summary>Nghe tiếng động (NPCHearing) trong NoiseMemory giây mà chưa giao chiến: đi xem.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Heard Noise", story: "[Self] heard a noise", category: "Enemy", id: "a41d6e2b9c7f4e3a8b5d1c6e9f2a7b38")]
public partial class HeardNoiseCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        NPC npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (npc == null || npc.IsDead || npc.IsSurrendered || npc.IsAlerted) return false;
        if (npc.MoraleState == MoraleState.Panicked) return false; // hoảng loạn: không đi xem tiếng động
        EnemyTactics t = EnemyNodeUtils.TacticsOf(npc);
        return t.Investigate && NPCHearing.TryGetNoise(npc.gameObject, t.NoiseMemory, out _);
    }
}

/// <summary>Cố thủ: EnemyTactics.Barricaded, chưa giao chiến.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Is Barricaded", story: "[Self] is barricaded", category: "Enemy", id: "e5b3c8d1a7f24b9e9d6a3c1f8e4b7d52")]
public partial class IsBarricadedCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        NPC npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        return npc != null && !npc.IsDead && !npc.IsSurrendered && !npc.IsAlerted
               && EnemyNodeUtils.TacticsOf(npc).Barricaded;
    }
}
