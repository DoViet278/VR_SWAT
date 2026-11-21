using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[Condition(name: "IsDead", story: "[Health] is depleted", category: "Conditions", id: "d57a2edd40a03d3336d798d4d54e52ca")]
public partial class IsDeadCondition : Condition
{
    [SerializeReference] public BlackboardVariable<float> Health;

    public override bool IsTrue()
    {
        NPC npc = NPCConditionUtils.StatsOf(GameObject);
        if (!NPCConditionUtils.IsLinked(Health) && npc == null) return false;
        return NPCConditionUtils.Resolve(Health, npc, n => n.Health) <= 0f;
    }
}
