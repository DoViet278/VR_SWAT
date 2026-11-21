using System;
using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[Condition(name: "GetNextWayPoint", story: "Get next [CurrentWayPoint] from [WayPoints] at [Index]", category: "Conditions", id: "0a25c35a97f4d8045252582d178c4040")]
public partial class GetNextWayPointCondition : Condition
{
    [SerializeReference] public BlackboardVariable<List<GameObject>> WayPoints;
    [SerializeReference] public BlackboardVariable<int> Index;
    [SerializeReference] public BlackboardVariable<GameObject> CurrentWayPoint;

    private GameObject _current;

    // Chỉ chuyển waypoint một lần mỗi khi bắt đầu đánh giá, để việc kiểm tra lại mỗi frame (Abort If) không bỏ qua waypoint.
    public override void OnStart()
    {
        NPC npc = NPCConditionUtils.StatsOf(GameObject);
        List<GameObject> wayPoints = NPCConditionUtils.Resolve(WayPoints, npc, n => n.WayPoints);
        _current = null;

        if (wayPoints != null && wayPoints.Count > 0)
        {
            int index = Index != null ? Mathf.Abs(Index.Value) % wayPoints.Count : 0;
            _current = wayPoints[index];
            if (Index != null) Index.Value = (index + 1) % wayPoints.Count;
        }

        // Ghi cả vào biến Blackboard (nếu có) và NPC, để MoveToWayPoint đọc được dù ô WayPoint có nối hay không.
        if (CurrentWayPoint != null) CurrentWayPoint.Value = _current;
        if (npc != null) npc.CurrentWayPoint = _current;
    }

    public override bool IsTrue() => _current != null;
}
