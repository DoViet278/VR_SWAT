using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Is Restrained", story: "[Self] is restrained", category: "Hostage", id: "3f9a1c7e5b2d48e6a1c0d9e8f7b6a501")]
public partial class HostageIsRestrainedCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && !h.IsDead && h.IsRestrained;
    }
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Should Surrender", story: "[Self] is ordered to surrender", category: "Hostage", id: "7c2e9b4a1d6f43b8a5e0c3d2b1f9e802")]
public partial class HostageShouldSurrenderCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && !h.IsDead && (h.IsSurrendered || h.ComplyOrdered);
    }
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Is Scared", story: "[Self] is scared of [Target]", category: "Hostage", id: "b5d8e2f1a9c64e7d8b3a2c1e0f9d7a03")]
public partial class HostageIsScaredCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && h.IsScared;
    }
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Should Call For Help", story: "[Self] should call for help", category: "Hostage", id: "e1a7c3b9d5f2486a9c8e7b6d5a4f3c04")]
public partial class HostageShouldCallForHelpCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && h.ShouldCallForHelp;
    }
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Get Next WayPoint", story: "[Self] gets next [CurrentWayPoint] from [WayPoints] at [Index]", category: "Hostage", id: "9d4b2e8f6a1c47e3b0d9c8a7e6f5b405")]
public partial class HostageGetNextWayPointCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<GameObject> CurrentWayPoint;
    [SerializeReference] public BlackboardVariable<List<GameObject>> WayPoints;
    [SerializeReference] public BlackboardVariable<int> Index;

    // Chỉ chuyển waypoint một lần mỗi khi bắt đầu đánh giá (kiểm tra lại mỗi frame không bỏ qua waypoint).
    private GameObject _current;

    public override void OnStart()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        List<GameObject> wayPoints = HostageNodeUtils.Resolve(WayPoints, h, x => x.WayPoints);
        _current = null;
        if (wayPoints != null && wayPoints.Count > 0)
        {
            int index = Index != null ? Mathf.Abs(Index.Value) % wayPoints.Count : 0;
            _current = wayPoints[index];
            if (Index != null) Index.Value = (index + 1) % wayPoints.Count;
        }
        if (CurrentWayPoint != null) CurrentWayPoint.Value = _current;
        if (h != null) h.CurrentWayPoint = _current;
    }

    public override bool IsTrue() => _current != null;
}
