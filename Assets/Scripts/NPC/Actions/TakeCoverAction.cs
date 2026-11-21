using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

/// <summary>Một chỗ nấp: vật che (collider có tag Cover) và điểm đứng phía sau nó, khuất khỏi Target.</summary>
public struct CoverSpot
{
    public Collider Cover;
    public Vector3 Point;
}

/// <summary>
/// Chạy tới phía sau vật che đang thấy (có tag CoverTag, khuất khỏi Target) rồi ngồi xuống.
/// Tới nơi thì trả Success ngay và VẪN NGỒI; graph đánh giá lại:
/// - Target trong tầm bắn -> Attack (đứng dậy bắn rồi ngồi lại).
/// - Target ngoài tầm bắn -> Chase (thôi nấp, chạy ra đuổi).
/// Thời gian nấp / số phát bắn chỉnh trong Inspector của NPC (Cover combat).
///
/// Phần static bên dưới (TryFindCover, Protects) là logic tìm chỗ nấp dùng chung cho
/// CanAttack, CanHiding, Chase và NPC.IsInCover.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "TakeCover", story: "Take cover tagged [CoverTag] within [SearchRadius] from [Target]", category: "Action", id: "a2bd2d03a9db8d52c90434937478a164")]
public partial class TakeCoverAction : Action
{
    [Tooltip("Mối đe dọa cần núp khỏi. Để trống thì dùng Target của NPC.")]
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<string> CoverTag = new("Cover");
    [SerializeReference] public BlackboardVariable<float> SearchRadius = new(10f);
    [Tooltip("Độ lớn blend: 1 = đi bộ, 2 = chạy.")]
    [SerializeReference] public BlackboardVariable<float> Speed = new(2f);
    [Tooltip("Không còn dùng (giữ để không hỏng ô trong graph). Thời gian nấp chỉnh ở NPC > Cover combat.")]
    [SerializeReference] public BlackboardVariable<float> HideTime = new(3f);

    private NavMeshAgent _agent;
    private NPC _npc;
    private CoverSpot _spot;

    protected override Status OnStart()
    {
        _npc = NPCConditionUtils.StatsOf(GameObject);        if (_npc == null || !NPCActionUtils.TryGetAgent(GameObject, out _agent))
            return Status.Failure;

        string coverTag = NPCConditionUtils.Resolve(CoverTag, _npc, n => n.CoverTag);
        float radius = NPCConditionUtils.Resolve(SearchRadius, _npc, n => n.SearchRadius);
        if (!TryFindCover(_npc, coverTag, radius, out _spot))
            return Status.Failure;

        _npc.LeaveCover();
        GameObject threat = NPCConditionUtils.ResolveTarget(Target, _npc);
        NPCActionUtils.SetupLocomotion(GameObject, threat != null ? threat.transform : null);
        float speed = NPCActionUtils.SpeedFor(GameObject, Speed.Value);
        return NPCActionUtils.MoveTo(_agent, _spot.Point, speed, 0.2f) ? Status.Running : Status.Failure;
    }

    protected override Status OnUpdate()
    {
        if (_spot.Cover == null || NPCActionUtils.PathFailed(_agent)) return Status.Failure;
        if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;

        NPCActionUtils.Stop(_agent);
        _npc.EnterCover(_spot);
        return Status.Success;
    }

    protected override void OnEnd() => NPCActionUtils.Stop(_agent);

    // ---------- Tìm chỗ nấp (dùng chung) ----------

    private const float CoverGap = 0.6f;
    private const float NavMeshSnap = 2f;
    private const float MinProgress = 1f;
    private const float MinTargetDistance = 2f;
    private static readonly Collider[] s_Hits = new Collider[32];

    /// <summary>Tìm chỗ nấp theo CoverTag / SearchRadius của NPC.</summary>
    public static bool TryFindCover(NPC npc, out CoverSpot spot) =>
        TryFindCover(npc, npc != null ? npc.CoverTag : null, npc != null ? npc.SearchRadius : 0f, out spot);

    /// <summary>
    /// Chỗ nấp gần NPC nhất, hướng về vị trí Target đang biết. Một vật che hợp lệ khi:
    /// - có tag CoverTag, nằm trong SearchRadius và NPC NHÌN THẤY nó (không bị vật khác chắn);
    /// - điểm sau vật che (phía ngược với Target) nằm trên NavMesh;
    /// - điểm đó giúp tiến lên: đứng đó bắn tới Target được (trong AttackRange), hoặc gần Target hơn vị trí hiện tại.
    ///   Nhờ vậy NPC không lùi về chỗ nấp cũ khi Target đã ra khỏi tầm bắn.
    /// </summary>
    public static bool TryFindCover(NPC npc, string coverTag, float radius, out CoverSpot best)
    {
        best = default;
        if (npc == null || string.IsNullOrEmpty(coverTag)) return false;

        GameObject self = npc.gameObject;
        Vector3 pos = self.transform.position;
        Vector3 threat = npc.ThreatPosition;
        float selfToThreat = Flat(threat - pos).magnitude;
        float bestSqr = float.MaxValue;

        int count = Physics.OverlapSphereNonAlloc(pos, radius, s_Hits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider cover = s_Hits[i];
            // So sánh chuỗi thay vì CompareTag: CompareTag báo lỗi mỗi frame nếu tag chưa khai báo trong Tag Manager.
            if (cover.tag != coverTag || cover.transform.IsChildOf(self.transform)) continue;
            if (!TryGetCoverPoint(cover, threat, self.transform.forward, out Vector3 point)) continue;

            float pointToThreat = Flat(threat - point).magnitude;
            if (pointToThreat < MinTargetDistance) continue;
            bool canShootFromThere = pointToThreat <= npc.AttackRange;
            bool movesCloser = pointToThreat <= selfToThreat - MinProgress;
            if (!canShootFromThere && !movesCloser) continue;

            if (!CanSeeCover(self, cover)) continue;

            float sqr = (point - pos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = new CoverSpot { Cover = cover, Point = point };
            }
        }
        return best.Cover != null;
    }

    /// <summary>
    /// Chỗ nấp GẦN NHẤT (dùng khi hết đạn / hết bom): chỉ cần có tag CoverTag, điểm sau vật che nằm trên NavMesh
    /// và không quá sát Target. Không đòi nhìn thấy vật che hay tiến lên được như TryFindCover.
    /// Trong radius không có thì tìm rộng dần (x2, x4).
    /// </summary>
    public static bool TryFindNearestCover(NPC npc, string coverTag, float radius, out CoverSpot best)
    {
        best = default;
        if (npc == null || string.IsNullOrEmpty(coverTag)) return false;

        GameObject self = npc.gameObject;
        Vector3 pos = self.transform.position;
        Vector3 threat = npc.ThreatPosition;
        radius = Mathf.Max(1f, radius);

        for (int pass = 0; pass < 3 && best.Cover == null; pass++, radius *= 2f)
        {
            float bestSqr = float.MaxValue;
            int count = Physics.OverlapSphereNonAlloc(pos, radius, s_Hits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider cover = s_Hits[i];
                if (cover.tag != coverTag || cover.transform.IsChildOf(self.transform)) continue;
                if (!TryGetCoverPoint(cover, threat, self.transform.forward, out Vector3 point)) continue;
                if (Flat(threat - point).magnitude < MinTargetDistance) continue;

                float sqr = (point - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = new CoverSpot { Cover = cover, Point = point };
                }
            }
        }
        return best.Cover != null;
    }

    /// <summary>Vật che còn che được: NPC và threat ở hai phía đối diện của vật che.</summary>
    public static bool Protects(Collider cover, Vector3 npcPos, Vector3 threat, float minAngle)
    {
        if (cover == null) return false;
        Vector3 center = cover.bounds.center;
        return Vector3.Angle(Flat(threat - center), Flat(npcPos - center)) >= minAngle;
    }

    /// <summary>Điểm phía sau vật che, ngược hướng với threat, đã bám xuống NavMesh.</summary>
    private static bool TryGetCoverPoint(Collider cover, Vector3 threat, Vector3 fallbackForward, out Vector3 point)
    {
        Bounds b = cover.bounds;
        Vector3 away = Flat(b.center - threat).normalized;
        if (away == Vector3.zero) away = -Flat(fallbackForward).normalized;
        Vector3 candidate = b.center + away * (Mathf.Max(b.extents.x, b.extents.z) + CoverGap);
        candidate.y = b.min.y;

        bool ok = NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavMeshSnap, NavMesh.AllAreas);
        point = ok ? hit.position : default;
        return ok;
    }

    private static bool CanSeeCover(GameObject self, Collider cover)
    {
        Vector3 eye = NPCConditionUtils.EyeOf(self);
        if (!Physics.Linecast(eye, cover.bounds.center, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return true;
        return hit.collider == cover || hit.transform.IsChildOf(cover.transform) || hit.transform.IsChildOf(self.transform);
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
