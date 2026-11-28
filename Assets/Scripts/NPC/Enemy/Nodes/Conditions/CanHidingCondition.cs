using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

/// <summary>
/// Được chạy đi nấp khi: đang giao chiến (KHÔNG nấp lúc tuần tra), chưa nấp,
/// và nhìn thấy vật che hợp lệ (xem TakeCoverAction.TryFindCover) trong SearchRadius.
/// Health / HideThreshold giữ lại để không hỏng ô đã nối trong graph, logic mới không dùng tới.
/// </summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "CanHiding", story: "[Self] is engaged and sees cover tagged [CoverTag] within [SearchRadius]", category: "Conditions", id: "ef7471406ce4bff0d4674fa3bbe9c735")]
public partial class CanHidingCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<float> Health;
    [SerializeReference] public BlackboardVariable<float> HideThreshold = new(50f);
    [SerializeReference] public BlackboardVariable<string> CoverTag = new("Cover");
    [SerializeReference] public BlackboardVariable<float> SearchRadius = new(10f);

    public override bool IsTrue()
    {
        NPC npc = NPCConditionUtils.StatsOf(NPCConditionUtils.ResolveSelf(Self, GameObject));
        if (npc == null || !npc.IsAlerted || npc.IsInCover) return false;

        string coverTag = NPCConditionUtils.Resolve(CoverTag, npc, n => n.CoverTag);
        float radius = NPCConditionUtils.Resolve(SearchRadius, npc, n => n.SearchRadius);
        return TakeCoverAction.TryFindCover(npc, coverTag, radius, out _);
    }
}
