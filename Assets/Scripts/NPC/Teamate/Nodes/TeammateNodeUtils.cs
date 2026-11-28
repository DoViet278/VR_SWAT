using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using VRSwat.Doors;

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

    // ---------- Cửa ----------

    /// <summary>Phía cửa (+1 / -1 theo trục Z của cửa) của point.</summary>
    public static float DoorSide(DoorController door, Vector3 point) =>
        door.transform.InverseTransformPoint(point).z >= 0f ? 1f : -1f;

    /// <summary>
    /// Tay nắm cửa phía side (gọi lúc cửa còn đóng): DoorHandleGrab trong cửa nằm xa nhất về phía đó,
    /// không có thì LockPoint nếu đã gán. Không có cả hai thì null (dùng DoorHandlePoint để ước lượng).
    /// </summary>
    public static Transform FindDoorHandle(DoorController door, float side)
    {
        Vector3 normal = door.transform.forward * side;
        Transform best = null;
        float bestScore = float.NegativeInfinity;
        foreach (DoorHandleGrab h in door.GetComponentsInChildren<DoorHandleGrab>())
        {
            float score = Vector3.Dot(h.transform.position - door.transform.position, normal);
            if (score > bestScore) { bestScore = score; best = h.transform; }
        }
        if (best != null) return best;
        return door.LockPoint != door.transform ? door.LockPoint : null;
    }

    /// <summary>
    /// Vị trí tay nắm hiện tại (quay theo cánh cửa). handle null: ước lượng cách bản lề 0.8 m, cao 1 m,
    /// lệch 5 cm về phía side.
    /// </summary>
    public static Vector3 DoorHandlePoint(DoorController door, Transform handle, float side)
    {
        if (handle != null) return handle.position;
        Quaternion swing = Quaternion.Euler(0f, door.Angle, 0f);
        Vector3 local = door.HingeLocalPosition + swing * new Vector3(0.8f * door.LeafAxisSign, 0f, 0.05f * side);
        local.y = door.transform.InverseTransformPoint(door.transform.position + Vector3.up).y;
        return door.transform.TransformPoint(local);
    }

    // ---------- Góc phòng ----------

    private const float SweepStep = 10f;      // độ giữa 2 tia quét
    private const float SweepFrom = 160f;     // bắt đầu từ góc chết sát tường cửa
    private const float SweepTo = -20f;       // quét qua giữa phòng một chút
    private const float SweepRange = 15f;     // m
    private const float MinCornerDist = 1.2f; // góc gần hơn thế là tường sát người, bỏ
    private const float MergeAngle = 25f;     // 2 góc cách nhau ít hơn thế thì giữ góc xa hơn

    /// <summary>
    /// Người này vào bên trái (clearLeft) hay bên phải. Cửa không đủ điểm thì theo Slot (A trái, B phải).
    /// </summary>
    public static bool IsLeftEntry(Teammate t, Vector3 from, DoorController door)
    {
        if (door != null && door.clearLeft != null && door.clearRight != null)
            return Vector3.Distance(from, door.clearLeft.position) <= Vector3.Distance(from, door.clearRight.position);
        return t.Slot == 0;
    }

    /// <summary>
    /// Các điểm chĩa súng khi clear phòng, theo thứ tự quét: góc chết phía mình -> các góc xa -> cuối cùng là look
    /// (giữa phòng). Dùng TeammateRoomCorners trên cửa nếu có, không thì tự dò bằng NavMesh.Raycast.
    /// </summary>
    public static void BuildClearPoints(Teammate t, Vector3 from, Vector3 look, DoorController door, List<Vector3> result)
    {
        result.Clear();
        Vector3 into = look - from;
        into.y = 0f;
        if (into.sqrMagnitude < 0.01f) into = t.transform.forward;
        into.y = 0f;
        into.Normalize();
        float sign = IsLeftEntry(t, from, door) ? -1f : 1f; // góc âm = bên trái

        TeammateRoomCorners manual = door != null ? door.GetComponent<TeammateRoomCorners>() : null;
        if (manual != null && manual.corners != null && manual.corners.Length > 0) AddManualCorners(t, from, into, sign, manual, result);
        else AddAutoCorners(t, from, into, sign, result);

        result.Add(AimHeight(look, from));
    }

    private static void AddManualCorners(Teammate t, Vector3 from, Vector3 into, float sign, TeammateRoomCorners manual, List<Vector3> result)
    {
        var mine = new List<(float angle, Vector3 point)>();
        var all = new List<(float angle, Vector3 point)>();
        foreach (Transform c in manual.corners)
        {
            if (c == null) continue;
            Vector3 d = c.position - from;
            d.y = 0f;
            float a = Vector3.SignedAngle(into, d, Vector3.up) * sign; // dương = phía mình
            all.Add((a, c.position));
            if (a >= -15f) mine.Add((a, c.position));
        }
        List<(float angle, Vector3 point)> list = mine.Count > 0 ? mine : all;
        list.Sort((x, y) => y.angle.CompareTo(x.angle)); // góc chết (xa trục nhất) trước
        for (int i = 0; i < list.Count && i < t.MaxCorners; i++) result.Add(AimHeight(list[i].point, from));
    }

    private static void AddAutoCorners(Teammate t, Vector3 from, Vector3 into, float sign, List<Vector3> result)
    {
        Vector3 origin = NavMesh.SamplePosition(from, out NavMeshHit onMesh, 1f, NavMesh.AllAreas) ? onMesh.position : from;
        int n = Mathf.RoundToInt((SweepFrom - SweepTo) / SweepStep) + 1;
        var angles = new float[n];
        var dist = new float[n];
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            angles[i] = sign * (SweepFrom - i * SweepStep);
            Vector3 dir = Quaternion.AngleAxis(angles[i], Vector3.up) * into;
            Vector3 end = origin + dir * SweepRange;
            if (NavMesh.Raycast(origin, end, out NavMeshHit hit, NavMesh.AllAreas))
            {
                dist[i] = hit.distance;
                pts[i] = hit.position;
            }
            else
            {
                dist[i] = SweepRange;
                pts[i] = end;
            }
        }

        // Góc phòng = chỗ tia đi xa nhất cục bộ (phòng chữ nhật: tia vào góc dài hơn tia vào giữa tường).
        var corners = new List<int>();
        for (int i = 1; i < n - 1; i++)
        {
            if (dist[i] < MinCornerDist || dist[i] < dist[i - 1] || dist[i] <= dist[i + 1]) continue;
            int last = corners.Count - 1;
            if (last >= 0 && Mathf.Abs(angles[i] - angles[corners[last]]) < MergeAngle)
            {
                if (dist[i] > dist[corners[last]]) corners[last] = i;
                continue;
            }
            corners.Add(i);
        }

        // Không thấy góc nào (phòng tròn / hành lang): chĩa vào chỗ xa nhất phía mình.
        if (corners.Count == 0)
        {
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Abs(angles[i]);
                if (a < 30f || a > 120f) continue;
                if (best < 0 || dist[i] > dist[best]) best = i;
            }
            if (best >= 0) corners.Add(best);
        }

        // Nhiều hơn MaxCorners: bỏ góc gần nhất, giữ thứ tự quét.
        while (corners.Count > t.MaxCorners)
        {
            int nearest = 0;
            for (int k = 1; k < corners.Count; k++)
                if (dist[corners[k]] < dist[corners[nearest]]) nearest = k;
            corners.RemoveAt(nearest);
        }
        foreach (int i in corners) result.Add(AimHeight(pts[i], from));
    }
}
