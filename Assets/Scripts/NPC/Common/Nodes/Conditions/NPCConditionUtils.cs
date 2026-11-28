using System;
using Unity.Behavior;
using UnityEngine;

/// <summary>
/// Hàm dùng chung cho các Condition và Action của NPC.
///
/// Ô của node được NỐI với biến Blackboard thì dùng giá trị Blackboard.
/// Ô CHƯA NỐI thì lấy từ component NPC (một nguồn thông số chung), nên graph vẫn chạy đúng khi quên nối.
/// </summary>
public static class NPCConditionUtils
{
    public const float EyeHeight = 1.6f;

    /// <summary>Biến runtime đã nối với Blackboard thì có tên, ô để giá trị riêng thì tên rỗng.</summary>
    public static bool IsLinked(BlackboardVariable variable) => variable != null && !string.IsNullOrEmpty(variable.Name);

    public static NPC StatsOf(GameObject go) => go != null && go.TryGetComponent(out NPC npc) ? npc : null;

    /// <summary>Self chưa nối hoặc trống thì dùng chính GameObject đang chạy graph.</summary>
    public static GameObject ResolveSelf(BlackboardVariable<GameObject> self, GameObject graphOwner) =>
        IsLinked(self) && self.Value != null ? self.Value : graphOwner;

    /// <summary>Target phải là object trong scene. Trống hoặc là prefab asset thì lấy NPC.Target.</summary>
    public static GameObject ResolveTarget(BlackboardVariable<GameObject> target, NPC npc)
    {
        GameObject go = target?.Value;
        if (go != null && go.scene.IsValid()) return go;
        return npc != null ? npc.Target : null;
    }

    public static T Resolve<T>(BlackboardVariable<T> variable, NPC npc, Func<NPC, T> fromNpc)
    {
        if (IsLinked(variable) || npc == null) return variable != null ? variable.Value : default;
        return fromNpc(npc);
    }

    /// <summary>Điểm giữa thân của object (tâm collider), không phải chân. Không có collider thì lấy pivot + 1m.</summary>
    public static Vector3 CenterOf(GameObject go)
    {
        Collider col = go.GetComponentInChildren<Collider>();
        return col != null ? col.bounds.center : go.transform.position + Vector3.up;
    }

    public static Vector3 EyeOf(GameObject go) => go.transform.position + Vector3.up * EyeHeight;

    public static bool IsWithin(Vector3 a, Vector3 b, float range) => (a - b).sqrMagnitude <= range * range;

    /// <summary>Góc (độ) giữa hướng mặt của self và điểm, chỉ tính trên mặt phẳng ngang.</summary>
    public static float HorizontalAngleTo(GameObject self, Vector3 point)
    {
        Vector3 flat = Vector3.ProjectOnPlane(point - self.transform.position, Vector3.up);
        return Vector3.Angle(self.transform.forward, flat);
    }

    /// <summary>Target trong tầm nhìn: đủ gần, trong góc nhìn ngang và không bị chắn.</summary>
    public static bool CanSee(GameObject self, GameObject target, float viewDistance, float viewAngle)
    {
        Vector3 aim = CenterOf(target);
        return IsWithin(EyeOf(self), aim, viewDistance)
            && HorizontalAngleTo(self, aim) <= viewAngle * 0.5f
            && HasLineOfSight(self, target);
    }

    /// <summary>Không có gì chắn giữa mắt self và thân target (bỏ qua collider của chính hai bên).</summary>
    public static bool HasLineOfSight(GameObject self, GameObject target)
    {
        if (!Physics.Linecast(EyeOf(self), CenterOf(target), out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return true;
        return hit.transform.IsChildOf(target.transform) || hit.transform.IsChildOf(self.transform);
    }
}
