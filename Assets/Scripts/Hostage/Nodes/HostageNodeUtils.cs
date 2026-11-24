using System;
using Unity.Behavior;
using UnityEngine;

/// <summary>
/// Hàm dùng chung cho Condition / Action của Hostage.
/// Ô của node NỐI với biến Blackboard thì dùng giá trị Blackboard, CHƯA NỐI thì lấy từ component Hostage
/// (cùng quy ước với NPCConditionUtils).
/// </summary>
public static class HostageNodeUtils
{
    public static Hostage Of(GameObject go) => go != null && go.TryGetComponent(out Hostage h) ? h : null;

    /// <summary>Self chưa nối / trống thì là GameObject đang chạy graph.</summary>
    public static Hostage Of(BlackboardVariable<GameObject> self, GameObject graphOwner) =>
        Of(NPCConditionUtils.ResolveSelf(self, graphOwner));

    public static T Resolve<T>(BlackboardVariable<T> variable, Hostage hostage, Func<Hostage, T> fromHostage)
    {
        if (NPCConditionUtils.IsLinked(variable) || hostage == null) return variable != null ? variable.Value : default;
        return fromHostage(hostage);
    }
}
