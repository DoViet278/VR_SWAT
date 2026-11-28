using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

/// <summary>
/// Điều kiện ném bom (xem NPCGrenadeThrower.WantsToThrow):
/// SÚNG HẾT SẠCH ĐẠN (băng trên súng rỗng, đã bắn hết băng dự phòng), còn bom, đang giao chiến,
/// Target trong ThrowRange, rảnh tay, VÀ có phương án ném mà vùng nổ với tới Player: đường bay không đâm
/// vào vật cản, điểm nổ đủ gần Player, không bị tường / vật che chắn, không trúng chính NPC hay đồng đội.
/// </summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "CanThrowGrenade", story: "[Self] is out of ammo and can throw a grenade at [Target]", category: "Conditions", id: "5b0e8f3c6a1d4e27b9c2f4a8d3e1c706")]
public partial class CanThrowGrenadeCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    public override bool IsTrue()
    {
        GameObject self = NPCConditionUtils.ResolveSelf(Self, GameObject);
        NPC npc = NPCConditionUtils.StatsOf(self);
        if (npc == null || NPCConditionUtils.ResolveTarget(Target, npc) == null) return false;

        NPCGrenadeThrower thrower = self.GetComponentInChildren<NPCGrenadeThrower>();
        return thrower != null && thrower.WantsToThrow();
    }
}
