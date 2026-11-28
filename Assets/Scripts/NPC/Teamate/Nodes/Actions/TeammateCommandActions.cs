using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using VRSwat.Doors;
using Action = Unity.Behavior.Action;
using Unity.Properties;

// Lệnh SWAT 4 thêm cho đồng đội: Restrain (còng), Mirror (soi gương dưới cửa).
// Cùng quy ước với TeammateActions.cs: OrderId đổi / có địch / có nghi phạm cần canh thì Failure ngay; việc thất bại
// thì ReportDone(id, false) rồi Success (node ReportDone phía sau chỉ đứng giữ chỗ).

/// <summary>Order Is Restrain.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is Restrain", story: "[Self] order is Restrain", category: "Teammate", id: "6d2b8f4a1c9e47b3a5e0d7c2f8b1a914")]
public partial class OrderIsRestrainCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.Restrain;
}

/// <summary>Order Is Mirror.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is Mirror", story: "[Self] order is Mirror", category: "Teammate", id: "b3e7a1d9c4f2486e8b5a2d6c9e1f3a15")]
public partial class OrderIsMirrorCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.Mirror;
}

/// <summary>
/// Còng (lệnh Restrain, OrderTarget = nghi phạm / con tin đã đầu hàng):
/// 1. Approach: đi bộ tới sau lưng người đó (cách ~0.75 m).
/// 2. Cuff: ngồi xuống, hạ súng, tay trái (IK) đưa tới cổ tay (CuffPoint), hô "Restraining.", còng Teammate.RestrainTime
///    giây (NPCCuffable / HostageCuffable.TickAICuff), xong báo "Suspect secured." / "Civilian secured.".
/// Người đó chết / đứng dậy / đã bị người chơi còng trước: thôi (đã còng thì coi như xong).
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Restrain", story: "[Self] restrains the ordered target", category: "Teammate", id: "e4a9c2f7b1d84e6a9c3b5f1d7a2e8c16")]
public partial class TeammateRestrainAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float BehindDistance = 0.75f;
    private const float CloseEnough = 1.1f;

    private enum Phase { Approach, Cuff }

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private GameObject _target;
    private NPCCuffable _npcCuff;
    private HostageCuffable _hostageCuff;
    private int _id;
    private Phase _phase;
    private float _deadline;
    private float _duration;
    private bool _crouched;
    private bool _cuffing;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;

        _target = _t.OrderTarget;
        _npcCuff = _target != null ? _target.GetComponent<NPCCuffable>() : null;
        _hostageCuff = _target != null ? _target.GetComponent<HostageCuffable>() : null;
        if (IsRestrained()) return Status.Success;
        if (!IsCuffable() || !NPCActionUtils.TryGetAgent(_t.gameObject, out _agent))
        {
            Debug.LogWarning($"[{_t.CallSign}] Không còng được {(_target != null ? _target.name : "(trống)")}: chưa đầu hàng / không có NavMeshAgent.", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        _holder = _t.GetComponentInChildren<NPCWeaponHolder>();
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        _cuffing = false;
        _crouched = false;

        if (Flat(_t.transform.position - _target.transform.position).magnitude <= CloseEnough) BeginCuff();
        else
        {
            _phase = Phase.Approach;
            NPCActionUtils.MoveTo(_agent, BehindSpot(), NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.15f);
            _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
        }
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;
        if (IsRestrained())
        {
            StandUp();
            return Status.Success; // người chơi còng trước, hoặc vừa còng xong
        }
        if (!IsCuffable())
        {
            Debug.LogWarning($"[{_t.CallSign}] {_target?.name} không còn chờ còng, thôi.", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }

        switch (_phase)
        {
            case Phase.Approach:
                // Giơ súng chĩa vào người đó trong lúc tiến lại.
                if (_holder != null && _holder.HasWeapon && !_holder.IsReloading) _holder.KeepAiming();
                bool close = Flat(_t.transform.position - _target.transform.position).magnitude <= CloseEnough;
                if (NPCActionUtils.PathFailed(_agent) || Time.time > _deadline)
                {
                    if (!close)
                    {
                        Debug.LogWarning($"[{_t.CallSign}] Không tới được chỗ còng {_target.name}.", _t);
                        _t.ReportDone(_id, false);
                        return Status.Success;
                    }
                }
                else if (!NPCActionUtils.HasArrived(_agent) && !close) return Status.Running;
                BeginCuff();
                return Status.Running;

            default: // Cuff
                NPCActionUtils.Stop(_agent);
                Transform point = CuffPoint();
                if (point != null)
                {
                    _t.LookAt(point.position);
                    if (_holder != null) _holder.ReachLeftHand(point.position);
                }
                bool done = _npcCuff != null
                    ? _npcCuff.TickAICuff(_t, Time.deltaTime, _duration)
                    : _hostageCuff != null && _hostageCuff.TickAICuff(_t, Time.deltaTime, _duration);
                if (!done) return Status.Running;
                _cuffing = false;
                _t.SaySecured(_npcCuff != null);
                StandUp();
                return Status.Success;
        }
    }

    protected override void OnEnd()
    {
        if (_cuffing)
        {
            if (_npcCuff != null) _npcCuff.CancelAICuff(_t);
            if (_hostageCuff != null) _hostageCuff.CancelAICuff(_t);
            _cuffing = false;
        }
        StandUp();
    }

    private void BeginCuff()
    {
        NPCActionUtils.Stop(_agent);
        TeammateFollowPlayerAction.StopAiming(_t);
        bool ok = _npcCuff != null ? _npcCuff.BeginAICuff(_t) : _hostageCuff != null && _hostageCuff.BeginAICuff(_t);
        if (!ok) return;
        _cuffing = true;
        _phase = Phase.Cuff;
        _duration = _t.RollRestrainTime();
        if (_t.Npc != null)
        {
            _t.Npc.CrouchInPlace(); // ngồi xuống ngang tầm người đang quỳ
            _crouched = true;
        }
        _t.SayRestraining();
    }

    private void StandUp()
    {
        if (_holder != null) _holder.ReleaseLeftHand();
        if (_crouched && _t != null && _t.Npc != null) _t.Npc.LeaveCover();
        _crouched = false;
    }

    /// <summary>Sau lưng người cần còng (trên NavMesh); không có chỗ thì đứng cạnh.</summary>
    private Vector3 BehindSpot()
    {
        Transform tr = _target.transform;
        foreach (Vector3 dir in new[] { -tr.forward, -tr.forward + tr.right, -tr.forward - tr.right, tr.right, -tr.right })
        {
            Vector3 wanted = tr.position + Flat(dir).normalized * BehindDistance;
            if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 0.5f, _agent.areaMask)) return hit.position;
        }
        return tr.position;
    }

    private Transform CuffPoint() => _npcCuff != null ? _npcCuff.CuffPoint : _hostageCuff != null ? _hostageCuff.CuffPoint : null;

    private bool IsCuffable() => (_npcCuff != null && _npcCuff.IsCuffable) || (_hostageCuff != null && _hostageCuff.IsCuffable);

    private bool IsRestrained()
    {
        if (_target == null) return false;
        if (_target.TryGetComponent(out NPC n)) return n.IsRestrained;
        return _target.TryGetComponent(out Hostage h) && h.IsRestrained;
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}

/// <summary>
/// Soi gương dưới cửa (lệnh Mirror, OrderDoor = cửa, OrderPos = chỗ stack):
/// 1. Approach: đi bộ tới chân cửa (giữa ô cửa, phía đội, cách mặt cửa ~0.55 m).
/// 2. Peek: quỳ xuống, hạ súng, tay trái (IK) đưa gương xuống khe dưới cửa, giữ Teammate.MirrorTime giây.
/// 3. Báo (SquadController.ReportMirror): "I see suspects." / "Civilians inside." / "Looks clear.", lưu LastMirror.
/// 4. Return: về chỗ stack.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Mirror Under Door", story: "[Self] mirrors under the ordered door", category: "Teammate", id: "f7c1b5e3a9d24c68b0e4a2f6d8c3b517")]
public partial class TeammateMirrorAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float StandOff = 0.55f;

    private enum Phase { Approach, Peek, Return }

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private DoorController _door;
    private int _id;
    private Phase _phase;
    private float _phaseEnd;
    private Vector3 _doorPoint;
    private float _side;
    private bool _crouched;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;

        _door = _t.OrderDoor;
        if (_door == null || !NPCActionUtils.TryGetAgent(_t.gameObject, out _agent))
        {
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        _holder = _t.GetComponentInChildren<NPCWeaponHolder>();
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        TeammateFollowPlayerAction.StopAiming(_t);
        _crouched = false;

        // Giữa ô cửa (chiếu trung điểm 2 chỗ stack lên mặt cửa) và phía đội.
        Transform d = _door.transform;
        Vector3 team = _door.stackLeft != null && _door.stackRight != null
            ? (_door.stackLeft.position + _door.stackRight.position) * 0.5f
            : _t.OrderPos;
        _side = TeammateNodeUtils.DoorSide(_door, team);
        Vector3 local = d.InverseTransformPoint(team);
        local.z = 0f;
        _doorPoint = d.TransformPoint(local);

        Vector3 wanted = _doorPoint + d.forward * (_side * StandOff);
        Vector3 spot = NavMesh.SamplePosition(wanted, out NavMeshHit hit, 1f, _agent.areaMask) ? hit.position : _t.transform.position;
        _phase = Phase.Approach;
        NPCActionUtils.MoveTo(_agent, spot, NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.1f);
        _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;

        switch (_phase)
        {
            case Phase.Approach:
                bool timedOut = NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd;
                if (!timedOut && !NPCActionUtils.HasArrived(_agent)) return Status.Running;
                if (timedOut) Debug.LogWarning($"[{_t.CallSign}] Không tới sát chân cửa {_door.name}, soi từ chỗ đang đứng.", _t);
                NPCActionUtils.Stop(_agent);
                if (_t.Npc != null)
                {
                    _t.Npc.CrouchInPlace();
                    _crouched = true;
                }
                _phase = Phase.Peek;
                _phaseEnd = Time.time + Mathf.Max(0.5f, _t.MirrorTime);
                return Status.Running;

            case Phase.Peek:
                NPCActionUtils.Stop(_agent);
                // Gương sát sàn, lùa vào khe dưới cửa.
                Vector3 gap = new Vector3(_doorPoint.x, _t.transform.position.y + 0.06f, _doorPoint.z) + _door.transform.forward * (_side * 0.08f);
                _t.LookAt(gap);
                if (_holder != null) _holder.ReachLeftHand(gap);
                if (Time.time < _phaseEnd) return Status.Running;
                SquadController squad = SquadController.Instance != null ? SquadController.Instance : SquadController.GetOrCreate();
                squad.ReportMirror(_t, _door);
                StandUp();
                _phase = Phase.Return;
                TeammateNodeUtils.MoveTo(_t, _agent, _t.OrderPos, 0.2f);
                _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
                return Status.Running;

            default: // Return
                bool arrived = NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd || NPCActionUtils.HasArrived(_agent);
                return arrived ? Status.Success : Status.Running;
        }
    }

    protected override void OnEnd() => StandUp();

    private void StandUp()
    {
        if (_holder != null) _holder.ReleaseLeftHand();
        if (_crouched && _t != null && _t.Npc != null) _t.Npc.LeaveCover();
        _crouched = false;
    }
}
