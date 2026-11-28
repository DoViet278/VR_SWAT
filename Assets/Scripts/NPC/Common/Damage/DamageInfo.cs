using UnityEngine;

/// <summary>Loại sát thương: đạn, nổ (lựu đạn sát thương), choáng (flashbang nổ sát người).</summary>
public enum DamageType
{
    Bullet,
    Explosion,
    Flash,
}

/// <summary>Vùng trúng đạn: đầu (nhân nhiều), thân (giáp thân chặn bớt), tay chân (nhân ít).</summary>
public enum HitZone
{
    Torso,
    Head,
    Limb,
}

/// <summary>
/// Một lần gây sát thương (thiết kế lại theo SWAT 4).
/// Amount: sát thương gốc của vũ khí (chưa nhân vùng trúng / giáp). From: vị trí người bắn / tâm vụ nổ (chọn anim trúng
/// đạn / chết đúng hướng). Point: điểm trúng (xác định vùng trúng nếu Zone chưa biết). Source: ai gây ra.
/// </summary>
public struct DamageInfo
{
    public float Amount;
    public DamageType Type;
    public HitZone Zone;
    public bool ZoneKnown;
    public Vector3 From;
    public bool HasFrom;
    public Vector3 Point;
    public bool HasPoint;
    public GameObject Source;

    public static DamageInfo Bullet(float amount, Vector3 from, GameObject source = null) => new()
    {
        Amount = amount, Type = DamageType.Bullet, From = from, HasFrom = true, Source = source,
    };

    public static DamageInfo Explosion(float amount, Vector3 center, GameObject source = null) => new()
    {
        Amount = amount, Type = DamageType.Explosion, Zone = HitZone.Torso, ZoneKnown = true, From = center, HasFrom = true,
        Point = center, HasPoint = true, Source = source,
    };

    public DamageInfo WithZone(HitZone zone)
    {
        Zone = zone;
        ZoneKnown = true;
        return this;
    }

    public DamageInfo WithPoint(Vector3 point)
    {
        Point = point;
        HasPoint = true;
        return this;
    }

    public override string ToString() => $"{Type} {Amount:0.#}{(ZoneKnown ? " " + Zone : "")}";
}

/// <summary>Xác định vùng trúng và hệ số sát thương theo vùng / giáp (dùng chung cho NPC, con tin, người chơi).</summary>
public static class DamageModel
{
    /// <summary>Hệ số theo vùng trúng (chỉ áp cho đạn).</summary>
    public const float HeadMultiplier = 3f;
    public const float LimbMultiplier = 0.6f;

    /// <summary>Giáp chặn nổ bằng ngần này phần so với chặn đạn.</summary>
    public const float ArmorVsExplosion = 0.5f;

    /// <summary>
    /// Vùng trúng của point trên một người (Animator Humanoid): gần xương đầu = Head; thấp hơn hông hoặc xa trục thân
    /// (tay) = Limb; còn lại = Torso. Không có xương thì theo chiều cao so với gốc (> 85% chiều cao = đầu).
    /// </summary>
    public static HitZone ZoneOf(GameObject body, Vector3 point)
    {
        Animator anim = body.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Transform chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest)
                              ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            if (head != null && Vector3.Distance(point, head.position + Vector3.up * 0.06f) <= 0.17f) return HitZone.Head;
            if (hips != null && point.y < hips.position.y - 0.12f) return HitZone.Limb;
            if (hips != null && chest != null)
            {
                // Xa trục hông -> ngực hơn 0.24 m theo phương ngang: tay.
                Vector3 a = hips.position, b = chest.position + (chest.position - hips.position) * 0.3f;
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                Vector3 off = point - (a + ab * t);
                off.y = 0f;
                if (off.magnitude > 0.24f) return HitZone.Limb;
            }
            return HitZone.Torso;
        }
        float h = point.y - body.transform.position.y;
        float height = body.TryGetComponent(out CapsuleCollider cap) ? cap.height : 1.8f;
        if (h > height * 0.85f) return HitZone.Head;
        return h < height * 0.45f ? HitZone.Limb : HitZone.Torso;
    }

    /// <summary>
    /// Vùng trúng cho phát bắn AI nhắm vào giữa thân (không có điểm trúng thật): phần lớn trúng thân, đôi khi đầu / tay
    /// chân. accuracy cao thì dễ trúng đầu hơn một chút.
    /// </summary>
    public static HitZone RollAimedZone(float accuracy)
    {
        float r = Random.value;
        float head = Mathf.Lerp(0.04f, 0.12f, Mathf.Clamp01(accuracy));
        if (r < head) return HitZone.Head;
        return r < head + 0.2f ? HitZone.Limb : HitZone.Torso;
    }

    /// <summary>
    /// Sát thương sau vùng trúng và giáp. bodyArmor / helmet: 0..1 (phần sát thương đạn bị chặn ở thân / đầu).
    /// Đạn: nhân theo vùng, giáp thân chặn ở thân, mũ chặn ở đầu. Nổ: giáp thân chặn ArmorVsExplosion phần.
    /// Flash: không giáp, không vùng.
    /// </summary>
    public static float Resolve(in DamageInfo info, float bodyArmor, float helmet)
    {
        float amount = Mathf.Max(0f, info.Amount);
        switch (info.Type)
        {
            case DamageType.Bullet:
                switch (info.Zone)
                {
                    case HitZone.Head: return amount * HeadMultiplier * (1f - Mathf.Clamp01(helmet));
                    case HitZone.Limb: return amount * LimbMultiplier;
                    default: return amount * (1f - Mathf.Clamp01(bodyArmor));
                }
            case DamageType.Explosion:
                return amount * (1f - Mathf.Clamp01(bodyArmor) * ArmorVsExplosion);
            default:
                return amount;
        }
    }
}

/// <summary>
/// Định tuyến sát thương tới đúng người nhận: NPC (Enemy / Teammate), Hostage, SwatPlayerHealth. Object khác: gửi
/// SendMessage("TakeDamage", float) như cũ (bia tập bắn...).
/// </summary>
public static class Damage
{
    /// <summary>Gây sát thương cho target (hoặc object cha của nó). Trả true nếu có người nhận theo hệ thống mới.</summary>
    public static bool Apply(GameObject target, DamageInfo info)
    {
        if (target == null) return false;
        NPC npc = target.GetComponentInParent<NPC>();
        if (npc != null) { npc.ApplyDamage(info); return true; }
        Hostage hostage = target.GetComponentInParent<Hostage>();
        if (hostage != null) { hostage.ApplyDamage(info); return true; }
        SwatPlayerHealth player = SwatPlayerHealth.Of(target);
        if (player != null) { player.ApplyDamage(info); return true; }
        target.SendMessageUpwards("TakeDamage", info.Amount, SendMessageOptions.DontRequireReceiver);
        return false;
    }
}
