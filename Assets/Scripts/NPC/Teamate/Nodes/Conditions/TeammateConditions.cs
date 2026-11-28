using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

/// <summary>Đang thấy (hoặc vừa thấy) một NPC phe Suspect chưa đầu hàng.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Teammate Has Enemy Target", story: "[Self] has an enemy target", category: "Teammate", id: "3ba7aec4fcae400496d385e4de263f39")]
public partial class TeammateHasEnemyTargetCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Teammate t = TeammateNodeUtils.Of(Self, GameObject);
        return t != null && t.HasEnemyTarget;
    }
}

/// <summary>Squad giao canh một nghi phạm đã đầu hàng, chưa bị còng.</summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "Teammate Has Guard Target", story: "[Self] has a suspect to guard", category: "Teammate", id: "4cac418350194cae822b414d7cdd4624")]
public partial class TeammateHasGuardTargetCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue()
    {
        Teammate t = TeammateNodeUtils.Of(Self, GameObject);
        return t != null && t.HasGuardTarget;
    }
}

/// <summary>
/// OrderIs: so OrderType hiện tại. Mỗi loại lệnh là một Condition riêng (Order Is Follow, Order Is MoveTo...)
/// để khi dựng graph không phải chọn giá trị enum cho từng ô.
/// </summary>
[Serializable]
public abstract class TeammateOrderIsCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    protected abstract TeammateOrder Order { get; }

    public override bool IsTrue()
    {
        Teammate t = TeammateNodeUtils.Of(Self, GameObject);
        return t != null && !t.IsDead && t.Order == Order;
    }
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is Follow", story: "[Self] order is Follow", category: "Teammate", id: "636170106e2e415c8855fbfd90abb078")]
public partial class OrderIsFollowCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.Follow;
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is MoveTo", story: "[Self] order is MoveTo", category: "Teammate", id: "137538b8461a4ca29d6e718514a0569b")]
public partial class OrderIsMoveToCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.MoveTo;
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is HoldAim", story: "[Self] order is HoldAim", category: "Teammate", id: "d6c5fc199ec44cf886ba488fc559156c")]
public partial class OrderIsHoldAimCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.HoldAim;
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is OpenDoor", story: "[Self] order is OpenDoor", category: "Teammate", id: "0df1e09afb9c4888a94575a8b92038fe")]
public partial class OrderIsOpenDoorCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.OpenDoor;
}

[Serializable, GeneratePropertyBag]
// Giữ tên lớp / id cũ (BreachDoor) để graph đã dựng vẫn trỏ đúng; lệnh nay là PickLock (phá khóa).
[Condition(name: "Order Is PickLock", story: "[Self] order is PickLock", category: "Teammate", id: "5b131508683c420394cf519bf4fa8019")]
public partial class OrderIsBreachDoorCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.PickLock;
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is ClearRoom", story: "[Self] order is ClearRoom", category: "Teammate", id: "e8d24b6f0a7c4c91b35f6a2d9c1e7b40")]
public partial class OrderIsClearRoomCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.ClearRoom;
}

[Serializable, GeneratePropertyBag]
[Condition(name: "Order Is ThrowFlash", story: "[Self] order is ThrowFlash", category: "Teammate", id: "2686b46932de45cd9f0a7b4fdcd7bfb7")]
public partial class OrderIsThrowFlashCondition : TeammateOrderIsCondition
{
    protected override TeammateOrder Order => TeammateOrder.ThrowFlash;
}
