using UnityEngine;
using UnityEngine.AI;

/// <summary>Hàm dùng chung cho các Action: điều khiển NavMeshAgent và nối với NPCLocomotion.</summary>
public static class NPCActionUtils
{
    private const float DefaultWalkSpeed = 1.4f;
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

    /// <summary>Đổi độ lớn blend (1 = đi bộ, 2 = chạy) sang m/s khớp bước chân của clip (đi bộ và chạy có tốc độ riêng).</summary>
    public static float SpeedFor(GameObject npc, float blendSpeed) =>
        npc.TryGetComponent(out NPCLocomotion locomotion) ? locomotion.SpeedForBlend(blendSpeed) : DefaultWalkSpeed * blendSpeed;

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

    /// <summary>
    /// Chỗ chạy trốn (nghi phạm hoảng loạn, con tin nghe súng): thử 12 hướng quanh self, điểm trên NavMesh cách mối đe
    /// dọa ít nhất minDistance, đi tới được (đường hoàn chỉnh), đường đi không lướt sát mối đe dọa. Chọn điểm xa mối đe
    /// dọa nhất / đường ngắn; preferHidden: cộng điểm cho chỗ khuất tầm nhìn từ mối đe dọa (sau tường / cửa).
    /// </summary>
    public static bool TryFindFleePoint(GameObject self, Vector3 threat, float minDistance, bool preferHidden, out Vector3 point)
    {
        point = self != null ? self.transform.position : Vector3.zero;
        if (!TryGetAgent(self, out NavMeshAgent agent)) return false;
        Vector3 from = self.transform.position;
        var path = new NavMeshPath();
        float bestScore = float.NegativeInfinity;
        bool found = false;
        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
            foreach (float dist in new[] { minDistance, minDistance * 0.7f, minDistance * 1.4f })
            {
                if (!NavMesh.SamplePosition(from + dir * dist, out NavMeshHit hit, 2f, agent.areaMask)) continue;
                float fromThreat = Flat(hit.position - threat);
                if (fromThreat < minDistance * 0.8f || fromThreat <= Flat(from - threat) + 2f) continue;
                if (!NavMesh.CalculatePath(agent.nextPosition, hit.position, agent.areaMask, path)
                    || path.status != NavMeshPathStatus.PathComplete) continue;
                float length = 0f, closest = float.PositiveInfinity;
                for (int c = 1; c < path.corners.Length; c++)
                {
                    length += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                    closest = Mathf.Min(closest, Flat(path.corners[c] - threat));
                }
                if (closest < 2.5f) continue; // đường chạy lướt qua sát mối đe dọa
                float score = fromThreat - length * 0.3f;
                if (preferHidden && !NPCMorale.ClearPath(threat + Vector3.up * 1.5f, hit.position + Vector3.up * 1.2f, self.transform))
                    score += 6f;
                if (score <= bestScore) continue;
                bestScore = score;
                point = hit.position;
                found = true;
            }
        }
        return found;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
