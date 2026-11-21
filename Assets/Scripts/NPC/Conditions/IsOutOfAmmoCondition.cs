using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

/// <summary>
/// Hết đạn: băng trên súng rỗng và đã bắn hết băng dự phòng. Khi đúng, graph chạy HideNoAmmo
/// (núp vào Cover gần nhất để trốn Player). Đúng khi:
/// - đã ném hết bom, hoặc
/// - vừa ném xong (đang Cooldown của NPCGrenadeThrower): ném xong là đi nấp, kể cả khi còn bom, hoặc
/// - còn bom nhưng không có đường ném an toàn (mọi đường đều đâm tường / nổ trúng mình, đồng đội):
///   NPC núp chờ, HideNoAmmo trả Success mỗi CheckInterval giây nên graph xét lại nhánh CanThrowGrenade.
/// Không đúng khi đang rút / ném bom dở.
/// </summary>
[Serializable, GeneratePropertyBag]
[Condition(name: "IsOutOfAmmo", story: "[Self] is out of ammo and must hide in cover", category: "Conditions", id: "c41a7d92e5b84f0a8d6e2b13f97a0c58")]
public partial class IsOutOfAmmoCondition : Condition
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    public override bool IsTrue() => ShouldHide(NPCConditionUtils.ResolveSelf(Self, GameObject));

    public static bool ShouldHide(GameObject self)
    {
        if (self == null) return false;
        NPC npc = NPCConditionUtils.StatsOf(self);
        if (npc != null && npc.IsDead) return false;

        NPCWeaponHolder holder = self.GetComponentInChildren<NPCWeaponHolder>();
        if (holder == null || !holder.HasWeapon || !holder.IsOutOfAmmo || holder.IsReloading) return false;

        NPCGrenadeThrower thrower = self.GetComponentInChildren<NPCGrenadeThrower>();
        if (thrower == null) return true;
        if (thrower.IsBusy) return false;                 // đang ném dở
        return !thrower.HasGrenade || !thrower.WantsToThrow(); // hết bom, hoặc chưa ném được: núp
    }
}
