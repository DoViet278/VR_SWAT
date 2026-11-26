using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Hàm dùng chung cho Condition / Action của đồng đội.
/// Lệnh (Order*) đọc từ component Teammate (nguồn chính, Teammate ghi lại lên Blackboard để xem trong graph),
/// nên node chỉ cần ô Self (chưa nối thì là GameObject đang chạy graph).
/// </summary>
public static class TeammateNodeUtils
{
    /// <summary>Mỗi bước di chuyển tối đa ngần này giây, để lệnh không bị treo.</summary>
    public const float MoveTimeout = 10f;

    public static Teammate Of(GameObject go) => go != null && go.TryGetComponent(out Teammate t) ? t : null;

    public static Teammate Of(BlackboardVariable<GameObject> self, GameObject graphOwner) =>
        Of(NPCConditionUtils.ResolveSelf(self, graphOwner));

    /// <summary>Đi tới destination: chạy nếu xa hơn RunDistance, không thì đi bộ. Trả false nếu không có đường.</summary>
    public static bool MoveTo(Teammate t, NavMeshAgent agent, Vector3 destination, float stoppingDistance = 0.15f)
    {
        float blend = Vector3.Distance(t.transform.position, destination) > t.RunDistance ? 2f : 1f;
        return NPCActionUtils.MoveTo(agent, destination, NPCActionUtils.SpeedFor(t.gameObject, blend), stoppingDistance);
    }

    /// <summary>Đứng yên, quay mặt về look và giữ súng ở tư thế ngắm.</summary>
    public static void HoldAim(Teammate t, NavMeshAgent agent, Vector3 look)
    {
        NPCActionUtils.Stop(agent);
        t.LookAt(look);
        NPCWeaponHolder holder = t.GetComponentInChildren<NPCWeaponHolder>();
        if (holder != null && holder.HasWeapon && !holder.IsReloading) holder.KeepAiming();
    }

    /// <summary>Điểm ngắm ngang tầm ngực (đặt lệnh HoldAim bằng điểm trên sàn cũng được).</summary>
    public static Vector3 AimHeight(Vector3 point, Vector3 self) =>
        point.y < self.y + 0.5f ? new Vector3(point.x, self.y + 1.3f, point.z) : point;
}
