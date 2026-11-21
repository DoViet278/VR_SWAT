using UnityEngine;
using UnityEngine.AI;

/// <summary>Hàm dùng chung cho các Action: điều khiển NavMeshAgent và nối với NPCLocomotion.</summary>
public static class NPCActionUtils
{
    private const float DefaultWalkSpeed = 1.5f;
    private const float ArriveTolerance = 0.1f;

    /// <summary>
    /// NPC đang giao chiến (vừa thấy Target hoặc vừa bị bắn).
    /// Action tuần tra/đứng chờ dùng để tự dừng, vì nhánh tuần tra trong graph không có Abort.
    /// </summary>
    public static bool HasThreat(GameObject npcGo)
    {
        NPC npc = NPCConditionUtils.StatsOf(npcGo);
        return npc != null && npc.IsAlerted;
    }

    public static bool TryGetAgent(GameObject npc, out NavMeshAgent agent)
    {
        agent = npc != null ? npc.GetComponent<NavMeshAgent>() : null;
        return agent != null && agent.enabled && agent.isOnNavMesh;
    }

    /// <summary>Cho NPCLocomotion lấy hướng đi từ agent và đặt hướng quay mặt khi đứng yên.</summary>
    public static void SetupLocomotion(GameObject npc, Transform lookTarget)
    {
        if (npc.TryGetComponent(out NPCLocomotion locomotion))
        {
            locomotion.DriveFromAgent = true;
            locomotion.LookTarget = lookTarget;
        }
    }

    /// <summary>Đổi độ lớn blend (1 = đi bộ, 2 = chạy) sang m/s để agent chạy khớp với clip.</summary>
    public static float SpeedFor(GameObject npc, float blendSpeed)
    {
        float walk = npc.TryGetComponent(out NPCLocomotion locomotion) ? locomotion.WalkSpeed : DefaultWalkSpeed;
        return walk * blendSpeed;
    }

    public static bool MoveTo(NavMeshAgent agent, Vector3 destination, float speed, float stoppingDistance)
    {
        agent.speed = speed;
        agent.stoppingDistance = stoppingDistance;
        return agent.SetDestination(destination);
    }

    public static bool HasArrived(NavMeshAgent agent) =>
        !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + ArriveTolerance;

    public static bool PathFailed(NavMeshAgent agent) =>
        !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid;

    public static void Stop(NavMeshAgent agent)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.ResetPath();
    }
}
