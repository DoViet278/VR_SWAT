using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "MoveToWayPoint", story: "Move to [WayPoint] at [Speed]", category: "Action", id: "d95a6b8db68d81a68563ed362bf772a5")]
public partial class MoveToWayPointAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> WayPoint;
    [Tooltip("Độ lớn blend: 1 = đi bộ, 2 = chạy.")]
    [SerializeReference] public BlackboardVariable<float> Speed = new(1f);
    [SerializeReference] public BlackboardVariable<float> StoppingDistance = new(0.3f);

    private NavMeshAgent _agent;
    private GameObject _wayPoint;

    protected override Status OnStart()
    {
        _wayPoint = NPCConditionUtils.Resolve(WayPoint, NPCConditionUtils.StatsOf(GameObject), n => n.CurrentWayPoint);
        if (_wayPoint == null || !NPCActionUtils.TryGetAgent(GameObject, out _agent))
            return Status.Failure;

        // Tuần tra không nấp: nếu còn đang ngồi núp (vừa mất dấu Target) thì đứng dậy.
        NPC npc = NPCConditionUtils.StatsOf(GameObject);
        if (npc != null) npc.LeaveCover();

        NPCActionUtils.SetupLocomotion(GameObject, null);
        float speed = NPCActionUtils.SpeedFor(GameObject, Speed.Value);
        return NPCActionUtils.MoveTo(_agent, _wayPoint.transform.position, speed, StoppingDistance.Value)
            ? Status.Running
            : Status.Failure;
    }

    protected override Status OnUpdate()
    {
        // Thấy Player thì bỏ tuần tra để graph chuyển sang Chase/Attack.
        if (_wayPoint == null || NPCActionUtils.HasThreat(GameObject) || NPCActionUtils.PathFailed(_agent))
            return Status.Failure;
        return NPCActionUtils.HasArrived(_agent) ? Status.Success : Status.Running;
    }

    protected override void OnEnd() => NPCActionUtils.Stop(_agent);
}
