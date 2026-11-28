using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using VRSwat.Doors;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using Random = UnityEngine.Random;

// Quy ước chung của các node làm việc theo lệnh:
// - Ghi nhớ OrderId lúc bắt đầu. OrderId đổi, hoặc có địch / có nghi phạm cần canh (Teammate.NeedsAttention)
//   thì trả Failure ngay để Selector chọn lại nhánh (lệnh mới luôn cắt lệnh cũ trong vòng 1 frame).
// - Việc thất bại (cửa khóa, hết giờ...) thì gọi ReportDone(id, false) rồi trả Success, để node ReportDone phía sau
//   chỉ đứng giữ vị trí. Việc thành công thì trả Success, node ReportDone báo true.
// - Lệnh đã báo xong (bị ngắt vì giao chiến rồi quay lại) thì trả Success ngay, không làm lại.

/// <summary>Đi theo người chơi: slot sau lưng 1.5 m, A bên trái, B bên phải. Xa hơn RunDistance thì chạy.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Follow Player", story: "[Self] follows the player", category: "Teammate", id: "a9cbe1cdcfee4c3eb4384d36fbeea1aa")]
public partial class TeammateFollowPlayerAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float RepathInterval = 0.25f;

    private Teammate _t;
    private NavMeshAgent _agent;
    private int _id;
    private float _nextRepath;
    private Vector3 _slot;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        _nextRepath = 0f;
        StopAiming(_t);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;
        if (_agent == null && !NPCActionUtils.TryGetAgent(_t.gameObject, out _agent)) return Status.Running;

        if (Time.time >= _nextRepath)
        {
            _nextRepath = Time.time + RepathInterval;
            if (_t.TryGetFollowSlot(out Vector3 slot) && (!_agent.hasPath || Vector3.Distance(slot, _slot) > 0.4f))
            {
                _slot = slot;
                TeammateNodeUtils.MoveTo(_t, _agent, slot, 0.3f);
            }
        }

        // Tới chỗ: quay mặt cùng hướng người chơi đang nhìn.
        Transform head = Teammate.PlayerHead;
        if (head != null && NPCActionUtils.HasArrived(_agent))
        {
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            _t.LookAt(_t.transform.position + fwd.normalized * 3f + Vector3.up * 1.3f);
        }
        return Status.Running;
    }

    internal static void StopAiming(Teammate t)
    {
        NPCWeaponHolder holder = t.GetComponentInChildren<NPCWeaponHolder>();
        if (holder != null) holder.StopAiming();
    }
}

/// <summary>Đi tới OrderPos (chạy nếu xa), timeout 10 s. Tới nơi: Success (ReportDone báo xong và đứng nhìn OrderLook).</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Move To Order", story: "[Self] moves to the ordered position", category: "Teammate", id: "ae1e6e50cf1c4fc1b6a72c761c41d7f0")]
public partial class TeammateMoveToOrderAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float TacticalDistance = 3f; // còn cách chỗ ngần này (m) thì giơ súng

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private int _id;
    private float _deadline;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;

        if (!NPCActionUtils.TryGetAgent(_t.gameObject, out _agent))
        {
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        _holder = _t.GetComponentInChildren<NPCWeaponHolder>();
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        TeammateFollowPlayerAction.StopAiming(_t);
        TeammateNodeUtils.MoveTo(_t, _agent, _t.OrderPos);
        _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;
        if (NPCActionUtils.PathFailed(_agent) || Time.time > _deadline)
        {
            Debug.LogWarning($"[{_t.CallSign}] Không tới được {_t.OrderPos} ({(Time.time > _deadline ? "quá 10 s" : "không có đường")}).", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        if (!NPCActionUtils.HasArrived(_agent))
        {
            // Gần tới chỗ (đi bộ, gần cửa): giơ súng sẵn sàng; xa thì hạ súng để chạy.
            if (_holder != null && _holder.HasWeapon && !_holder.IsReloading
                && Vector3.Distance(_t.transform.position, _t.OrderPos) < TacticalDistance)
                _holder.KeepAiming();
            return Status.Running;
        }

        // Đường chỉ tới được một phần (vd. NavMesh không nối qua cửa): dừng ở chỗ gần nhất và báo thất bại.
        if (_agent.pathStatus == NavMeshPathStatus.PathPartial
            && Vector3.Distance(_t.transform.position, _t.OrderPos) > _agent.stoppingDistance + 0.5f)
        {
            Debug.LogWarning($"[{_t.CallSign}] Chỉ đi được tới gần {_t.OrderPos} (đường bị chặn). " +
                             "Nếu là cửa: bake NavMesh khi cửa mở hoặc cho cánh cửa Not Walkable / bỏ khỏi NavMesh.", _t);
            _t.ReportDone(_id, false);
        }
        return Status.Success;
    }
}

/// <summary>Báo Squad đã xong lệnh rồi đứng giữ chỗ, chĩa súng về OrderLook cho tới khi có lệnh mới.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Report Done", story: "[Self] reports the order done and holds", category: "Teammate", id: "9b091f3d07d44be4a495f883ca670c99")]
public partial class TeammateReportDoneAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private Teammate _t;
    private NavMeshAgent _agent;
    private int _id;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        if (!_t.IsOrderFinished) _t.ReportDone(_id, true);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;
        if (_t.OrderLook != Vector3.zero) TeammateNodeUtils.HoldAim(_t, _agent, TeammateNodeUtils.AimHeight(_t.OrderLook, _t.transform.position));
        else NPCActionUtils.Stop(_agent);
        return Status.Running;
    }
}

/// <summary>Đứng tại chỗ, chĩa súng về OrderLook (giữa cửa, trong phòng...) cho tới khi có lệnh mới.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Hold Aim", story: "[Self] holds and aims at the ordered point", category: "Teammate", id: "22567b418f914f8c85803d475a5806ee")]
public partial class TeammateHoldAimAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private Teammate _t;
    private NavMeshAgent _agent;
    private int _id;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;
        TeammateNodeUtils.HoldAim(_t, _agent, TeammateNodeUtils.AimHeight(_t.OrderLook, _t.transform.position));
        return Status.Running;
    }
}

/// <summary>
/// Mở cửa OrderDoor như người thật:
/// 1. Approach: đi bộ tới trước tay nắm (phía chỗ stack, cách mặt cửa HandleStandOff m).
/// 2. Turn: quay mặt về tay nắm.
/// 3. Reach: hạ súng, vươn tay trái (IK, NPCWeaponHolder.ReachLeftHand) tới tay nắm.
/// 4. Push: door.Request(Open), tay bám tay nắm đẩy cửa tới khi hết tầm tay. Cửa khóa: lắc tay nắm 0.5 s rồi thôi.
///    Cửa quay về phía mình (phía kia bị tường chặn): buông tay, lùi ngay khỏi đường quay của cánh cửa.
/// 5. Return: thu tay về súng, quay lại OrderPos (chỗ stack, ngoài "fatal funnel") rồi Success.
/// Không có NPCWeaponHolder (rig không phải người) thì bỏ bước vươn tay. Busy (người chơi đang nắm cửa) thì chờ,
/// quá MoveTimeout = thất bại. Cửa không mở được (khóa / hết giờ) = ReportDone(false).
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Open Door", story: "[Self] opens the ordered door", category: "Teammate", id: "7dfb9c01683149dc8a75467ad662dbf7")]
public partial class TeammateOpenDoorAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float HandleStandOff = 0.55f; // đứng cách mặt cửa (m)
    private const float FacingTolerance = 25f;
    private const float TurnTime = 1f;          // quay quá lâu (vướng) thì vươn tay luôn
    private const float ReachTime = 0.6f;       // tay chưa tới thì vẫn đẩy (tránh treo)
    private const float RattleTime = 0.5f;      // lắc tay nắm cửa khóa
    private const float MaxPushTime = 0.8f;     // tay bám cửa tối đa
    private const float PalmOffset = 0.05f;     // lòng bàn tay đặt trước tay nắm

    private enum Phase { Approach, Turn, Reach, Push, Rattle, Return }

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private DoorController _door;
    private Transform _handle;
    private int _id;
    private Phase _phase;
    private float _side;
    private float _phaseEnd;
    private bool _requested;
    private bool _failed;

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
        _requested = false;
        _failed = false;

        // Phía cửa của đội: theo chỗ stack (OrderPos), trùng mặt cửa thì theo chỗ đang đứng.
        Vector3 local = _door.transform.InverseTransformPoint(_t.OrderPos);
        _side = Mathf.Abs(local.z) > 0.05f ? Mathf.Sign(local.z) : TeammateNodeUtils.DoorSide(_door, _t.transform.position);
        _handle = TeammateNodeUtils.FindDoorHandle(_door, _side);

        // Bị ngắt (giao chiến) sau khi cửa đã mở: chỉ cần quay về chỗ stack.
        if (SquadController.IsOpen(_door))
        {
            BeginReturn();
            return Status.Running;
        }

        if (TryGetHandleSpot(out Vector3 spot) && Vector3.Distance(Flat(_t.transform.position), Flat(spot)) > 0.25f)
        {
            _phase = Phase.Approach;
            NPCActionUtils.MoveTo(_agent, spot, NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.1f);
            _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
        }
        else BeginTurn();
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;
        Vector3 handle = TeammateNodeUtils.DoorHandlePoint(_door, _handle, _side);

        switch (_phase)
        {
            case Phase.Approach:
                if (NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd)
                {
                    // Không tới sát được: thử mở từ chỗ đang đứng (tay với không tới thì cửa vẫn mở, như bản cũ).
                    Debug.LogWarning($"[{_t.CallSign}] Không tới được tay nắm cửa {_door.name}, mở từ chỗ đang đứng.", _t);
                    BeginTurn();
                    return Status.Running;
                }
                if (NPCActionUtils.HasArrived(_agent)) BeginTurn();
                return Status.Running;

            case Phase.Turn:
                NPCActionUtils.Stop(_agent);
                _t.LookAt(handle);
                if (NPCConditionUtils.HorizontalAngleTo(_t.gameObject, handle) > FacingTolerance && Time.time < _phaseEnd)
                    return Status.Running;
                _phase = Phase.Reach;
                _phaseEnd = Time.time + ReachTime;
                return Status.Running;

            case Phase.Reach:
                NPCActionUtils.Stop(_agent);
                _t.LookAt(handle);
                ReachHandle(handle);
                if (_holder != null && _holder.ReachWeight < 0.95f && Time.time < _phaseEnd) return Status.Running;
                return TryOpen();

            case Phase.Push:
                if (!_requested) return TryOpen(); // đang chờ người chơi buông cửa
                NPCActionUtils.Stop(_agent);
                // Tay bám tay nắm tới khi hết tầm với / hết giờ / cửa dừng.
                bool inReach = _holder == null || _holder.LeftShoulder == null
                    || Vector3.Distance(_holder.LeftShoulder.position, handle) < _holder.LeftArmLength * 0.95f;
                if (SwingsTowardMe())
                {
                    BeginReturn(); // cánh cửa quay vào người: lùi ngay
                    return Status.Running;
                }
                if (inReach && _door.IsMoving && Time.time < _phaseEnd)
                {
                    ReachHandle(handle);
                    return Status.Running;
                }
                BeginReturn();
                return Status.Running;

            case Phase.Rattle:
                NPCActionUtils.Stop(_agent);
                // Lắc nhẹ tay nắm (cửa khóa) rồi thôi.
                float wobble = Mathf.Sin(Time.time * 40f) * 0.02f;
                ReachHandle(handle + _door.transform.right * wobble);
                if (Time.time < _phaseEnd) return Status.Running;
                _failed = true;
                BeginReturn();
                return Status.Running;

            default: // Return
                if (_requested && !_door.IsMoving)
                {
                    _door.Release(_t);
                    _requested = false;
                }
                bool arrived = NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd || NPCActionUtils.HasArrived(_agent);
                if (!arrived) return Status.Running;
                if (_requested)
                {
                    if (_door.IsMoving && Time.time <= _phaseEnd) return Status.Running;
                    _door.Release(_t);
                    _requested = false;
                }
                if (_failed || !SquadController.IsOpen(_door)) _t.ReportDone(_id, false);
                return Status.Success;
        }
    }

    protected override void OnEnd()
    {
        if (_holder != null) _holder.ReleaseLeftHand();
        if (_requested && _door != null) _door.Release(_t);
        _requested = false;
    }

    /// <summary>Gửi yêu cầu mở. Accepted: sang Push. Refused (khóa): lắc tay nắm. Busy: chờ tới hết giờ.</summary>
    private Status TryOpen()
    {
        switch (_door.Request(DoorAction.Open, _t, _t.transform.position))
        {
            case DoorRequestResult.Accepted:
                _requested = true;
                _phase = Phase.Push;
                _phaseEnd = Time.time + MaxPushTime;
                return Status.Running;
            case DoorRequestResult.Refused:
                _phase = Phase.Rattle;
                _phaseEnd = Time.time + RattleTime;
                return Status.Running;
            default: // Busy: người chơi / người khác đang giữ cửa: thu tay, chờ
                if (_holder != null) _holder.ReleaseLeftHand();
                if (_phase != Phase.Push)
                {
                    _phase = Phase.Push;
                    _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
                }
                if (Time.time <= _phaseEnd) return Status.Running;
                _failed = true;
                BeginReturn();
                return Status.Running;
        }
    }

    private void BeginTurn()
    {
        NPCActionUtils.Stop(_agent);
        _phase = Phase.Turn;
        _phaseEnd = Time.time + TurnTime;
    }

    private void BeginReturn()
    {
        if (_holder != null) _holder.ReleaseLeftHand();
        _phase = Phase.Return;
        TeammateNodeUtils.MoveTo(_t, _agent, _t.OrderPos, 0.2f);
        _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
    }

    private void ReachHandle(Vector3 handle)
    {
        if (_holder == null) return;
        Vector3 toMe = Flat(_t.transform.position - handle);
        _holder.ReachLeftHand(handle + (toMe.sqrMagnitude > 0.0001f ? toMe.normalized * PalmOffset : Vector3.zero));
    }

    /// <summary>
    /// Cánh cửa đang quay về phía đội (DoorController.LeafSide trùng phía đội). Bình thường cửa được đẩy vào trong
    /// phòng; chỉ xảy ra khi phía trong bị tường chặn.
    /// </summary>
    private bool SwingsTowardMe() => Mathf.Abs(_door.Angle) > 3f && _door.LeafSide == (int)_side;

    /// <summary>Điểm trên NavMesh trước tay nắm, cùng phía với đội.</summary>
    private bool TryGetHandleSpot(out Vector3 spot)
    {
        Vector3 handle = TeammateNodeUtils.DoorHandlePoint(_door, _handle, _side);
        Vector3 wanted = handle + _door.transform.forward * (_side * HandleStandOff);
        wanted.y = _t.transform.position.y;
        bool ok = NavMesh.SamplePosition(wanted, out NavMeshHit hit, 1f, NavMesh.AllAreas);
        spot = ok ? hit.position : _t.transform.position;
        return ok;
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}

/// <summary>
/// Phá khóa OrderDoor (lệnh PickLock): tới trước ổ khóa (phía đang đứng), ngồi phá Teammate.PickLockTime giây
/// (thanh tiến trình UnlockProgressBar nếu có), xong door.Unlock() rồi quay về OrderPos (chỗ stack).
/// Bị ngắt vì giao chiến thì giữ tiến trình, chạy lại thì phá tiếp. Cửa không khóa: chỉ quay về OrderPos.
/// (Giữ tên lớp / id cũ "Breach Door" để graph đã dựng vẫn trỏ đúng.)
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Pick Lock", story: "[Self] picks the lock of the ordered door", category: "Teammate", id: "b94bdb5640da40008fa18477158e1361")]
public partial class TeammateBreachDoorAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float LockStandOff = 0.6f; // đứng cách mặt cửa (m)

    private enum Phase { Approach, Pick, Return }

    private static VRSwat.Doors.UnlockProgressBar s_Bar;

    private Teammate _t;
    private NavMeshAgent _agent;
    private DoorController _door;
    private int _id;
    private Phase _phase;
    private float _deadline;
    private bool _crouched;
    private bool _barShown;
    private NPCWeaponHolder _holder;
    private Transform _handle;
    private float _side;

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
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        TeammateFollowPlayerAction.StopAiming(_t);
        _holder = _t.GetComponentInChildren<NPCWeaponHolder>();
        Vector3 local = _door.transform.InverseTransformPoint(_t.OrderPos);
        _side = Mathf.Abs(local.z) > 0.05f ? Mathf.Sign(local.z) : TeammateNodeUtils.DoorSide(_door, _t.transform.position);
        _handle = TeammateNodeUtils.FindDoorHandle(_door, _side);
        if (_t.PickDoor != _door)
        {
            _t.PickDoor = _door;
            _t.PickProgress = 0f;
        }

        if (_door.State != DoorState.Locked) BeginReturn();
        else if (TryGetPickSpot(out Vector3 spot))
        {
            _phase = Phase.Approach;
            NPCActionUtils.MoveTo(_agent, spot, NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.1f);
            _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
        }
        else
        {
            Debug.LogWarning($"[{_t.CallSign}] Không tìm được chỗ đứng trên NavMesh trước ổ khóa cửa {_door.name}.", _t);
            _t.ReportDone(_id, false);
            BeginReturn(); // về chỗ stack, không đứng giữa cửa
        }
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;

        switch (_phase)
        {
            case Phase.Approach:
                if (NPCActionUtils.PathFailed(_agent) || Time.time > _deadline)
                {
                    Debug.LogWarning($"[{_t.CallSign}] Không tới được ổ khóa cửa {_door.name}, quay về chỗ stack.", _t);
                    _t.ReportDone(_id, false);
                    BeginReturn();
                    return Status.Running;
                }
                if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;
                NPCActionUtils.Stop(_agent);
                _t.LookAt(LockPos());
                if (_t.Npc != null) _t.Npc.CrouchInPlace();
                _crouched = true;
                if (_t.PickProgress <= 0f) _t.SayPicking();
                _phase = Phase.Pick;
                return Status.Running;

            case Phase.Pick:
                // Người chơi phá / mở khóa trước, hoặc cửa bị phá tung: thôi.
                if (_door.State != DoorState.Locked)
                {
                    StandUp();
                    BeginReturn();
                    return Status.Running;
                }
                NPCActionUtils.Stop(_agent);
                _t.LookAt(LockPos());
                // Tay trái thao tác ở ổ khóa (rung nhẹ như đang xoay dụng cụ).
                if (_holder != null) _holder.ReachLeftHand(LockPos() + _door.transform.right * (Mathf.Sin(Time.time * 9f) * 0.015f));
                _t.PickProgress += Time.deltaTime / Mathf.Max(0.1f, _t.PickLockTime);
                ShowBar(_t.PickProgress);
                if (_t.PickProgress < 1f) return Status.Running;

                _door.Unlock();
                _t.PickProgress = 0f;
                _t.PickDoor = null;
                _t.SayUnlocked();
                StandUp();
                BeginReturn();
                return Status.Running;

            default: // Return
                if (NPCActionUtils.PathFailed(_agent) || Time.time > _deadline || NPCActionUtils.HasArrived(_agent))
                    return Status.Success;
                return Status.Running;
        }
    }

    protected override void OnEnd() => StandUp();

    private void BeginReturn()
    {
        _phase = Phase.Return;
        TeammateNodeUtils.MoveTo(_t, _agent, _t.OrderPos, 0.2f);
        _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
    }

    /// <summary>Điểm trên NavMesh ngay trước ổ khóa, cùng phía cửa với chỗ stack (OrderPos).</summary>
    private bool TryGetPickSpot(out Vector3 spot)
    {
        Vector3 wanted = LockPos() + _door.transform.forward * (_side * LockStandOff);
        bool ok = NavMesh.SamplePosition(wanted, out NavMeshHit hit, 1.5f, NavMesh.AllAreas);
        spot = ok ? hit.position : wanted;
        return ok;
    }

    /// <summary>
    /// Ổ khóa phía đội: LockPoint nếu đã gán, không thì tay nắm cửa (DoorHandleGrab), không nữa thì ước lượng.
    /// (Trước đây LockPoint trống = gốc cửa dưới sàn, nên đồng đội nhìn xuống đất khi phá khóa.)
    /// </summary>
    private Vector3 LockPos() => _door.LockPoint != _door.transform
        ? _door.LockPoint.position
        : TeammateNodeUtils.DoorHandlePoint(_door, _handle, _side);

    private void StandUp()
    {
        if (_holder != null) _holder.ReleaseLeftHand();
        if (_crouched && _t != null && _t.Npc != null) _t.Npc.LeaveCover();
        _crouched = false;
        if (_barShown && s_Bar != null) s_Bar.Hide();
        _barShown = false;
    }

    private void ShowBar(float progress)
    {
        if (!_t.ShowPickProgress) return;
        if (s_Bar == null) s_Bar = UnityEngine.Object.FindAnyObjectByType<VRSwat.Doors.UnlockProgressBar>();
        if (s_Bar == null) return;
        s_Bar.Show(_door.LockPoint, progress);
        _barShown = true;
    }
}

/// <summary>
/// Clear phòng (lệnh ClearRoom): chạy tới OrderPos (điểm clearLeft / clearRight), rồi lần lượt chĩa súng vào từng
/// góc phòng phía mình (góc chết sát tường cửa trước, xa dần), cuối cùng nhìn vào giữa phòng (OrderLook).
/// Góc lấy từ TeammateRoomCorners trên cửa, không có thì tự dò bằng NavMesh. Gặp địch thì nhường Engage,
/// xong quay lại quét tiếp từ góc đang dở. Quét xong: Success (ReportDone báo xong và giữ súng vào trong phòng).
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Clear Room", story: "[Self] enters and clears the room corners", category: "Teammate", id: "c3f1a7d25e8b4f0e9a6d2b71c48e5f13")]
public partial class TeammateClearRoomAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float FacingTolerance = 12f;
    private const float MaxTurnTime = 2.5f; // quay mãi không tới (vướng) thì sang góc tiếp

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private int _id;
    private bool _moving;
    private float _deadline;
    private bool _onPoint;
    private float _dwellEnd;
    private float _turnDeadline;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;
        if (!NPCActionUtils.TryGetAgent(_t.gameObject, out _agent))
        {
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);

        if (_t.ClearOrderId != _id)
        {
            _t.ClearOrderId = _id;
            _t.ClearStep = 0;
            _t.ClearPoints.Clear();
        }

        _holder = _t.GetComponentInChildren<NPCWeaponHolder>();
        _moving = Vector3.Distance(_t.transform.position, _t.OrderPos) > 0.6f;
        if (_moving)
        {
            TeammateNodeUtils.MoveTo(_t, _agent, _t.OrderPos);
            _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
        }
        _onPoint = false;
        _turnDeadline = Time.time + MaxTurnTime;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;

        if (_moving)
        {
            // Xông vào phòng với súng giơ sẵn (tư thế ngắm trên thân trên, chân vẫn đi).
            if (_holder != null && _holder.HasWeapon && !_holder.IsReloading) _holder.KeepAiming();
            bool failed = NPCActionUtils.PathFailed(_agent) || Time.time > _deadline;
            if (failed) Debug.LogWarning($"[{_t.CallSign}] Không tới được điểm clear {_t.OrderPos}, quét góc từ chỗ đang đứng.", _t);
            else if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;
            _moving = false;
            _turnDeadline = Time.time + MaxTurnTime;
        }

        if (_t.ClearPoints.Count == 0)
            TeammateNodeUtils.BuildClearPoints(_t, _t.transform.position, _t.OrderLook, _t.OrderDoor, _t.ClearPoints);
        if (_t.ClearStep >= _t.ClearPoints.Count) return Status.Success;

        Vector3 point = _t.ClearPoints[_t.ClearStep];
        TeammateNodeUtils.HoldAim(_t, _agent, point);

        if (!_onPoint)
        {
            if (NPCConditionUtils.HorizontalAngleTo(_t.gameObject, point) <= FacingTolerance || Time.time > _turnDeadline)
            {
                _onPoint = true;
                _dwellEnd = Time.time + _t.CornerDwell;
            }
        }
        else if (Time.time >= _dwellEnd)
        {
            _t.ClearStep++;
            _onPoint = false;
            _turnDeadline = Time.time + MaxTurnTime;
        }
        return Status.Running;
    }
}

/// <summary>
/// Ném flashbang vào phòng QUA CỬA (lệnh ThrowFlash, OrderPos = điểm giữa phòng, OrderDoor = cửa):
/// 1. Reposition: chọn chỗ đứng trước cửa (phía đội, lệch về phía mình, càng sát tường càng tốt) mà từ tay tới phòng
///    có đường bay lọt qua ô cửa: NPCGrenadeThrower.TryPlanFlashbang mô phỏng đường bay thấp (dưới khung cửa),
///    không chạm tường / khung / cánh cửa / đồng đội, rơi sâu trong phòng. Chỗ đang đứng ném được thì khỏi đi.
/// 2. Turn: quay mặt theo hướng ném (không phải hướng thẳng tới giữa phòng, có thể xuyên tường).
/// 3. Throw: BeginThrowFlashbangThrough (lúc bom rời tay lập lại kế hoạch từ vị trí tay thật).
/// 4. Return: lùi về chỗ stack ngay (ra khỏi khung cửa trước khi flash nổ) rồi Success.
/// Không tìm được chỗ ném nào: báo thất bại, không ném bừa vào tường. Không có OrderDoor: ném thẳng vào OrderPos như cũ.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Throw Flash", story: "[Self] throws a flashbang at the ordered point", category: "Teammate", id: "55675269b45142ba87d0538a8c5c2f9e")]
public partial class TeammateThrowFlashAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float FacingTolerance = 10f;
    private const float TurnTime = 1.5f;
    private static readonly float[] SpotDepths = { 1.0f, 1.4f, 1.8f, 2.3f };   // lùi khỏi mặt cửa (m)
    private static readonly float[] SpotSides = { 0.6f, 0.35f, 0f, -0.35f };   // lệch về phía mình (m)

    private enum Phase { Reposition, Turn, Throw, Return }

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCGrenadeThrower _thrower;
    private DoorController _door;
    private int _id;
    private Phase _phase;
    private float _phaseEnd;
    private bool _started;
    private Vector3 _doorPoint;
    private Vector3 _into;
    private Vector3 _home;
    private Vector3 _spot;
    private Vector3 _throwDir;
    private bool _retried; // đã thử chỗ ném thứ hai

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;

        _thrower = _t.GetComponentInChildren<NPCGrenadeThrower>();
        if (_thrower == null || !NPCActionUtils.TryGetAgent(_t.gameObject, out _agent))
        {
            Debug.LogWarning($"[{_t.CallSign}] Không có NPCGrenadeThrower / NavMeshAgent để ném flashbang.", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        TeammateFollowPlayerAction.StopAiming(_t);
        _started = false;
        _retried = false;
        _door = _t.OrderDoor;
        _home = HomeSpot();

        if (_door == null)
        {
            // Không biết cửa: ném thẳng từ chỗ đứng (hành vi cũ).
            _spot = _t.transform.position;
            _throwDir = Flat(_t.OrderPos - _spot);
            BeginTurn();
            return Status.Running;
        }

        ComputeDoorFrame();
        if (!FindThrowSpot(out _spot, out _throwDir))
        {
            Debug.LogWarning($"[{_t.CallSign}] Không tìm được chỗ đứng ném flashbang lọt qua cửa {_door.name} " +
                             "(cửa chưa mở hết / bị chắn / NavMesh trước cửa thiếu). Không ném.", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }

        if (Vector3.Distance(Flat(_t.transform.position), Flat(_spot)) > 0.2f)
        {
            _phase = Phase.Reposition;
            NPCActionUtils.MoveTo(_agent, _spot, NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.1f);
            _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
        }
        else BeginTurn();
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.IsDead) return Status.Failure;

        // Đã rút flash thì ném cho xong rồi mới xử lý việc khác (không rút quả thứ hai).
        if (_phase == Phase.Throw && _started)
        {
            NPCActionUtils.Stop(_agent);
            if (_thrower.IsBusy) return Status.Running;
            BeginReturn();
            return Status.Running;
        }
        if (_t.NeedsAttention(_id)) return Status.Failure;

        switch (_phase)
        {
            case Phase.Reposition:
                if (NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd)
                {
                    // Không tới được chỗ đã chọn: tìm lại từ chỗ đang đứng; vẫn không có đường bay lọt cửa thì
                    // không ném bừa vào tường mà báo thất bại và lùi về chỗ stack.
                    bool found = FindThrowSpot(out _spot, out _throwDir);
                    bool here = Vector3.Distance(Flat(_t.transform.position), Flat(_spot)) <= 0.2f;
                    if (found && !here && !_retried)
                    {
                        // Thử một chỗ khác (một lần).
                        Debug.LogWarning($"[{_t.CallSign}] Không tới được chỗ ném flashbang, thử chỗ khác.", _t);
                        _retried = true;
                        NPCActionUtils.MoveTo(_agent, _spot, NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.1f);
                        _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout * 0.5f;
                        return Status.Running;
                    }
                    if (!found || !here)
                    {
                        Debug.LogWarning($"[{_t.CallSign}] Không có chỗ ném flashbang lọt cửa {_door.name}. Không ném.", _t);
                        _t.ReportDone(_id, false);
                        BeginReturn();
                        return Status.Running;
                    }
                    BeginTurn();
                    return Status.Running;
                }
                if (NPCActionUtils.HasArrived(_agent)) BeginTurn();
                return Status.Running;

            case Phase.Turn:
                NPCActionUtils.Stop(_agent);
                Vector3 look = _t.transform.position + _throwDir * 3f + Vector3.up * 1.4f;
                _t.LookAt(look);
                if (NPCConditionUtils.HorizontalAngleTo(_t.gameObject, look) > FacingTolerance && Time.time < _phaseEnd)
                    return Status.Running;
                _phase = Phase.Throw;
                _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
                return Status.Running;

            case Phase.Throw:
                NPCActionUtils.Stop(_agent);
                if (Time.time > _phaseEnd)
                {
                    Debug.LogWarning($"[{_t.CallSign}] Không ném được flashbang (thiếu Flashbang Prefab, hết flash hoặc tay đang bận).", _t);
                    _t.ReportDone(_id, false);
                    BeginReturn();
                    return Status.Running;
                }
                bool begun = _door != null
                    ? _thrower.BeginThrowFlashbangThrough(_t.OrderPos, _doorPoint, _into)
                    : _thrower.BeginThrowFlashbangAt(_t.OrderPos);
                if (begun)
                {
                    _started = true;
                    _t.SayFlashOut();
                }
                return Status.Running;

            default: // Return
                bool arrived = NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd || NPCActionUtils.HasArrived(_agent);
                return arrived ? Status.Success : Status.Running;
        }
    }

    protected override void OnEnd()
    {
        if (_t != null) _t.ClearLook();
    }

    private void BeginTurn()
    {
        NPCActionUtils.Stop(_agent);
        if (_throwDir.sqrMagnitude < 0.0001f) _throwDir = _t.transform.forward;
        _throwDir = _throwDir.normalized;
        _phase = Phase.Turn;
        _phaseEnd = Time.time + TurnTime;
    }

    private void BeginReturn()
    {
        _phase = Phase.Return;
        if (Vector3.Distance(Flat(_t.transform.position), Flat(_home)) > 0.3f)
            TeammateNodeUtils.MoveTo(_t, _agent, _home, 0.2f);
        _phaseEnd = Time.time + TeammateNodeUtils.MoveTimeout;
    }

    /// <summary>Chỗ stack gần nhất (về đó sau khi ném), không có thì chỗ đang đứng.</summary>
    private Vector3 HomeSpot()
    {
        Vector3 self = _t.transform.position;
        if (_door == null || _door.stackLeft == null || _door.stackRight == null) return self;
        Vector3 l = _door.stackLeft.position, r = _door.stackRight.position;
        return Vector3.Distance(self, l) <= Vector3.Distance(self, r) ? l : r;
    }

    /// <summary>Điểm giữa ô cửa (trên mặt cửa) và hướng từ cửa vào phòng (ngược phía đội đứng).</summary>
    private void ComputeDoorFrame()
    {
        Transform d = _door.transform;
        bool stacks = _door.stackLeft != null && _door.stackRight != null;
        Vector3 teamPoint = stacks ? (_door.stackLeft.position + _door.stackRight.position) * 0.5f : _t.transform.position;
        float side = TeammateNodeUtils.DoorSide(_door, teamPoint);
        _into = Flat(d.forward * -side).normalized;

        // Hai điểm stack đứng đối xứng 2 bên cửa: chiếu trung điểm của chúng lên mặt cửa = giữa ô cửa.
        Vector3 local = d.InverseTransformPoint(teamPoint);
        local.z = 0f;
        _doorPoint = d.TransformPoint(local);
    }

    /// <summary>
    /// Thử chỗ đang đứng trước, rồi các điểm trước cửa (gần cửa trước, lệch về phía mình trước) đi tới được trên NavMesh,
    /// mỗi điểm hỏi NPCGrenadeThrower có đường bay lọt qua cửa không.
    /// </summary>
    private bool FindThrowSpot(out Vector3 spot, out Vector3 dir)
    {
        Vector3 self = _t.transform.position;
        Vector3 lateral = Vector3.Cross(Vector3.up, _into);
        float mySide = Mathf.Sign(Vector3.Dot(self - _doorPoint, lateral));

        var candidates = new System.Collections.Generic.List<Vector3> { self };
        foreach (float depth in SpotDepths)
            foreach (float s in SpotSides)
                candidates.Add(_doorPoint - _into * depth + lateral * (mySide * s));

        var path = new NavMeshPath();
        foreach (Vector3 c in candidates)
        {
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 0.4f, _agent.areaMask)) continue;
            // Phải ở phía đội (không lỡ đứng vào trong phòng).
            if (Vector3.Dot(hit.position - _doorPoint, _into) > -0.3f) continue;
            if (c != self && (!NavMesh.CalculatePath(_agent.nextPosition, hit.position, _agent.areaMask, path)
                              || path.status != NavMeshPathStatus.PathComplete)) continue;

            Vector3 fwd = Flat(_t.OrderPos - hit.position);
            Vector3 hand = _thrower.EstimateReleasePoint(hit.position, fwd);
            Vector3 body = hit.position + Vector3.up * 1.4f;
            if (!_thrower.TryPlanFlashbang(hand, body, _t.OrderPos, _doorPoint, _into, out NPCGrenadeThrower.ThrowPlan plan)) continue;
            spot = hit.position;
            dir = Flat(plan.Velocity);
            return true;
        }
        spot = self;
        dir = Flat(_t.OrderPos - self);
        return false;
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}

/// <summary>
/// Giao chiến theo luật SWAT 4: dừng, quay mặt, hô, chờ ShoutWait giây, vẫn chưa đầu hàng VÀ mức đe dọa đủ cao
/// (Teammate.MayFireAt: đang chĩa súng vào cảnh sát / vừa nổ súng / ném bom) thì bắn loạt 2-3 viên.
/// Chưa đủ đe dọa: chĩa súng và hô lại, không bắn. Nghi phạm đang nổ súng: bắn trả không chờ. Trước mỗi viên kiểm tra đường đạn: người chơi / đồng đội chắn thì
/// bỏ viên đó và bước ngang 1 m. Máu thấp: lùi về sau lưng người chơi. Hết địch / địch đầu hàng: Success.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Engage", story: "[Self] engages the enemy target", category: "Teammate", id: "663ec615284a436b8118762d47b65c1a")]
public partial class TeammateEngageAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float AimTolerance = 15f;

    private Teammate _t;
    private NPC _npc;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private NPC _enemy;
    private int _shotsLeft;
    private float _nextShot;
    private float _nextBurst;
    private float _sideStepUntil;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null || !_t.HasEnemyTarget) return Status.Failure;
        _npc = _t.Npc;
        _holder = _t.GetComponentInChildren<NPCWeaponHolder>();
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        _enemy = null;
        _sideStepUntil = 0f;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.IsDead) return Status.Failure;
        NPC enemy = _t.EnemyTarget;
        if (enemy == null || !_t.HasEnemyTarget) return Status.Success;
        if (enemy != _enemy)
        {
            if (_enemy == null && _npc.CanSeeTarget) _t.SayContact(); // phát hiện nghi phạm: "Contact!"
            _enemy = enemy;
            _shotsLeft = 0;
            _nextBurst = 0f;
        }

        bool canSee = _npc.CanSeeTarget;
        Vector3 aim = canSee ? NPCConditionUtils.CenterOf(enemy.gameObject) : _npc.ThreatPosition + Vector3.up * 1.2f;
        if (canSee) enemy.NotifyAttackedBy(_t.gameObject); // đang chĩa súng vào nó: địch ưu tiên nhắm lại mình

        // Máu thấp: lùi về sau lưng người chơi, không đứng bắn.
        if (_t.IsLowHealth && _agent != null
            && _t.TryGetBehindPlayer(2f, _t.Slot == 0 ? -0.8f : 0.8f, out Vector3 safe)
            && Vector3.Distance(_t.transform.position, safe) > 0.8f)
        {
            if (_holder != null) _holder.StopAiming();
            _t.ClearLook();
            TeammateNodeUtils.MoveTo(_t, _agent, safe, 0.3f);
            return Status.Running;
        }

        // Đang bước ngang tránh người chơi / đồng đội.
        if (Time.time < _sideStepUntil && _agent != null && !NPCActionUtils.HasArrived(_agent)) return Status.Running;

        NPCActionUtils.Stop(_agent);
        _t.LookAt(aim);
        if (_holder != null && _holder.HasWeapon) _holder.KeepAiming();
        if (!canSee) return Status.Running; // mất dấu: chĩa súng về chỗ thấy lần cuối, không bắn mù

        // Hô trước. Đang cooldown hô (vừa hô người khác) thì chờ, không bắn người chưa được hô
        // (trừ khi nó đang nổ súng: bắn trả ngay).
        bool underFire = _t.IsUnderFireFrom(enemy);
        if (!_t.TryGetShoutTime(enemy, out float shoutTime))
        {
            _t.TryShoutAt(enemy);
            if (!underFire) return Status.Running;
            shoutTime = float.NegativeInfinity;
        }
        if (enemy.IsSurrendered) return Status.Success;

        // Luật nổ súng: nghi phạm chưa đủ đe dọa (không súng / không chĩa súng vào ai) thì chỉ chĩa súng và hô tiếp.
        if (!_t.MayFireAt(enemy))
        {
            _t.TryShoutAt(enemy);
            _shotsLeft = 0;
            return Status.Running;
        }
        if (!underFire && Time.time < shoutTime + _t.ShoutWait) return Status.Running;
        if (NPCConditionUtils.HorizontalAngleTo(_t.gameObject, aim) > AimTolerance) return Status.Running;

        if (_holder != null)
        {
            if (!_holder.HasWeapon || _holder.IsReloading || _holder.IsThrowing) return Status.Running;
            if (_holder.NeedsReload)
            {
                _holder.Reload();
                return Status.Running;
            }
        }

        if (_shotsLeft <= 0)
        {
            if (Time.time < _nextBurst) return Status.Running;
            _shotsLeft = Random.Range(Mathf.Max(1, _t.Burst.x), Mathf.Max(_t.Burst.x, _t.Burst.y) + 1);
        }
        if (Time.time < _nextShot) return Status.Running;

        Vector3 muzzle = _holder != null && _holder.Weapon != null && _holder.Weapon.Muzzle != null
            ? _holder.Weapon.Muzzle.position
            : NPCConditionUtils.EyeOf(_t.gameObject);
        if (!_t.IsLineOfFireClear(muzzle, aim, out Vector3 blocker))
        {
            SideStep(aim, blocker);
            _nextShot = Time.time + 0.2f;
            return Status.Running;
        }

        bool hit = Random.value <= _t.Accuracy;
        if (_holder != null && !_holder.Fire(enemy.gameObject, hit)) return Status.Running;
        if (hit) Damage.Apply(enemy.gameObject, DamageInfo.Bullet(_t.Damage, muzzle, _t.gameObject).WithZone(DamageModel.RollAimedZone(_t.Accuracy)));

        _nextShot = Time.time + _t.FireInterval;
        if (--_shotsLeft <= 0) _nextBurst = Time.time + Random.Range(_t.BurstPause.x, Mathf.Max(_t.BurstPause.x, _t.BurstPause.y));
        return Status.Running;
    }

    /// <summary>Bước ngang (vuông góc hướng bắn) về phía xa người chắn.</summary>
    private void SideStep(Vector3 aim, Vector3 blocker)
    {
        if (_agent == null) return;
        Vector3 self = _t.transform.position;
        Vector3 toAim = aim - self;
        toAim.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toAim.normalized);
        if (Vector3.Dot(blocker - self, side) > 0f) side = -side;
        foreach (Vector3 dir in new[] { side, -side })
        {
            if (!NavMesh.SamplePosition(self + dir * _t.SideStep, out NavMeshHit hit, 0.5f, _agent.areaMask)) continue;
            TeammateNodeUtils.MoveTo(_t, _agent, hit.position, 0.1f);
            _sideStepUntil = Time.time + 1.2f;
            return;
        }
    }

    protected override void OnEnd()
    {
        if (_t != null) _t.ClearLook();
    }
}

/// <summary>Đứng cách nghi phạm đã đầu hàng 2-3 m, chĩa súng vào nó tới khi bị còng / chết. Có địch khác thì nhường Engage.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Guard Suspect", story: "[Self] guards the surrendered suspect", category: "Teammate", id: "9e010454986847578470f9df18890e13")]
public partial class TeammateGuardSuspectAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float RepathInterval = 0.5f;

    private Teammate _t;
    private NavMeshAgent _agent;
    private float _nextRepath;
    private bool _moving;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null || !_t.HasGuardTarget) return Status.Failure;
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        NPCActionUtils.SetupLocomotion(_t.gameObject, null);
        _nextRepath = 0f;
        _moving = false;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.IsDead || _t.HasEnemyTarget) return Status.Failure;
        if (!_t.HasGuardTarget) return Status.Success;

        GameObject suspect = _t.GuardTarget;
        Vector3 self = _t.transform.position;
        Vector3 pos = suspect.transform.position;
        Vector3 flat = self - pos;
        flat.y = 0f;
        float dist = flat.magnitude;
        Vector2 range = _t.GuardDistance;

        if (_agent != null && Time.time >= _nextRepath && (dist < range.x || dist > range.y))
        {
            _nextRepath = Time.time + RepathInterval;
            Vector3 dir = dist > 0.01f ? flat / dist : -suspect.transform.forward;
            Vector3 wanted = pos + dir * ((range.x + range.y) * 0.5f);
            if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 1f, _agent.areaMask))
            {
                NPCActionUtils.MoveTo(_agent, hit.position, NPCActionUtils.SpeedFor(_t.gameObject, 1f), 0.2f);
                _moving = true;
            }
        }

        if (_moving && _agent != null && !NPCActionUtils.HasArrived(_agent))
        {
            _t.LookAt(NPCConditionUtils.CenterOf(suspect));
            return Status.Running;
        }
        _moving = false;
        TeammateNodeUtils.HoldAim(_t, _agent, NPCConditionUtils.CenterOf(suspect));
        return Status.Running;
    }

    protected override void OnEnd()
    {
        if (_t != null) _t.ClearLook();
    }
}
