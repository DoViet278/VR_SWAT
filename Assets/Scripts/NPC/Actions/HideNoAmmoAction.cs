using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

/// <summary>
/// Hết đạn (và vừa ném bom xong / hết bom): chạy tới Cover GẦN NHẤT (tag CoverTag, phía khuất khỏi Target) rồi ngồi núp.
/// Khác TakeCover: không đòi chỗ nấp phải tiến lên hay bắn được tới Target, chỉ cần gần và che được.
/// Trong SearchRadius không có thì tìm rộng dần (x2, x4). Không có Cover nào thì ngồi thụp tại chỗ.
/// Đường tới Cover đi xuyên vùng nổ của quả bom đang cháy ngòi (vd. bom mình vừa ném) mà NPC đang ở ngoài
/// vùng nổ: ngồi thụp tại chỗ chờ bom nổ xong rồi mới chạy đi nấp.
/// Đã núp thì trả Success sau mỗi CheckInterval để graph đánh giá lại, nhưng VẪN NGỒI.
/// Target vòng ra sau vật che (vật che không còn chắn) thì tìm Cover gần nhất khác.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "HideNoAmmo", story: "Hide in nearest cover tagged [CoverTag] within [SearchRadius]", category: "Action", id: "2d7c4e9a1f6b43c8b5a0e3d9c7f2b814")]
public partial class HideNoAmmoAction : Action
{
    [SerializeReference] public BlackboardVariable<string> CoverTag = new("Cover");
    [SerializeReference] public BlackboardVariable<float> SearchRadius = new(10f);
    [Tooltip("Độ lớn blend: 1 = đi bộ, 2 = chạy.")]
    [SerializeReference] public BlackboardVariable<float> Speed = new(2f);
    [SerializeReference] public BlackboardVariable<float> CheckInterval = new(0.5f);

    private const float BlastMargin = 1f;

    private NavMeshAgent _agent;
    private NPC _npc;
    private CoverSpot _spot;
    private bool _moving;
    private bool _waitingForBlast;
    private bool _fleeing;
    private float _checkAt;
    private NavMeshPath _path;

    protected override Status OnStart()
    {
        _npc = NPCConditionUtils.StatsOf(GameObject);
        if (_npc == null) return Status.Failure;
        NPCActionUtils.TryGetAgent(GameObject, out _agent);
        _checkAt = Time.time + CheckInterval.Value;
        _moving = false;
        _waitingForBlast = false;

        GameObject target = _npc.Target;
        NPCActionUtils.SetupLocomotion(GameObject, target != null ? target.transform : null);

        // Đã ngồi núp đúng chỗ: giữ nguyên.
        if (_npc.IsInCover) return Status.Running;

        GoToCover();
        return Status.Running;
    }

    private void GoToCover()
    {
        _moving = false;
        _fleeing = false;
        _waitingForBlast = false;
        string coverTag = NPCConditionUtils.Resolve(CoverTag, _npc, n => n.CoverTag);
        float radius = NPCConditionUtils.Resolve(SearchRadius, _npc, n => n.SearchRadius);
        if (_agent != null && TakeCoverAction.TryFindNearestCover(_npc, coverTag, radius, out _spot))
        {
            // Chạy tới Cover phải đi qua vùng nổ của bom đang cháy ngòi: ngồi chờ nổ xong (nếu chỗ đang đứng an toàn).
            _path ??= new NavMeshPath();
            bool hasPath = NavMesh.CalculatePath(_agent.nextPosition, _spot.Point, _agent.areaMask, _path);
            bool crosses = hasPath && NPCGrenade.PathCrossesBlast(_path.corners, BlastMargin);
            if (crosses && NPCGrenade.TryGetBlastAt(GameObject.transform.position, BlastMargin, out NPCGrenade blast))
            {
                // Đang đứng trong vùng nổ: chạy thẳng ra xa quả bom (không chạy xuyên qua nó), ra khỏi rồi mới tìm Cover.
                _moving = _fleeing = TryFlee(blast);
            }
            else if (crosses)
            {
                _waitingForBlast = true;
            }
            else
            {
                _npc.LeaveCover();
                float speed = NPCActionUtils.SpeedFor(GameObject, Speed.Value);
                _moving = NPCActionUtils.MoveTo(_agent, _spot.Point, speed, 0.2f);
            }
        }

        // Không có chỗ nấp (hoặc không có NavMesh), hoặc đang chờ bom nổ: ngồi thụp tại chỗ.
        if (!_moving)
        {
            NPCActionUtils.Stop(_agent);
            _npc.CrouchInPlace();
        }
    }

    /// <summary>Chạy ra khỏi vùng nổ theo hướng ngược với quả bom (thử lệch hướng nếu NavMesh không cho).</summary>
    private bool TryFlee(NPCGrenade blast)
    {
        Vector3 pos = GameObject.transform.position;
        Vector3 away = pos - blast.BlastCenter;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : -GameObject.transform.forward;
        float need = blast.Radius + BlastMargin + 0.5f - Vector3.Distance(pos, blast.BlastCenter);
        float distance = Mathf.Max(1.5f, need);

        foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f })
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;
            if (!NavMesh.SamplePosition(pos + dir * distance, out NavMeshHit hit, 1f, _agent.areaMask)) continue;
            if (NPCGrenade.InAnyBlast(hit.position, 0f)) continue;
            _npc.LeaveCover();
            float speed = NPCActionUtils.SpeedFor(GameObject, Speed.Value);
            if (NPCActionUtils.MoveTo(_agent, hit.position, speed, 0.2f)) return true;
        }
        return false;
    }

    protected override Status OnUpdate()
    {
        if (_npc.IsDead) return Status.Failure;

        if (_waitingForBlast)
        {
            if (Time.time < _checkAt) return Status.Running;
            _checkAt = Time.time + CheckInterval.Value;
            GoToCover(); // bom nổ rồi (hoặc đường đã an toàn) thì chạy đi, chưa thì tiếp tục chờ
            return Status.Running;
        }

        if (_fleeing)
        {
            if (NPCActionUtils.PathFailed(_agent)) return Status.Failure;
            if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;
            GoToCover(); // đã ra khỏi vùng nổ: chờ bom nổ hoặc đi nấp
            return Status.Running;
        }

        if (_moving)
        {
            if (_spot.Cover == null || NPCActionUtils.PathFailed(_agent)) return Status.Failure;
            if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;
            NPCActionUtils.Stop(_agent);
            _npc.EnterCover(_spot);
            _moving = false;
            _checkAt = Time.time + CheckInterval.Value;
            return Status.Running;
        }

        if (!_npc.IsInCover && _npc.Cover.Cover == null) _npc.CrouchInPlace();
        return Time.time >= _checkAt ? Status.Success : Status.Running;
    }

    protected override void OnEnd()
    {
        if (_moving) NPCActionUtils.Stop(_agent);
    }
}
