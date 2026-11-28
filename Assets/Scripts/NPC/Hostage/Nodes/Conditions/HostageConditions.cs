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

/// <summary>Đang bị nghi phạm khống chế (EnemyTactics / Hostage.SetHeld).</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Is Held", story: "[Self] is held by a suspect", category: "Hostage", id: "4c8e1a7d2b9f46e3a5d0c7b1e8f2a906")]
public partial class HostageIsHeldCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && !h.IsDead && h.IsHeld;
    }
}

/// <summary>Nghe súng / nổ (không quá gần) mà chưa thấy cảnh sát: bỏ chạy.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Should Flee", story: "[Self] should flee from gunfire", category: "Hostage", id: "9a2f6c3e8d1b47a5b6e9c2d4f1a8e307")]
public partial class HostageShouldFleeCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && h.ShouldFlee;
    }
}

/// <summary>Súng nổ sát bên / nổ gần: ngồi ôm đầu.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Hostage Should Cower", story: "[Self] should cower from gunfire", category: "Hostage", id: "d7b3e9a1c6f2485e9a1b4c8d3e7f2a08")]
public partial class HostageShouldCowerCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Hostage h = HostageNodeUtils.Of(Self, GameObject);
        return h != null && h.ShouldCower;
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
