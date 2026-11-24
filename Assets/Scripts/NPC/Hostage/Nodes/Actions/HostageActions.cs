using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

/// <summary>Đã bị còng: đứng yên (anim Cuffed) cho tới hết màn.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Hostage Restrained", story: "[Self] stays restrained", category: "Hostage", id: "a6c1e9d3b7f54a28c0e1d2b3a4c5d601")]
public partial class HostageRestrainedAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    protected override Status OnStart() => HostageNodeUtils.Of(Self, GameObject) != null ? Status.Running : Status.Failure;

    protected override Status OnUpdate()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h == null || h.IsDead ? Status.Failure : Status.Running;
    }
}

/// <summary>Giơ tay đầu hàng (quỳ, tay giơ cao) và chờ người chơi còng tay.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Hostage Surrender", story: "[Self] raises hands and waits to be cuffed", category: "Hostage", id: "c3e8a1f6d2b947c5a9e0b1c2d3e4f702")]
public partial class HostageSurrenderAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    protected override Status OnStart()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        if (h == null || h.IsDead) return Status.Failure;
        h.Surrender();
        return Status.Running;
    }

    // Còng xong thì Success để graph chuyển sang nhánh Restrained.
    protected override Status OnUpdate()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        if (h == null || h.IsDead) return Status.Failure;
        return h.IsRestrained ? Status.Success : Status.Running;
    }
}

/// <summary>Sợ hãi: đứng lại, co người ôm đầu, quay mặt về phía người chơi tới khi hết sợ.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Hostage Cower", story: "[Self] cowers in fear of [Target]", category: "Hostage", id: "e9b2d7a4c1f6483e8d5a0b9c8e7f6a03")]
public partial class HostageCowerAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    private Hostage _hostage;

    protected override Status OnStart()
    {
        _hostage = HostageNodeUtils.Of(Self, GameObject);
        if (_hostage == null || _hostage.IsDead) return Status.Failure;
        if (NPCActionUtils.TryGetAgent(_hostage.gameObject, out NavMeshAgent agent)) NPCActionUtils.Stop(agent);
        NPCActionUtils.SetupLocomotion(_hostage.gameObject, null);
        _hostage.SetScared(true);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_hostage == null || _hostage.IsDead) return Status.Failure;
        // Bị hô thì thôi run sợ để graph chuyển sang đầu hàng.
        if (_hostage.ComplyOrdered || _hostage.IsSurrendered) return Status.Failure;
        return _hostage.IsScared ? Status.Running : Status.Success;
    }

    protected override void OnEnd()
    {
        if (_hostage != null) _hostage.SetScared(false);
    }
}

/// <summary>Kêu cứu: đứng lại, quỳ gọi "Help!" (anim + tiếng) trong một khoảng thời gian.</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Hostage Call For Help", story: "[Self] calls for help", category: "Hostage", id: "f2a5c8e1b4d74f96a3c2e1d0b9a8c704")]
public partial class HostageCallForHelpAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private Hostage _hostage;
    private float _endTime;

    protected override Status OnStart()
    {
        _hostage = HostageNodeUtils.Of(Self, GameObject);
        if (_hostage == null || _hostage.IsDead) return Status.Failure;
        if (NPCActionUtils.TryGetAgent(_hostage.gameObject, out NavMeshAgent agent)) NPCActionUtils.Stop(agent);
        NPCActionUtils.SetupLocomotion(_hostage.gameObject, null);
        _endTime = Time.time + _hostage.CallForHelp();
        return Status.Running;
    }

    // Thấy người chơi / bị hô thì dừng kêu cứu để graph chọn lại.
    protected override Status OnUpdate()
    {
        if (_hostage == null || _hostage.IsDead) return Status.Failure;
        if (_hostage.IsScared || _hostage.ComplyOrdered) return Status.Failure;
        return Time.time >= _endTime ? Status.Success : Status.Running;
    }
}

/// <summary>Đi tới waypoint (đi bộ / chạy theo Speed: 1 = đi bộ, 2 = chạy).</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Hostage Move To WayPoint", story: "[Self] moves to [WayPoint] at [Speed]", category: "Hostage", id: "b8d1f4a7c2e5493b8e6d5c4b3a2f1e05")]
public partial class HostageMoveToWayPointAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> WayPoint;
    [Tooltip("Độ lớn blend: 1 = đi bộ, 2 = chạy.")]
    [SerializeReference] public BlackboardVariable<float> Speed = new(1f);
    [SerializeReference] public BlackboardVariable<float> StoppingDistance = new(0.3f);

    private Hostage _hostage;
    private NavMeshAgent _agent;
    private GameObject _wayPoint;

    protected override Status OnStart()
    {
        _hostage = HostageNodeUtils.Of(Self, GameObject);
        if (_hostage == null) return Status.Failure;
        _wayPoint = HostageNodeUtils.Resolve(WayPoint, _hostage, h => h.CurrentWayPoint);
        if (_wayPoint == null || !NPCActionUtils.TryGetAgent(_hostage.gameObject, out _agent)) return Status.Failure;

        NPCActionUtils.SetupLocomotion(_hostage.gameObject, null);
        float speed = NPCActionUtils.SpeedFor(_hostage.gameObject, Speed.Value);
        return NPCActionUtils.MoveTo(_agent, _wayPoint.transform.position, speed, StoppingDistance.Value)
            ? Status.Running
            : Status.Failure;
    }

    protected override Status OnUpdate()
    {
        if (_wayPoint == null || _hostage == null || _hostage.NeedsAttention || NPCActionUtils.PathFailed(_agent))
            return Status.Failure;
        return NPCActionUtils.HasArrived(_agent) ? Status.Success : Status.Running;
    }

    protected override void OnEnd() => NPCActionUtils.Stop(_agent);
}

/// <summary>Đứng chờ. Dừng sớm khi có việc gấp hơn (sợ, bị hô, tới lúc kêu cứu).</summary>
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Hostage Idle", story: "[Self] waits for [Duration] seconds", category: "Hostage", id: "d4f7b1e9a3c64d82b5f0e9d8c7b6a506")]
public partial class HostageIdleAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<float> Duration = new(2f);

    private Hostage _hostage;
    private float _endTime;

    protected override Status OnStart()
    {
        _hostage = HostageNodeUtils.Of(Self, GameObject);
        if (_hostage == null) return Status.Failure;
        if (NPCActionUtils.TryGetAgent(_hostage.gameObject, out NavMeshAgent agent)) NPCActionUtils.Stop(agent);
        NPCActionUtils.SetupLocomotion(_hostage.gameObject, null);
        _endTime = Time.time + Duration.Value;
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_hostage == null || _hostage.NeedsAttention) return Status.Failure;
        return Time.time >= _endTime ? Status.Success : Status.Running;
    }
}
