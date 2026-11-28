using System.Collections.Generic;
using UnityEngine;

/// <summary>Loại tiếng động NPC nghe được (xếp theo mức nguy hiểm tăng dần).</summary>
public enum NoiseType
{
    Footstep,
    DoorOpen,
    Shout,
    Gunshot,
    DoorBreach,
    Explosion,
}

/// <summary>Một tiếng động đã nghe: ở đâu, loại gì, lúc nào, ai gây ra.</summary>
public struct HeardNoise
{
    public Vector3 Position;
    public NoiseType Type;
    public float Time;
    public GameObject Source;

    /// <summary>Tiếng "nguy hiểm" (súng, phá cửa, nổ) chứ không phải tiếng bình thường.</summary>
    public bool IsViolent => Type >= NoiseType.Gunshot;
}

/// <summary>
/// Thính giác của NPC / con tin (SWAT 4: nghi phạm nghe tiếng súng / phá cửa thì đi xem, con tin hoảng sợ).
///
/// - NPCHearing.Report(pos, radius, type, source) phát một tiếng động. Ai trong radius nghe được; bị tường chắn thì
///   chỉ nghe trong radius * ThroughWallFactor. Nguồn tiếng: NPCWorldListener (súng người chơi, cửa), NPCWeaponHolder
///   (NPC bắn), NPCGrenade / NPCMorale.ReportExplosion (nổ), PlayerShout (hô).
/// - Mỗi người nhớ tiếng đáng chú ý nhất gần đây (LastNoise): tiếng nguy hiểm hơn hoặc mới hơn thì thay.
/// - Node của graph đọc HasNoise(maxAge) / LastNoise rồi gọi Consume() khi đã xử lý.
/// - Con tin nghe tiếng nguy hiểm trong ScareRadius: sợ ngay (Hostage.NotifyThreat).
/// Component tự được thêm vào NPC / con tin lần đầu nghe thấy.
/// </summary>
[DisallowMultipleComponent]
public class NPCHearing : MonoBehaviour
{
    /// <summary>Qua tường chỉ nghe được trong ngần này phần bán kính.</summary>
    public const float ThroughWallFactor = 0.5f;

    /// <summary>Con tin nghe tiếng nguy hiểm trong khoảng này (m) thì hoảng sợ.</summary>
    private const float HostageScareRadius = 12f;

    [Tooltip("Nhân bán kính nghe (nghe kém < 1, thính > 1).")]
    [SerializeField, Range(0f, 2f)] private float _sensitivity = 1f;
    [SerializeField] private bool _log;

    private bool _hasNoise;
    private HeardNoise _last;

    public HeardNoise LastNoise => _last;

    /// <summary>(người nghe, tiếng) mỗi khi ai đó nghe thấy.</summary>
    public static event System.Action<NPCHearing, HeardNoise> AnyHeard;

    /// <summary>Phát tiếng động tại pos, nghe được trong radius (m). source: người gây ra (không tự nghe chính mình).</summary>
    public static void Report(Vector3 pos, float radius, NoiseType type, GameObject source = null)
    {
        if (radius <= 0f) return;
        var listeners = new List<GameObject>();
        foreach (NPC npc in NPC.Active) if (npc != null && !npc.IsDead) listeners.Add(npc.gameObject);
        foreach (Hostage h in Hostage.Active) if (h != null && !h.IsDead) listeners.Add(h.gameObject);

        foreach (GameObject go in listeners)
        {
            if (source != null && (go == source || source.transform.IsChildOf(go.transform))) continue;
            NPCHearing hearing = go.TryGetComponent(out NPCHearing h) ? h : null;
            float r = radius * (hearing != null ? hearing._sensitivity : 1f);
            Vector3 ear = NPCConditionUtils.EyeOf(go);
            float dist = Vector3.Distance(ear, pos);
            if (dist > r) continue;
            if (dist > r * ThroughWallFactor && !NPCMorale.ClearPath(pos, ear, go.transform)) continue;
            if (hearing == null) hearing = go.AddComponent<NPCHearing>();
            hearing.Hear(new HeardNoise { Position = pos, Type = type, Time = Time.time, Source = source }, dist);
        }
    }

    /// <summary>Có tiếng động chưa xử lý, nghe trong maxAge giây trở lại.</summary>
    public bool HasNoise(float maxAge) => _hasNoise && Time.time - _last.Time <= maxAge;

    /// <summary>Có tiếng nguy hiểm (súng, phá cửa, nổ) trong maxAge giây trở lại.</summary>
    public bool HasViolentNoise(float maxAge) => HasNoise(maxAge) && _last.IsViolent;

    /// <summary>Đã xử lý tiếng động (đã đi xem / đã chạy) thì quên đi.</summary>
    public void Consume() => _hasNoise = false;

    /// <summary>Tiếng động chưa xử lý của go trong maxAge giây trở lại.</summary>
    public static bool TryGetNoise(GameObject go, float maxAge, out HeardNoise noise)
    {
        noise = default;
        if (go == null || !go.TryGetComponent(out NPCHearing h) || !h.HasNoise(maxAge)) return false;
        noise = h._last;
        return true;
    }

    private void Hear(HeardNoise noise, float distance)
    {
        // Giữ tiếng đáng chú ý hơn: nguy hiểm hơn hoặc ngang mức, hoặc tiếng cũ đã quá 3 s.
        bool replace = !_hasNoise || Time.time - _last.Time > 3f || noise.Type >= _last.Type;
        if (!replace) return;
        _last = noise;
        _hasNoise = true;
        if (_log) Debug.Log($"[Hearing] {name}: nghe {noise.Type} cách {distance:0.0} m.", this);

        if (noise.IsViolent && distance <= HostageScareRadius && TryGetComponent(out Hostage hostage)) hostage.NotifyThreat();
        AnyHeard?.Invoke(this, noise);
    }
}
