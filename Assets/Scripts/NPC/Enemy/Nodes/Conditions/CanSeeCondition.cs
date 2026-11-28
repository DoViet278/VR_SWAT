using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[Condition(name: "CanSee", story: "[Self] can see [Target] within [ViewDistance] and [ViewAngle]", category: "Conditions", id: "80f156ea44605c8ed224ea22aaef31f8")]
public partial class CanSeeCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<float> ViewDistance = new(15f);
    [SerializeReference] public BlackboardVariable<float> ViewAngle = new(120f);

    public override bool IsTrue()
    {
        GameObject self = NPCConditionUtils.ResolveSelf(Self, GameObject);
        NPC npc = NPCConditionUtils.StatsOf(self);
        GameObject target = NPCConditionUtils.ResolveTarget(Target, npc);
        if (self == null || target == null) return false;

        // Đang giao chiến thì vẫn đuổi theo dù tạm khuất (vd. đang ngồi sau vật che) trong AlertMemory giây.
        if (npc != null && npc.IsAlerted) return true;

        float distance = NPCConditionUtils.Resolve(ViewDistance, npc, n => n.ViewDistance);
        float angle = NPCConditionUtils.Resolve(ViewAngle, npc, n => n.ViewAngle);
        return NPCConditionUtils.CanSee(self, target, distance, angle);
    }
}
