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

    private Teammate _t;
    private NavMeshAgent _agent;
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
        if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;

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
/// Mở cửa OrderDoor: door.Request(Open) -> chờ cửa quay xong (!IsMoving) -> Release.
/// Refused (cửa khóa) = thất bại. Busy (người chơi đang nắm cửa) thì chờ, quá 10 s = thất bại.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Open Door", story: "[Self] opens the ordered door", category: "Teammate", id: "7dfb9c01683149dc8a75467ad662dbf7")]
public partial class TeammateOpenDoorAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private Teammate _t;
    private NavMeshAgent _agent;
    private DoorController _door;
    private int _id;
    private bool _requested;
    private float _deadline;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;

        _door = _t.OrderDoor;
        if (_door == null)
        {
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        NPCActionUtils.Stop(_agent);
        _t.LookAt(_door.LockPoint.position);
        _requested = false;
        _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.NeedsAttention(_id)) return Status.Failure;

        if (!_requested)
        {
            switch (_door.Request(DoorAction.Open, _t, _t.transform.position))
            {
                case DoorRequestResult.Accepted:
                    _requested = true;
                    return Status.Running;
                case DoorRequestResult.Refused:
                    _t.ReportDone(_id, false);
                    return Status.Success;
                default: // Busy
                    if (Time.time <= _deadline) return Status.Running;
                    _t.ReportDone(_id, false);
                    return Status.Success;
            }
        }

        if (_door.IsMoving && Time.time <= _deadline) return Status.Running;
        _door.Release(_t);
        _requested = false;
        bool open = _door.State == DoorState.Open || _door.State == DoorState.Breached;
        if (!open) _t.ReportDone(_id, false);
        return Status.Success;
    }

    protected override void OnEnd()
    {
        if (_requested && _door != null) _door.Release(_t);
        _requested = false;
    }
}

/// <summary>
/// Phá cửa: TẠM ĐỂ TRỐNG, trả Success ngay (quyết định 24/11: cửa khóa thì chờ người chơi mở / phá).
/// Sau này thêm mở khóa / phá cửa (BreachCharge, DoorController.Breach, điểm breachFrom / breachAim) vào đây.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Breach Door", story: "[Self] breaches the ordered door", category: "Teammate", id: "b94bdb5640da40008fa18477158e1361")]
public partial class TeammateBreachDoorAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    protected override Status OnStart()
    {
        Teammate t = TeammateNodeUtils.Of(Self, GameObject);
        if (t == null || t.NeedsAttention(t.OrderId)) return Status.Failure;
        return Status.Success;
    }
}

/// <summary>Quay về OrderPos (điểm giữa phòng) và ném flashbang (NPCGrenadeThrower.BeginThrowFlashbangAt). Ném xong: Success.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Teammate Throw Flash", story: "[Self] throws a flashbang at the ordered point", category: "Teammate", id: "55675269b45142ba87d0538a8c5c2f9e")]
public partial class TeammateThrowFlashAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float FacingTolerance = 20f;

    private Teammate _t;
    private NavMeshAgent _agent;
    private NPCGrenadeThrower _thrower;
    private int _id;
    private bool _started;
    private float _deadline;

    protected override Status OnStart()
    {
        _t = TeammateNodeUtils.Of(Self, GameObject);
        if (_t == null) return Status.Failure;
        _id = _t.OrderId;
        if (_t.NeedsAttention(_id)) return Status.Failure;
        if (_t.IsOrderFinished) return Status.Success;

        _thrower = _t.GetComponentInChildren<NPCGrenadeThrower>();
        if (_thrower == null)
        {
            Debug.LogWarning($"[{_t.CallSign}] Không có NPCGrenadeThrower để ném flashbang.", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        NPCActionUtils.TryGetAgent(_t.gameObject, out _agent);
        NPCActionUtils.Stop(_agent);
        TeammateFollowPlayerAction.StopAiming(_t);
        _t.LookAt(_t.OrderPos + Vector3.up);
        _started = false;
        _deadline = Time.time + TeammateNodeUtils.MoveTimeout;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_t == null || _t.IsDead) return Status.Failure;

        // Đã rút flash thì ném cho xong rồi mới xử lý việc khác (không rút quả thứ hai).
        if (_started) return _thrower.IsBusy ? Status.Running : Status.Success;
        if (_t.NeedsAttention(_id)) return Status.Failure;

        if (Time.time > _deadline)
        {
            Debug.LogWarning($"[{_t.CallSign}] Không ném được flashbang (thiếu Flashbang Prefab hoặc hết flash).", _t);
            _t.ReportDone(_id, false);
            return Status.Success;
        }
        NPCActionUtils.Stop(_agent);
        if (NPCConditionUtils.HorizontalAngleTo(_t.gameObject, _t.OrderPos) > FacingTolerance) return Status.Running;
        if (_thrower.BeginThrowFlashbangAt(_t.OrderPos)) _started = true;
        return Status.Running;
    }
}

/// <summary>
/// Giao chiến theo luật SWAT 4: dừng, quay mặt, hô 1 lần mỗi nghi phạm, chờ ShoutWait giây,
/// vẫn chưa đầu hàng thì bắn loạt 2-3 viên. Trước mỗi viên kiểm tra đường đạn: người chơi / đồng đội chắn thì
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

        // Hô trước. Đang cooldown hô (vừa hô người khác) thì chờ, không bắn người chưa được hô.
        if (!_t.TryGetShoutTime(enemy, out float shoutTime))
        {
            _t.TryShoutAt(enemy);
            return Status.Running;
        }
        if (enemy.IsSurrendered) return Status.Success;
        if (Time.time < shoutTime + _t.ShoutWait) return Status.Running;
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
        if (hit) enemy.TakeDamageFrom(_t.Damage, muzzle);

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
