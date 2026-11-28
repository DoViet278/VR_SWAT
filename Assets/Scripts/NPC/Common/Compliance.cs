using UnityEngine;

/// <summary>
/// Lệnh hô hàng chung ("Police! Drop your weapon! / Get on the ground!") của người chơi và đồng đội, như SWAT 4:
/// mọi nghi phạm và con tin nghe được (trong radius, có đường thông hoặc rất gần trong throughWallsRadius) đều xét.
/// - Nghi phạm: NPCMorale.ReceiveComplianceOrder (morale thấp / bị choáng / bị thương nặng thì đầu hàng,
///   đang nổ súng thì phớt lờ).
/// - Con tin: Hostage.ReceiveComplianceOrder (thường phải hô vài lần, bị choáng thì giơ tay ngay).
/// </summary>
public static class Compliance
{
    /// <summary>Hô hàng tại origin. exclude: bỏ qua người này (đã xử lý riêng). Trả số người chịu đầu hàng.</summary>
    public static int Order(Vector3 origin, float radius, float throughWallsRadius, GameObject exclude = null)
    {
        int complied = NPCMorale.IssueComplianceOrder(origin, radius, throughWallsRadius, exclude);
        foreach (Hostage h in Hostage.Active.ToArray())
        {
            if (h == null || h.IsDead || h.IsSurrendered || h.gameObject == exclude) continue;
            Vector3 ear = NPCConditionUtils.EyeOf(h.gameObject);
            float sqr = (ear - origin).sqrMagnitude;
            if (sqr > radius * radius) continue;
            if (sqr > throughWallsRadius * throughWallsRadius && !NPCMorale.ClearPath(origin, ear, h.transform)) continue;
            if (h.ReceiveComplianceOrder(origin)) complied++;
        }
        return complied;
    }
}
