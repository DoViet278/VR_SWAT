using System;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Idle", story: "Idle for [Duration] seconds", category: "Action", id: "e80fcd84fbc708a7c328e13428b1786d")]
public partial class IdleAction : Action
{
    [SerializeReference] public BlackboardVariable<float> Duration = new(2f);

    private float _endTime;

    protected override Status OnStart()
    {
        if (NPCActionUtils.TryGetAgent(GameObject, out NavMeshAgent agent))
            NPCActionUtils.Stop(agent);
        NPCActionUtils.SetupLocomotion(GameObject, null);
        if (GameObject.TryGetComponent(out NPC npc)) npc.LeaveCover();

        _endTime = Time.time + Duration.Value;
        return Status.Running;
    }

    // Thấy Player thì thôi đứng chờ (Failure) để graph đánh giá lại từ đầu.
    protected override Status OnUpdate()
    {
        if (NPCActionUtils.HasThreat(GameObject)) return Status.Failure;
        return Time.time >= _endTime ? Status.Success : Status.Running;
    }
}
