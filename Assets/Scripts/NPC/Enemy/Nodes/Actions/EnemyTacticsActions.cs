using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using VRSwat.Doors;
using Action = Unity.Behavior.Action;
using Unity.Properties;
using Random = UnityEngine.Random;

// Các nhánh chiến thuật SWAT 4 của nghi phạm. Mỗi action tự thoát (Failure / Success) khi có việc gấp hơn để
// Selector chọn lại nhánh (graph Enemy không dùng Abort).

/// <summary>
/// Đi xem tiếng động: đi bộ cảnh giác tới chỗ nghe thấy, nhìn quanh (quay trái / phải) LookAroundTime giây, rồi đi
/// về chỗ cũ (Home). Thấy cảnh sát (IsAlerted) thì Failure để nhánh giao chiến chạy. Nghe tiếng mới hơn thì đổi chỗ.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Investigate Noise", story: "[Self] investigates the heard noise", category: "Enemy", id: "5d8a2f7c1b9e4c3d8a6f2e9b7c1d4a85")]
public partial class InvestigateAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private enum Phase { GoTo, LookAround, Return }

    private NPC _npc;
    private EnemyTactics _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private Transform _look;
    private Phase _phase;
    private Vector3 _spot;
    private float _noiseTime;
    private float _phaseEnd;
    private float _lookStart;

    protected override Status OnStart()
    {
        _npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (_npc == null || !NPCActionUtils.TryGetAgent(_npc.gameObject, out _agent)) return Status.Failure;
        _t = EnemyNodeUtils.TacticsOf(_npc);
        if (!NPCHearing.TryGetNoise(_npc.gameObject, _t.NoiseMemory, out HeardNoise noise)) return Status.Failure;
        _holder = _npc.GetComponentInChildren<NPCWeaponHolder>();
        if (_look == null) _look = new GameObject($"{_npc.name}_InvestigateLook").transform;
        _npc.LeaveCover();
        _npc.Say(NPCVoiceLine.Investigate); // "What was that?"
        BeginGoTo(noise);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_npc == null || _npc.IsDead || _npc.IsSurrendered) return Status.Failure;
        if (_npc.IsAlerted) return Status.Failure; // thấy cảnh sát: nhánh giao chiến lo
        if (_npc.MoraleState == MoraleState.Panicked) return Status.Failure; // hoảng: không dám đi xem

        // Tiếng mới hơn (vd. lại có súng nổ): đi tới chỗ mới.
        if (NPCHearing.TryGetNoise(_npc.gameObject, _t.NoiseMemory, out HeardNoise newer) && newer.Time > _noiseTime + 0.5f)
            BeginGoTo(newer);

        // Đi với súng giơ sẵn (cảnh giác).
        if (_holder != null && _holder.HasWeapon && !_holder.IsReloading) _holder.KeepAiming();

        switch (_phase)
        {
            case Phase.GoTo:
                if (NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd || NPCActionUtils.HasArrived(_agent))
                {
                    NPCActionUtils.Stop(_agent);
                    _phase = Phase.LookAround;
                    _lookStart = Time.time;
                    _phaseEnd = Time.time + Random.Range(_t.LookAroundTime.x, Mathf.Max(_t.LookAroundTime.x, _t.LookAroundTime.y));
                    NPCActionUtils.SetupLocomotion(_npc.gameObject, _look);
                }
                return Status.Running;

            case Phase.LookAround:
                // Quay nhìn qua lại ±70° quanh hướng tiếng động.
                Vector3 toNoise = EnemyNodeUtils.Flat(_spot - _npc.transform.position);
                if (toNoise.sqrMagnitude < 0.01f) toNoise = _npc.transform.forward;
                float sweep = Mathf.Sin((Time.time - _lookStart) * 1.3f) * 70f;
                _look.position = _npc.transform.position + Quaternion.Euler(0f, sweep, 0f) * toNoise.normalized * 4f + Vector3.up * 1.4f;
                if (Time.time < _phaseEnd) return Status.Running;
                if (_npc.TryGetComponent(out NPCHearing hearing)) hearing.Consume();
                _phase = Phase.Return;
                NPCActionUtils.SetupLocomotion(_npc.gameObject, null);
                NPCActionUtils.MoveTo(_agent, _t.Home, NPCActionUtils.SpeedFor(_npc.gameObject, 1f), 0.3f);
                _phaseEnd = Time.time + _t.InvestigateTimeout;
                return Status.Running;

            default: // Return
                bool done = NPCActionUtils.PathFailed(_agent) || Time.time > _phaseEnd || NPCActionUtils.HasArrived(_agent);
                return done ? Status.Success : Status.Running;
        }
    }

    protected override void OnEnd()
    {
        NPCActionUtils.Stop(_agent);
        if (_npc != null) NPCActionUtils.SetupLocomotion(_npc.gameObject, null);
        if (_holder != null) _holder.StopAiming();
    }

    private void BeginGoTo(HeardNoise noise)
    {
        _noiseTime = noise.Time;
        _spot = NavMesh.SamplePosition(noise.Position, out NavMeshHit hit, 3f, _agent.areaMask) ? hit.position : noise.Position;
        _phase = Phase.GoTo;
        NPCActionUtils.SetupLocomotion(_npc.gameObject, null);
        NPCActionUtils.MoveTo(_agent, _spot, NPCActionUtils.SpeedFor(_npc.gameObject, 1f), 1.2f);
        _phaseEnd = Time.time + _t.InvestigateTimeout;
    }
}

/// <summary>
/// Cố thủ: đứng (hoặc ngồi) tại chỗ, chĩa súng vào cửa cố thủ (EnemyTactics.BarricadeDoor), không tuần tra. Thấy
/// cảnh sát thì Failure để nhánh giao chiến chạy. Nghe tiếng động cũng không bỏ chỗ (chỉ quay về phía cửa).
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Barricade", story: "[Self] holds the room and covers the door", category: "Enemy", id: "8e1c4b7a2d9f4e6c9b3a5d8e1f7c2b96")]
public partial class BarricadeAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private NPC _npc;
    private EnemyTactics _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private Transform _look;

    protected override Status OnStart()
    {
        _npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (_npc == null) return Status.Failure;
        _t = EnemyNodeUtils.TacticsOf(_npc);
        NPCActionUtils.TryGetAgent(_npc.gameObject, out _agent);
        _holder = _npc.GetComponentInChildren<NPCWeaponHolder>();
        if (_look == null) _look = new GameObject($"{_npc.name}_BarricadeLook").transform;
        NPCActionUtils.Stop(_agent);
        if (_t.BarricadeCrouch) _npc.CrouchInPlace();
        NPCActionUtils.SetupLocomotion(_npc.gameObject, _look);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_npc == null || _npc.IsDead || _npc.IsSurrendered || _npc.IsAlerted || !_t.Barricaded) return Status.Failure;
        DoorController door = _t.BarricadeDoor();
        Vector3 aim = door != null
            ? door.transform.position + Vector3.up * 1.3f
            : _npc.transform.position + _npc.transform.forward * 4f + Vector3.up * 1.3f;
        _look.position = aim;
        NPCActionUtils.Stop(_agent);
        if (_holder != null && _holder.HasWeapon && !_holder.IsReloading) _holder.KeepAiming();
        return Status.Running;
    }

    protected override void OnEnd()
    {
        if (_npc == null) return;
        NPCActionUtils.SetupLocomotion(_npc.gameObject, null);
        if (_t != null && _t.BarricadeCrouch && !_npc.IsDead && !_npc.IsSurrendered) _npc.LeaveCover();
    }
}

/// <summary>
/// Bỏ chạy (morale Panicked): hô "Run!", chạy tới chỗ xa cảnh sát (EnemyNodeUtils.TryFindFleePoint). Bị dồn vào
/// góc (không có chỗ chạy) thì Failure ngay (nhánh khác: đánh / nấp; dễ đầu hàng khi bị hô). Tới nơi: Success.
/// Đặt EnemyTactics.LastFleeTime để không chạy liên tục.
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Flee", story: "[Self] flees from the police", category: "Enemy", id: "2f9b6d3e8c1a4f7b8e2d6c9a3b5f1e74")]
public partial class FleeAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float Timeout = 10f;

    private NPC _npc;
    private EnemyTactics _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private float _deadline;

    protected override Status OnStart()
    {
        _npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (_npc == null || !NPCActionUtils.TryGetAgent(_npc.gameObject, out _agent)) return Status.Failure;
        _t = EnemyNodeUtils.TacticsOf(_npc);
        _t.LastFleeTime = Time.time;
        if (!EnemyNodeUtils.TryFindFleePoint(_npc, _npc.ThreatPosition, _t.FleeDistance, out Vector3 point))
        {
            Debug.Log($"[Enemy] {_npc.name}: hoảng nhưng bị dồn vào góc, không chạy được.", _npc);
            return Status.Failure;
        }
        _holder = _npc.GetComponentInChildren<NPCWeaponHolder>();
        if (_holder != null) _holder.StopAiming();
        _npc.LeaveCover();
        _npc.Say(NPCVoiceLine.Flee); // "Run!"
        NPCActionUtils.SetupLocomotion(_npc.gameObject, null);
        NPCActionUtils.MoveTo(_agent, point, NPCActionUtils.SpeedFor(_npc.gameObject, 2f), 0.3f);
        _deadline = Time.time + Timeout;
        Debug.Log($"[Enemy] {_npc.name}: bỏ chạy tới {point}.", _npc);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_npc == null || _npc.IsDead || _npc.IsSurrendered) return Status.Failure;
        if (NPCActionUtils.PathFailed(_agent) || Time.time > _deadline) return Status.Failure;
        if (!NPCActionUtils.HasArrived(_agent)) return Status.Running;
        _t.OnFleeArrived(_npc.transform.position); // cố thủ ở chỗ mới, không quay về
        Debug.Log($"[Enemy] {_npc.name}: chạy tới nơi, cố thủ tại {_npc.transform.position}.", _npc);
        return Status.Success;
    }

    protected override void OnEnd() => NPCActionUtils.Stop(_agent);
}

/// <summary>
/// Khống chế con tin: bắt con tin gần nhất (Hostage.SetHeld), đứng sau con tin (con tin chắn giữa mình và cảnh sát),
/// chĩa súng vào con tin, hô "Back off or he dies!" mỗi ThreatenInterval giây. Buông (Success) khi morale tụt dưới
/// Aggressive hoặc bị hô đủ ReleaseAfterShouts lần (graph chọn lại: thường là đầu hàng / đánh).
/// </summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Threaten Hostage", story: "[Self] takes a hostage", category: "Enemy", id: "6a3e8c1f5b2d4a9e8f1c7b3d6e2a9c57")]
public partial class ThreatenHostageAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private const float BehindDistance = 0.8f;
    private const float RepathInterval = 0.4f;

    private NPC _npc;
    private EnemyTactics _t;
    private NavMeshAgent _agent;
    private NPCWeaponHolder _holder;
    private Transform _look;
    private Hostage _hostage;
    private float _nextThreat;
    private float _nextRepath;

    protected override Status OnStart()
    {
        _npc = EnemyNodeUtils.NpcOf(Self, GameObject);
        if (_npc == null) return Status.Failure;
        _t = EnemyNodeUtils.TacticsOf(_npc);
        _hostage = _t.HeldHostage != null ? _t.HeldHostage : _t.FindHostageToGrab();
        if (_hostage == null || !_t.GrabHostage(_hostage)) return Status.Failure;
        NPCActionUtils.TryGetAgent(_npc.gameObject, out _agent);
        _holder = _npc.GetComponentInChildren<NPCWeaponHolder>();
        if (_look == null) _look = new GameObject($"{_npc.name}_HostageLook").transform;
        _npc.LeaveCover();
        NPCActionUtils.SetupLocomotion(_npc.gameObject, _look);
        _nextThreat = 0f;
        _nextRepath = 0f;
        Debug.Log($"[Enemy] {_npc.name}: khống chế con tin {_hostage.name}.", _npc);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_npc == null || _npc.IsDead || _npc.IsSurrendered) return Status.Failure;
        if (_hostage == null || !_hostage.IsHeld || _hostage.HeldBy != _npc) return Status.Failure;
        if (_t.ShouldReleaseHostage || _npc.MoraleState != MoraleState.Aggressive) return Status.Success; // buông

        // Đứng sau con tin, con tin chắn giữa mình và cảnh sát.
        Vector3 threat = _npc.ThreatPosition;
        Vector3 away = EnemyNodeUtils.Flat(_hostage.transform.position - threat);
        Vector3 behind = _hostage.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : -_hostage.transform.forward) * BehindDistance;
        if (_agent != null && Time.time >= _nextRepath)
        {
            _nextRepath = Time.time + RepathInterval;
            if (Vector3.Distance(EnemyNodeUtils.Flat(_npc.transform.position), EnemyNodeUtils.Flat(behind)) > 0.4f
                && NavMesh.SamplePosition(behind, out NavMeshHit hit, 0.6f, _agent.areaMask))
                NPCActionUtils.MoveTo(_agent, hit.position, NPCActionUtils.SpeedFor(_npc.gameObject, 1f), 0.15f);
            else NPCActionUtils.Stop(_agent);
        }

        // Chĩa súng vào đầu con tin.
        _look.position = NPCConditionUtils.EyeOf(_hostage.gameObject);
        if (_holder != null && _holder.HasWeapon && !_holder.IsReloading) _holder.KeepAiming();

        if (Time.time >= _nextThreat)
        {
            _nextThreat = Time.time + _t.ThreatenInterval;
            _npc.Say(NPCVoiceLine.Threaten); // "Back off or he dies!"
        }
        return Status.Running;
    }

    protected override void OnEnd()
    {
        if (_t != null) _t.ReleaseHostage();
        NPCActionUtils.Stop(_agent);
        if (_npc != null) NPCActionUtils.SetupLocomotion(_npc.gameObject, null);
        if (_npc != null && _hostage != null) Debug.Log($"[Enemy] {_npc.name}: buông con tin {_hostage.name}.", _npc);
    }
}
