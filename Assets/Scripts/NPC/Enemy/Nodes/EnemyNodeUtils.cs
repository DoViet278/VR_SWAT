using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Hàm dùng chung cho các node chiến thuật SWAT 4 của nghi phạm.</summary>
public static class EnemyNodeUtils
{
    public static NPC NpcOf(BlackboardVariable<GameObject> self, GameObject graphOwner) =>
        NPCConditionUtils.StatsOf(NPCConditionUtils.ResolveSelf(self, graphOwner));

    /// <summary>EnemyTactics của npc; prefab chưa gắn thì thêm lúc chạy (thông số mặc định).</summary>
    public static EnemyTactics TacticsOf(NPC npc) =>
        npc.TryGetComponent(out EnemyTactics t) ? t : npc.gameObject.AddComponent<EnemyTactics>();

    /// <summary>Điểm ngắm ngang ngực của một object.</summary>
    public static Vector3 Chest(GameObject go) => NPCConditionUtils.CenterOf(go);

    /// <summary>Chỗ chạy trốn xa cảnh sát (NPCActionUtils.TryFindFleePoint).</summary>
    public static bool TryFindFleePoint(NPC npc, Vector3 threat, float minDistance, out Vector3 point) =>
        NPCActionUtils.TryFindFleePoint(npc.gameObject, threat, minDistance, false, out point);

    public static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
