using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

/// <summary>
/// Được bắn khi: đang giao chiến, Target trong tầm bắn, và
/// - đang nấp (đứng dậy bắn từ chỗ nấp), hoặc
/// - không có chỗ nấp nào dùng được (bắn ngoài trống).
/// Có chỗ nấp mà chưa nấp thì để nhánh TakeCover chạy trước.
/// </summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "CanAttack", story: "[Target] is within [AttackRange] of [Self]", category: "Conditions", id: "cc66fa21cf0f2de9b29c5c37c8e17881")]
public partial class CanAttackCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<float> AttackRange = new(10f);

    /// <summary>Đang nấp thì cho thêm khoảng này mới rời nấp, tránh nhấp nháy ở mép tầm bắn.</summary>
    public const float CoverRangeMargin = 1f;

    public override bool IsTrue()
    {
        GameObject self = NPCConditionUtils.ResolveSelf(Self, GameObject);
        NPC npc = NPCConditionUtils.StatsOf(self);
        GameObject target = NPCConditionUtils.ResolveTarget(Target, npc);
        if (self == null || target == null) return false;

        float range = NPCConditionUtils.Resolve(AttackRange, npc, n => n.AttackRange);
        if (npc == null)
            return NPCConditionUtils.IsWithin(self.transform.position, target.transform.position, range);

        if (!npc.IsAlerted) return false;

        // Có súng mà hết sạch đạn thì không bắn được nữa.
        NPCWeaponHolder holder = self.GetComponentInChildren<NPCWeaponHolder>();
        if (holder != null && holder.HasWeapon && holder.IsOutOfAmmo) return false;

        bool inCover = npc.IsInCover;
        if (inCover) range += CoverRangeMargin;
        if (!NPCConditionUtils.IsWithin(self.transform.position, target.transform.position, range))
            return false;

        return inCover || !TakeCoverAction.TryFindCover(npc, out _);
    }
}
