using UnityEngine;

/// <summary>Một dòng cấu hình bề mặt: decal lỗ đạn + tiếng va chạm cho 1 loại vật liệu.</summary>
[System.Serializable]
public class ImpactSurfaceEntry
{
    [Tooltip("Loại bề mặt mà dòng này phục vụ.")]
    public ImpactSurfaceType surfaceType = ImpactSurfaceType.Default;

    [Tooltip("Prefab lỗ đạn (quad + material). Để trống thì bề mặt này không để lại lỗ.")]
    public GameObject decalPrefab;

    [Tooltip("Hệ số kích thước lỗ đạn cho loại bề mặt này. 1 = giữ nguyên scale của prefab.")]
    public float decalScale = 1f;

    [Tooltip("Danh sách tiếng đạn chạm; mỗi phát bốc ngẫu nhiên 1 clip. Để trống thì không có tiếng.")]
    public AudioClip[] impactClips;

    [Tooltip("Âm lượng riêng cho loại bề mặt này.")]
    [Range(0f, 1f)] public float volume = 1f;

    /// <summary>Bốc ngẫu nhiên 1 clip, null nếu bề mặt này chưa gán tiếng.</summary>
    public AudioClip PickClip() =>
        impactClips == null || impactClips.Length == 0
            ? null
            : impactClips[Random.Range(0, impactClips.Length)];
}
