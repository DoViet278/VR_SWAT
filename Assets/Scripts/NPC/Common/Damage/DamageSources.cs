using UnityEngine;

/// <summary>
/// Nhận biết nguồn sát thương đi qua IDamageable (Gun.cs / VRGrenade.cs nằm ngoài folder NPC, không sửa): cả súng lẫn
/// lựu đạn của người chơi đều gọi ApplyDamage(float, RaycastHit), không cho biết loại.
/// VRGrenade.Explode tắt hết collider của quả bom rồi mới gây sát thương (ApplyBlast) và Destroy cuối frame, nên quả
/// VRGrenade đã tắt hết collider trong tầm nổ = vụ nổ đang gây sát thương.
/// </summary>
public static class DamageSources
{
    private const float MaxBlastRadius = 8f;

    public static bool TryFindExplodingGrenade(Vector3 point, out Vector3 center)
    {
        center = point;
        float best = MaxBlastRadius;
        bool found = false;
        foreach (VRGrenade g in Object.FindObjectsByType<VRGrenade>(FindObjectsSortMode.None))
        {
            if (g == null) continue;
            float d = Vector3.Distance(g.transform.position, point);
            if (d > best || !AllCollidersOff(g)) continue;
            best = d;
            center = g.transform.position;
            found = true;
        }
        return found;
    }

    private static bool AllCollidersOff(VRGrenade g)
    {
        Collider[] cols = g.GetComponentsInChildren<Collider>();
        if (cols.Length == 0) return false;
        foreach (Collider c in cols) if (c.enabled) return false;
        return true;
    }
}
