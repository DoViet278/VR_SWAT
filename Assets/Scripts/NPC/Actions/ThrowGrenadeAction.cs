using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

/// <summary>
/// Rút bom từ hông phải và ném về phía Target (anim DrawGrenade -> Throw, xem NPCGrenadeThrower).
/// - Từ chỗ đứng hiện tại ném được: dừng di chuyển, quay mặt về Target rồi rút bom. Đang ngồi nấp thì ném luôn
///   từ tư thế ngồi (bom bay vòng cầu qua vật che).
/// - Không ném được tại chỗ (vật che cao chắn đường ném, Player đứng quá gần): NPC rời chỗ nấp, chạy tới
///   ThrowPosition (bước ngang ra khỏi vật che / lùi xa Player), đứng dậy rồi mới ném.
/// Trả Success khi anim ném xong và súng đã về tay. Sau đó graph chạy HideNoAmmo: NPC quay vào nấp.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "ThrowGrenade", story: "Draw and throw a grenade at [Target]", category: "Action", id: "9e3f1b7a2c5d48e6a0b4c7d2e8f61a93")]
public partial class ThrowGrenadeAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [Tooltip("Lệch hướng (độ) tối đa so với Target để bắt đầu rút bom.")]
    [SerializeReference] public BlackboardVariable<float> AimTolerance = new(20f);
    [Tooltip("Chờ quay mặt tối đa bao lâu (giây), quá thì ném luôn.")]
    [SerializeReference] public BlackboardVariable<float> MaxTurnTime = new(1.2f);

    private const float MoveSpeedBlend = 2f;  // chạy
    private const float MaxMoveTime = 4f;

    private NavMeshAgent _agent;
    private NPC _npc;
    private NPCGrenadeThrower _thrower;
    private bool _started;
    private bool _moving;
    private float _turnDeadline;
    private float _moveDeadline;

    protected override Status OnStart()
    {
        _npc = NPCConditionUtils.StatsOf(GameObject);
        _thrower = GameObject.GetComponentInChildren<NPCGrenadeThrower>();
        GameObject target = NPCConditionUtils.ResolveTarget(Target, _npc);
        if (_thrower == null || target == null || !_thrower.CanThrow)
            return Status.Failure;

        bool hasAgent = NPCActionUtils.TryGetAgent(GameObject, out _agent);
        NPCActionUtils.SetupLocomotion(GameObject, target.transform);
        _started = false;
        _moving = false;

        if (_thrower.NeedsReposition && hasAgent)
        {
            // Bước ra khỏi vật che / lùi xa Player, đứng dậy để ném.
            if (_npc != null) _npc.LeaveCover();
            float speed = NPCActionUtils.SpeedFor(GameObject, MoveSpeedBlend);
            _moving = NPCActionUtils.MoveTo(_agent, _thrower.ThrowPosition, speed, 0.1f);
            _moveDeadline = Time.time + MaxMoveTime;
        }
        if (!_moving) NPCActionUtils.Stop(_agent);

        _turnDeadline = Time.time + MaxTurnTime.Value;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_npc != null && _npc.IsDead) return Status.Failure;

        if (_moving)
        {
            if (NPCActionUtils.PathFailed(_agent)) return Status.Failure;
            if (!NPCActionUtils.HasArrived(_agent) && Time.time < _moveDeadline) return Status.Running;
            NPCActionUtils.Stop(_agent);
            _moving = false;
            _turnDeadline = Time.time + MaxTurnTime.Value;
            return Status.Running;
        }

        if (!_started)
        {
            Vector3 aim = _npc != null ? _npc.ThreatPosition : NPCConditionUtils.ResolveTarget(Target, _npc).transform.position;
            bool facing = NPCConditionUtils.HorizontalAngleTo(GameObject, aim) <= AimTolerance.Value;
            if (!facing && Time.time < _turnDeadline) return Status.Running;
            if (!_thrower.BeginThrow()) return Status.Failure;
            _started = true;
            return Status.Running;
        }

        return _thrower.IsBusy ? Status.Running : Status.Success;
    }

    protected override void OnEnd()
    {
        NPCActionUtils.Stop(_agent);
        // Còn ở chỗ nấp: giữ tư thế ngồi, chờ random trước lần đứng dậy bắn.
        if (_npc != null && _npc.IsInCover) _npc.CoverReadyTime = Mathf.Max(_npc.CoverReadyTime, Time.time + 0.5f);
    }
}
