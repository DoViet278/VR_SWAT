using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

/// <summary>
/// Rời chỗ nấp (nếu đang nấp) rồi chạy tới Target. Đang không thấy Target thì chạy tới vị trí lần cuối thấy.
/// Trả Success để graph đánh giá lại khi: tới tầm bắn, hoặc NHÌN THẤY chỗ nấp dùng được (graph sẽ chọn TakeCover).
/// Tới vị trí lần cuối thấy mà vẫn không thấy Target thì quên Target (về tuần tra).
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Chase", story: "Chase [Target] until within [AttackRange]", category: "Action", id: "0727e546c89afc1c9af815e890c7653f")]
public partial class ChaseAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [Tooltip("Nên nối cùng biến với AttackRange của CanAttack.")]
    [SerializeReference] public BlackboardVariable<float> AttackRange = new(10f);
    [Tooltip("Độ lớn blend: 1 = đi bộ, 2 = chạy.")]
    [SerializeReference] public BlackboardVariable<float> Speed = new(2f);
    [Tooltip("Đuổi quá thời gian này mà chưa tới tầm bắn thì bỏ, để graph đánh giá lại.")]
    [SerializeReference] public BlackboardVariable<float> GiveUpTime = new(10f);

    private const float RepathInterval = 0.25f;
    private const float CoverScanInterval = 0.3f;
    private const float LastKnownReached = 1f;

    private NavMeshAgent _agent;
    private NPC _npc;
    private float _giveUpAt;
    private float _nextRepath;
    private float _nextCoverScan;

    protected override Status OnStart()
    {
        _npc = NPCConditionUtils.StatsOf(GameObject);
        if (NPCConditionUtils.ResolveTarget(Target, _npc) == null || !NPCActionUtils.TryGetAgent(GameObject, out _agent))
            return Status.Failure;

        // Target ra khỏi tầm bắn khi đang nấp: thôi nấp, chạy ra đuổi.
        if (_npc != null) _npc.LeaveCover();

        NPCActionUtils.SetupLocomotion(GameObject, null);
        _giveUpAt = Time.time + GiveUpTime.Value;
        _nextRepath = 0f;
        // Chờ một nhịp rồi mới tìm chỗ nấp, để không quay lại ngay chỗ vừa rời.
        _nextCoverScan = Time.time + CoverScanInterval;
        return OnUpdate();
    }

    protected override Status OnUpdate()
    {
        GameObject target = NPCConditionUtils.ResolveTarget(Target, _npc);
        if (target == null || Time.time >= _giveUpAt || (_npc != null && !_npc.IsAlerted))
            return Status.Failure;

        float range = NPCConditionUtils.Resolve(AttackRange, _npc, n => n.AttackRange);
        if (NPCConditionUtils.IsWithin(GameObject.transform.position, target.transform.position, range))
            return Status.Success;

        // Đang chạy tới mà thấy chỗ nấp: dừng để graph chọn nhánh TakeCover.
        if (_npc != null && Time.time >= _nextCoverScan)
        {
            _nextCoverScan = Time.time + CoverScanInterval;
            if (TakeCoverAction.TryFindCover(_npc, out _)) return Status.Success;
        }

        bool visible = _npc == null || _npc.CanSeeTarget;
        Vector3 destination = visible ? target.transform.position : _npc.LastKnownTargetPosition;

        // Tới chỗ lần cuối thấy mà vẫn không thấy: mất dấu.
        if (!visible && NPCConditionUtils.IsWithin(GameObject.transform.position, destination, LastKnownReached))
        {
            _npc.ForgetTarget();
            return Status.Failure;
        }

        if (Time.time >= _nextRepath)
        {
            _nextRepath = Time.time + RepathInterval;
            float speed = NPCActionUtils.SpeedFor(GameObject, Speed.Value);
            if (!NPCActionUtils.MoveTo(_agent, destination, speed, 0f))
                return Status.Failure;
        }
        return NPCActionUtils.PathFailed(_agent) ? Status.Failure : Status.Running;
    }

    protected override void OnEnd() => NPCActionUtils.Stop(_agent);
}
